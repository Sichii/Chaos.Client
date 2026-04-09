#region
using System.Buffers;
using System.Collections.Concurrent;
using Chaos.Client.Data;
using Microsoft.Xna.Framework.Audio;
using NAudio.Wave;
#endregion

namespace Chaos.Client.Systems;

/// <summary>
///     Manages sound effect playback. Caches compressed MP3 bytes from the game archives and decodes on background threads
///     when played. Music is decoded async and never cached. SoundEffect creation and playback happen on the main thread
///     via <see cref="Update" />.
/// </summary>
public sealed class SoundSystem : IDisposable
{
    private const int MAX_CACHED_SOUNDS = 64;
    private const int VOLUME_STEPS = 10;
    private readonly ConcurrentQueue<DecodedSound> PendingSoundQueue = new();
    private readonly List<PlayingSound> PlayingSounds = [];

    private readonly Dictionary<int, (byte[]? Data, long Timestamp)> SoundCache = new();
    private int CurrentMusicId = -1;
    private SoundEffect? MusicEffect;
    private SoundEffectInstance? MusicInstance;
    private float MusicVolume = 0.75f;
    private int PendingMusicId;
    private Task<(byte[] PcmBytes, int SampleRate, AudioChannels Channels)?>? PendingMusicLoad;
    private long SoundCacheTimestamp;
    private float Volume = 0.75f;

    /// <inheritdoc />
    public void Dispose()
    {
        StopMusic();

        foreach (var ps in PlayingSounds)
        {
            ps.Instance.Dispose();
            ps.Effect.Dispose();
        }

        PlayingSounds.Clear();
        SoundCache.Clear();
    }

    private static DecodedSound? DecodeMp3(byte[] compressedBytes, float volume)
    {
        try
        {
            using var ms = new MemoryStream(compressedBytes);
            using var mp3Reader = new Mp3FileReader(ms);
            using var pcmStream = WaveFormatConversionStream.CreatePcmStream(mp3Reader);

            using var pcmMs = new MemoryStream();
            pcmStream.CopyTo(pcmMs);

            var pcmLength = (int)pcmMs.Length;

            if (pcmLength == 0)
                return null;

            var pcmBuffer = ArrayPool<byte>.Shared.Rent(pcmLength);
            pcmMs.Position = 0;
            pcmMs.ReadExactly(pcmBuffer, 0, pcmLength);

            var format = pcmStream.WaveFormat;
            var channels = format.Channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo;

            return new DecodedSound(
                pcmBuffer,
                pcmLength,
                format.SampleRate,
                channels,
                volume);
        } catch
        {
            return null;
        }
    }

    private static (byte[] PcmBytes, int SampleRate, AudioChannels Channels)? DecodeMusicFile(string path)
    {
        try
        {
            using var musStream = File.OpenRead(path);
            using var mp3Reader = new Mp3FileReader(musStream);
            using var pcmStream = WaveFormatConversionStream.CreatePcmStream(mp3Reader);

            using var ms = new MemoryStream();
            pcmStream.CopyTo(ms);

            var pcmBytes = ms.ToArray();

            if (pcmBytes.Length == 0)
                return null;

            var format = pcmStream.WaveFormat;
            var channels = format.Channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo;

            return (pcmBytes, format.SampleRate, channels);
        } catch
        {
            return null;
        }
    }

    private void EvictOldest()
    {
        while (SoundCache.Count > MAX_CACHED_SOUNDS)
        {
            var oldestKey = -1;
            var oldestTime = long.MaxValue;

            foreach ((var key, var entry) in SoundCache)
                if (entry.Timestamp < oldestTime)
                {
                    oldestTime = entry.Timestamp;
                    oldestKey = key;
                }

            if (oldestKey < 0)
                break;

            SoundCache.Remove(oldestKey);
        }
    }

    private static byte[]? LoadCompressedSound(int soundId)
    {
        if (!DatArchives.Legend.TryGetValue($"{soundId}.mp3", out var entry))
            return null;

        try
        {
            using var archiveStream = entry.ToStreamSegment();
            using var ms = new MemoryStream();
            archiveStream.CopyTo(ms);

            return ms.ToArray();
        } catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Plays background music by ID. Kicks off async MP3 decode — call <see cref="Update" /> each frame to start playback
    ///     when ready. musicId 0 stops playback.
    /// </summary>
    public void PlayMusic(int musicId)
    {
        if (musicId == CurrentMusicId)
            return;

        StopMusic();

        if (musicId == 0)
            return;

        var path = Path.Combine(DataContext.DataPath, "music", $"{musicId}.mus");

        if (!File.Exists(path))
            return;

        PendingMusicId = musicId;

        PendingMusicLoad = Task.Run(() => DecodeMusicFile(path));
    }

    /// <summary>
    ///     Plays a sound effect by ID. Caches compressed MP3 bytes on first use and decodes on a background thread. The
    ///     decoded sound is played on the main thread in <see cref="Update" />.
    /// </summary>
    public void PlaySound(int soundId)
    {
        if (Volume <= 0f)
            return;

        byte[]? compressedBytes;

        if (SoundCache.TryGetValue(soundId, out var cached))
        {
            compressedBytes = cached.Data;
            SoundCache[soundId] = (cached.Data, SoundCacheTimestamp++);
        } else
        {
            compressedBytes = LoadCompressedSound(soundId);
            SoundCache[soundId] = (compressedBytes, SoundCacheTimestamp++);

            if (SoundCache.Count > MAX_CACHED_SOUNDS)
                EvictOldest();
        }

        if (compressedBytes is null)
            return;

        var volume = Volume;

        Task.Run(() =>
        {
            var decoded = DecodeMp3(compressedBytes, volume);

            if (decoded is not null)
                PendingSoundQueue.Enqueue(decoded);
        });
    }

    /// <summary>
    ///     Sets the music volume. Range: 0 (mute) to 10 (max).
    /// </summary>
    public void SetMusicVolume(int volume)
    {
        MusicVolume = Math.Clamp(volume, 0, VOLUME_STEPS) / (float)VOLUME_STEPS;

        MusicInstance?.Volume = MusicVolume;
    }

    /// <summary>
    ///     Sets the sound effect volume. Range: 0 (mute) to 10 (max).
    /// </summary>
    public void SetSoundVolume(int volume) => Volume = Math.Clamp(volume, 0, VOLUME_STEPS) / (float)VOLUME_STEPS;

    /// <summary>
    ///     Stops the currently playing music track.
    /// </summary>
    public void StopMusic()
    {
        CurrentMusicId = -1;
        PendingMusicLoad = null;

        if (MusicInstance is not null)
        {
            MusicInstance.Stop();
            MusicInstance.Dispose();
            MusicInstance = null;
        }

        if (MusicEffect is not null)
        {
            MusicEffect.Dispose();
            MusicEffect = null;
        }
    }

    /// <summary>
    ///     Pumps pending sound and music loads. Call once per frame from the game loop.
    /// </summary>
    public void Update()
    {
        //clean up finished sounds
        for (var i = PlayingSounds.Count - 1; i >= 0; i--)
        {
            if (PlayingSounds[i].Instance.State != SoundState.Stopped)
                continue;

            PlayingSounds[i]
                .Instance
                .Dispose();

            PlayingSounds[i]
                .Effect
                .Dispose();
            PlayingSounds.RemoveAt(i);
        }

        //play sounds that finished decoding on background threads
        while (PendingSoundQueue.TryDequeue(out var pending))
        {
            try
            {
                var sfx = new SoundEffect(
                    pending.PcmBuffer,
                    0,
                    pending.PcmLength,
                    pending.SampleRate,
                    pending.Channels,
                    0,
                    0);
                var instance = sfx.CreateInstance();
                instance.Volume = pending.Volume;
                instance.Play();
                PlayingSounds.Add(new PlayingSound(sfx, instance));
            } catch
            {
                //sound creation failure is non-critical
            } finally
            {
                ArrayPool<byte>.Shared.Return(pending.PcmBuffer);
            }
        }

        //music
        if (PendingMusicLoad is not { IsCompleted: true })
            return;

        var result = PendingMusicLoad.Result;
        PendingMusicLoad = null;

        if (result is null)
            return;

        (var pcmBytes, var sampleRate, var channels) = result.Value;

        try
        {
            MusicEffect = new SoundEffect(pcmBytes, sampleRate, channels);
            MusicInstance = MusicEffect.CreateInstance();
            MusicInstance.IsLooped = true;
            MusicInstance.Volume = MusicVolume;
            MusicInstance.Play();
            CurrentMusicId = PendingMusicId;
        } catch
        {
            StopMusic();
        }
    }

    private record DecodedSound(
        byte[] PcmBuffer,
        int PcmLength,
        int SampleRate,
        AudioChannels Channels,
        float Volume);

    private record PlayingSound(SoundEffect Effect, SoundEffectInstance Instance);
}
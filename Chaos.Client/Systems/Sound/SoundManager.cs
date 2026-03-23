#region
using Chaos.Client.Data;
using Microsoft.Xna.Framework.Audio;
using NAudio.Wave;
#endregion

namespace Chaos.Client.Systems.Sound;

/// <summary>
///     Manages sound effect playback. Decodes MP3 streams from the game archives via NAudio, caches a bounded number of
///     SoundEffect instances. Reads directly from memory-mapped archives — no intermediate stream caching.
/// </summary>
public sealed class SoundManager : IDisposable
{
    private const int MAX_CACHED_SOUNDS = 64;
    private const int VOLUME_STEPS = 10;
    private readonly Dictionary<int, SoundEffect?> SoundCache = new();
    private int CurrentMusicId = -1;
    private SoundEffect? MusicEffect;
    private SoundEffectInstance? MusicInstance;
    private float MusicVolume = 0.75f;
    private float Volume = 0.75f;

    /// <inheritdoc />
    public void Dispose()
    {
        StopMusic();

        foreach (var sfx in SoundCache.Values)
            sfx?.Dispose();

        SoundCache.Clear();
    }

    private void EvictOldest()
    {
        var toRemove = SoundCache.Count - MAX_CACHED_SOUNDS;

        foreach (var key in SoundCache.Keys
                                      .Take(toRemove)
                                      .ToList())
        {
            SoundCache[key]
                ?.Dispose();
            SoundCache.Remove(key);
        }
    }

    private static SoundEffect? LoadSound(int soundId)
    {
        if (!DatArchives.Legend.TryGetValue($"{soundId}.mp3", out var entry))
            return null;

        try
        {
            using var archiveStream = entry.ToStreamSegment();
            using var mp3Reader = new Mp3FileReader(archiveStream);
            using var pcmStream = WaveFormatConversionStream.CreatePcmStream(mp3Reader);

            using var ms = new MemoryStream();
            pcmStream.CopyTo(ms);

            var pcmBytes = ms.ToArray();

            if (pcmBytes.Length == 0)
                return null;

            var format = pcmStream.WaveFormat;

            return new SoundEffect(pcmBytes, format.SampleRate, format.Channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
        } catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Plays background music by ID. Loads MP3 from music directory, decodes to PCM, loops continuously.
    ///     musicId 0 stops playback.
    /// </summary>
    public void PlayMusic(int musicId)
    {
        if (musicId == CurrentMusicId)
            return;

        StopMusic();

        if (musicId == 0)
            return;

        try
        {
            var path = Path.Combine(DataContext.DataPath, "music", $"{musicId}.mus");

            if (!File.Exists(path))
                return;

            using var musStream = File.OpenRead(path);
            using var mp3Reader = new Mp3FileReader(musStream);
            using var pcmStream = WaveFormatConversionStream.CreatePcmStream(mp3Reader);

            using var ms = new MemoryStream();
            pcmStream.CopyTo(ms);

            var pcmBytes = ms.ToArray();

            if (pcmBytes.Length == 0)
                return;

            var format = pcmStream.WaveFormat;

            MusicEffect = new SoundEffect(pcmBytes, format.SampleRate, format.Channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);

            MusicInstance = MusicEffect.CreateInstance();
            MusicInstance.IsLooped = true;
            MusicInstance.Volume = MusicVolume;
            MusicInstance.Play();
            CurrentMusicId = musicId;
        } catch
        {
            StopMusic();
        }
    }

    /// <summary>
    ///     Plays a sound effect by ID. Loads and caches on first use.
    /// </summary>
    public void PlaySound(int soundId)
    {
        if (Volume <= 0f)
            return;

        if (!SoundCache.TryGetValue(soundId, out var sfx))
        {
            sfx = LoadSound(soundId);
            SoundCache[soundId] = sfx;

            if (SoundCache.Count > MAX_CACHED_SOUNDS)
                EvictOldest();
        }

        sfx?.Play(Volume, 0f, 0f);
    }

    /// <summary>
    ///     Sets the music volume. Range: 0 (mute) to 10 (max).
    /// </summary>
    public void SetMusicVolume(int volume)
    {
        MusicVolume = Math.Clamp(volume, 0, VOLUME_STEPS) / (float)VOLUME_STEPS;

        if (MusicInstance is not null)
            MusicInstance.Volume = MusicVolume;
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
}
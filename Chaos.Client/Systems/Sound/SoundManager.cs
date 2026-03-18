#region
using Chaos.Client.Data;
using Microsoft.Xna.Framework.Audio;
using NAudio.Wave;
#endregion

namespace Chaos.Client.Systems.Sound;

/// <summary>
///     Manages sound effect playback. Decodes MP3 streams from the game archives via NAudio and caches the resulting
///     MonoGame SoundEffect instances.
/// </summary>
public sealed class SoundManager : IDisposable
{
    private const int MAX_CACHED_SOUNDS = 128;
    private const int VOLUME_STEPS = 10;
    private readonly Dictionary<int, SoundEffect?> SoundCache = new();
    private float Volume = 0.75f;

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var sfx in SoundCache.Values)
            sfx?.Dispose();

        SoundCache.Clear();
    }

    private void EvictOldest()
    {
        // Simple eviction: remove first entries until under limit
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

    private SoundEffect? LoadSound(int soundId)
    {
        try
        {
            using var mp3Stream = DataContext.Sounds.GetEffectSound(soundId);

            if (mp3Stream is null)
                return null;

            using var mp3Reader = new Mp3FileReader(mp3Stream);
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
    ///     Plays background music by ID. Stub for future implementation.
    /// </summary>
    public void PlayMusic(int musicId)
    {
        // Music playback deferred to a later milestone
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
    ///     Sets the sound effect volume. Range: 0 (mute) to 10 (max).
    /// </summary>
    public void SetSoundVolume(int volume) => Volume = Math.Clamp(volume, 0, VOLUME_STEPS) / (float)VOLUME_STEPS;
}
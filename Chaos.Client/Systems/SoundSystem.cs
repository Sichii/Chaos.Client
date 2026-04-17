#region
using System.Collections.Concurrent;
using Chaos.Client.Data;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
#endregion

namespace Chaos.Client.Systems;

/// <summary>
///     Manages sound effect and music playback via NAudio. Decoded PCM is normalized to a canonical 44.1kHz stereo
///     float format and fed through a single <see cref="MixingSampleProvider" /> routed to a <see cref="WasapiOut" />
///     on the current default audio render endpoint. Listens for Windows default-device changes via
///     <see cref="MMDeviceEnumerator" /> and recreates the output on the next <see cref="Update" /> so audio follows
///     the user's default device swap while the client is running.
/// </summary>
public sealed class SoundSystem : IDisposable
{
    private const int CANONICAL_SAMPLE_RATE = 44100;
    private const int CANONICAL_CHANNELS = 2;
    private const int MAX_CACHED_SOUNDS = 64;
    private const int VOLUME_STEPS = 10;
    private const int OUTPUT_LATENCY_MS = 50;
    private const int FALLBACK_LATENCY_MS = 100;

    private static readonly WaveFormat CanonicalFormat
        = WaveFormat.CreateIeeeFloatWaveFormat(CANONICAL_SAMPLE_RATE, CANONICAL_CHANNELS);

    private readonly MixingSampleProvider Mixer = new(CanonicalFormat) { ReadFully = true };
    private readonly ConcurrentQueue<PendingDecode> PendingDecodedSounds = new();
    private readonly HashSet<int> PendingDecodes = [];
    private readonly HashSet<int> PlayedThisFrame = [];
    private readonly Dictionary<int, (float[] Samples, long Timestamp)> SoundCache = [];

    private int CurrentMusicId = -1;
    private StreamingMusicProvider? CurrentMusicSource;
    private MMDeviceEnumerator? DeviceEnumerator;
    private volatile bool DeviceChangePending;
    private bool IsDisposed;
    private float MusicVolume = 0.75f;
    private VolumeSampleProvider? MusicVolumeWrapper;
    private DefaultDeviceNotificationClient? NotificationClient;
    private IWavePlayer? Output;
    private long SoundCacheTimestamp;
    private float Volume = 0.75f;

    public SoundSystem()
    {
        Mixer.MixerInputEnded += OnMixerInputEnded;
        RegisterDeviceNotifications();
        InitializeDevice();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;

        UnregisterDeviceNotifications();
        StopMusic();

        try
        {
            Output?.Stop();
        } catch
        {
            //ignore: output may already be in a faulted state
        }

        try
        {
            Output?.Dispose();
        } catch
        {
            //ignore
        }

        Output = null;

        Mixer.MixerInputEnded -= OnMixerInputEnded;
        Mixer.RemoveAllMixerInputs();

        SoundCache.Clear();
    }

    /// <summary>
    ///     Plays background music by ID. Kicks off async MP3 decode — call <see cref="Update" /> each frame to start
    ///     playback when ready. musicId 0 stops playback.
    /// </summary>
    public void PlayMusic(int musicId)
    {
        if (IsDisposed)
            return;

        if (musicId == CurrentMusicId)
            return;

        StopMusic();

        if (musicId == 0)
            return;

        var path = Path.Combine(DataContext.DataPath, "music", $"{musicId}.mus");

        if (!File.Exists(path))
            return;

        if (Output is null)
            return;

        try
        {
            CurrentMusicSource = new StreamingMusicProvider(path, CANONICAL_SAMPLE_RATE, CANONICAL_CHANNELS);
        } catch
        {
            return;
        }

        MusicVolumeWrapper = new VolumeSampleProvider(CurrentMusicSource)
        {
            Volume = MusicVolume
        };

        Mixer.AddMixerInput(MusicVolumeWrapper);
        CurrentMusicId = musicId;
    }

    /// <summary>
    ///     Plays a sound effect by ID. Decoded PCM is cached per sound on first use; subsequent calls create a
    ///     lightweight provider and hand it straight to the mixer.
    /// </summary>
    public void PlaySound(int soundId)
    {
        if (IsDisposed || (Volume <= 0f))
            return;

        //collapse same-frame duplicate triggers (e.g. AOE hitting multiple targets in a single tick)
        if (!PlayedThisFrame.Add(soundId))
            return;

        if (SoundCache.TryGetValue(soundId, out var cached))
        {
            SoundCache[soundId] = (cached.Samples, SoundCacheTimestamp++);
            PlayCachedSamples(cached.Samples, Volume);

            return;
        }

        //avoid dispatching a duplicate decode for a sound that's already being decoded
        if (!PendingDecodes.Add(soundId))
            return;

        var volumeSnapshot = Volume;

        Task.Run(() =>
        {
            var samples = LoadAndDecodeSoundEffect(soundId);
            PendingDecodedSounds.Enqueue(new PendingDecode(soundId, samples, volumeSnapshot));
        });
    }

    /// <summary>
    ///     Sets the music volume. Range: 0 (mute) to 10 (max). Applies immediately to the currently playing track.
    /// </summary>
    public void SetMusicVolume(int volume)
    {
        MusicVolume = Math.Clamp(volume, 0, VOLUME_STEPS) / (float)VOLUME_STEPS;

        if (MusicVolumeWrapper is not null)
            MusicVolumeWrapper.Volume = MusicVolume;
    }

    /// <summary>
    ///     Sets the sound effect volume. Range: 0 (mute) to 10 (max). Future plays use the new volume; sounds already
    ///     in flight are not retroactively scaled.
    /// </summary>
    public void SetSoundVolume(int volume) => Volume = Math.Clamp(volume, 0, VOLUME_STEPS) / (float)VOLUME_STEPS;

    /// <summary>
    ///     Pumps pending sound/music decodes and processes any deferred audio device swap. Call once per frame from
    ///     the game loop.
    /// </summary>
    public void Update()
    {
        if (IsDisposed)
            return;

        //reset same-frame dedup window; any PlaySound later this frame starts from a clean set
        PlayedThisFrame.Clear();

        if (DeviceChangePending)
        {
            DeviceChangePending = false;
            InitializeDevice();
        }

        while (PendingDecodedSounds.TryDequeue(out var pending))
        {
            PendingDecodes.Remove(pending.SoundId);

            if (pending.Samples is null)
                continue;

            SoundCache[pending.SoundId] = (pending.Samples, SoundCacheTimestamp++);

            if (SoundCache.Count > MAX_CACHED_SOUNDS)
                EvictOldest();

            //deferred decode counts as this frame's play — block any PlaySound(soundId) later this frame
            PlayedThisFrame.Add(pending.SoundId);
            PlayCachedSamples(pending.Samples, pending.Volume);
        }
    }

    private static float[]? ConvertToCanonical(Mp3FileReader reader)
    {
        var source = reader.ToSampleProvider();

        if (source.WaveFormat.SampleRate != CANONICAL_SAMPLE_RATE)
            source = new WdlResamplingSampleProvider(source, CANONICAL_SAMPLE_RATE);

        if (source.WaveFormat.Channels == 1)
            source = new MonoToStereoSampleProvider(source);

        var readBuffer = new float[4096];
        var result = new float[CANONICAL_SAMPLE_RATE * CANONICAL_CHANNELS];
        var total = 0;
        int read;

        while ((read = source.Read(readBuffer, 0, readBuffer.Length)) > 0)
        {
            if (total + read > result.Length)
            {
                var newLen = Math.Max(result.Length * 2, total + read);
                Array.Resize(ref result, newLen);
            }

            Array.Copy(readBuffer, 0, result, total, read);
            total += read;
        }

        if (total == 0)
            return null;

        if (total < result.Length)
            Array.Resize(ref result, total);

        return result;
    }

    private static float[]? DecodeMp3Bytes(byte[] compressed)
    {
        try
        {
            using var ms = new MemoryStream(compressed);
            using var reader = new Mp3FileReader(ms);

            return ConvertToCanonical(reader);
        } catch
        {
            return null;
        }
    }


    private static float[]? LoadAndDecodeSoundEffect(int soundId)
    {
        if (!DatArchives.Legend.TryGetValue($"{soundId}.mp3", out var entry))
            return null;

        byte[] compressed;

        try
        {
            using var archiveStream = entry.ToStreamSegment();
            using var ms = new MemoryStream();
            archiveStream.CopyTo(ms);
            compressed = ms.ToArray();
        } catch
        {
            return null;
        }

        return DecodeMp3Bytes(compressed);
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

    private void InitializeDevice()
    {
        if (IsDisposed)
            return;

        try
        {
            Output?.Stop();
        } catch
        {
            //ignore
        }

        try
        {
            Output?.Dispose();
        } catch
        {
            //ignore
        }

        Output = null;

        //wrap the mixer so WasapiOut/WaveOutEvent can consume it: IWavePlayer.Init takes IWaveProvider,
        //and SampleToWaveProvider adapts our float-sample mixer chain to that interface
        var waveProvider = new SampleToWaveProvider(Mixer);

        try
        {
            var wasapi = new WasapiOut(AudioClientShareMode.Shared, OUTPUT_LATENCY_MS);
            wasapi.Init(waveProvider);
            wasapi.Play();
            Output = wasapi;

            return;
        } catch
        {
            //fall through to the WinMM-based fallback
        }

        try
        {
            var waveOut = new WaveOutEvent
            {
                DesiredLatency = FALLBACK_LATENCY_MS
            };
            waveOut.Init(waveProvider);
            waveOut.Play();
            Output = waveOut;
        } catch
        {
            Output = null;
        }
    }

    private void OnMixerInputEnded(object? sender, SampleProviderEventArgs e)
    {
        //sfx samples live in SoundCache and are reused on future plays; nothing to release here
    }

    private void PlayCachedSamples(float[] samples, float volume)
    {
        if (Output is null)
            return;

        var provider = new CachedSoundSampleProvider(samples, CanonicalFormat);

        var wrapped = new VolumeSampleProvider(provider)
        {
            Volume = volume
        };

        Mixer.AddMixerInput(wrapped);
    }

    private void RegisterDeviceNotifications()
    {
        try
        {
            DeviceEnumerator = new MMDeviceEnumerator();
            NotificationClient = new DefaultDeviceNotificationClient(() => DeviceChangePending = true);
            DeviceEnumerator.RegisterEndpointNotificationCallback(NotificationClient);
        } catch
        {
            DeviceEnumerator = null;
            NotificationClient = null;
        }
    }

    private void StopMusic()
    {
        CurrentMusicId = -1;

        if (MusicVolumeWrapper is not null)
        {
            Mixer.RemoveMixerInput(MusicVolumeWrapper);
            MusicVolumeWrapper = null;
        }

        CurrentMusicSource?.Dispose();
        CurrentMusicSource = null;
    }

    private void UnregisterDeviceNotifications()
    {
        if (DeviceEnumerator is not null && NotificationClient is not null)
        {
            try
            {
                DeviceEnumerator.UnregisterEndpointNotificationCallback(NotificationClient);
            } catch
            {
                //ignore
            }
        }

        try
        {
            DeviceEnumerator?.Dispose();
        } catch
        {
            //ignore
        }

        DeviceEnumerator = null;
        NotificationClient = null;
    }

    private readonly record struct PendingDecode(int SoundId, float[]? Samples, float Volume);

    private sealed class CachedSoundSampleProvider : ISampleProvider
    {
        private readonly float[] Samples;
        private int Position;
        public WaveFormat WaveFormat { get; }

        public CachedSoundSampleProvider(float[] samples, WaveFormat waveFormat)
        {
            Samples = samples;
            WaveFormat = waveFormat;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var available = Samples.Length - Position;

            if (available <= 0)
                return 0;

            var toRead = Math.Min(available, count);
            Array.Copy(Samples, Position, buffer, offset, toRead);
            Position += toRead;

            return toRead;
        }
    }

    private sealed class StreamingMusicProvider : ISampleProvider, IDisposable
    {
        private readonly string FilePath;
        private readonly int TargetSampleRate;
        private FileStream? Stream;
        private Mp3FileReader? Reader;
        private ISampleProvider? Source;

        public WaveFormat WaveFormat { get; }

        public StreamingMusicProvider(string filePath, int sampleRate, int channels)
        {
            FilePath = filePath;
            TargetSampleRate = sampleRate;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);

            OpenPipeline();
        }

        public int Read(float[] buffer, int offset, int count)
        {
            if (Source is null)
                return 0;

            var totalRead = 0;

            while (totalRead < count)
            {
                var read = Source.Read(buffer, offset + totalRead, count - totalRead);

                if (read > 0)
                {
                    totalRead += read;

                    continue;
                }

                //end of track — loop by rebuilding the decode pipeline from the same file
                ClosePipeline();
                OpenPipeline();

                if (Source is null)
                    break;
            }

            return totalRead;
        }

        private void OpenPipeline()
        {
            Stream = File.OpenRead(FilePath);
            Reader = new Mp3FileReader(Stream);

            var source = Reader.ToSampleProvider();

            if (source.WaveFormat.SampleRate != TargetSampleRate)
                source = new WdlResamplingSampleProvider(source, TargetSampleRate);

            if (source.WaveFormat.Channels == 1)
                source = new MonoToStereoSampleProvider(source);

            Source = source;
        }

        private void ClosePipeline()
        {
            Source = null;
            Reader?.Dispose();
            Reader = null;
            Stream?.Dispose();
            Stream = null;
        }

        public void Dispose() => ClosePipeline();
    }

    private sealed class DefaultDeviceNotificationClient : IMMNotificationClient
    {
        private readonly Action OnChange;

        public DefaultDeviceNotificationClient(Action onChange) => OnChange = onChange;

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow != DataFlow.Render)
                return;

            //both Multimedia and Console typically fire when the user swaps their default device in Windows;
            //we coalesce via the DeviceChangePending flag so a double-fire is idempotent
            if (role is not Role.Multimedia and not Role.Console)
                return;

            OnChange();
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
            //unused
        }

        public void OnDeviceRemoved(string deviceId)
        {
            //unused
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            //unused
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
            //unused
        }
    }
}
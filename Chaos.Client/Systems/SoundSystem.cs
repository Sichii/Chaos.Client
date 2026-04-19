#region
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Chaos.Client.Data;
#endregion

namespace Chaos.Client.Systems;

/// <summary>
///     Manages sound-effect and background-music playback via SDL2_mixer (with minimp3 compiled directly into the
///     mixer DLL). SFX are loaded from legend.dat as MP3 bytes, decoded to PCM once via <c>Mix_LoadWAV_RW</c> and
///     cached as <c>Mix_Chunk</c> pointers; playback uses the mixer's channel pool with per-channel volume. Music
///     is streamed from disk via <c>Mix_LoadMUS</c>, with cross-map transitions driven by SDL_mixer's built-in
///     <c>Mix_FadeOutMusic</c>/<c>Mix_FadeInMusic</c>. When the same sound id is triggered while an earlier
///     instance is still audible, the prior live instances have their channel volume halved so the new play stands
///     out (overlap ducking instead of voice-stealing like the original Miles-based client).
/// </summary>
public sealed class SoundSystem : IDisposable
{
    //the original client opens its Miles driver at 22050 Hz / stereo; we match that rate and let Windows do
    //the single resample to the output device rate rather than stacking our own
    private const int MIX_FREQUENCY = 22050;
    private const int MIX_CHANNELS = 2;
    //sample chunk size fed to the audio callback; ~93ms at 22050Hz, good balance of latency vs callback overhead
    private const int MIX_CHUNK_SIZE = 2048;
    //default Mix_AllocateChannels is 8; we bump to 32 so overlap-heavy situations (AOE effects, crowds of mobs)
    //don't run out of voices and return -1 from Mix_PlayChannel
    private const int CHANNEL_COUNT = 32;
    //fade duration for map-transition music swaps, matched to the feel of the original client's ramp
    private const int MUSIC_FADE_MS = 500;
    private const int MAX_CACHED_SOUNDS = 64;
    private const int VOLUME_STEPS = 10;
    //volume scale multiplier mapping our 0..10 slider to SDL_mixer's 0..128 range; at tick=10 we reach MAX_VOLUME
    private const int VOLUME_SCALE = SdlMixer.MIX_MAX_VOLUME / VOLUME_STEPS;
    //duck prior live instances of the same sound id on every new play; -3 dB (equal-power) preserves perceived loudness when two copies overlap
    private const float OVERLAP_EQUAL_POWER_MULTIPLIER = 0.7071068f;
    //per-frame volume delta while ramping a channel toward its duck target. ~10 units/frame at 60fps
    //spreads a full duck (a ~38-unit drop) over ~65ms so the volume change never happens in a single
    //sample — an instant Mix_Volume drop creates a step in the output waveform that's audible as a click
    //when the chunk happens to be at a high-amplitude moment at the time of the duck
    private const int DUCK_RAMP_UNITS_PER_FRAME = 10;

    //delegate instance kept as a field so the GC doesn't collect the callback SDL holds a native pointer to
    private readonly SdlMixer.ChannelFinishedCallback ChannelFinishedDelegate;
    //populated on SDL's audio thread when a channel naturally finishes; drained in Update() on the game thread
    private readonly ConcurrentQueue<int> FinishedChannels = new();
    //channel → sound id mapping for ducking + completion; only touched on the game thread
    private readonly Dictionary<int, int> ChannelToSoundId = [];
    //inverse lookup: sound id → currently-playing channels (for per-id ducking on new plays)
    private readonly Dictionary<int, List<int>> SoundIdToChannels = [];
    //decoded Mix_Chunk pointers indexed by sound id, with a monotonic timestamp for LRU eviction
    private readonly Dictionary<int, (nint Chunk, long Timestamp)> SoundCache = [];
    //same-frame dedup (e.g. AOE hitting multiple targets in one tick trying to play the same sound N times)
    private readonly HashSet<int> PlayedThisFrame = [];
    //channels that are currently being ramped down toward a duck target, mapped to that target volume.
    //Update() advances current Mix_Volume toward the target by DUCK_RAMP_UNITS_PER_FRAME each frame rather
    //than applying the full drop in a single Mix_Volume call
    private readonly Dictionary<int, int> ChannelDuckTargets = [];

    private int CurrentMusicId = -1;
    private nint CurrentMusicPtr;
    private bool Initialized;
    private bool IsDisposed;
    private bool MusicFadingOut;
    private int MusicVolumeValue = SdlMixer.MIX_MAX_VOLUME;
    //music id queued to start once the current music's fade-out completes; 0 means "stop music"
    private int PendingMusicId;
    private int SfxVolume = SdlMixer.MIX_MAX_VOLUME;
    private long SoundCacheTimestamp;

    public SoundSystem()
    {
        ChannelFinishedDelegate = OnChannelFinished;
        InitializeMixer();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;

        if (!Initialized)
            return;

        //clear the callback BEFORE halting so the audio thread can't fire OnChannelFinished while we tear down
        SdlMixer.Mix_ChannelFinished(nint.Zero);
        SdlMixer.Mix_HaltChannel(SdlMixer.MIX_DEFAULT_CHANNEL);
        SdlMixer.Mix_HaltMusic();

        if (CurrentMusicPtr != nint.Zero)
        {
            SdlMixer.Mix_FreeMusic(CurrentMusicPtr);
            CurrentMusicPtr = nint.Zero;
        }

        foreach (var entry in SoundCache.Values)
            if (entry.Chunk != nint.Zero)
                SdlMixer.Mix_FreeChunk(entry.Chunk);

        SoundCache.Clear();

        SdlMixer.Mix_CloseAudio();
        SdlMixer.Mix_Quit();
        Sdl.SDL_QuitSubSystem(Sdl.SDL_INIT_AUDIO);

        Initialized = false;
    }

    /// <summary>
    ///     Plays background music by id. Triggers a fade-out of the current track (if any) and a fade-in of the new
    ///     track once the fade-out completes. musicId 0 stops playback with a fade-out and leaves no pending track.
    /// </summary>
    public void PlayMusic(int musicId)
    {
        if (IsDisposed || !Initialized)
            return;

        //already mid-fade-out — just update what plays next once the fade completes
        if (MusicFadingOut)
        {
            PendingMusicId = musicId;

            return;
        }

        //already playing the requested track — nothing to do
        if (musicId == CurrentMusicId)
            return;

        //stop request when nothing is currently playing: no fade needed
        if ((musicId == 0) && (CurrentMusicPtr == nint.Zero))
            return;

        //nothing currently playing — skip the fade-out phase and start the new track directly
        if (CurrentMusicPtr == nint.Zero)
        {
            StartMusic(musicId);

            return;
        }

        //kick off the async fade-out; Update() will pick up the completion and start PendingMusicId
        SdlMixer.Mix_FadeOutMusic(MUSIC_FADE_MS);
        MusicFadingOut = true;
        PendingMusicId = musicId;
    }

    /// <summary>
    ///     Plays a sound effect by id. First-time plays decode the MP3 synchronously (minimp3 is fast on short
    ///     files); subsequent plays grab the cached <c>Mix_Chunk</c>. If a previous instance of the same id is
    ///     still playing, its channel volume is halved so the new play is more audible.
    /// </summary>
    public void PlaySound(int soundId)
    {
        if (IsDisposed || !Initialized || (SfxVolume <= 0))
            return;

        //collapse same-frame duplicate triggers (e.g. AOE hitting multiple targets in a single tick)
        if (!PlayedThisFrame.Add(soundId))
            return;

        nint chunk;

        if (SoundCache.TryGetValue(soundId, out var cached))
        {
            chunk = cached.Chunk;
            SoundCache[soundId] = (chunk, SoundCacheTimestamp++);
        } else
        {
            chunk = LoadChunk(soundId);

            if (chunk == nint.Zero)
                return;

            SoundCache[soundId] = (chunk, SoundCacheTimestamp++);

            if (SoundCache.Count > MAX_CACHED_SOUNDS)
                EvictOldest();
        }

        //duck any currently-playing instances of this sound so the new play is more prominent.
        //skip any channel that already finished on the audio thread but hasn't been drained by Update()
        //yet — ducking a silent channel just reduces its persisted Mix_Volume, which would then carry
        //into the next play SDL_mixer assigns to that channel and cause a volume-step pop.
        //record the target volume rather than applying the drop instantly; Update() ramps each frame so
        //the waveform out of the channel has no single-sample discontinuity (which would click)
        if (SoundIdToChannels.TryGetValue(soundId, out var existing))
            foreach (var ch in existing)
            {
                if (SdlMixer.Mix_Playing(ch) == 0)
                    continue;

                //if a duck ramp is already in flight for this channel, chain off the pending target so
                //overlapping triggers compound the way they would with the old instant-duck logic; otherwise
                //read the current channel volume via Mix_Volume(ch, -1) (negative volume queries without setting)
                var startingVolume = ChannelDuckTargets.TryGetValue(ch, out var pendingTarget)
                    ? pendingTarget
                    : SdlMixer.Mix_Volume(ch, -1);

                ChannelDuckTargets[ch] = (int)(startingVolume * OVERLAP_EQUAL_POWER_MULTIPLIER);
            }

        //manually find a free channel instead of letting Mix_PlayChannel(-1) pick. this lets us set the channel
        //volume BEFORE starting the play, which closes a race where a channel that finished on the audio thread
        //mid-frame (before Update() could reset its volume) would be reassigned here at its old ducked level:
        //the audio callback could then read the first samples at the reduced volume before a post-Mix_PlayChannel
        //Mix_Volume reset caught up, producing a volume-step pop at the start of the chunk
        var channel = -1;

        for (var i = 0; i < CHANNEL_COUNT; i++)
            if (SdlMixer.Mix_Playing(i) == 0)
            {
                channel = i;

                break;
            }

        if (channel < 0)
            return;

        //set volume before play begins so the first audio callback after Mix_PlayChannel sees the correct level
        SdlMixer.Mix_Volume(channel, SfxVolume);

        //clear any leftover duck ramp on this channel — the Update() loop would otherwise try to ramp
        //the volume back down from SfxVolume toward a stale target left over from the previous play
        ChannelDuckTargets.Remove(channel);

        //Mix_PlayChannel with an explicit channel index still stops any sound currently on that channel, but we
        //just verified Mix_Playing(ch) == 0 so the channel is idle
        if (SdlMixer.Mix_PlayChannel(channel, chunk, 0) < 0)
            return;

        ChannelToSoundId[channel] = soundId;

        if (!SoundIdToChannels.TryGetValue(soundId, out var list))
        {
            list = [];
            SoundIdToChannels[soundId] = list;
        }

        //guard against a race where SDL re-assigns a channel we still have tracked (audio thread finished
        //it between Update()'s drain and this call) — duplicate entries would survive one drain pass each
        //and accumulate as zombies that the duck loop keeps shrinking the volume on
        if (!list.Contains(channel))
            list.Add(channel);
    }

    /// <summary>
    ///     Sets the music volume. Range: 0 (mute) to 10 (max). Applies immediately to the currently playing track.
    /// </summary>
    public void SetMusicVolume(int volume)
    {
        MusicVolumeValue = Math.Clamp(volume, 0, VOLUME_STEPS) * VOLUME_SCALE;

        if (Initialized)
            SdlMixer.Mix_VolumeMusic(MusicVolumeValue);
    }

    /// <summary>
    ///     Sets the sound effect volume. Range: 0 (mute) to 10 (max). Future plays use the new volume; sounds
    ///     already in flight keep their current channel volume (matching the prior NAudio-based behavior).
    /// </summary>
    public void SetSoundVolume(int volume) => SfxVolume = Math.Clamp(volume, 0, VOLUME_STEPS) * VOLUME_SCALE;

    /// <summary>
    ///     Pumps deferred audio-thread work back into the game state. Call once per frame from the game loop.
    /// </summary>
    public void Update()
    {
        if (IsDisposed || !Initialized)
            return;

        //reset same-frame dedup window; any PlaySound later this frame starts from a clean set
        PlayedThisFrame.Clear();

        //reap channels that finished naturally on the audio thread so their tracking entries don't leak.
        //restore per-channel volume to the current SFX slider here: SDL_mixer preserves channel volume
        //across plays, so if this channel was ducked while playing, the reduction would persist and
        //the next chunk SDL_mixer assigns to it would briefly play at the ducked level before the
        //post-Mix_PlayChannel volume reset catches up (pop at sound start)
        while (FinishedChannels.TryDequeue(out var channel))
        {
            SdlMixer.Mix_Volume(channel, SfxVolume);
            ChannelDuckTargets.Remove(channel);

            if (!ChannelToSoundId.Remove(channel, out var soundId))
                continue;

            if (!SoundIdToChannels.TryGetValue(soundId, out var list))
                continue;

            list.Remove(channel);

            if (list.Count == 0)
                SoundIdToChannels.Remove(soundId);
        }

        //advance any in-flight duck ramps toward their target volumes. stepping by a fixed number of
        //units per frame spreads the volume change across ~4-8 frames rather than applying it in one
        //Mix_Volume call — a single-sample volume drop creates a step in the waveform that's audible
        //as a click, especially when the chunk is at a high-amplitude moment during the duck
        if (ChannelDuckTargets.Count > 0)
            foreach (var ch in ChannelDuckTargets.Keys.ToArray())
            {
                //audio thread finished this channel while a duck was pending — the finish path above
                //will already have reset its volume, so just drop the ramp state
                if (SdlMixer.Mix_Playing(ch) == 0)
                {
                    ChannelDuckTargets.Remove(ch);

                    continue;
                }

                var target = ChannelDuckTargets[ch];
                var currentVol = SdlMixer.Mix_Volume(ch, -1);

                if (currentVol <= target)
                {
                    ChannelDuckTargets.Remove(ch);

                    continue;
                }

                SdlMixer.Mix_Volume(ch, Math.Max(target, currentVol - DUCK_RAMP_UNITS_PER_FRAME));
            }

        //detect fade-out completion and start the queued track (if any)
        if (MusicFadingOut && (SdlMixer.Mix_PlayingMusic() == 0))
        {
            MusicFadingOut = false;

            if (CurrentMusicPtr != nint.Zero)
            {
                SdlMixer.Mix_FreeMusic(CurrentMusicPtr);
                CurrentMusicPtr = nint.Zero;
            }

            CurrentMusicId = -1;

            if (PendingMusicId > 0)
                StartMusic(PendingMusicId);

            PendingMusicId = 0;
        }
    }

    private void EvictOldest()
    {
        while (SoundCache.Count > MAX_CACHED_SOUNDS)
        {
            var oldestKey = -1;
            var oldestTime = long.MaxValue;

            foreach ((var key, var entry) in SoundCache)
            {
                //skip anything that's still audibly playing — Mix_FreeChunk on a live chunk corrupts the mixer
                if (SoundIdToChannels.ContainsKey(key))
                    continue;

                if (entry.Timestamp < oldestTime)
                {
                    oldestTime = entry.Timestamp;
                    oldestKey = key;
                }
            }

            //every cached sound is currently playing; defer eviction rather than risk crashing the mixer
            if (oldestKey < 0)
                break;

            var chunk = SoundCache[oldestKey].Chunk;
            SoundCache.Remove(oldestKey);

            if (chunk != nint.Zero)
                SdlMixer.Mix_FreeChunk(chunk);
        }
    }

    private void InitializeMixer()
    {
        if (Sdl.SDL_InitSubSystem(Sdl.SDL_INIT_AUDIO) != 0)
            return;

        //Mix_Init is effectively a no-op for minimp3 (statically linked since SDL_mixer 2.6) but is still part of
        //the official init sequence — safe to call unconditionally
        SdlMixer.Mix_Init(SdlMixer.MIX_INIT_MP3);

        if (SdlMixer.Mix_OpenAudio(MIX_FREQUENCY, SdlMixer.AUDIO_S16LSB, MIX_CHANNELS, MIX_CHUNK_SIZE) != 0)
        {
            SdlMixer.Mix_Quit();
            Sdl.SDL_QuitSubSystem(Sdl.SDL_INIT_AUDIO);

            return;
        }

        SdlMixer.Mix_AllocateChannels(CHANNEL_COUNT);
        SdlMixer.Mix_VolumeMusic(MusicVolumeValue);

        var cb = Marshal.GetFunctionPointerForDelegate(ChannelFinishedDelegate);
        SdlMixer.Mix_ChannelFinished(cb);

        Initialized = true;
    }

    private static nint LoadChunk(int soundId)
    {
        if (!DatArchives.Legend.TryGetValue($"{soundId}.mp3", out var entry))
            return nint.Zero;

        byte[] bytes;

        try
        {
            using var archiveStream = entry.ToStreamSegment();
            using var ms = new MemoryStream();
            archiveStream.CopyTo(ms);
            bytes = ms.ToArray();
        } catch
        {
            return nint.Zero;
        }

        return LoadChunkFromBytes(bytes);
    }

    private static nint LoadChunkFromBytes(byte[] bytes)
    {
        //pin the managed byte[] so SDL can read it during Mix_LoadWAV_RW; Mix_LoadWAV_RW decodes the whole file
        //to PCM synchronously before returning, after which the buffer can be unpinned and freed
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);

        try
        {
            var rw = Sdl.SDL_RWFromConstMem(handle.AddrOfPinnedObject(), bytes.Length);

            if (rw == nint.Zero)
                return nint.Zero;

            //freesrc=1 asks SDL to close the RWops for us after the load completes
            return SdlMixer.Mix_LoadWAV_RW(rw, 1);
        } finally
        {
            handle.Free();
        }
    }

    private void OnChannelFinished(int channel)
        //called on the SDL audio thread — keep this to a lock-free enqueue so we never touch the tracking dicts
        //from anywhere except the game-loop Update
        => FinishedChannels.Enqueue(channel);

    private void StartMusic(int musicId)
    {
        if (musicId == 0)
            return;

        var path = Path.Combine(DataContext.DataPath, "music", $"{musicId}.mus");

        if (!File.Exists(path))
            return;

        var handle = SdlMixer.Mix_LoadMUS(path);

        if (handle == nint.Zero)
            return;

        CurrentMusicPtr = handle;
        CurrentMusicId = musicId;

        //Mix_FadeInMusic ramps from silence up to the current Mix_VolumeMusic over ms milliseconds
        SdlMixer.Mix_FadeInMusic(handle, -1, MUSIC_FADE_MS);
    }
}
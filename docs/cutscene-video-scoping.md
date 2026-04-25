# Cutscene Video Playback — Scoping

Scope for adding modern video playback to Chaos.Client for cutscenes (intros, in-game story beats, expansion teasers). Vanilla Dark Ages has no cutscenes Hybrasyl wants to keep — the one Bink asset (Nexon intro logo) is Nexon branding and out of scope. This is a greenfield modern pipeline, not a legacy-format port.

## Problem

- The client has zero video support today: no decoder, no codec dependency, no playback path. Searches for `bink`/`.bik`/`video`/`cutscene` come up empty across `Chaos.Client` and DALib.
- MonoGame.DesktopGL does not bundle a video player. The `Microsoft.Xna.Framework.Media.VideoPlayer` API exists in some MonoGame backends but is not implemented for DesktopGL — relying on it is a dead end.
- Hybrasyl's content roadmap wants cutscenes for new-player intros, region transitions, and event hooks. A flexible video pipeline now beats bolting one on later.
- AGPL-3.0-or-later licensing constrains the codec/decoder choices: anything we link must be license-compatible, and proprietary SDKs (RAD/Bink, Apple ProRes, Microsoft Media Foundation per-platform) are off the table. Vanilla Dark Ages's one Bink asset (Nexon intro) would be transcoded once offline if ever needed — never decoded at runtime.

## Goals

1. **Format that's modern, royalty-free, and AGPL-compatible.** Pick once; don't paint into a corner.
2. **A decoder we can ship cross-platform.** Same binary path on Windows / Linux / macOS, no platform-specific media frameworks.
3. **A playback surface that fits the existing UI patterns.** A popup over the world viewport for in-world triggers; a screen on the stack for pre-world contexts. Same conventions as existing popups and screens.
4. **Server-triggerable.** A new opcode in the 0xFF custom range lets the server play a cutscene at any narrative beat, with optional preload hints to mask first-frame decode latency.
5. **Distributable via `.datf` packs.** Cutscenes ship as a new `content_type` so the existing pack pipeline carries the assets — no parallel content tree.

Non-goals for v1:

- Subtitles / closed captions. Ship without; revisit when there's actual narrated content to caption.
- In-cutscene branching or interactive overlays. v1 is linear playback with skip-only interaction.
- Real-time effects on top of the video (post-process shaders, transition wipes between scenes). Author the effects into the video.
- Frame-perfect server sync. Cutscenes block the world packet stream — the server pauses gameplay when it triggers one.

## Format choice — WebM (VP9 + Opus)

**Recommendation: WebM container, VP9 video track, Opus audio track.**

| Candidate | Verdict | Why |
|---|---|---|
| **WebM (VP9 + Opus)** | ✅ Pick | Royalty-free, mature, excellent compression at 640×480, decoder ecosystem is BSD/LGPL, every authoring tool exports it (FFmpeg, Premiere, DaVinci, OBS). |
| AV1 in WebM/MKV | Defer | Better compression than VP9, but software decode is heavier (~3–5× CPU at our resolution) and hardware decode is uneven on older client hardware. Revisit in 2–3 years. |
| H.264 in MP4 | ❌ Reject | MPEG-LA patent pool — bulk of essential patents have expired (2013–2027) but the AGPL-vs-patents story is messier than just picking a clean royalty-free codec. No upside vs VP9. |
| Theora (Ogg) | ❌ Reject | Open, but visibly worse compression than VP9 at the same bitrate, and the codec is effectively EOL. |
| Bink / Smacker | ❌ Reject | Proprietary RAD SDK, per-title licensing fees, can't ship under AGPL. |

**Authoring guidance** (deferred to a separate authoring guide once Phase 1 lands): VP9 CRF 30–35 at 30fps, Opus stereo at 96 kbps, source resolution authored at 1280×960 or 1920×1440 (2×/3× of 640×480) with point-filter downscale at playback for the pixel-art aesthetic. Or author at native 640×480 if a cutscene is meant to feel period-accurate.

## Decoder choice — FFmpeg via FFmpeg.AutoGen

**Recommendation: FFmpeg (LGPL build) consumed through [FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen) bindings, native binaries shipped alongside the client.**

- LGPL is AGPL-compatible (FFmpeg's own LGPL build, no `--enable-gpl` flags required for our codec set — VP9, Opus, and WebM demuxing are all LGPL-clean).
- Cross-platform: same `libavcodec`/`libavformat`/`libavutil` shared libs work on Windows / Linux / macOS. FFmpeg.AutoGen exposes the C API as idiomatic C# without reinventing P/Invoke.
- Decoder produces YUV420 frames; we colour-convert on the GPU (cheap) or CPU (also cheap at 640×480) to RGBA8888 and upload as `Texture2D` via the existing pipeline.
- Decode runs on a worker thread; the game loop polls a frame queue and uploads the next-due frame each `Update`. No need for stalling the main thread.

**Alternatives considered:**

- **libvpx + libwebm directly** — leaner dependency, but we'd have to demux WebM ourselves and hand-roll the audio decode path. FFmpeg gives us a unified container/codec story for ~30 MB more shipped.
- **Native MonoGame.VideoPlayer** — DesktopGL implementation is incomplete. Non-starter.
- **MediaFoundation (Windows) + AVFoundation (macOS) + GStreamer (Linux)** — three platforms, three APIs, three test surfaces. Hard pass for a feature this small.
- **Pure C# decoder** — none exist for VP9 at production quality. Not seriously considered.

**Distribution:** ship `avcodec`/`avformat`/`avutil`/`swscale`/`swresample` shared libs in the client's runtime folder (similar to how SDL2 ships today). One-time build chore per platform; FFmpeg's release builds are reusable.

## Playback architecture

### Playback surface — popup over the world viewport, not full-screen takeover

Cutscenes draw as a **popup framed over the world render area**, not a full-screen takeover. The surrounding HUD (chat, panels, hotbar, stats) stays visible and rendered; the cutscene popup occupies the iso-map viewport bounds. This keeps the player oriented in their session — the cutscene feels like an event happening in the world, not a context switch out of the game.

Two surface modes:

1. **`CutscenePopupControl`** — a `UIElement` pushed as a child of `WorldScreen.Root`, positioned + sized to match the world viewport rect. Used for in-world cutscene triggers (server `PlayCutscene` opcode, region transitions, story beats). World rendering is hidden behind the popup; HUD remains live but pointer/keyboard input is filtered (Esc still works for skip when allowed). Pushed via `InputDispatcher.Instance.PushControl(this)` per the existing popup convention in [CLAUDE.md](../CLAUDE.md).
2. **`CutsceneScreen : IScreen`** — full-screen takeover for pre-world contexts (future title intros, expansion teasers triggered before login). Sits on the `ScreenManager` stack with no underlying world. Draws on a black background centered to the client area at the source's native pixel size, point-filtered.

Both surfaces share the same playback core (`VideoPlayer`, `VideoSource`, audio hook) and skip/pause behavior. Only the host differs — popup vs screen. The constructor of each takes a `VideoSource` (abstraction so tests can supply a fake) and an options bag (`Skippable`, `OnComplete`, audio routing).

Sizing within the surface: source video is point-filtered scaled to the surface bounds preserving aspect ratio. A 4:3 source fills a 4:3 viewport; mismatched aspects letterbox/pillarbox **inside** the popup frame, not outside it. Authors should target 4:3 to match the world viewport unless a cutscene is intentionally cinematic and accepts black bars.

### Decoder pipeline

Folded into `Chaos.Client.Rendering` (the surface is small enough not to warrant a new project). Single new namespace:

- `VideoSource` — opens a `.webm` byte stream, owns the FFmpeg context, exposes `TryGetNextVideoFrame(out FrameRgba)` and `TryGetNextAudioFrame(out PcmBuffer)`.
- `VideoPlayer` — orchestration: worker thread pumps `VideoSource`, ring-buffers frames, fires events for "next video frame ready" and "audio buffer ready". Owns the playback clock and decides which video frame is current for a given `GameTime`.
- `VideoTextureUploader` — converts a decoded YUV420 frame to RGBA and `SetData`s into a reusable `Texture2D` (one texture allocated at start, reused every frame — same pattern as renderer caches).

### Audio routing

Cutscene audio decodes through FFmpeg into PCM. Two integration options:

1. **Custom Mix_Music hook** — register a `Mix_HookMusic` callback that pulls PCM from the cutscene's audio queue. Shares the same SDL2_mixer pipeline as in-game music, so fade-out works for free. Music currently playing is suspended on `Mix_HookMusic` registration.
2. **Pre-decode to PCM and play via Mix_PlayChannel** — simpler, fits short cutscenes (under ~30s). Trade memory for code simplicity.

**Recommendation: option 1.** It's only ~50 LOC more and works for arbitrary cutscene length.

**Volume routing:** new `ClientSettings.CutsceneVolume` slider, independent of `MusicVolume` and `SoundVolume`. The mixer applies the slider's gain to cutscene PCM in the `Mix_HookMusic` callback before it reaches SDL2_mixer's master mix. Settings UI gets a third row in the audio section alongside Sound and Music. Persisted via the same `ClientSettings` mechanism.

### Rendering pass

- `CutsceneScreen.Draw` runs in its own `SpriteBatch.Begin/End` pass (per `IScreen` contract). `CutscenePopupControl.Draw` runs inside `WorldScreen`'s UI overlay pass alongside other popups.
- Point filter for pixel-art aesthetic (matches existing `GlobalSettings.Sampler = PointClamp`). Authors shipping high-res sources accept the downscale.
- Skip indicator (small "Press Esc to skip" prompt) drawn in the surface's bottom-right corner if `Skippable` is true. Fades out 2s after the cutscene starts so it doesn't pollute the frame.
- A subtle frame border or fade-to-edge mask around the popup is at the artist's discretion via authoring; the renderer doesn't impose chrome.

## Asset pack integration — new `cutscenes` content type

Cutscenes ship via `.datf` packs, registering as a new `content_type` in [AssetPackRegistry](../Chaos.Client.Data/AssetPacks/AssetPackRegistry.cs). No legacy fallback — there are no legacy cutscenes — so the lookup just returns a pack-or-null and the trigger flow is a no-op when no pack exists.

### Pack structure

```
hyb-cutscenes.datf
├── _manifest.json
├── cutscene_0001.webm       # intro on first launch
├── cutscene_0002.webm       # mileth_arrival narrative beat
├── cutscene_0042.webm
└── cutscene_0099.webm
```

### Manifest

```json
{
  "schema_version": 1,
  "pack_id": "hybrasyl-cutscenes",
  "pack_version": "0.1.0",
  "content_type": "cutscenes",
  "priority": 100,
  "covers": {
    "cutscenes": { "container": "webm", "video_codec": "vp9", "audio_codec": "opus" }
  }
}
```

`covers.cutscenes` advertises codec choice for client-side validation — mismatched packs reject at registration with a `[asset-pack]` warning, same path as today's manifest validation.

### Naming convention

`cutscene_{id:D4}.webm` — same 4-digit-zero-padded ID convention as ability icons / nation badges. Cutscene ID space starts at 1; ID 0 reserved for "no cutscene." IDs are server-allocated; pack authors coordinate with the server-side registry.

### `AssetPackRegistry` accessor

```csharp
public static CutscenePack? GetCutscenePack() => CurrentCutscenePack;
```

`CutscenePack` exposes `TryOpenStream(int cutsceneId, out Stream? stream)` — returns the ZIP entry stream for the requested ID, or null if absent. `VideoSource` consumes the stream directly; FFmpeg can demux from any seekable stream.

## Server triggering — new opcode 0xFF range

New Hybrasyl modernization opcodes start at 0xFF and work down. Cutscene triggering allocates one:

- **`PlayCutscene` (server → client)** — payload: `ushort cutsceneId`, `byte flags` (bit 0 = skippable, bit 1 = block input on dismiss until server says go, bit 2 = preload-only / don't start playback yet), `string displayName` (length-prefixed; shown in the Options → Cutscenes replay list).
- **`CutsceneFinished` (client → server)** — payload: `ushort cutsceneId`, `byte reason` (0 = completed, 1 = skipped, 2 = error, 3 = preload acknowledged). Lets the server resume gameplay, chain the next narrative beat, or confirm a preload hint landed.

Both opcodes registered in [ConnectionManager](../Chaos.Client.Networking/ConnectionManager.cs)'s array dispatch (see [CLAUDE.md](../CLAUDE.md) "Adding a handler"). Client-local replay (Options → Cutscenes) bypasses the network and reuses the playback machinery directly with the cached display name and ID.

## Loading & buffering — scripting-driven preload + spinner fallback

First-frame decode latency is small (~50–200 ms on modern hardware) but visible if a cutscene is triggered cold. Two complementary strategies:

1. **Scripting-driven preload (preferred).** The server can issue `PlayCutscene` with the preload-only flag ahead of the actual trigger — e.g., when the player enters a region whose dialog tree might fire a cutscene, the server preloads it while the player walks. The client opens the `.datf` entry, primes the FFmpeg context, and decodes the first ~10 frames into the ring buffer. When the real `PlayCutscene` fires, playback starts on the next frame with zero perceived latency.
2. **Spinner over black background (fallback).** When a cutscene is triggered without preload (cold trigger, or preload hint dropped), the popup paints black with a small spinner overlay until the first decoded frame is ready. Caps at ~500 ms before either showing the frame or aborting with a `[cutscene]` warning.

Preload state is bounded — at most one cutscene preloaded at a time. If the server hints a second preload, the first is discarded. Memory cost (one FFmpeg context + ~10 RGBA frames) is ~15 MB max.

## Replay — Options → Cutscenes

Players can re-watch cutscenes they've already seen via a new entry in the Options panel (Shift+F4 / `MainOptionsControl`). Concrete shape:

- New `CutscenesListControl` popup (peer of `MacrosListControl`, `FriendsListControl`, etc. in [Popups/Options/](../Chaos.Client/Popups/Options/)).
- List populated from a "seen cutscene IDs" persistent set in [ClientSettings](../Chaos.Client/Systems/ClientSettings.cs). Each entry shows the cutscene's display name (provided by the server in `PlayCutscene` payload — see opcode section) and the date first seen.
- Selecting an entry triggers a client-local replay through the same `CutscenePopupControl` machinery. No server round-trip; replay is always `Skippable=true` and never blocking.
- "Seen" state grows as the server triggers new cutscenes during play. No retroactive seeding from packs — players only see entries for cutscenes they've actually witnessed in-session.

Cheap to add (~150 LOC for the list control + persistence) and gives the cinematic content lasting value beyond first-watch. Included in v1 rather than deferred.

## User controls

- **Esc** — skip (only when `Skippable` flag is set; otherwise consumed and ignored).
- **Window focus loss** — pause both video and audio; resume on focus return. Reuses MonoGame's `Game.IsActive` polling pattern.
- **Volume** — routes through new `ClientSettings.CutsceneVolume` slider. Independent of music/sound sliders.
- **No mouse interaction inside the popup.** Clicks fall through to the popup's frame chrome only (skip-indicator hover, etc.); cursor remains the default pointer.

## Performance & resource budget

- **Decode CPU:** VP9 at 640×480 / 30fps is ~1–3 ms per frame on a single modern core. Worker thread, well within budget.
- **GPU upload:** one `Texture2D.SetData` per frame at 640×480 RGBA = ~1.2 MB/frame, ~36 MB/s at 30fps. Trivial.
- **Memory:** one decoded RGBA frame buffer (~1.2 MB) + one in-flight texture + 3–5 frame ring buffer = under 10 MB resident during a cutscene. Cleared on `UnloadContent`.
- **Disk:** VP9 CRF 30 at 640×480 is ~500 KB–2 MB per minute of cutscene. A 60s intro fits easily in a `.datf` pack.
- **Startup cost:** none. FFmpeg native libs load lazily on first cutscene; no impact on cold-start time for users who never see a cutscene.

## Rollout phases

Each phase ends with a bug/regression review + architecture review per [CLAUDE.md](../CLAUDE.md) review policy before the next phase starts.

**Phase 1 — FFmpeg integration & decoder smoke test (~1 week).** Add `FFmpeg.AutoGen` package, ship native libs in the runtime folder, build a CLI smoke test that decodes a bundled `.webm` and dumps frame count / first-frame PNG. No game integration. Verifies licensing, distribution, and decoder happy path before any client work.

**Phase 2 — `VideoSource` + `VideoPlayer` core (~1 week).** Decoder/player abstractions, frame ring buffer, audio PCM queue. Unit-tested with a fixture `.webm`. Worker thread orchestration. No screen integration yet.

**Phase 3 — Playback surfaces + `SoundSystem` audio hook (~1 week).** `CutscenePopupControl` (in-world popup over the viewport) and `CutsceneScreen` (pre-world full-screen) on top of the shared playback core. New `ClientSettings.CutsceneVolume` slider wired into `Mix_HookMusic`. Pilot: client-local cutscene triggered from a debug command in both surfaces. Verifies playback end-to-end.

**Phase 4 — `cutscenes` content type + `.datf` integration (~3 days).** New `CutscenePack`, `AssetPackRegistry.GetCutscenePack()`, manifest validation, `_manifest.json` schema docs added to [asset-pack-format.md](asset-pack-format.md). Client now plays cutscenes from a pack rather than a hardcoded resource.

**Phase 5 — Server triggering opcode + preload (~3 days).** `PlayCutscene` / `CutsceneFinished` opcode pair in 0xFF range, including the preload-only flag and display-name field. Server-side hook (separate work in the Hybrasyl server repo) calls into it. Client-side handler in `ConnectionManager` raises an event; `WorldScreen` subscribes and shows the popup. Preload primes the FFmpeg context + initial frames without starting playback.

**Phase 6 — Replay UI (~3 days).** `CutscenesListControl` popup under Options → Cutscenes. "Seen IDs" persistent set in `ClientSettings`. Replay reuses `CutscenePopupControl` with `Skippable=true` and bypasses the server.

**Phase 7 — Final review + authoring guide.** Comprehensive review of the full changeset. Author `cutscene-authoring-guide.md` covering encoding settings, dimension recommendations, audio mixing, the in-world popup viewport target size, and pack creation walkthrough. QA pass on Linux + Mac builds.

## Open questions

- **Pre-world full-screen surface — centered fixed-size or stretched-to-client?** When a cutscene plays before the world exists (e.g., a future Hybrasyl title intro), the popup falls back to `CutsceneScreen` over a black background. Default to centered at the source's native pixel size, or stretch to client bounds with point filter? Leaning centered + native — feels intentional, matches the pixel-art aesthetic.
- **Preload eviction policy.** If the server preloads cutscene A, then preloads B before A plays, A is discarded. Should we instead keep a small LRU cache (2–3 entries, ~45 MB max) so quick A→B→A re-trigger sequences don't redecode? Probably not for v1 — single-slot is simpler and the redecode cost is bounded.
- **Replay list ordering.** Most-recently-seen first, or chronological-first-seen? Will affect feel of the Options panel as the seen list grows.

### Resolved decisions (recorded for the implementation plan that follows this doc)

- Separate `CutsceneVolume` slider in `ClientSettings`. Not folded into `MusicVolume`.
- Loading: scripting-driven preload (preferred) + spinner-over-black fallback.
- Replay UI in scope for v1 via Options → Cutscenes.
- CPU decode only for v1; defer FFmpeg hwaccel until we ship higher-resolution content.
- Cutscene IDs are server-allocated; pack authors coordinate with the server registry.
- One codec only (VP9/Opus/WebM). No GIF/WebP/Theora fallback.

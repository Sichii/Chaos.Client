# Chaos.Client Branch Changelog

Additive log of notable changes organized by branch. Each merged branch gets a new section below; don't rewrite or consolidate past sections. Intended as a quick catch-up doc for teammates joining mid-stream.

## Foundation (pre-branch work on main)

Everything before `feature/tiles` was cut. The client now boots, authenticates against Hybrasyl, and renders the world end-to-end with modern asset support layered over legacy Dark Ages formats.

- **Hybrasyl compatibility layer** — debug logging, protocol quirks, the 3px/3px render offset fix, general `Draw()` and state-tracking cleanup.
- **Upstream DALib alignment** — repo now references `hybrasyl/dalib` as upstream. `dalib-sichii` is the staging fork that merges back to it.
- **Modern asset pipeline (`.datf`)** — ZIP+manifest container registry (`AssetPackRegistry`), pluggable `content_type` entries, modern dimensions/density for non-native assets.
- **Ability icons pilot** — first concrete use of `.datf`: modern 32×32 PNG skill/spell icons override legacy 31×31 EPF frames via the `IconPack` content type.
- **Repo & identity** — repository moved to `hybrasyl/Chaos.Client`; package `RepositoryUrl` updated.

## feature/tiles (merged 2026-04-19)

### BMFont / TTF font pipeline

Adds [BmFont.cs](../Chaos.Client.Rendering/BmFont.cs), [TgaLoader.cs](../Chaos.Client.Rendering/TgaLoader.cs), and [FontAtlas.cs](../Chaos.Client.Rendering/FontAtlas.cs) glyph-atlas support so BMFont/TTF files can back `TextRenderer` alongside the legacy bitmap font. Foundation for later text-rendering modernization; see [font-modernization-findings.md](font-modernization-findings.md).

### Center-screen pause menu (Escape key)

- New [PauseMenuControl](../Chaos.Client/Controls/World/Popups/Options/PauseMenuControl.cs) replaces the deleted `MainOptionsControl` and the HUD's `BTN_OPTION` button (dropped from both `WorldHudControl` and `LargeWorldHudControl` plus the `IWorldHud` interface).
- Escape opens the menu when the `InputDispatcher` control stack is empty; cancel-targeting still takes priority. Q hotkey still toggles.
- Panel centered in `WorldHud.ViewportBounds` (re-centers on HUD layout swap) with a 2×2 action grid — Friends / Macros / Settings / Exit Game — plus a Close button. Clicking a sub-action hides the menu so the submenu takes over; user presses Escape again to reopen.
- Sound/music sliders preserved, same wiring as before.
- See [pause-menu-scoping.md](pause-menu-scoping.md) for the scoping that drove the implementation.

### Friends list fixes

- Slots are now editable `UITextBox`es; were display-only `UILabel`s.
- Entries fill slots sequentially across both columns (left 1-10, right 11-20). Previously split by `IsOnline` which pushed every locally-loaded entry (always `IsOnline=false`) into the right column starting at slot 11.
- Row stride set to 21px to match the prefab's wooden slot lines.
- Tab cycles between slots.

### Macros list polish

- Removed `FocusedBackgroundColor` so focused macros no longer paint a solid black rectangle behind the text.
- Added `IsTabStop = true` for Tab cycling, matching the friends list.

### Scoping docs (not yet implemented)

Team-visible design docs for upcoming work:

- [ground-layer-png-scoping.md](ground-layer-png-scoping.md) — single-PNG ground layer as a `.datf` override, refusing animated maps for v1.
- [outside-map-rendering-scoping.md](outside-map-rendering-scoping.md) — border PNG + server-pushed adjacent-map grid to replace black space at map edges; layered fallback.
- [pause-menu-scoping.md](pause-menu-scoping.md) — the scoping doc that produced the pause menu implementation above.

---

*To add a new branch section: copy the `feature/tiles` heading with its merge date, then summarize what that branch delivered under focused subheadings. Keep entries catch-up-friendly — file links and one-paragraph summaries, not full diffs.*

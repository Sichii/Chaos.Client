# Ground Layer as a Single PNG — Scoping Answer

**Nature of this doc:** This is a *scoping answer to "what would it take"*, not a commitment to build. It maps the integration surface, identifies the real cost centers, and surfaces the decisions that would need to be made if/when this is prioritized.

## Context

Today, the ground (background) layer of every map is rendered tile-by-tile: DALib `Tileset` → `Graphics.RenderTile()` → `SKImage` → `TextureConverter.ToTexture2D()` → either per-tile `Texture2D` cache or a grid-packed `TextureAtlas`, then one `spriteBatch.Draw` per visible tile. A single-PNG ground would replace the per-tile path with one pre-composited image blitted in a single draw call.

This aligns with three pre-recorded directions:

- Unified asset format direction — move off HPF/EPF/MPF toward ZIP+manifest image containers.
- Additive modernization pattern — add new path alongside legacy; never rewrite.
- `.datf` asset-pack system already exists (`IconPack`, `NationBadgePack`) and was designed to grow with new content types.

## Scope of Work

### 1. Data layer — new asset-pack content type

- New `GroundPack` alongside [IconPack.cs](../Chaos.Client.Data/AssetPacks/IconPack.cs), same `ZipArchive`-per-pack + case-insensitive entry lookup + decode-failure-as-miss discipline.
- Extend `AssetPackRegistry.RegisterByContentType()` switch at [AssetPackRegistry.cs:106](../Chaos.Client.Data/AssetPacks/AssetPackRegistry.cs#L106) with a new content_type; add typed accessor `GetGroundPack()`.
- Format spec addition in [asset-pack-format.md](asset-pack-format.md) covering: entry naming, required dimensions formula, world-origin convention, and the animated-map refusal rule (below).

### 2. Rendering layer — single-texture path

- In [MapRenderer.cs:145](../Chaos.Client.Rendering/MapRenderer.cs#L145) `DrawBackground`: before the visible-tile loop, check for a loaded `GroundTexture`. If present, blit the whole PNG in one `spriteBatch.Draw`, source-rect clipped against the camera's visible world rect, then return.
- In [MapRenderer.cs:392](../Chaos.Client.Rendering/MapRenderer.cs#L392) `PreloadMapTiles`: if pack has a ground image for this map, decode → `TextureConverter.ToTexture2D()` → cache as `GroundTexture`. Skip `BuildBgAtlas` for background on this map (foreground atlas still built). If no override, behavior unchanged.
- `GroundTexture` disposed with the existing `MapRenderer` dispose path at [WorldScreen.Map.cs:50](../Chaos.Client/Screens/WorldScreen.Map.cs#L50).
- Legacy per-tile path remains the default — zero behavior change for maps without a pack entry.

### 3. Animated-tile gate (per decision)

- On pack load, `GroundPack` scans the `MapFile` for any tile ID that `ResolveAnimatedTileId` would mutate ([MapRenderer.cs:162](../Chaos.Client.Rendering/MapRenderer.cs#L162)). If any animated ID is present, the override is refused with a warning and the map falls back to the legacy path.
- Keeps both paths clean. Limits coverage to fully-static maps until the animation story is designed.

### 4. Isometric math (already verified during exploration)

- PNG dimensions for a map with W columns × H rows: `(W+H) * 28` wide × `(W+H) * 14` tall.
- Tile (0,0) sits at world pixel `((H-1)*28, 0)`; this is the natural origin the authored PNG must match (via `Camera.TileToWorld` at [Camera.cs:159](../Chaos.Client.Rendering/Camera.cs#L159)).
- These go in the format spec so authors can bake deterministically.

### 5. Size limits (documented, not solved)

- MonoGame's `Texture2D` safe ceiling is 8192 on a lot of hardware, 16384 on modern. Very large maps (~255×255 → 14280×7140) risk exceeding the conservative limit.
- Format spec documents a recommended max dimension; `GroundPack` logs + refuses packs beyond a configurable cap and falls back. No chunking support in v1.

## Critical Files (if built)

- [AssetPackRegistry.cs](../Chaos.Client.Data/AssetPacks/AssetPackRegistry.cs) — switch extension, typed accessor
- [IconPack.cs](../Chaos.Client.Data/AssetPacks/IconPack.cs) — pattern to mirror for `GroundPack`
- [MapRenderer.cs](../Chaos.Client.Rendering/MapRenderer.cs) — `PreloadMapTiles` + `DrawBackground` branching
- [TextureConverter.cs](../Chaos.Client.Rendering/TextureConverter.cs) — SKImage → Texture2D reuse, no changes expected
- [asset-pack-format.md](asset-pack-format.md) — spec addition

## What This Doesn't Touch

- Foreground tiles, entities, effects, lighting, weather — all unaffected.
- The `.map` file format — server still sends tile indices; only the rendered image is replaced.
- `PaletteCyclingManager` — not changed. Animation on overridden maps is handled by *refusing to override animated maps*, not by reworking the cycler.
- `TabMapRenderer` mini-map — continues to use the legacy tileset path; mini-map generation is orthogonal.

## Open Decisions (deferred)

These don't need to be answered to scope the work, but would need answers before implementation:

- **Pack scope** (per-map entries in one `.datf` vs. one `.datf` per map) — either works; `IconPack` precedent favors one pack with many entries.
- **Animation story** — when override-on-animated-maps is needed, the simplest next step is a sparse animation-mask layer stamped per-frame, matching the unified asset format direction.
- **Very large maps** — chunked PNGs are the obvious next step if 14K-wide textures become a real need.

## Verification (when built)

- Author a test `.datf` by stitching tiles via `DALib.Graphics.RenderTile()` at the computed offsets for one static map, then pack as a PNG.
- Load into that map and a non-overridden map; confirm the overridden one draws via the single-texture path (verify via debug counters or a breakpoint) and the non-overridden one is byte-identical to before.
- Spot-check an intentionally-animated map is correctly refused and falls back.

## Review Gates

Per CLAUDE.md — if/when this moves from scoping to implementation, it needs phase-level bug/regression + architecture/design review after each milestone, and a final review at the end. Not applicable while this remains a scoping doc.

## Bottom Line

**The work is small but touches three layers:** one new pack class (~100 lines mirroring `IconPack`), one registry switch case + accessor, one branch + early-return in `MapRenderer.DrawBackground`, one pre-decode hook in `PreloadMapTiles`, and a spec update. The real cost isn't the code — it's the asset-authoring pipeline (how artists bake PNGs at the correct dimensions/origin) and the deferred animation story. The additive pattern means legacy keeps working and the override is opt-in per map.

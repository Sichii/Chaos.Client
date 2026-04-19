# Outside-Map Rendering (Border PNG + Neighbor Grid) — Scoping Answer

**Nature of this doc:** Scoping answer to *"what would it take to fill the blackspace outside the current map?"* — not a build commitment. Covers two layered features that together replace the current black-fill with a richer boundary.

## Context

Today everything outside the current map's isometric diamond is black — `MapRenderer.DrawBackground` only iterates tiles within `mapFile.Width × mapFile.Height`, and the `Camera.GetVisibleTileBounds` clamp at [Camera.cs](../Chaos.Client.Rendering/Camera.cs) prevents any draw outside those bounds. Two layered features together replace that:

**(a) Border PNG** — a pre-rendered image filling a ~20-tile-wide diamond skirt around the map. Always present as a last-resort fill.

**(b) Neighbor grid** — server-declared adjacency (NW/N/NE/W/E/SW/S/SE) that loads neighbor `MapFile`s and renders their **ground + foreground** layers in the appropriate offset. Declared neighbors override the border PNG in their direction; undeclared directions keep the PNG.

This aligns with:

- Open-world + mounts roadmap — neighbor-grid rendering is the first visible step toward open-world; cosmetically hides the hard map boundary that fluid movement would otherwise collide with.
- Additive modernization pattern — new opcode for the neighbor grid, client falls back cleanly when server doesn't send it.
- [ground-layer-png-scoping.md](ground-layer-png-scoping.md) — the border PNG reuses the same `.datf` extension shape.

## Scope of Work

### 1. Data layer — asset additions

- Extend the planned `GroundPack` (see [ground-layer-png-scoping.md](ground-layer-png-scoping.md)) with a second entry kind: `border_{mapId:D5}.png`. Same pack, optional per map.
- **Border PNG dimensions** for a map W × H: `(W + H + 40) * 28` wide × `(W + H + 40) * 14` tall (20-tile skirt on each of the four diamond edges). Document in [asset-pack-format.md](asset-pack-format.md).
- Alternative: a small set of *generic* border PNGs keyed by biome (forest / field / cave / ocean), referenced from a new per-map metadata field. Less artist burden, reusable. Pick at implementation time.

### 2. Networking — neighbor grid opcode

- New additive server opcode `MapNeighbors` (or equivalent), Hybrasyl-only. Payload: 8 slots (NW/N/NE/W/E/SW/S/SE), each either `(ushort mapId, byte width, byte height)` or empty.
- Pushed immediately after `MapInfo` ([ConnectionManager.cs:1478](../Chaos.Client.Networking/ConnectionManager.cs#L1478)) when the server supports it. Client treats absence as "no neighbors" (border PNG only).
- Widths/heights are sent eagerly so the client can size frustum bounds before it finishes loading the neighbor's `.map` file from disk.
- New `ConnectionManager` event `MapNeighborsReceived` consumed by `WorldScreen.Map.cs`.

### 3. Rendering layer — three new draw passes

Insert into [MapRenderer.cs](../Chaos.Client.Rendering/MapRenderer.cs) in this order, before the existing `DrawBackground` at [line 145](../Chaos.Client.Rendering/MapRenderer.cs#L145):

1. **`DrawBorderBackground`** — single-texture blit if border PNG loaded. Positioned so the map-center aligns with the PNG center, or (cheaper) its top-left at world `((H-1-20)*28, -20*14)` when tile (0,0) sits at `((H-1)*28, 0)`.
2. **`DrawNeighborGrounds`** — for each loaded neighbor: run the same tile-iteration as `DrawBackground` but on the *neighbor's* `MapFile.Tiles` with its own `MapRenderer`-style bg cache, at an offset. Frustum-cull against the camera in neighbor-local coords by inversing the offset.
3. **`DrawNeighborForegrounds`** — flat foreground pass (no entity interleave). Uses existing [DrawForegroundTile at line 209](../Chaos.Client.Rendering/MapRenderer.cs#L209). Boundary seam at current-map edge is acceptable for v1 — entities on current map will still draw *over* neighbor foreground because neighbor pass runs before current-map's diagonal-stripe pass in [WorldScreen.Draw.cs](../Chaos.Client/Screens/WorldScreen.Draw.cs).

Existing passes (current background / current foreground+entities / overlays) unchanged.

### 4. Neighbor map lifecycle

- Store neighbor state in a new `NeighborMaps` struct inside `WorldScreen` (8 slots, mirroring the opcode layout).
- On `MapNeighborsReceived`: async-load each declared neighbor's `MapFile` via `MapFileRepository.GetMapFile(mapId)`, then kick a `PreloadMapTiles`-equivalent on a lightweight neighbor renderer. Lazy per-neighbor — a distant neighbor off-screen doesn't need eager GPU upload, though first pass can just preload all eight.
- On map change (back to [WorldScreen.Map.cs:50](../Chaos.Client/Screens/WorldScreen.Map.cs#L50)): dispose all eight neighbor renderers alongside the current one.
- **Cache pressure** is the main worry. Eight neighbors each with their own bg atlas = ~9× texture-memory. Mitigations: share the bg `TextureAtlas` across neighbors of the same tileset variant (most maps share "tilea"); don't build fg atlases for neighbors (use per-tile fallback path since fg draw counts will be low at boundary).

### 5. Camera / frustum extension

- [Camera.GetVisibleTileBounds](../Chaos.Client.Rendering/Camera.cs) currently clamps to the current map's `[0..W-1, 0..H-1]`. Needs an overload that accepts an extended min/max in current-map coord space (computed from declared neighbors + border skirt) so the frustum cull includes extended tiles.
- Alternative: each neighbor pass computes its own visible bounds in its own coord space by inversing the offset — keeps `Camera` untouched. Probably cleaner.

### 6. Offset math (current map W × H, neighbor Wn × Hn)

Neighbor's tile (0, 0) renders at world offset `(Δx, Δy)` in current's coord space, where `(dx, dy)` is the tile-grid offset to neighbor's origin:

- NW: `dx = -Wn`, `dy = -Hn`
- N:  `dx = 0`,   `dy = -Hn`
- NE: `dx = +W`,  `dy = -Hn`
- W:  `dx = -Wn`, `dy = 0`
- E:  `dx = +W`,  `dy = 0`
- SW: `dx = -Wn`, `dy = +H`
- S:  `dx = 0`,   `dy = +H`
- SE: `dx = +W`,  `dy = +H`

World pixel offset = `((H - Hn + dx - dy) * 28, (dx + dy) * 14)`. Derivation in `Camera.TileToWorld` at [Camera.cs:159](../Chaos.Client.Rendering/Camera.cs#L159).

Compass↔grid mapping is a convention choice; verify against server expectations before wiring.

## Critical Files (if built)

- [AssetPackRegistry.cs](../Chaos.Client.Data/AssetPacks/AssetPackRegistry.cs) — register `border_*.png` entries in `GroundPack`
- [MapRenderer.cs](../Chaos.Client.Rendering/MapRenderer.cs) — three new draw passes + neighbor-renderer hosting
- [Camera.cs](../Chaos.Client.Rendering/Camera.cs) — (maybe) extended-bounds overload
- [ConnectionManager.cs:1478](../Chaos.Client.Networking/ConnectionManager.cs#L1478) — MapInfo handler region; new `HandleMapNeighbors` + opcode + event
- [WorldScreen.Map.cs](../Chaos.Client/Screens/WorldScreen.Map.cs) — `NeighborMaps` state, load/dispose lifecycle
- [WorldScreen.Draw.cs](../Chaos.Client/Screens/WorldScreen.Draw.cs) — ensure neighbor foreground pass slots in correctly before the current-map diagonal stripe
- [asset-pack-format.md](asset-pack-format.md) — border PNG entry spec

## What This Doesn't Touch

- **Entities on neighbor maps** — not rendered. Neighbors are cosmetic; ViewModel/WorldState remains scoped to the current map.
- **Pathfinding** — the pathfinding grid stays scoped to the current map. Click-to-move on a neighbor tile is rejected client-side (or treated as "walk to edge").
- **Edge crossing / map transition** — still server-driven. When the player walks onto a boundary tile, server sends the existing `MapInfo` + eventual new `MapNeighbors`. Client does not pre-empt.
- **Mini-map / TabMapRenderer** — unchanged; shows current map only.
- **Animated neighbor tiles** — work for free. Neighbors use the tileset path (per-tile textures), so `PaletteCyclingManager` continues to animate their water/flames. No refusal rule needed (unlike the ground-PNG override case).
- **Lighting / darkness / weather** — scoped to current map. Neighbors are flatly lit. Documenting this as intentional; a future pass could extend.

## Open Decisions (deferred)

- **Per-map border PNG vs. biome-keyed shared PNGs** — tradeoff is artist time vs. visual richness. Recommend biome-keyed for v1.
- **Opcode payload shape** — whether width/height ride the `MapNeighbors` opcode or are fetched from each neighbor's `.map` header. Eager width/height lets the client size frustum before disk load completes.
- **Neighbor preload strategy** — eager all-eight on receipt vs. lazy on visibility. Eager is simpler; revisit if memory pressure shows up.
- **Empty-slot fallback** — if a map is isolated (no neighbors declared), does the server omit `MapNeighbors` entirely or send all 8 empty? Affects client's default rendering decision.

## Verification (when built)

- Test map set: a 3×3 grid of small (e.g. 40×40) maps with visually distinct ground tilesets. Author border PNG for each.
- Stand in center map, pan camera toward each of 8 edges — neighbor ground+foreground should be visible and continuous, border PNG visible beyond that in each direction.
- Walk off an edge (server sends `MapInfo` for the new center) — neighbor set recomputes, render shifts.
- Isolated map (no neighbors pushed) — only border PNG visible in blackspace.
- Memory sanity: confirm neighbor texture caches dispose on map change (no leak across transitions).
- Animated-tile regression: neighbor water tile should still shimmer.

## Review Gates

Per CLAUDE.md — if/when implementation starts, phase-level bug/regression + architecture/design review after each milestone (opcode + loader, renderer passes, border PNG integration), and a final review at the end.

## Bottom Line

**Two features at different weights.** The border PNG is a small extension of the ground-PNG scoping doc — one new pack entry, one blit pass, one format-spec update. The neighbor grid is materially larger: a new additive opcode (Hybrasyl-side change), neighbor MapFile loading/caching, two new draw passes, and the first use of **multi-map client state**. The additive pattern keeps legacy black-space rendering intact when neither feature is present. Memory pressure (8× tile caches) and pathfinding-scoping are the two things to watch; entity-scoping is explicitly punted to keep v1 bounded.

# Chair Sitting — Scoping

Allow players to sit in chairs by walking into them. The character's sprite is folded client-side (post-composite crop/shift) rather than requiring new sitting art frames for every armor set.

## Problem

- Existing `RestPosition` poses (0x01–0x03) are cross-legged floor sits, not "seated in a chair"
- Chairs are wall tiles — players can't walk onto them
- Creating dedicated sitting sprite frames for every armor across 50+ species is an art pipeline blocker
- Need a way to visually represent sitting using the existing standing/walking sprite frames

## Interaction model (server-driven)

1. Player walks toward a wall tile whose sprite ID is in a configured "chairs" list
2. Instead of blocking, the server sets `RestPosition.Seated` (new enum value `0x04`) and faces the player toward the chair
3. Player stays on their current tile — no position change
4. **While seated:**
   - Direction keys turn in place (change facing without moving or leaving the chair)
   - Assail key (spacebar) exits the chair — resets to `RestPosition.Standing`
   - Other movement keys just turn, don't stand up
5. Server broadcasts DisplayUser update on sit/stand/turn

## Client rendering — post-composite sprite fold

When `RestPosition == 0x04` (Seated), apply a transform to the fully-assembled character sprite:

### The transform

1. **Composite normally** — body + armor + weapon + accessories, same as standing
2. **Crop** — remove the lower portion of the sprite below a "waist line" Y offset (hides legs)
3. **Shift down** — move the remaining upper body down by a "seat height" value (lowers character into the chair)

### Per-direction rules

Each facing direction needs its own crop/shift parameters since the sprite silhouette differs:

```
Direction   Crop Y (from sprite origin)   Shift Down
---------   ---------------------------   ----------
North       ~60% of sprite height         chair-dependent
South       ~60% of sprite height         chair-dependent
East        ~55% of sprite height         chair-dependent
West        ~55% of sprite height         chair-dependent
```

These are rough starting points — need to be tuned against actual sprite data. The waist line is roughly consistent across armor sets since DA sprites share a common body frame.

### Seat height as metadata

Different chairs have different seat heights:

- Bar stool: shift down 4px
- Standard chair: shift down 8px
- Throne: shift down 6px
- Bench: shift down 10px

This metadata comes from the server's chair tile config and could be sent as part of the seated state, or derived client-side from the tile sprite ID.

## Where to intercept in the rendering pipeline

The transform should happen **after** the existing sprite compositing and **before** the final draw call. Investigation needed:

- Find where body + armor + weapon layers are assembled into a final sprite
- Add a conditional transform pass: if `RestPosition == Seated`, apply crop + shift to the composite result
- The crop could be implemented as a source rectangle adjustment on the final texture draw
- The shift is a Y offset on the draw position

MonoGame's `SpriteBatch.Draw` supports source rectangles natively — cropping the bottom portion of a sprite is just adjusting the source rect height and shifting the destination Y.

## Visual quality considerations

- Pixel art doesn't respond well to rotation/scaling, but **cropping and shifting are pixel-perfect operations** — no aliasing or quality loss
- The "fold" is really just "hide legs, lower torso" — no actual deformation
- Some armors with long skirts/robes may look slightly odd at the crop line, but this is the accepted tradeoff for avoiding hundreds of new sprite frames
- The crop line could be tuned per body style (species variant) if needed, since different species may have different proportions

## Relationship to other systems

- **Species system**: Each species may need a slightly different crop Y offset if body proportions vary significantly. Start with one universal value, tune per-species later if needed.
- **RestPosition enum**: Server adds `Seated = 0x04`. Stock DA client ignores unknown values. Chaos.Client handles it.
- **stats-display-direction.md**: The seated state is a display concern, not a stat concern — no interaction with the extended stats panel.

## Server changes needed (Hybrasyl)

Small set of changes in the server walk/direction/assail handlers:

| Change | File | Scope |
|--------|------|-------|
| Add `Seated = 0x04` to RestPosition enum | `Internals/Enums/RestPosition.cs` | 1 line |
| Walk handler: detect chair wall tile, set Seated | `Objects/User.cs` walk method | ~10 lines |
| Direction handler: allow turn-in-place while Seated | `Objects/User.cs` or direction handler | ~5 lines |
| Assail handler: if Seated, stand up instead of assail | Assail handler | ~5 lines |
| Config: chair tile sprite ID list | ServerConfig XML | New config section |

## Client prerequisites

Three decisions must land before client-side implementation begins. Each is an upstream choice (protocol or server config), not a rendering choice.

### 1. Chair tile discovery — how does the client know a tile is a chair?

The client pre-rejects walks into wall tiles in `WorldScreen.Map.cs` `IsTilePassable()` before a `Walk` packet is ever sent. Without a signal, the server never sees the walk attempt and can never switch the player into `Seated`. Pick one:

- **(A) Server push on map load** — server sends chair sprite IDs with the map; client adds a `ChairTiles.Contains(spriteId)` exception to `IsTilePassable()`. Preferred.
- **(B) Tile metadata flag** — mark chairs in SOTP or a parallel table so client reads directly from DALib data. Requires authoring-tool changes.
- **(C) Walk unconditionally into walls** — simplest client change, worst network citizenship. Not recommended.

### 2. `RestPosition.Seated = 0x04` — where does the enum live?

`RestPosition` comes from the `Chaos.DarkAges` NuGet package (Sichii-owned), not this repo. Pick one:

- **Upstream the new value** into `Chaos.DarkAges` via PR. Cleanest; requires coordination.
- **Handle by byte compare client-side** (`(byte)entity.RestPosition == 4`) and skip the named constant. Works today, drifts from upstream.

### 3. Seat height transport

Per-chair seat heights (4–10 px) need to reach the renderer. Pick one:

- **Server field on `DisplayAislingArgs`** (e.g., `byte SeatHeight`) — clean, protocol-breaking.
- **Client-side sprite-ID → height table** — overlaps with choice #1; requires the same chair catalog.
- **Single universal constant for v1** (e.g., 8 px) — ships now, defers metadata until art tuning proves it necessary.

## Client implementation notes (once prerequisites land)

- **Do not extend `DrawResting`.** Existing rest positions (Kneel/Lay/Sprawl) use pre-rendered SPF sprites. Seated is different — it needs the full `Composite()` path followed by a source-rect crop and a Y-shift on the final `batch.Draw`. Add a separate branch in `DrawEntity` that routes Seated to the normal composite pipeline with a transform applied at the final draw call.
- **Per-direction crop constants** live as a static table next to `AislingRenderer`. No repository/cache infrastructure needed.
- **Scope estimate:** ~50–80 lines total, concentrated in `AislingRenderer` (~30), `IsTilePassable` (~5), packet/state plumbing (0–3 depending on enum choice), plus a `ChairTiles` holder if choice 1(A) is taken (~20 + handler wiring).

## Open questions

- Should sitting regenerate HP/MP faster (like the existing rest positions do)?
- Can other players see the seated player's facing direction update in real-time?
- Should there be an animation/transition when sitting down vs. an instant snap?
- Should the server send seat height metadata in the packet, or should the client derive it from the tile sprite?

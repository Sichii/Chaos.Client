# Dwarf Rendering — Transform + Head Overlay Approach

Interim scoping for rendering dwarves as the first non-human species. The goal is a recognizable dwarf silhouette **without authoring a full body + armor + equipment sprite set** — get something playable on day one, upgrade to full-custom assets later.

Related: [character-creator-revamp.md](character-creator-revamp.md) item 3 (Species selection).

## Context

The current aisling pipeline ([Chaos.Client.Rendering/AislingRenderer.cs](../Chaos.Client.Rendering/AislingRenderer.cs)) composites ~20 layers (body, pants, face, boots, armor, arms, weapon, shield, hair/helm, accessories) into a single 111×85 texture per entity per animation frame, cached per `AislingAppearance`. Every human equipment sprite is hand-drawn against this exact anatomy. Authoring a parallel dwarf sprite set for every piece of gear is a huge art task.

A cheaper path: render the existing human body + armor bundle *non-uniformly scaled* (shorter, slightly wider), and overlay a dwarf-specific head + beard sprite at native scale on top. Species identity lives in the head; stature comes from the transform.

## Approach at a Glance

```text
Dwarf render = transform(body + armor + arms + pants + boots + weapon + shield + accessories)
             + native-scale(face + hair/helm + beard overlay)
             + per-species anchor offsets for UI overlays and hitbox
```

- Body bundle: shared human sprites, drawn with `Vector2 scale` ≠ `(1, 1)` (e.g. ~`(1.05, 0.85)`)
- Head bundle: native scale, species-specific sprites for hair + beard
- UI overlays (chat bubble, health bar, group box, chant text, name text) and the hitbox anchor from a species-specific Y offset, not the hardcoded `CANVAS_CENTER_Y = 70`

Why this reads as a dwarf and not just a squished human: the beard / head silhouette carries most of the species identity in D2/DA-style pixel art. The proportional change in the body reinforces it. Armor squishing is lore-safe — dwarves in DA canon wear human-scale gear.

## The Two-Bundle Model

The current `Composite()` ([AislingRenderer.cs:496-532](../Chaos.Client.Rendering/AislingRenderer.cs#L496-L532)) draws all layers into one 111×85 SKBitmap. For the dwarf path, split composition into two bundles:

| Bundle | Layer slots | Scale |
| --- | --- | --- |
| **Body bundle** | `BodyB`, `Body`, `Pants`, `Boots`, `Armor`, `Arms`, `WeaponW`, `WeaponP`, `Shield`, `Acc1G`, `Acc2G`, `Acc3G`, `Acc1C`, `Acc2C`, `Acc3C` | `SpeciesProfile.BodyScale` (e.g. `(1.05, 0.85)`) |
| **Head bundle** | `Face`, `HeadH`, `HeadE`, `HeadF`, `Emotion`, + new beard overlay | Native `(1, 1)` |

Two separate textures, two `SpriteBatch.Draw` calls per aisling. The head-bundle draw anchors to the top of the *scaled* body bundle, not the native canvas top — so `ScaledBodyTopY = TileCenterY - SpeciesProfile.BodyScale.Y * CANVAS_CENTER_Y`.

For humans, `BodyScale = (1, 1)`, the two bundles compose into the same visual as today, and the caching layer (`CompositeCache` keyed by `EntityId` + full appearance) absorbs the change transparently. The human path stays byte-identical if we preserve the single-texture fast path for `Species = Human`.

## `SpeciesProfile` Data Shape

New static catalog keyed by a new `Species` enum. Sketch:

```csharp
public readonly record struct SpeciesProfile(
    Species Species,
    Vector2 BodyScale,           // e.g. (1.05f, 0.85f) for dwarf; (1, 1) for human
    int BodyAnchorYOffset,       // additive to CANVAS_CENTER_Y for tile-bottom alignment
    int HeadAnchorYOffset,       // where the head bundle sits relative to scaled body top
    int WeaponAnchorYOffset,     // compensation for weapon hand position drift
    int UiOverlayYOffset,        // additive adjustment for chat bubble / health bar / name
    int HitboxBottomYOffset,     // additive adjustment for EntityHitBox
    bool UseBeardOverlay,        // enables the beard overlay layer
    int BeardSpriteIdBase);      // sprite ID lookup base for the dwarf beard pool
```

Humans: all zeros / identity. Dwarves: populated.

New field on `AislingAppearance`: `Species Species { get; init; }` (default `Human` — legacy path untouched).

## Code Sites

### Rendering

- **[AislingRenderer.Draw](../Chaos.Client.Rendering/AislingRenderer.cs#L321-L390)** — main in-world draw. Single `batch.Draw` at line 387 today; becomes two draws (body scaled, head native) when `Species != Human`. Cache entry grows a `BodyTexture` + `HeadTexture` pair; current `Texture` field stays for the human fast path.
- **[AislingRenderer.Render](../Chaos.Client.Rendering/AislingRenderer.cs#L554-L626)** — splits into `RenderBodyBundle` + `RenderHeadBundle` returning two textures. `Composite()` grows a `LayerSlot[] slotFilter` parameter so each bundle runs it against a filtered slot list.
- **[AislingRenderer.RenderPreview](../Chaos.Client.Rendering/AislingRenderer.cs#L631-L663)** — character-creator preview. Needs the same two-bundle path so the preview shows an actually-dwarf dwarf, not a squished human.
- **[AislingRenderer.DrawSwimming](../Chaos.Client.Rendering/AislingRenderer.cs#L1083-L1122)** / **DrawResting** — rest + swim frames are single-texture paths that bypass composition. Dwarf rest/swim can defer to phase 2; acceptable day-one gap is "dwarves rest/swim as shorter humans."

### UI overlays and hitbox

- **[ChatBubble](../Chaos.Client/Controls/World/ViewPort/ChatBubble.cs)** — positioned by the WorldScreen draw code above the aisling. Needs `SpeciesProfile.UiOverlayYOffset` added to its Y anchor.
- **[HealthBar](../Chaos.Client/Controls/World/ViewPort/HealthBar.cs)** — same.
- **GroupBox, ChantText, name/title text** — same pattern; all anchor off aisling top.
- **EntityHitBox** (in `Chaos.Client/Models/`) — `HitboxBottomYOffset` shifts the hitbox to match the scaled body's actual feet position.

The cleanest implementation is a single helper `GetAislingOverlayAnchor(WorldEntity, out int topY, out int bottomY)` on WorldScreen (or a static utility) that each overlay consults, so no single overlay owns the species math.

### Character creation

- **[CharacterCreationControl](../Chaos.Client/Controls/LobbyLogin/CharacterCreationControl.cs)** — add a species selector (starter five per [character-creator-revamp.md](character-creator-revamp.md)). Wire selection into `RenderPreview` via the new `Species` field.

### Networking

- **`CreateCharFinalize`** — legacy opcode stays. New species-aware `CreateCharFinalizeV2` (or rolled into the broader modernization opcode) at `0xFF` per the custom-opcode convention, gated by capability handshake.

## Anchor-Drift Problem

The thing that will bite us if we don't plan for it:

1. **Weapon hand position**: weapons draw as a layer at a specific Y within the human frame. Squish the body by 0.85 vertically and the weapon ends up near the hip instead of the hand. `WeaponAnchorYOffset` compensates, but non-integer scaling means it won't land on a perfect pixel — visible weapon jitter across animation frames.
2. **Beard attachment**: the beard overlay sits at a Y relative to the head bundle top. Different human face sprites have different chin heights; a single `HeadAnchorYOffset` per species may not be enough. Might need per-face-sprite beard offsets, or constrain dwarves to a restricted face-sprite set.
3. **Chat bubble / health bar drift**: the bottom of the scaled body lands at `TileCenterY - BodyScale.Y * (CANVAS_CENTER_Y - contentBottomY)`, not the cached `contentBottomY`. Overlays that anchor off "top of aisling" need to account for this — hence `UiOverlayYOffset`.
4. **Hitbox**: `EntityHitBox` is used for click/hover detection. If the hitbox stays at human size but the sprite is scaled, the player clicks on empty air above the dwarf's head or can't click the feet. `HitboxBottomYOffset` + a `HitboxScale` field close this.

## Sampling and Pixel Art

MonoGame defaults to `SamplerState.LinearClamp` which blurs on non-integer scale. Set `SamplerState.PointClamp` for the species draw to keep pixels crisp — accept the jaggies; blur is worse for this art style. The world SpriteBatch likely already uses `PointClamp` — confirm during implementation.

Alternative: use scale factors that are rational fractions (e.g. `5/6 = 0.8333...` is ugly; `17/20 = 0.85` is still ugly). The only truly clean option is integer scales, which are too coarse. Accept the nearest-neighbor jaggies for v1; full-custom sprites in v2 eliminate them.

## Asset Needs

- **Dwarf beard sprite set** — new EPF file(s) with beard frames aligned to the head Y. Needs one set per hair-color swatch (13 colors) × per gender (2) × per animation (01 walk minimum, 04 idle ideal) × per frame = significant but bounded.
- **Dwarf hair sprite set (optional)** — can reuse human hair at v1. If we want genuinely dwarf-shaped hair, new assets.
- **Dwarf face sprite set (optional)** — can reuse human at v1. Stubbier face / rounder cheeks would help.

None of armor, weapons, or boots need new assets.

## Open Questions

1. **Single scale or per-frame scale?** Walk animation frames have slightly different anatomical proportions per frame. A uniform `(1.05, 0.85)` applied to all frames may read as stilted. Per-frame scale tables help but balloon the `SpeciesProfile` shape.
2. **Do we squish from top or bottom?** Top-anchored squish keeps feet at tile bottom (correct); bottom-anchored squish floats the dwarf above the tile. Top-anchored is almost certainly right but confirm on first build.
3. **Beard-over-helm ordering?** If the dwarf wears a full helm ('h' HeadH layer), does the beard still show? Probably yes (beards stick out below the helm). Needs an ordering decision in the head-bundle composition.
4. **What about dyes on the beard?** Hair color dye applies to head sprites via palette. Beard presumably wants the same dye. Straightforward if the beard sprite ships with the standard palette-swap metadata.
5. **Animation timing for rest/swim:** dwarves currently fall back to unscaled human rest/swim sprites. Acceptable for v1?

## Out of Scope (For This Branch)

- Implementing the renderer changes — this is a scoping doc only.
- New dwarf beard/hair/face art authoring — blocked on approval of this approach.
- Server-side species catalog / capability handshake — tracked in [character-creator-revamp.md](character-creator-revamp.md), implementation lives in the Hybrasyl repo.
- Other species (elves, orcs, etc.) — pattern generalizes but each species is a separate decision (e.g. "tall" species reverses the Y scale, may not need head overlay).

## Verification Plan (When Implemented)

- **Unit-visual**: character-creator preview shows a recognizable dwarf. Compare to human with the same equipment selected.
- **In-world**: spawn on a test map with a human + dwarf side-by-side. Verify relative heights, armor silhouette continuity, weapon attachment sanity.
- **UI anchors**: chat-bubble, health-bar, name, group box all sit flush above the dwarf (not floating, not clipping into the head).
- **Hitbox**: click-to-select works on the dwarf's actual sprite bounds.
- **Rest / swim / ability animations**: acceptable fallback to native-scale human frames (document as known v1 limitation).

## Review Gates (When Implemented)

Per the repo review policy in CLAUDE.md:

- **Phase 1** — `SpeciesProfile` + renderer two-bundle split (humans byte-identical). Bug/regression review and architecture review before Phase 2.
- **Phase 2** — dwarf body scale applied; anchor offsets wired to UI overlays and hitbox. Review before Phase 3.
- **Phase 3** — beard overlay layer and character-creator species selector. Final review covers the full changeset.

Each phase ships behind the capability handshake; retail-compat servers see the unchanged human path.

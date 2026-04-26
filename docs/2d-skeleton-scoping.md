# 2D Skeletal Animation — Scoping

Scope for adding 2D skeletal animation (vector mesh deformation with weighted bones — Spine/DragonBones-style) to the Chaos.Client. This is greenfield modernization for new content surfaces only — **not** a replacement for the legacy frame-based MPF/EPF pipeline that drives existing creatures and aislings.

Companion to [cutscene-video-scoping.md](cutscene-video-scoping.md). That doc covers pre-rendered video playback (WebM/VP9). This one covers real-time animated rigs. They're complementary techniques for different cutscene needs and there is no conflict between them — a cutscene-system implementation could route some scenes to video and others to skeletal actors.

## Problem

- Hybrasyl wants higher-fidelity character animation for new content (cutscene actors, signature special effects, special boss enemies) than legacy frame-array sprites can deliver.
- Skeletal animation gives smooth tweening, IK, and per-bone control that frame arrays can't — but the entire current pipeline assumes frame arrays. There's no skeletal infrastructure today.
- Building it must not destabilize the legacy pipeline that vanilla content depends on. The strategic constraint is **add a parallel rendering path; do not refactor the existing one**.
- Hybrasyl's open-source / AGPL-3.0 stance rules out per-developer paid tooling (Spine Pro). The format and runtime have to be open.

## Goals

1. **Greenfield only.** A new entity surface (cutscene actors, modern FX, special bosses) can opt into skeletal rendering. Existing creatures, aislings, items, and effects keep using legacy MPF/EPF/EFA — untouched.
2. **Open format + runtime.** Pick once; AGPL-compatible, no paid licenses.
3. **Coexists with the legacy pipeline.** Same additive-modernization shape as `.datf` asset packs — skeletal assets ship in a new `content_type` alongside legacy formats.
4. **Pilot on cutscenes first.** Cutscenes render in their own screen, outside the world `WorldScreen.Draw` pipeline — the most isolated possible integration point.
5. **Make the in-world path clear, even if it's not the first ship.** Once cutscene-actor rendering works, the same code can power in-world FX and signature bosses with one additional integration step.

Non-goals for v1:

- **No legacy replacement.** Existing MPF/EPF/EFA rendering stays. Skeletal does not retire frame-based — they coexist forever.
- **No skeletal aislings.** Full character system migration is out of scope. Aisling rendering keeps its multi-layer EPF compositor.
- **No server protocol changes for skeletal-as-creature.** Bosses or NPCs that use skeletal are still represented to the server as standard creatures with a sprite ID; the client just routes that sprite ID's render path differently. (A future opcode could declare "this sprite is skeletal" but that's not v1.)
- **No mesh-deformation editor inside Hybrasyl tooling for v1.** Use stock authoring tools at the start; revisit Taliesin integration when content volume justifies it.

## Current state

Confirmed by reading the codebase as of `cdcc4ee`:

- **Frame-array animation primitives only.** [SpriteFrame.cs](../Chaos.Client.Rendering/Models/SpriteFrame.cs) is `(Texture2D, short CenterX, short CenterY, short Left, short Top)`. [SpriteAnimation.cs](../Chaos.Client.Rendering/Models/SpriteAnimation.cs) is `SpriteFrame[]` + a uniform `FrameIntervalMs` + `BlendMode`. No bones, no transforms, no per-frame rotation/scale metadata.
- **Render calls are quad-only.** Every entity draws via `SpriteBatch.Draw(Texture2D, position, ...)`:
  - [CreatureRenderer.cs:122–131](../Chaos.Client.Rendering/CreatureRenderer.cs#L122-L131) — `batch.Draw(texture, screenPos, null, Color.White * alpha, 0f, Vector2.Zero, 1f, effects, 0f)`. Rotation hardcoded to `0f`, scale hardcoded to `1f`.
  - [AislingRenderer.cs:342](../Chaos.Client.Rendering/AislingRenderer.cs#L342) — `batch.Draw(finalTexture, screenPos, Color.White * effectiveAlpha)`. Position-only overload, identity transforms.
  - [EffectRenderer.cs:75](../Chaos.Client.Rendering/EffectRenderer.cs#L75) — same shape.
- **MonoGame supports custom mesh draws.** `MonoGame.Framework.DesktopGL 3.8.4.1` exposes `GraphicsDevice.DrawIndexedPrimitives()` and `DrawUserPrimitives()` outside `SpriteBatch`. Both bake-to-frames and vertex-buffer paths are technically open.
- **Additive-modernization precedent already operationalized.** [AssetPackRegistry](../Chaos.Client.Data/AssetPacks/AssetPackRegistry.cs) gives new content types (recently `legend_mark_icons`, `static_tiles`) a clean coexistence with legacy formats. Skeletal would follow the same pattern.

The architectural conclusion is straightforward: there's no in-place retrofit story. The render call shape doesn't carry the data skeletal animation needs (mesh, bone matrices). Skeletal lives on its own path, in parallel.

## Why open over Spine

[Spine](https://esotericsoftware.com/) by Esoteric Software is the de facto industry standard for 2D skeletal animation. Mature C# runtime, broad artist familiarity, best mesh-deformation tooling. **But the editor is paid per-developer:** Spine Essential is roughly $69/seat, Spine Professional (full mesh deformation, IK, weights) is roughly $299/seat. Hybrasyl's open-source posture and contributor model don't align with that — every artist who touches a rig needs a license.

Even if licensing were resolved, it'd be a strategic dependency on a single vendor's pricing and tooling roadmap. Open alternatives exist with adequate-to-good tooling and runtimes, so we pick from there.

## Candidate libraries

| Candidate | License | Tooling | C# runtime | Verdict |
| --- | --- | --- | --- | --- |
| **DragonBones** | MIT | Free editor (DragonBonesPro), JSON export, mature exporters | Official `DragonBones-CSharp` (Unity-targeted, MIT) | ✅ Recommend |
| **Spine** | Paid | Best-in-class | Official Spine-C# (MIT runtime, paid editor) | ❌ Per-dev license |
| **Custom format via Taliesin** | N/A | Hybrasyl's authoring tool | Would author from scratch | Defer — large authoring-tool dev cost |
| **OGSS / open community formats** | Mixed | Niche, fragmented | Sparse | ❌ Tooling gaps |

**Recommendation: DragonBones export format + a bespoke MonoGame-targeted C# runtime that parses what we need.**

The official DragonBones-CSharp runtime is Unity-targeted; pulling it onto MonoGame in full is a meaningful port. But the DragonBones JSON format is well-documented and stable — for the v1 pilot we don't need every feature (filters, layered animation blending, runtime IK solver). A focused parser + bone pose computation + mesh deformation is on the order of a few thousand LOC, gives us a runtime we own and understand, and avoids carrying Unity-isms into MonoGame code.

## Two integration architectures

Both work mechanically. The choice is a tradeoff between integration simplicity and per-frame perf.

### Bake-to-frames (recommended for the pilot)

Each tick, compute bone poses CPU-side, deform the mesh, **rasterize the result to a `Texture2D`**, and feed the existing `SpriteBatch.Draw(Texture2D, ...)` pipeline.

- **Compatible with current renderers without modification.** The output of skeletal rendering looks identical to a baked frame from a legacy MPF — just produced fresher.
- **Same pattern AislingRenderer already uses** — its multi-layer EPF compositor rasterizes to a single per-entity `Texture2D` cached on the GPU. Skeletal becomes a more sophisticated version of that.
- **CPU cost is on the main thread** at the bake step. At cutscene scales (1–4 actors on screen at low frame rates) this is comfortable. At in-world FX scales (dozens of effects firing simultaneously) it could matter — measure first.
- **GPU memory cost** for the cached frame textures, but they live for milliseconds, not the whole session.

### Custom vertex-buffer draw (option for in-world surfaces if perf demands)

Bypass `SpriteBatch` for skeletal entities; issue `GraphicsDevice.DrawIndexedPrimitives` directly with the deformed mesh.

- **No bake step**, so no transient `Texture2D` per frame and no CPU rasterization cost.
- **Custom shader required** for blend modes (Normal/Additive/SelfAlpha — match `EffectBlendMode`) and tinting (alpha modulation). Non-trivial — maps to a small HLSL/GLSL effect.
- **Sits outside the existing renderer architecture.** Need a parallel draw pass and ordering integration with the diagonal-stripe entity interleaving used by [WorldScreen.Draw.cs](../Chaos.Client/Screens/WorldScreen.Draw.cs).
- **Right answer for high-volume in-world skeletal entities** if and when bake-to-frames cost shows up in a profile. Not the right starting point.

**Recommendation:** start with bake-to-frames everywhere. Cutscenes are trivial. World-bound FX and bosses likely stay on bake-to-frames forever — measure before promoting.

## Asset pack integration

New content type `skeletal_animations` following the established [.datf pack pattern](asset-pack-format.md). One pack per actor (or one bundle for an actor set):

```
hyb-cutscene-actors.datf
├── _manifest.json          # content_type: "skeletal_animations", schema_version: 1
├── danaan_ske.json         # DragonBones project export — skeleton structure
├── danaan_tex.json         # texture atlas slice metadata
├── danaan_tex.png          # texture atlas image
├── voltigeur_ske.json
├── voltigeur_tex.json
└── voltigeur_tex.png
```

Manifest:

```json
{
  "schema_version": 1,
  "pack_id": "hybrasyl-cutscene-actors",
  "pack_version": "0.1.0",
  "content_type": "skeletal_animations",
  "priority": 100,
  "covers": {
    "skeletal_animations": { }
  }
}
```

Pack class lives at `Chaos.Client.Data/AssetPacks/SkeletalAnimationPack.cs`, follows the same shape as [LegendMarkIconPack](../Chaos.Client.Data/AssetPacks/LegendMarkIconPack.cs) / [StaticTilePack](../Chaos.Client.Data/AssetPacks/StaticTilePack.cs) — ZipArchive-backed entry index, `TryGetActor(actorId, out Skeleton?)` lookup, decode-fail-is-not-present convention.

A new `SkeletalRenderer` class lives parallel to `CreatureRenderer` / `EffectRenderer`. Owns:

- DragonBones JSON parsing → in-memory `Skeleton` + `Animation[]` model.
- Per-instance pose state (current animation, time, bone transforms).
- Bake-to-frames rasterization → `Texture2D` cache (LRU, evicted on map change like the other per-entity caches).

Naming convention proposal (pin in pack format spec when v1 lands): `{actor_id}_ske.json`, `{actor_id}_tex.json`, `{actor_id}_tex.png`. Matches DragonBones default exporter output verbatim.

## Pilot target — a cutscene actor

The most isolated possible pilot is **a single skeletal actor in a dedicated cutscene screen**. Cutscenes render outside `WorldScreen.Draw` entirely (cutscene logic is being scoped in [cutscene-video-scoping.md](cutscene-video-scoping.md), which has its own popup/screen surface). A skeletal cutscene actor is:

- Drawn in its own `SpriteBatch` pass on a cutscene screen, no diagonal-stripe interleaving with world entities.
- Triggered by a server cutscene event, same as a video cutscene.
- Authored by an artist in DragonBonesPro, exported as JSON + atlas, dropped into a `.datf` pack.
- Played back end-to-end, validated visually, no perf concerns at one-actor scale.

That's the v1 ship surface. Once it works:

- **In-world FX** comes next: route signature special-effect sprite IDs through `SkeletalRenderer` instead of `EffectRenderer`. Trivial extension if bake-to-frames perf holds.
- **Special bosses** comes after that: same routing logic for creature sprite IDs, gated by a new `MetaFile` capability flag or pack manifest declaration.

## Out of scope (explicit)

- Replacing legacy MPF / EPF / EFA rendering.
- Skeletal aisling rigs (full character animation system).
- Server protocol changes that declare "this sprite is skeletal." For v1, it's a client-side decision based on which packs are loaded.
- Spine Pro tooling or any per-developer paid software.
- DragonBones full feature parity with the Unity runtime (filters, runtime IK, layered animation blending). v1 ships bone-only forward kinematics + mesh skinning; FFD and IK are open questions for later.
- Hot-reload during authoring iteration. Same answer as current asset pack v1: restart to reload.

## Dependencies & licensing

- **DragonBones format** — MIT-licensed, freely consumable. Source: `github.com/DragonBones`.
- **Authoring tool** — DragonBonesPro is free and includes the export tools we need.
- **C# runtime** — write our own MonoGame-targeted minimal runtime that parses DragonBones JSON. No external runtime dependency.
- **AGPL-3.0 compatibility** — MIT-licensed inputs are fine to consume from an AGPL-licensed project. The combined work is governed by AGPL-3.0; the MIT-licensed format spec doesn't impose obligations beyond attribution.
- **Texture atlas atlasing** — DragonBones exports atlases in its own JSON shape, but the underlying PNG is just a PNG. No new image-format dependency.

No new NuGet packages required for v1. The runtime is hand-rolled C# in `Chaos.Client.Rendering`.

## Effort estimate (rough)

For the cutscene-actor pilot via bake-to-frames + bespoke DragonBones JSON parser:

| Workstream | Estimate |
| --- | --- |
| DragonBones JSON parser (skeleton structure, animation tracks, atlas metadata) | ~1 week |
| Bone pose computation + forward kinematics + mesh skinning math | ~1 week |
| Bake-to-`Texture2D` rasterization + integration with cutscene screen | ~1 week |
| One demo actor authored in DragonBonesPro, packaged as `.datf`, end-to-end visual QA | ~1 week |
| **Subtotal — cutscene pilot** | **~3–4 weeks** |
| Promote to in-world FX (route through `SkeletalRenderer`) | +1 week |
| Promote to special bosses (creature sprite ID routing + capability flag) | +1 week |

The estimate assumes bone-only rigs (no FFD, no runtime IK). Adding FFD support is roughly another 1–2 weeks; runtime IK another 1–2.

## Open questions

- **Authoring tool integration.** v1 uses stock DragonBonesPro. When does Taliesin grow skeletal-authoring features? Defer to the Taliesin roadmap; don't block this pilot on it.
- **Mesh deformation fidelity.** Bone-only rigs (forward kinematics, no mesh deformation per vertex) are simpler but expressively limited — joints look segmented. Full FFD (free-form deformation per vertex) gives smooth bending but doubles parser+runtime complexity. Pilot starts bone-only; FFD is a follow-up.
- **Audio sync.** Cutscene actors with dialogue need lip-sync timing. The skeletal runtime needs hooks to dispatch animation events on frame boundaries. Out of scope for the rendering pipeline; flag for the cutscene-system scoping doc.
- **Frame baking cost at scale.** When (if ever) does bake-to-frames become a perf concern? Plan to measure during the cutscene pilot at single-actor scale, then again if/when in-world FX lands. Vertex-buffer draw stays in the back pocket.
- **Bundling with cutscene video assets.** Cutscene-video-scoping proposes a `cutscenes` content type for video files. Should skeletal actors be a sub-namespace of that, or a peer `skeletal_animations` type? Recommend: peer type. A cutscene composition (the orchestration of which actors play which animations when) might eventually become its own content type referencing both.

# Doors Modernization Direction — Interactable Entities, Generic Object-State Opcode, Retail Bridge

Design doc for evolving Hybrasyl's door system away from the retail-baked sprite-pair model toward a data-driven **interactable entity** pattern that can also subsume signposts, chests, shrines, and other world objects. Not an implementation plan — decisions flagged inline.

## Motivation

The current Hybrasyl doors implementation is functional but structurally constrained in ways that block several roadmap items:

- **Hardcoded sprite pairs.** [`Hybrasyl.Internals.Sprites.OpenDoorSprites` / `ClosedDoorSprites`](../../server/hybrasyl/Internals/Sprites.cs) is a 66-entry dictionary baked from `DarkAges.exe` offset `0x0068b8b0`. The Chaos.Client mirrors the same table at [`Chaos.Client/Definitions/DoorTable.cs`](../Chaos.Client/Definitions/DoorTable.cs). Any map using a non-retail sprite cannot have doors. Any retail sprite that happens to look like a door but isn't in the 66-pair list also can't.
- **Inference-based registration.** Hybrasyl populates `Map.Doors` at map load by scanning the tile layer for known door sprite IDs. There's no way for map data to declare "this thing is a door" — the sprite ID *is* the declaration. Custom maps must reuse retail door sprites to get door behavior.
- **Binary state.** Doors are open or closed. There is no representation for locked / keyed / trapped / timed / one-way / party-size-gated doors. Anything beyond the binary requires a parallel system.
- **Protocol ossification.** Opcode `0x32` carries only `{x, y, closed, isLeftRight}`. It cannot describe lock state, animation progression, a key prompt, or a failure reason. Adding any of those would require a new opcode.
- **Doors are first-class; signposts, shrines, chests aren't.** Hybrasyl's [`PacketHandler_0x43_PointClick`](../../server/hybrasyl/Servers/World.cs) already forks on `user.Map.Doors.ContainsKey` vs `user.Map.Signposts.ContainsKey` in the same switch arm — two custom registries, two custom discovery mechanisms, one shared click opcode. A third interactable kind means a third registry.

The retail constraint (sprite-pair inference + 0x32 toggle) solved a 2001 problem: shipping doors without a map editor that understood "object placement." Hybrasyl inherited that shape. The Chaos.Client, because it is a modernized client paired with a modernized asset pipeline (Taliesin + `.datf` packs), can drop the constraint without breaking retail players — there are none on this client.

## The modern pattern

Treat doors as one specialization of a general **interactable entity** — a placed, server-managed object with a position, a sprite/model reference, a kind discriminator, and a typed state bag. Signposts, chests, shrines, levers, and portals are sibling specializations.

Concrete flow:

1. **Map authoring (Taliesin):** artist places interactables into map data like NPCs. Each interactable carries `{id, x, y, kind, spriteRef, properties}`. No more "draw the door sprite and the server will infer." Data-driven declaration replaces sprite-ID inference.
2. **Server load:** `Map.Interactables` dictionary keyed by `(x, y)` or by `id`. On map load the server walks the authored list and instantiates `Door`, `Signpost`, `Chest`, etc. from the `kind` field. `Map.Doors` / `Map.Signposts` become thin views over `Map.Interactables` filtered by kind (or get dropped entirely).
3. **Click dispatch (unchanged on the wire):** `0x43 ClickArgs TargetPoint` still flows from client to server. The server looks up the interactable at `(x, y)` and calls `OnInteract(user)`. The existing Hybrasyl fork in `PacketHandler_0x43_PointClick` collapses into a single polymorphic dispatch.
4. **State updates (new opcode, additive):** a generic `ObjectState` opcode carries `{id|coord, kind, stateVersion, typed-blob}` where the typed blob is decoded per-kind. Door state blob = `{closed, openRight, lockState, animFrame}`. Chest state blob = `{open, empty}`. Signpost has no state to push (it's click-only). The existing `0x32 Door` opcode keeps working in parallel for pure-retail doors that don't need the richer state — this is the **additive modernization** choice, not a replacement.
5. **Client rendering:** interactables render themselves. A door entity draws its current sprite based on its state. When `ObjectState` arrives, the entity updates its state and the next frame reflects it. No more "mutate the tile layer's foreground ID from the network handler." The tile layer becomes pure background art.

This dissolves the sprite-pair table entirely — the sprite to render is whatever the entity's current state says, looked up in the asset pack, not derived from a hardcoded pair. It also means a door can animate open over several frames instead of snapping between two sprites.

## Relationship to other modernization directions

This doc composes with, not conflicts with, several existing directions:

- **Unified asset format** ([asset_format_direction](../../../../.claude/projects/e--Dark-Ages-Dev-Repos-Chaos-Client/memory/asset_format_direction.md), memory entry): door sprites stop being EPF/MPF frames and become ZIP+manifest entries with `base + mask` dye support. A door's "open" and "closed" states are just named frames in the asset pack, not separate sprite IDs in a pair table.
- **Additive modernization pattern** ([additive_modernization_pattern](../../../../.claude/projects/e--Dark-Ages-Dev-Repos-Chaos-Client/memory/additive_modernization_pattern.md), memory entry): keep `0x32` alive for retail-style doors, add the `ObjectState` opcode alongside, let the capability handshake advertise modern-object-state support. Servers that don't send it, clients that don't expect it, still work.
- **Dialog modernization** ([dialog-modernization-direction](dialog-modernization-direction.md)): locked-door prompts (enter key, PIN, password, party-size check) naturally flow through the modernized dialog pipeline rather than requiring door-specific UI.
- **Plugin architecture** ([plugin_architecture_direction](../../../../.claude/projects/e--Dark-Ages-Dev-Repos-Chaos-Client/memory/plugin_architecture_direction.md), memory entry): once doors are entities with `OnInteract(user)`, the handler can be Lua. Custom door behavior (puzzle doors, riddles, quest-gated) becomes script data, not a new `Door` subclass.

## Compat matrix sketch

| Path                               | Retail doors          | Modern doors              |
|------------------------------------|-----------------------|---------------------------|
| Client→Server click                | 0x43 TargetPoint      | 0x43 TargetPoint          |
| Server→Client state change         | 0x32 Door             | `ObjectState` (new)       |
| Client rendering                   | tile foreground flip  | entity sprite update      |
| Collision                          | tile SOTP             | entity collision flag     |
| State                              | `{closed, openRight}` | typed per-kind blob       |
| Discovery                          | sprite-ID inference   | authored map data         |

Both paths coexist. Whether a given door uses the retail or modern path is declared per-door in map data (or implicitly: anything without authored metadata falls back to sprite-ID inference for retail compat).

## Open questions — flag for decision, don't pre-decide

- **Do we need `ObjectState`, or can we fold door/chest/etc. into a richer `ChatEvent`-style opcode?** The [chat system direction](../../../../.claude/projects/e--Dark-Ages-Dev-Repos-Chaos-Client/memory/chat_system_direction.md) memory entry already proposes a typed-metadata ushort-length event opcode. If doors, dialog state, and chat all flow through one generic event channel, there's less protocol surface to maintain. But it also conflates unrelated concerns and risks a "big fat envelope" anti-pattern. **Decision deferred.**
- **When does `Map.Doors` go away?** Keeping both `Map.Interactables` and `Map.Doors` is temporary scaffolding during migration. Plan to delete `Map.Doors` once all maps are authored with explicit interactables. But any retail-compat bridge for untouched maps probably wants to keep a thin `Doors` view indefinitely. **Decision deferred until migration scope is clearer.**
- **Chaos.Client's `DoorTable` — keep or drop?** If the client learns interactables from a modern map format, `DoorTable` is only needed for maps served via the retail bridge. Same scope-of-deletion question as `Map.Doors`.
- **Animation fidelity.** Real-world doors swing over ~150ms. The current sprite-pair flip is instant. An animated open is nice-to-have, not must-have; the entity model enables it but doesn't require it.
- **Locking and key prompts.** Once doors can be locked, the failure path needs a client-side prompt ("It's locked. Do you have a key?"). That's UI surface that doesn't exist today and should be scoped as its own mini-project, not bundled into the entity migration.

## What this isn't

- Not a retail-compat break. The retail client is already unsupported on Hybrasyl (the Chaos.Client replaces it); dropping `0x32` is on the table, but not urgent. Keep it working for as long as it's free.
- Not a justification for refactoring the Chaos.Client door path *today*. Current implementation works. This doc is about server-side modeling and protocol evolution; client changes follow the protocol, not the other way around.
- Not a replacement for the sprite-pair table while retail maps are still in play. The table stays as the fallback discovery mechanism for un-authored maps.

## Summary

Replace "the server infers doors from sprite IDs" with "the map author declares doors as entities." Replace "0x32 flips a sprite" with "a generic state opcode updates a typed entity." Keep the retail path as the compatibility bridge. The immediate win is that signposts, chests, shrines, and doors stop being four different custom registries in `MapObject.cs` and become one polymorphic interactable model. The strategic win is that locked / keyed / animated / scripted doors become data, not code.

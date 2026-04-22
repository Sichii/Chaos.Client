# Resource Nodes — Scoping

Client representation and wire protocol for profession resource nodes — stationary, harvestable world objects (mining veins, herb patches, fishing spots, timber) introduced by Hybrasyl's gathering system. Retail Dark Ages has no equivalent concept; this is a modernization layer.

## Context

Hybrasyl's profession system needs a client-visible object type that:

- Occupies a fixed tile position
- Has a lifecycle (available → harvesting → depleted → respawning → available)
- Triggers a harvest interaction on click, not an attack
- Is visually distinct from creatures, items, and ground tiles
- Shows on the tab map with a type-appropriate symbol
- Carries state metadata the client can render (harvest swings remaining, rare-spawn glow, skill-gated reveal)

The question is: what entity model best represents this?

## Approach — dedicated `ResourceNode` entity type

Resource nodes get their own entity type, their own opcodes, and their own client-side collection + renderer + input handler. Not a pseudo-creature. Not a ground item. Not a map overlay. A first-class new thing.

This follows the additive-modernization pattern — new capability ships as a parallel layer; legacy code paths (`WorldEntity`, items, tiles) unchanged.

### Why not pseudo-creatures

Tempting, because the creature pipeline already handles stationary-or-walking entities with sprites, click handlers, and tab-map dots. But creatures carry invariants that nodes violate: they can walk, take damage, participate in combat, show HP bars, enter aggro states, appear in creature-list popups. Routing a copper vein through the creature pipeline means every creature-aware system grows an `isActuallyAResourceNode` branch. Saves one opcode upfront, pays for it in a hundred small special cases forever.

### Why not ground items

Items have no combat semantics (good), but they're single-pickup, have no natural lifecycle state, and are visually small. A persistent multi-harvest node doesn't fit. Exception: if a node type is truly ephemeral (spawn → one harvest → gone, e.g. a glimmering pearl that appears briefly), the ground-item model could fit for *that specific subtype*. Design assumption: most nodes are persistent; optimize for that.

### Why not foreground tile overlay

Tile data is globally static; per-player depletion state is awkward to layer on top. Tiles don't participate in the entity list that feeds the tab map. No nameplate/tooltip infrastructure for tiles. Node-as-tile would require rebuilding most of the entity system anyway.

## Wire protocol

New opcodes, allocated from **0xFF downward** per the custom-opcode convention (kedian's directive). Allocation is independent per direction — both client→server and server→client have their own 0xFF-downward pool. Endgame: 16-bit opcodes via capability handshake.

Next-free top-of-range values as of this scoping pass. `0xFF` in the server→client direction is already taken by `ExtendedStats` (see [opcode-0xff-extended-stats.md](opcode-0xff-extended-stats.md)), so resource-node allocations start at `0xFE`. Client→server direction is untouched; `HarvestResourceNode` takes `0xFF` there.

### Server → Client

| Opcode | Name                  | Purpose                                                   |
|--------|-----------------------|-----------------------------------------------------------|
| `0xFE` | `DisplayResourceNode` | Full node state (map load / entering view)                |
| `0xFD` | `ResourceNodeUpdate`  | Incremental state change (harvest count, state transition)|
| `0xFC` | `ResourceNodeRemove`  | Node gone (fully depleted, map change)                    |

### Client → Server

| Opcode | Name                  | Purpose                                                   |
|--------|-----------------------|-----------------------------------------------------------|
| `0xFF` | `HarvestResourceNode` | Player clicked to harvest; server validates and responds  |

### `DisplayResourceNode` payload (server → client)

```text
Offset  Type     Field              Notes
------  -------  -----------------  --------------------------------------
0x00    uint32   NodeId             Unique runtime handle
0x04    uint16   TileX
0x06    uint16   TileY
0x08    uint16   SpriteId           Node appearance sprite
0x0A    byte     NodeType           0=mineral 1=herb 2=fish 3=wood 4=rare
0x0B    byte     State              0=available 1=depleted 2=respawning
0x0C    byte     HarvestRemaining   Swings remaining before depletion
0x0D    uint16   NameLength
0x0F    string   Name               UTF-8 display name ("Iron Vein", etc.)
```

Total: 15 bytes + name length.

### `ResourceNodeUpdate` payload (server → client)

```text
0x00    uint32   NodeId
0x04    byte     State              New state
0x05    byte     HarvestRemaining   Updated count
```

6 bytes fixed. Used for mid-lifecycle updates without re-sending the full node.

### `ResourceNodeRemove` payload (server → client)

```text
0x00    uint32   NodeId
```

4 bytes fixed.

### `HarvestResourceNode` payload (client → server)

```text
0x00    uint32   NodeId
```

4 bytes fixed. Server validates position, skill/tool requirements, and state before responding with update packets; outright rejection can surface via the existing orange-bar text path.

## Client architecture

### New model

`Chaos.Client/Models/ResourceNode.cs` — lightweight data bag:

```csharp
public sealed class ResourceNode
{
    public uint NodeId { get; set; }
    public ushort TileX { get; set; }
    public ushort TileY { get; set; }
    public ushort SpriteId { get; set; }
    public ResourceNodeType NodeType { get; set; }
    public ResourceNodeState State { get; set; }
    public byte HarvestRemaining { get; set; }
    public string Name { get; set; } = string.Empty;
}
```

Enums (`ResourceNodeType`, `ResourceNodeState`) live in `Chaos.Client/Definitions/Enums.cs` alongside the existing client-side enums — this is modernization code, no Chaos.DarkAges nuget extension needed.

### World state

`Chaos.Client/Collections/WorldState.ResourceNodes` — keyed by `NodeId`, cleared on map change (same lifecycle hook as `WorldEntity` list). Updated by the three S→C opcode handlers.

### Renderer

`Chaos.Client.Rendering/ResourceNodeRenderer.cs` — parallel to `CreatureRenderer` but simpler:

- Per-frame cache of `Texture2D` keyed on `(SpriteId, State)`
- No walk/animation state machine — nodes are stationary
- State-specific tints: `depleted` desaturated, `respawning` fade/pulse, `rare` soft glow (overlay)
- Draws in the existing painter's-algorithm stripe alongside entities — ground effects below, entities above, consistent with `WorldEntity` draw order
- `Clear()` on map change, matching the existing renderer cache convention

### Input handling

New branch in the click handler chain ([WorldScreen.InputHandlers.cs](../Chaos.Client/Screens/WorldScreen.InputHandlers.cs)):

- Left click on a node tile → `Connection.HarvestResourceNode(nodeId)` if state is `available`; otherwise no-op or short tooltip.
- Right click → context menu (Examine / Harvest / Cancel pathfinding if routing there).
- Arrow keys / spacebar during active harvest → cancel (by movement, implicitly).

Pathfinding: nodes block A* the same way walls and creatures do. `IsTilePassable` check extends to cover node positions.

### Tab map integration

[TabMapRenderer](../Chaos.Client.Rendering/TabMapRenderer.cs) gets per-NodeType dot colors, kept distinct from the creature/aisling/NPC palette:

| NodeType | Color                | Rationale |
|----------|----------------------|-----------|
| mineral  | light grey (#BBBBBB) | metallic  |
| herb     | green (#6AA84F)      | plant     |
| fish     | cyan (#4A90E2)       | water     |
| wood     | brown (#8B5A2B)      | timber    |
| rare     | gold (#D4AF37)       | special   |

Depleted nodes render dimmed but visible — players use the tab map to plan harvest routes, and ghosting a depleted node helps orientation until respawn.

## Interaction model

1. Player left-clicks a node tile.
2. Client sends `HarvestResourceNode(nodeId)`.
3. Server validates (range, tool, skill, state). Rejection surfaces as orange-bar text through the existing messaging path. Acceptance begins a server-side harvest tick.
4. On each successful tick, server emits `ResourceNodeUpdate` decrementing `HarvestRemaining`. Client plays harvest animation + sound, reuses the existing chant/cast-bar UI for progress.
5. When `HarvestRemaining` reaches zero, server transitions state to `depleted` via `ResourceNodeUpdate`. After respawn, either restores via `ResourceNodeUpdate` or removes via `ResourceNodeRemove` + fresh `DisplayResourceNode` elsewhere.

**Channel time** stays server-authoritative. Client shows progress but does not enforce timing.

**Abandon** is implicit — the next movement packet cancels. No explicit client-side cancel opcode.

## LOC estimate

| Component | LOC |
|---|---|
| `ResourceNode` model + enums | ~60 |
| `WorldState.ResourceNodes` + handlers | ~80 |
| `ResourceNodeRenderer` | ~200 |
| Click/interaction/pathfinding hooks | ~100 |
| `TabMapRenderer` per-type color patch | ~30 |
| Four opcode handlers in `ConnectionManager` | ~150 |
| **Total (client)** | **~620** |

Server-side work is out of scope — flagged for the Hybrasyl team.

## Rollout phases

Each phase ends with bug/regression + architecture review per [CLAUDE.md](../CLAUDE.md) review policy.

**Phase 1 — Protocol + model (~1 week).** Four opcodes wired into [ConnectionManager](../Chaos.Client.Networking/ConnectionManager.cs), `ResourceNode` model, `WorldState` collection. No rendering yet. Verify by logging received packets against a test map with manually-injected nodes.

**Phase 2 — Rendering (~1 week).** `ResourceNodeRenderer` with per-state tints. Draws in the painter's-algorithm stripe alongside entities. No tab map yet. Verify against hand-crafted server packets on a scratch map.

**Phase 3 — Interaction + tab map (~1 week).** Click handler, pathfinding block, harvest cast-bar, tab-map dot colors. Verify end-to-end against a Hybrasyl test server with live nodes.

**Phase 4 — Final review + docs.** Comprehensive review, update [CLAUDE.md](../CLAUDE.md) with the new entity type, document any new `WorldState` APIs.

## Open questions

- **Depleted visibility:** render depleted nodes greyed-out (helps player mental map) or remove from view? Recommendation: render dimmed.
- **Concurrent harvest arbitration:** two players click the same node simultaneously — how does the client show "someone else is harvesting this"? Likely reuse the respawning-state tint + orange-bar text. Server arbitrates; client renders.
- **Tool/skill gating visual:** if a player lacks the required tool, does the node render with a distinct "unavailable" indicator, or render normally and fail on click? Server decides via the `State` byte; client is dumb.
- **Nameplate/tooltip:** hover tooltip showing `Name` + `NodeType`? Reuse existing entity tooltip infrastructure if it accepts non-creature entities cleanly.
- **Per-type harvest sounds:** pickaxe (mineral), scythe (herb), splash (fish), axe (wood). In-scope for Phase 3 or defer to polish pass?
- **Collision check completed (2026-04-22):** retail `ServerOpCode` tops out at `0x7E AcceptConnection` and `ClientOpCode` at `0x7B MetaDataRequest`. 0x80–0xFF is entirely unused in both directions. Resource-node opcodes land in clean territory; no known crypto-routing or dispatch-path special cases at these values (`Chaos.Networking.Abstractions` 1.11.0-preview-0007-g1c0297d942).

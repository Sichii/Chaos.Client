## Chaos.Networking Removal Direction — Owning Our Protocol Types

Design doc for removing the `Chaos.Networking`, `Chaos.DarkAges`, `Chaos.Common`, `Chaos.Geometry`, and `Chaos.Pathfinding` NuGet packages from Chaos.Client, replacing them with local types shaped for Hybrasyl + legacy Dark Ages servers only. Captures the phasing that separates trivial cleanup from the heavy packet-converter rewrite. Not an implementation plan — decisions flagged inline.

## Motivation

Chaos.Client currently consumes 5 NuGet packages from the Chaos-Server ecosystem (all pinned to 1.11.0-preview in [Directory.Packages.props](../Directory.Packages.props)):

| Package | Role |
|---|---|
| `Chaos.Networking` | Packet converters, crypto, opcodes, `*Args` types |
| `Chaos.DarkAges` | Protocol enums (Direction, Class, Stat, Element, StatusIcon, …) |
| `Chaos.Common` | Case-insensitive string extensions (`EqualsI`, `StartsWithI`, …) |
| `Chaos.Geometry` | Point, Rectangle, Direction primitives |
| `Chaos.Pathfinding` | A* pathfinding |

These packages are maintained by the Chaos-Server project and track the **Chaos-Server** dialect of the Dark Ages protocol. Hybrasyl is a separate server with its own dialect — the [compat matrix](hybrasyl-compat-matrix.md) documents ~20 opcodes where Hybrasyl diverges from Chaos-Server in ways that are cosmetically fine today but will become an authoring blocker as the modernization tracks ship.

Every direction doc in this repo eventually wants a new or differently-shaped opcode:

- [Dialog modernization](dialog-modernization-direction.md) — new `DialogGraph` opcode or `ScriptInvoke` channel
- [Stats display](stats-display-direction.md) — `ScriptInvoke` / `ScriptSubscribe` / `ScriptUpdate`
- [UI modernization](ui-modernization-direction.md) — capability-handshake opcode for pack negotiation
- Chat system direction (memory-indexed) — new `ChatEvent` opcode with typed metadata
- Additive modernization pattern (memory-indexed) — one-time capability handshake

None of these fit the Chaos.Networking upstream cleanly. Each would force either (a) a Chaos-Server fork or (b) a negotiation with upstream to accept Hybrasyl-specific types. Owning the protocol layer removes both friction points. **The cost is real but the value compounds** — every future modernization opcode lands without external gating.

## Dependency inventory

Research pass (2026-04) across all 268 C# files in the solution:

- **75 files (28%)** reference one of the 5 Chaos.* namespaces
- Hotspots: [WorldScreen.cs](../Chaos.Client/Screens/WorldScreen.cs) (21 usings), [WorldScreen.ServerHandlers.cs](../Chaos.Client/Screens/WorldScreen.ServerHandlers.cs) (15), [WorldScreen.InputHandlers.cs](../Chaos.Client/Screens/WorldScreen.InputHandlers.cs) (13), [WorldState.cs](../Chaos.Client/Collections/WorldState.cs) (13), [LobbyLoginScreen.cs](../Chaos.Client/Screens/LobbyLoginScreen.cs) (11)
- Heaviest integration: [ConnectionManager.cs](../Chaos.Client.Networking/ConnectionManager.cs) — 117 `*Args` type references

**Package-by-package usage:**

| Package | Files | Scope |
|---|---|---|
| Chaos.Networking (+ Crypto, Packets) | 22 | 103 distinct `*Args` types, packet serialization hub |
| Chaos.DarkAges | 48 | Enums used throughout UI + networking + rendering |
| Chaos.Geometry | 15 | Point/Rectangle/Direction; world state + rendering |
| Chaos.Common | 17 | Case-insensitive string extensions (one-liners) |
| Chaos.Pathfinding | 2 | Single consumer: [Systems/Pathfinder.cs](../Chaos.Client/Systems/Pathfinder.cs) |

## Replacement cost by category

Work separates cleanly into **trivial cleanup** (~370 LOC, ~1 day) and **heavy migration** (~2500 LOC, 2–3 focused weeks):

| Category | LOC estimate | Effort | Risk |
|---|---|---|---|
| Geometry primitives | ~50 | Copy/inline | None — structural only |
| String extensions | ~20 | Copy/inline | None |
| Pathfinding (A*) | ~200 | Copy/port | Low — single consumer, unit-testable |
| Crypto (XTEA + MD5) | ~100 | Reimplement | Low — standard algorithms, login-only integration |
| **Packet args + converters + dispatcher** | **~2500** | **Dedicated sprint** | **High — 103 types, every server packet** |

The trivial bucket is **shrinkable dependency surface for near-zero cost.** The heavy bucket is a real project — it lands right across the middle of any interleaved modernization work, so it should be scheduled as a **focused sprint**, not slipped between tickets.

## What we gain

- **Hybrasyl-exact opcode shapes.** The compat matrix's 20 divergences become local types that match what the server actually sends; no more "Chaos-Server-shaped args that happen to parse Hybrasyl packets if you squint."
- **Free hand on new opcodes.** `ScriptInvoke`, `DialogGraph`, `CapabilityHandshake`, `ChatEvent` all land in our types without upstream negotiation.
- **No NuGet release gating.** Protocol changes ship when they're ready, not when the next `Chaos.Networking` preview drops.
- **Smaller attack surface.** 5 fewer transitive dependency chains; easier to audit for AGPL-incompatible sublicenses (relevant given the plugin-architecture direction's closed-source constraints).

## What we lose

- **Chaos-Server protocol fixes.** Any bug fixes upstream ships to its `*Args` types won't auto-propagate. For a Hybrasyl-only client this is mostly cosmetic — *Chaos-Server* fixes aren't *Hybrasyl* fixes.
- **Battle-tested converter matrix.** Chaos.Networking's converters have been exercised against multiple servers; our rewrites start with zero field hours. Mitigated by being a thin port rather than a ground-up rewrite.
- **Sync option.** If Hybrasyl and Chaos-Server ever realign, we can't just bump the package version. We're permanently diverged.

For a client explicitly targeting Hybrasyl + legacy Dark Ages, none of these losses are load-bearing.

## Phased plan

### Phase 1 — Trivial cleanup (geometry + extensions + pathfinding) — ~1 day

- Copy `Point`, `Rectangle`, `Direction` struct/enum definitions into `Chaos.Client` (or a new `Chaos.Client.Protocol` assembly — see D1).
- Inline the four case-insensitive string extensions into a local `StringExtensions` static class.
- Port the A* implementation from Chaos.Pathfinding into [Systems/Pathfinder.cs](../Chaos.Client/Systems/Pathfinder.cs) (it already has single-consumer scope).
- Remove three PackageReferences. Confirm solution builds and runs against Hybrasyl QA unchanged.

**Safe to do immediately.** No semantic impact; can land in a single PR.

### Phase 2 — Crypto localization — ~0.5 day

- Reimplement the XTEA-based packet encryption + MD5-derived key schedule in a local `Chaos.Client.Networking/Crypto.cs`.
- Swap [GameClient](../Chaos.Client.Networking/GameClient.cs) and the login-handshake caller off `Chaos.Cryptography`.
- Remove `Chaos.Networking`'s transitive Chaos.Cryptography dependency.

**Safe to do immediately** after Phase 1. Isolated, standard algorithms.

### Phase 3 — Packet args + converters + dispatcher — dedicated sprint

Recommended **only when a forcing function arrives** — i.e., the first modernization opcode that Chaos.Networking can't carry. Likely candidates (roughly in order of probability):

1. `ScriptInvoke` channel (stats-display, dialog, chat directions all converge on this)
2. `DialogGraph` opcode (if ScriptInvoke isn't ready first)
3. `CapabilityHandshake` (if the pack-negotiation work in UI/tiles/creatures modernization ships first)

**Sub-phases (within the sprint):**

3a. **Enum migration.** Clone `Chaos.DarkAges.Definitions` into a local `Chaos.Client.Protocol.Definitions` namespace. Replace all 48 files' `using Chaos.DarkAges.Definitions;` lines. Purely mechanical find-and-replace once the local enums exist. Low risk.

3b. **Args type migration.** Clone all 103 `*Args` classes. Group by opcode family (world, login, UI, combat). Migrate one group at a time; the whole repo builds and runs after each group because the dispatcher can route mixed legacy/local args during the transition (see D3).

3c. **Converter migration.** Port the byte-layout converters. For each opcode where the [compat matrix](hybrasyl-compat-matrix.md) flags Hybrasyl divergence, write the Hybrasyl-shaped converter directly — don't port the Chaos-Server-shaped one first.

3d. **Dispatcher migration.** [ConnectionManager.IndexHandlers](../Chaos.Client.Networking/ConnectionManager.cs) already uses array-indexed dispatch; swap the handler signatures to local args types.

3e. **Removal.** Drop the final two `PackageReference` entries for `Chaos.Networking` and `Chaos.DarkAges`.

3f. **Integration testing.** Against Hybrasyl QA: login → walk → receive every opcode type in the compat matrix → verify no regressions. Explicitly re-test the 4 pending-inspection opcodes from the Apr 2026 matrix (0x29, 0x0D, 0x32, 0x63).

## Decisions to flag

- **D1. New assembly or in-project?** Should the local protocol types live in a new `Chaos.Client.Protocol` assembly (clean dependency boundary, reusable by a future Godot client) or inline into `Chaos.Client.Networking` (simpler, fewer projects)? **Lean: new assembly.** The Godot-client bridge use case is the exact scenario the user cited when positioning this repo as a learning reference — protocol types are the most transferable artifact.
- **D2. Namespace scheme.** `Chaos.Client.Protocol.*` mirroring upstream? Or `Hybrasyl.Protocol.*` signaling the dialect explicitly? **Lean: `Chaos.Client.Protocol`** since the client's name stays `Chaos.Client` even though it targets Hybrasyl — keeps naming coherent.
- **D3. Mixed-args transitional dispatch.** During sub-phase 3b, can the dispatcher accept both Chaos-shaped and local-shaped args simultaneously, or does the migration have to be atomic? **Lean: mixed.** An adapter layer that converts Chaos args → local args during the transition keeps the tree green between commits.
- **D4. Preserve upstream PR channel?** If Hybrasyl contributes back to Chaos-Server on opcodes that happen to agree, is that a goal or a non-goal? **Lean: non-goal.** Hybrasyl is a fork-minded project; upstream alignment is incidental, not a sustained target.
- **D5. Enum parity with DarkAges.** The `Chaos.DarkAges` enums are exhaustive for every Dark Ages protocol version; we only need the subset Hybrasyl actually uses. Ship a trimmed set, or clone exhaustively? **Lean: clone exhaustively.** Trim later — incomplete enums surface as runtime cast errors, and the cost of carrying extra enum values is zero.
- **D6. Geometry type swap.** `Chaos.Geometry.Point` vs. `System.Drawing.Point` vs. `Microsoft.Xna.Framework.Point` (MonoGame already imports this). The rendering layer already uses MonoGame's `Point`; the networking layer uses Chaos.Geometry's. **Lean: introduce a local `Chaos.Client.Protocol.Point`** to avoid conflating wire-format geometry with render-format geometry. Conversion helpers at the boundary.
- **D7. Triggering condition for Phase 3.** Which forcing function actually starts the sprint? **Lean: the first modernization opcode that's blocked by upstream.** Writing it ourselves is cheaper than the Phase 3 sprint *until* you need the second, third, fourth custom opcode — at which point the sprint pays for itself immediately.

## How this interacts with other direction docs

- **[Dialog modernization](dialog-modernization-direction.md)** — D6 ("new opcode vs ScriptInvoke") directly depends on this doc's Phase 3 timing. If Phase 3 hasn't happened, dialogs ship over whatever transport we can hack into Chaos.Networking. If Phase 3 is done, dialogs get the cleaner path.
- **[Stats display](stats-display-direction.md)** — the endgame "property subscription" pattern wants `ScriptSubscribe` / `ScriptUpdate` opcodes. Phase 3 enables these cleanly.
- **[UI modernization](ui-modernization-direction.md)** — capability handshake opcode for pack/layout negotiation. Same enablement story.
- **Chat system direction (memory)** — new `ChatEvent` opcode with typed metadata is one of the forcing functions. If chat modernization ships first, it's the Phase 3 trigger.
- **Additive modernization pattern (memory)** — one-time capability handshake is the cross-cutting enabler. Phase 3 unblocks it.

## Summary table

| # | Decision | Lean |
|---|----------|------|
| D1 | New assembly or in-project | New assembly (`Chaos.Client.Protocol`) |
| D2 | Namespace scheme | `Chaos.Client.Protocol.*` |
| D3 | Mixed-args transitional dispatch | Mixed, via adapter |
| D4 | Preserve upstream PR alignment | Non-goal |
| D5 | DarkAges enum trimming | Clone exhaustively, trim later |
| D6 | Protocol Point vs MonoGame Point | Local `Chaos.Client.Protocol.Point`, convert at boundary |
| D7 | Phase 3 trigger | First modernization opcode blocked by upstream |

**Bottom line:** Phase 1 + Phase 2 are free wins (~1.5 days, no risk) and should land whenever convenient. Phase 3 is a 2–3 week dedicated sprint that earns its cost on the **second** custom opcode — trigger it when the modernization pipeline has enough momentum that multiple new opcodes are queued.

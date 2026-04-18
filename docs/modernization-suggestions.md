# Chaos.Client Modernization Suggestions

Companion to [hybrasyl-compat-matrix.md](hybrasyl-compat-matrix.md). This document captures the UX and architectural modernization directions surfaced during the initial Hybrasyl-compat exploration (April 2026).

## Context

This client is expected to serve as Hybrasyl's primary dev/QA client for approximately **2+ years** — long enough that investment in modernization pays back meaningfully. It is a transitional client bridging the legacy Dark Ages protocol era to a future Godot-based client. Terminal divergence from Sichii's upstream is accepted; nothing in this document is intended to flow back.

**Strategic filters** applied to every suggestion below:

1. **Additive, not surgical.** Legacy paths remain untouched; modern capabilities are new layers alongside. See the governing principle section below.
2. **Survives the Godot port.** Anything whose *code* ports forward is cheaper than work that is thrown away. Anything whose *design* ports forward (wire protocols, event models) also counts, even if the code is rewritten.
3. **Daily-use pain outranks marquee features.** A better inventory filter helps every player every session; a new raid system does not.

---

## Governing principle: additive modernization

Across every topic below, the recommended strategy is to **build new layers alongside legacy, not refactor or replace existing code.** Legacy code and protocol paths stay intact and functional. Modern features are built on parallel stacks. Migration happens naturally as content/features move over. Eventually the legacy path has no callers and gets deleted — but never in a big bang.

The pattern in practice:

| Area | Legacy stays | New layer |
|---|---|---|
| Asset formats | HPF/EPF/MPF loaders unchanged; still serve legacy DAT archives | Override hook checks modern container first, falls through if absent |
| Ability protocol | 8-value `SpellType` enum and cast packet unchanged | Extended enum + flexible cast packet + skill target parity for modern clients |
| Combat timing | Line-count cast path stays for legacy castables | Explicit `castTimeMs` parameter + weapon speed stat for modernized castables |
| Movement | Stepped-offset path remains for non-player entities | Player gets a true-lerp path via modernized `AnimationSystem` |
| Chat / combat log | `ServerMessageType` routing + 3-surface hairball untouched | New `ChatEvent` opcode with ushort length + typed metadata |
| Inventory | 60-slot flat model stays for server compat | Client-side categorization/search/filter/tag layer |
| Combat visibility | Current buff bar + no damage numbers stays | Structured combat events drive floaters, cast bars, buff-duration UI |
| Keybinds / UI state | Hardcoded hotkey table stays as default | Rebind UI + persistent settings store override the defaults |

**Why this works better than refactoring:**

- Zero risk to existing gameplay.
- Parallel work streams — server team adds new opcodes while client team builds new panels; neither blocks the other.
- The capability handshake (below) is a one-time investment that every subsequent modern feature reuses — second, third, fourth additive layers get progressively cheaper.
- Natural deprecation: when legacy has no callers, delete it; no flag-day required.
- Survives the Godot port: Godot speaks only the modern protocols, so anything built on the additive layer ports forward; anything on legacy simply doesn't.

---

## Capability handshake (one-time prerequisite)

Every modernization track below depends on the server knowing which clients speak the new opcodes. Add a **capability bitmask** to the login handshake: one ushort (or small set of bytes) where each bit indicates support for a specific modern feature. Server routes accordingly — modern client gets new opcode, legacy client keeps getting the legacy format.

**Effort:** ~2-3 days. Amortizes across every track below. If not already in place, this is the first piece of work.

---

## Modernization tracks

Each track has: current-state summary, proposed direction, rough effort, strategic value.

### 1. Unified asset format + modern dimensions/density (replacing HPF/EPF/MPF/SPF/EFA)

**Current.** Legacy palettized formats in DAT archives. Dye system constrained to 6 palette slots. Palette cycling for water/lava/glow requires a dedicated subsystem (`PaletteCyclingManager`). All assets authored at the 1999 tile grid: **56×27 isometric tiles** (half-tile 28×14) at **1× pixel density**. Virtual resolution is 640×480. On modern 1080p+ displays this looks correspondingly tiny. Artist authoring is through HPF-aware tooling; no direct PNG/Aseprite workflow.

**Proposed.** Single ZIP container per asset with `manifest.json` + `frames/N.png` + `frames/N_mask.png`. Dye via base+mask compositing at load time (CPU-side SkiaSharp, no shader authoring) — R/G/B/A mask channels encode dye regions. Supports Destiny-style per-region coloring with strictly more flexibility than the 6-slot palette model. Palette cycling gets pre-baked to explicit frames via a DALib-based build tool.

The manifest also carries **dimensions and density as metadata**, letting the client dispatch per-asset:

```json
{
  "tile_dimensions": { "width": 64, "height": 32 },
  "density": 2,
  "anchor": { "x": 32, "y": 27 }
}
```

New assets target **64×32 tiles** (industry-standard isometric, what every external tool — Tiled, Aseprite templates, Godot's tileset system — expects) at **2× density** (clean double-size pixel art). Legacy assets, served by the legacy loaders, implicitly declare 56×27 at 1×. A given map is either one or the other — never mixed on the same viewport — because the isometric projection math is fixed per map.

Virtual resolution doubles to 1280×960 when the active map is modern-format. Legacy UI prefab panels point-filter upscale (the global `PointClamp` already makes this look clean on pixel art). UI prefab 2× variants can ship later as polish.

Authoring: Aseprite native — layers map to dye masks, frame tags to animation semantics, slice tool to anchor points. A build step exports `.aseprite` → runtime ZIP.

**Effort.**

Asset pipeline core:

| Work | FTE-weeks |
|---|---|
| Override hook in `TileRepository` + `MapRenderer` (foregrounds only, static tiles) | 1-2 |
| Base+mask compositor (SkiaSharp CPU path) | 0.5-1 |
| ZIP container + manifest schema + loader + caching | 1-2 |
| Extend to EPF (UI), MPF (creatures), SPF (UI sprites), EFA (effects) | 2-4 each, shared plumbing discounts ~50% |
| Aseprite build tool (AsepriteDotNet integration) | 1 |
| Palette cycling pre-bake tool + content migration run | 0.5-1 |
| **Asset pipeline subtotal** | **6-10** |

Modern dimensions + density (coupled to the above):

| Work | FTE-weeks |
|---|---|
| Tile dimensions + density as manifest metadata | 0.5 |
| Renderer reads dims per map instead of global `CONSTANTS` | 1-2 |
| `Camera`, walk-offset, foreground draw-position math parameterized by tile dims | 1-1.5 |
| Virtual resolution doubling (640×480 → 1280×960 for modern-format maps) | 1-2 |
| UI HUD layout pass + point-filter upscale of legacy prefabs | 2-3 |
| Font atlas multi-density | 0.5-1 |
| QA regression pass (mouse picking, painter's algorithm, bubbles, bars, chat anchors) | 1 |
| **Dimensions/density subtotal** | **7-11** |

Combined:

| Total | FTE-weeks |
|---|---|
| **Dev total (asset pipeline + dimensions + density)** | **13-21** |
| Artist migration (re-authoring at 64×32, 2× density) | Open-ended, artist-months |

**Status: ability-icon pilot shipped (April 2026).** The first production slice of Track 1 is in place — `.datf` pack format, `AssetPackRegistry` discovery, `IconPack` ZIP loader, `IconTexture` offset-aware rendering, and modern-first/legacy-fallback dispatch for skill/spell icons. The legacy `skill002/003.epf` and `spell002/003.epf` "learnable/locked" sheets have been retired; those states now render as tints on the base icon. See [ability-icons-pilot-plan.md](ability-icons-pilot-plan.md) for the implementation record and [asset-pack-format.md](asset-pack-format.md) for the artist-facing format spec. Remaining Track 1 work extends this scaffolding to tiles, creatures, UI sprites, and effects — each new `content_type` reuses the existing `.datf`/manifest/registry plumbing rather than building new.

**Strategic value.** Highest of any item in this doc. Three compounding wins:

1. **Dye model** — strictly more expressive than the 6-slot palette ceiling, supports HSL shift / saturation / additive / per-region coloring for free.
2. **Industry-standard dimensions** — 64×32 means any external artist tool works without relearning. Tiled maps import cleanly. Future Godot port's `TileSet` node consumes the same assets natively.
3. **Modern density** — 2× looks correct on modern displays instead of tiny-on-a-4K-monitor. No loss of pixel-art aesthetic (PointClamp sampling stays).

Coupling these together is a discount: dimensions + density cost ~7-11 weeks when bolted to the format work but would cost 2-3 extra weeks if retrofitted later (format architecture would need revisiting). Doing them separately from the asset format is impossible — legacy assets can't be cleanly upscaled to 64×32 due to non-integer ratios (64/56 = 1.143).

Supersedes the `PaletteCyclingManager` subsystem entirely. Every hour of artist re-authoring doubles as Godot port prep — assets produced survive the port untouched, and the modern dimensions match what Godot expects natively.

---

### 2. Movement modernization

**Current.** Walk animation uses stepped offsets at 114ms per frame × 4 frames = **456ms per tile**. Visual offset snaps to 4 (or 8 w/ smooth) discrete positions over that 456ms — motion updates at ~17Hz on a 144Hz monitor. Integer-only offsets with 2:1 isometric quantization (code comment cites "1px wobble" rationale, likely a bug in a neighboring system). Turn-then-walk is two keypresses. `WALK_QUEUE_THRESHOLD = 0.75` creates a ~340ms input deadzone where next step can't be buffered.

**Proposed.** True per-frame offset lerp driven off elapsed time (decouple body position from sprite-frame stepping — legs animate in steps, body glides). Reduce `DEFAULT_WALK_FRAME_MS` from 114 toward ~70. Collapse turn-and-walk into a single intent when idle. Lower queue threshold. Delete integer-snap logic.

**Effort.**

| Work | FTE-weeks |
|---|---|
| Feel-fix pass (speed const, lerp, turn-collapse, queue threshold, drop quantization) | 1 |
| Per-entity speed property (mount prerequisite) | 0.5-1 |
| Smooth chase camera for open-world | 0.5-1 |
| Mount plumbing (server + client) | 1-2 |
| **Total** | **3** |

**Strategic value.** Huge immediate impact for tiny effort. First change on this list you should make — transforms play feel and unblocks informed feedback on every other combat/timing/ability decision. Mount and open-world plans both depend on it.

---

### 3. Ability protocol exposure (skills/spells unification)

**Current.** Hybrasyl server has a rich `Castable`/`CastableIntent` system (`Line`, `Cone`, `Square`, `Tile`, `Cross`) but it is trapped behind the 1999 8-value `SpellType` enum and single-target cast packet. Skills are worse: `UseSkill(byte slot)` has no targeting payload at all — melee-only by protocol.

**Proposed.** Extend (or fork) `SpellType` with UX-hint values (`TileTargeted`, `LineTargeted`, `ConeTargeted`, `Directional`). Extend cast packet to carry entity ID *or* tile coords *or* neither. Mirror on skill side — `SkillTargetType` analog and parallel cast-with-target packet. Client UX: targeting reticle, AoE radius preview, ground-aura rendering.

Ground auras and channeled spells are genuinely new features (not yet implemented server-side). Once the protocol work above is done they become parameters in the same system rather than separate projects.

**Effort.**

| Work | FTE-weeks |
|---|---|
| `SpellType` extension + flexible cast packet (server + client) | 1 |
| Skill target parity | 1-2 |
| Client UX: reticle, AoE preview, ground-aura render | 2 |
| Channeled spells (new feature, server + client) | 3-4 |
| Auras / ground effects (new feature, server + client) | 2-4 |
| **Protocol exposure only** | **3-4** |
| **+ channels + auras as new features** | **+5-8** |

**Strategic value.** High. Hybrasyl already has the internals — this surfaces existing capability, not builds from scratch. Unlocks ground-targeted AoE, line skills, cone AoE — all standard modern MMO toolkit features.

---

### 4. Combat timing decoupling

**Current.** Cast time is implemented as chant-line count × 1 second. `SpellSlot.CastLines` doubles as both RP chant length and timing mechanism. A 5-line spell takes 5 seconds. Weapons have no per-item speed stat. Haste/slow has no clean insertion point because cast duration is derived from line count.

**Proposed.** Explicit `castTimeMs` parameter per castable. Chant lines become pure RP flavor — emit as `/say` or `/emote` chat during the cast window, independent of timing. Proper progress bar drives visible cast. Per-weapon attack-interval property. Haste/slow as first-class multipliers applied uniformly to cast time and weapon speed.

**Effort.**

| Work | FTE-weeks |
|---|---|
| Decouple cast time from lines (server + client + progress bar UI) | 1 |
| Chant lines as /say or /emote chat | 0.5 |
| Weapon speed stat (server schema + combat resolver + client tooltip) | 1 |
| Haste/slow multipliers | 0.5-1 |
| **Total** | **3-4** |

**Strategic value.** Medium-high. Unblocks combat-build diversity (weapon-speed matters, cast-speed buffs matter), removes an immersion-breaking coupling (adding chant lines to make a spell slower). Prerequisite for channel UX since channel duration and tick interval are both explicit parameters.

---

### 5. Chat system dual-stack (with combat log)

**Current.** Client-side is a flat `CircularBuffer<ChatMessage>` where `ChatMessage = (string Text, Color Color)`. Metadata lost on insertion. Three redundant UI surfaces populated by hand for every message category. Server-side combat log is a `/combatlog` command that pushes `ICombatEvent.ToString()` output through the group-chat channel. Legacy chat opcodes have an effective ~50-60 char payload cap after prefix overhead, so combat events routinely truncate mid-sentence.

**Proposed.** Legacy opcodes stay as-is. New `ChatEvent` opcode (e.g., 0xB0) with ushort length prefix (65535 cap), typed `ChatChannel` enum (Public, Whisper, Group, Guild, System, Combat, Loot, Quest, Trade, ...), source/target entity info, and optional typed payload (combat: amount, eventType, castable, crit, etc.). Client registers for new opcode via capability handshake; gets structured events.

Client-side: new `ChatService` holds a single `CircularBuffer<ChatEvent>`. UI surfaces (chat panel, combat log, orange bar, system pane) subscribe via filter predicates. Tabs are multi-panel with different filters.

**Effort.**

| Work | FTE-weeks |
|---|---|
| Server: new opcode + struct + emission sites + capability byte | 2-3 |
| Client: `ChatService` + filtered panels + combat log panel | 3-4 |
| Per-channel config (color, sound, notif), search, tab UI | 1-2 |
| **Total** | **6-9** |

**Strategic value.** High. Affects every session. Combat log becomes a real data view (sortable, filterable by amount, threat analysis). Chat metadata enables features like @-mentions, private tabs, channel mutes. Design ports directly to Godot.

---

### 6. Inventory system

**Current.** Flat 60-slot grid. No bags, no tabs, no categories (gear/consumable/quest/junk all in one list). No sort, no search, no filter. No equipment comparison on hover. No bulk actions (mass-sell, mass-destroy, split-stack-by-input). Stack sizes presumably capped low. Bank/storage — unconfirmed whether it exists; if so, likely same-but-separate.

**Proposed.** Client-side structured item model: per-item metadata (category, tags, rarity, level, stats, equipment-slot fit, stackable?, max-stack). Categories drive filter pills ("show consumables only"). Fulltext search by name. Sort by name/type/value/level. Equipment comparison tooltip ("this is +3 Strength vs what you're wearing"). Bulk ops (shift-click range select, mass-sell to vendor, mass-split).

No server changes strictly required — the structured metadata can be derived client-side from item icons/types if the server doesn't send it. Optional server work: send the metadata explicitly instead of forcing client inference.

**Effort.**

| Work | FTE-weeks |
|---|---|
| Structured item model + client-side metadata table | 1 |
| Filter/search/sort UI | 1-2 |
| Equipment comparison tooltips | 1 |
| Bulk operations | 1 |
| Bank integration if exists | +1 |
| **Total** | **4-6** |

**Strategic value.** High. Daily-use pain — every player hits this every session. Design ports to Godot directly. Low risk (client-side only for the core work).

---

### 7. Combat visibility (damage numbers, cast bars, buff timers)

**Current.** No floating damage or heal numbers over entities. Probably no target cast bar (can't see "enemy is casting Fireball, interrupt now"). `EffectBarControl` shows buffs/debuffs but likely without prominent duration countdowns, without distinguishing your-buffs vs enemy-debuffs-on-you, without right-click cleanse. No threat meter, no DPS readout.

**Proposed.** Piggyback on the structured combat events from track 5. Client subscribes to `ChatChannel.Combat` events and also routes them to floater renderers — damage numbers on the target entity's position, color-coded by damage type (physical/magical/true/heal). Cast bar renders over entities in the viewport when their `IsCasting` flag is set (requires server to broadcast cast state). Buff bar gets per-effect duration countdowns, hover tooltips, right-click to dispel (where permitted), separation of self-buffs/debuffs/enemy-debuffs-I-cast.

Threat meter and DPS readout are derived client-side from the structured combat event stream.

**Effort.**

| Work | FTE-weeks |
|---|---|
| Floating damage/heal numbers (consumes track-5 combat events) | 1 |
| Target cast bar (requires server `IsCasting` broadcast) | 1-2 |
| Buff bar improvements (durations, tooltips, dispel) | 1-2 |
| Threat meter (derived from combat stream) | 1 |
| DPS readout | 0.5 |
| **Total** | **4-6** |

**Strategic value.** Medium-high. Combat feel. Depends on track 5 being in flight or done — can run in parallel with its later phases. All client-side once the combat event stream exists.

#### Sub-item: smooth buff-duration bar

Concrete design decision for the "Buff bar improvements" row above. The current buff bar is **server-driven and step-based** — server sends `EffectArgs(iconId, EffectColor)` where `EffectColor` is an enum (White/Red/Orange/Yellow/Green/Blue) that maps to fixed fill levels (7/7 down to 2/7). As the buff ages, the server pushes discrete color updates. The client doesn't know remaining duration and can't animate smoothly.

**Desired behavior:** bar stays blue and full until 30 seconds remain; last 30 seconds deplete smoothly with a green-to-red gradient. Solid "safe" signal for the bulk of the duration, escalating visual warning as it runs out.

**Required protocol extension.** Either extend `EffectArgs` or add a new opcode that carries `totalDurationMs` and `remainingMs` (or `expiresAt` timestamp). Client stores expiry per effect, ticks locally each frame:

```csharp
var remainingSec = Math.Max(0, expiresAt - Environment.TickCount) / 1000f;

if (remainingSec > 30f)
{
    Bar.Percent = 1f;
    Bar.FillColor = Color.Blue;
}
else
{
    var t = 1f - (remainingSec / 30f);                   // 0 at 30s, 1 at 0s
    Bar.Percent = remainingSec / 30f;
    Bar.FillColor = Color.Lerp(Color.Green, Color.Red, t);
}
```

Server continues emitting the old `EffectColor` step updates for legacy clients (additive-layer pattern). Modern client receives the richer duration payload and ignores the color enum when present.

**Effort:** ~1.5-2 FTE-weeks, within the 1-2 week "Buff bar improvements" line above. Breakdown: 2-3 days protocol + Hybrasyl-side duration plumbing, 3-4 days client local-tick + gradient math, 2-3 hours smooth-arrival logic for mid-countdown buff refreshes, 1-2 days edge-case testing (short/long buffs, stacking, reconnection, time skew).

**Free adjacent wins once duration is client-side:** hover tooltip showing "2:47 remaining" on buff icons; flash animation at 10s/5s/1s thresholds; optional audio cue before a critical buff drops; Godot port gets all of this identically since it's data-driven.

**Cheap alternative (not recommended):** client-side approximation by repurposing the existing color-enum updates as time-bucket markers (Green means "30s remaining", start local timer from there). Works for typical durations but desyncs on buff refreshes, atypical durations, and reconnection. Listed for completeness; path A is the right choice.

---

### 8. Keybind customization + persistent UI state

**Current.** Hotkey table (A=Inventory, S=Skills, D=Spells, F=Chat, G=Stats, H=Tools, F7=Mail, F8=Group, F10=Friends, numbers=slot cast, Ctrl+N=emotes, etc.) is hardcoded. Chat buffer lost on close. Window/panel positions reset each session. [SettingsControl](Chaos.Client/Controls/World/Popups/Options/SettingsControl.cs) likely covers only volume and display options. No accessibility toggles, color-blind modes, or UI-scale-per-panel.

**Proposed.** A keybind table backed by a persistent settings file (JSON in user AppData). Rebind UI in settings: click a binding, press new key, conflict detection. Chat log persisted to disk (rolling file). Panel positions/sizes saved per character. Settings surface gains accessibility toggles (high-contrast mode, larger fonts, reduced motion for the walk-lerp work).

**Effort.**

| Work | FTE-weeks |
|---|---|
| Persistent settings store (JSON, per-character profiles) | 1 |
| Rebind UI | 1-2 |
| Chat history file + replay on session start | 0.5-1 |
| Panel position persistence | 1 |
| Accessibility settings surface | 1 |
| **Total** | **4-6** |

**Strategic value.** Medium. Not flashy, but a huge retention factor. QWERTY-only defaults immediately bounce anyone on AZERTY/Dvorak/Colemak, anyone with RSI who has their own bindings, anyone with motor-accessibility needs. Design ports directly to Godot (settings file + keybind schema are engine-agnostic).

---

### 9. Plugin architecture (two-tier, AGPL-constrained)

**Current.** No plugin or scripting system. All features are compiled into the monolithic client. Hybrasyl has no way to ship proprietary modules (anti-cheat, telemetry, closed-source competitive features) without running afoul of the AGPL-3.0 viral copyleft that covers the existing codebase.

**Legal constraint — mandatory reading before designing this.** AGPL treats dynamically-loaded plugins that form a "combined work" with the host as derivative works. In-process closed-source C# plugins distributed alongside the client would be an AGPL violation. Adding a plugin exception to the license isn't possible unilaterally — Sichii holds the copyright on the bulk of the code, not Hybrasyl. Relicensing requires every contributor's consent. These paths are closed.

**Proposed.** A **two-tier plugin model** that's legally clean under AGPL:

**Tier 1 — Lua scripting (open-source only).** A sandboxed Lua runtime inside the client with a restricted API (`Jint` for JS or `MoonSharp` for Lua; both well-maintained C# libraries). Scripts can:

- Hook UI events (panel opens, hotkey presses, chat messages received)
- Call a curated API (`chat.send`, `inventory.useItem`, `ui.showPopup`, etc.)
- Read published state (player HP, target, buffs — read-only snapshots, not raw packets)

Scripts are *data* consumed by the client, not linked code. Same legal posture as WoW addons: modders can write and distribute these without license concerns. Ecosystem-enabling for UI themes, chat macros, auto-walkers, quest trackers, RP helpers.

**Tier 2 — Out-of-process plugins (closed-source capable).** The client hosts a plugin manager that spawns plugins as separate processes, communicating over localhost IPC (WebSocket or stdio + JSON/protobuf). Each plugin is a standalone binary in any language (C#, Rust, Go, Python). Hybrasyl (or any team) owns the copyright on each plugin; the plugin has its own license independent of the client.

The FSF position and multiple court interpretations treat separate-process communication as "two programs that talk" — not a combined derivative work. Blender, GIMP, Qt, and modern anti-cheat suites (EasyAntiCheat, BattlEye) all use this exact architecture to accommodate proprietary extensions under copyleft hosts.

Message protocol is small and versioned — plugins register for events (`packet_sent`, `packet_received`, `state_changed`, `hotkey_pressed`) and send commands (`modify_state`, `inject_packet`, `show_overlay`). Plugin host handles lifecycle (launch, restart, kill, crash-isolation).

**Anti-cheat specifically.** Out-of-process anti-cheat is becoming the industry standard because it's more secure than in-process checks. An external watcher is harder for cheaters to attack than hooks inside the game process. Modern solutions work exactly this way. For Hybrasyl, this also happens to be the legally-clean path — two good reasons stack on one decision.

**Effort.**

| Work | FTE-weeks |
|---|---|
| IPC protocol design (JSON or protobuf schema, event/command model, versioning) | 1 |
| Plugin host in the client (launch/restart/kill lifecycle, crash isolation) | 1-2 |
| Event bus from client internals to IPC layer (packet hooks, state-change events, hotkey hooks) | 2 |
| Lua scripting runtime + sandboxed API (can defer to phase 2) | 2-3 |
| Documentation + example plugins (open-source reference implementations) | 1 |
| **Total** | **7-9** |

Closed-source plugins that the Hybrasyl team authors are separate work outside this scope — that's a private backlog.

**Strategic value.** Durable infrastructure that:

- Unblocks proprietary anti-cheat without waiting for the Godot port.
- Ports cleanly: IPC protocols don't care about the host, so the same plugins work against a Godot client with minor adjustments (the shim between the new host and the existing IPC protocol).
- Opens a modding ecosystem (Lua tier) without licensing headaches.
- Lets the team ship private/competitive features without license conflicts.

**Network-clause caveat.** AGPL §13 requires source availability when the client is offered as a network service. IPC-connected plugins running locally alongside the client don't trigger this. But a cloud-streamed / remote-rendered version of the client would — worth flagging if that's ever on the roadmap.

---

## Backlog (lower priority, not yet scoped)

Listed without effort estimates — each is real pain but lower-leverage than the eight tracks above. Several mostly benefit the Godot port rather than this bridge client, so can be deferred.

- **Macros.** `MacrosListControl` exists but legacy DA macros are "send these chant strings, cast this spell." No castsequence, no conditionals (cast X if target < 50%), no focus targeting, no targeting priority. Power-user content; blocks expert play.
- **Social systems.** Friends list flat, no notes/categories/online notifications. Guild UI likely lacks officer permissions, MOTD formatting, rank management, member notes, guild bank. No group finder.
- **World traversal.** No persistent corner minimap — Tab toggles full map. No fog-of-war reveal history, no ping-my-location-to-party, no annotations.
- **NPC interaction.** Single-threaded dialog (can't browse multiple NPCs at once). No quest journal separate from the NPC. Dialog history lost on close.
- **Mail.** Current Boards/Mail UI is clunky — no attachments, no rich formatting, tied to mailbox objects. A structured-message-aware mail system with attachment support would be a major UX step.
- **Vendor interface.** `MenuShopPanel` — no bulk buy, no buy-out, no search, no sort by price/type, no confirm-before-buy-expensive. Overlaps with inventory system (track 6).
- **Bank / storage.** Unconfirmed if it exists in the current client. If so, presumably same-but-separate to inventory.
- **Exchange / trading.** `ExchangeControl` exists — probably legacy trade-window with limited safety (no second-confirm on valuable items, no trade history).
- **Death / respawn flow.** Unexamined.
- **Ground item pickup.** Probably key-press-while-standing-on-it. Click-to-pickup from adjacent tiles would be normal.
- **Logout to character select.** Currently reconnects to lobby — no smooth character swap.

---

## Architectural debt (not user-visible; blocks future work)

- **`WorldState` is entirely static.** Makes scene testing fragile, state migration on character swap painful, any "offline demo mode" impossible.
- **UI layout is legacy prefab-driven.** `_nXXX.txt` + `.spf`/`.epf` control files define rigid pixel-coordinate layouts. Adding a new button means editing a binary-adjacent text file from 1999. No flex/grid/responsive layout.
- **UI is packet-driven.** Most panels have no existence before a server message shows them. Character-select, test flows, offline screens all fight this.
- **`Chaos.Networking` coupling.** Every protocol modernization (tracks 3, 4, 5) has to work around the NuGet library's baked-in assumptions. A Hybrasyl-specific network layer forked or wrapped from `Chaos.Networking` would be more sustainable if this client's lifespan is truly 2+ years.

None of these are tracked as modernization work here — they are the *reasons* the tracks above are scoped as additive layers. Address them only if a specific track lands on one as a hard blocker.

---

## Effort rollup

| Track | FTE-weeks |
|---|---|
| Capability handshake (prerequisite) | 0.5 |
| 1. Unified asset format + modern dims/density (dev only; artist migration separate) | 13-21 |
| 2. Movement modernization (incl. mounts + chase cam) | 3 |
| 3. Ability protocol exposure (not counting channels/auras) | 3-4 |
| 3a. Auras + channels (new features) | 5-8 |
| 4. Combat timing decoupling | 3-4 |
| 5. Chat system dual-stack | 6-9 |
| 6. Inventory system | 4-6 |
| 7. Combat visibility (depends on track 5) | 4-6 |
| 8. Keybind + persistent UI state | 4-6 |
| 9. Plugin architecture (Lua + out-of-process tier) | 7-9 |
| **Totals** | |
| Must-haves for bridge-client modernization (1-9 minus 3a) | ~48-68 |
| + auras/channels | ~53-76 |

Parallelizable:
- Asset format (1) is client-only for the dev portion — no server coordination.
- Movement (2), Inventory (6), Keybinds (8) are client-only or client-dominant.
- Ability (3), Combat timing (4), Chat (5) all touch server and can be sequenced behind the capability handshake.
- Combat visibility (7) waits on track 5 for the structured combat events but its UI work can precede.

**Two-engineer calendar estimate:** ~5-7 months elapsed for the must-haves, assuming one on client-dominant work (track 1's asset format + dimensions, tracks 2/6/8) and one on server-heavy tracks (3/4/5). Track 1's scope is large enough that on a two-engineer team it effectively defines the calendar length; the other tracks finish inside track 1's window.

**Recommended sequence** for maximum leverage per week of work:

1. **Movement feel-fix** (1 week) — transforms play experience, informs all combat feedback later.
2. **Capability handshake** (0.5 week) — prerequisite for everything else.
3. **Asset format foundation: override hook + base+mask composite for foreground tiles** (1-2 weeks) — starts yielding modern assets immediately, even while broader format work continues in parallel.
4. **Chat dual-stack** (6-9 weeks) — unblocks combat visibility.
5. **Combat timing decoupling** (3-4 weeks) — unblocks channels.
6. **Ability protocol exposure** (3-4 weeks) — parallel with 4-5.
7. **Inventory + keybinds + combat visibility** (10+ weeks, parallelized across the above).
8. **Asset format rollout to remaining formats** (4-6 weeks) — continuous effort alongside everything.
9. **Auras + channels** — once 3, 4, 5 are in.

---

## What port to Godot

When the Godot client project begins, the following items' **code** can be lifted or adapted directly:

- Movement system (now written around per-entity speed + lerp)
- Keybind schema and settings file format
- Chat event model (same `ChatEvent` struct and `ChatChannel` enum)
- Inventory item model and filter/search UI patterns
- Asset format (same ZIP containers, same manifest schema). Assets authored at 64×32 at 2× density drop into Godot's `TileSet` natively — no dimension or density conversion needed at port time.
- Plugin IPC protocol. Closed-source plugins built against the out-of-process IPC in Track 9 continue to work against a Godot host with only a thin shim between the new host and the existing plugin protocol.

The following items' **designs** carry forward but code will be rewritten:

- Ability protocol (Godot can skip the `SpellType` legacy enum entirely and expose Hybrasyl's intent vocabulary natively)
- Combat timing model (`castTimeMs`, weapon-speed, haste/slow multipliers — same parameters, new implementation)
- Combat visibility rendering (different renderer, same event source)

The following items **do not port** because they are compensating for legacy constraints Godot does not have:

- `PaletteCyclingManager` (palette cycling is gone after the asset format migration)
- HPF/EPF/MPF legacy loaders
- Legacy `SpellType`/`ServerMessageType`/`PublicMessageType` enums and the compat shims around them
- Prefab-file UI system (replaced by Godot scene tree)

This provides a concrete filter when scoping each track: *does the work survive the port?* If yes, the investment is long-term; if no, keep it minimal and bounded.

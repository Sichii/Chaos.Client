# Stats Display Direction — Derived Stats, Shared Formulas, and the G/Shift+G Pane

Design doc for modernizing how player-visible stats — especially **derived stats** like crit chance, hit chance, gear score, effective damage — are computed and displayed. Captures the "client computes for display, server computes for authority, both use the same formula" pattern and its implications for the Chaos.Client UI. Not an implementation plan — decisions flagged inline.

## Motivation

The G (stats) and Shift+G (extended stats) HUD panels today are:

- Hand-drawn labels ("AC", "DMG", "HIT", offense element, defense element, magic resistance) baked into the `_nstatus` / `_nstatur` prefab backgrounds as PNG text
- Fixed-shape fields driven by a fixed `AttributesArgs` packet from the server
- No headroom for adding new derived stats (crit chance, gear score, weapon DPS, cast-time multipliers, resistance breakdowns, buff aggregates) without repainting art and extending `AttributesArgs`
- Hybrasyl's **Martial Awareness** feature — a player-visible display of derived combat stats — has no natural home in this shape and ends up awkwardly bolted on

The discussion that produced this doc also surfaced a design concern from Kedian: if derived stats (e.g. DEX contributing to crit chance) are computed server-side, does that force "constant server recalculation"? Clarifying that concern is one of the goals here.

## The modern MMO pattern (WoW, FFXIV, GW2, ESO, Lost Ark)

Universal across every major MMO:

> **Server is authoritative for combat. Client is authoritative for display. Both compute derived stats using the same formulas.**

Concrete flow:

1. **Primary stats** (STR, DEX, CON, INT, WIS, gear bonuses, active buffs) live on the server as state of record.
2. **Server pushes primaries to the client** *only when they change* — level-up, equip change, buff apply/fade. Not per-tick. A handful of updates per minute at most.
3. **Client maintains a full mirror** of primary stats in its ViewModel (already true today for `AttributesArgs`).
4. **Both sides independently compute derived stats** from the primaries, using the same formulas. Client for UI display; server for combat resolution.
5. **Server rolls against the derived value** during combat. Client displays what the derived value *would be*. They agree by construction because they computed from identical inputs.

This is *not* "client-side calculation" in the authoritative sense — combat rolls stay server-side to prevent trivial cheating. It's "client-side computation for display purposes," which is how every MMO has done it since EverQuest.

### Clarifying the server-load concern

Kedian's worry about "constant server recalculation" has a false premise: **primary stats don't change constantly.** Equipping, buffing, leveling, dying — these are seconds-apart events, not per-frame state churn. Recomputing derived stats on each primary-stat change is trivial arithmetic a few times per minute. Crit rolls happen every swing, but each roll is a formula application, not a state recompute.

What's true: **you want dirty-flag or event-based recomputation, not per-tick polling.** Primary stat changes fire events; derived stats recompute on those events; cached derived values serve all queries until the next change. This is standard computed-property pattern from any GUI framework (Vue computed props, MobX derivations, React useMemo, etc.).

So the "must be client side" framing is half-right and half-wrong:

- **Right:** client must compute derived stats locally for UI display. There's no alternative — you can't round-trip the server every time the character sheet is open.
- **Wrong:** client must be the *authority* for combat rolls. That's cheating-as-a-service. A hostile client would set local crit to 100%.

The hybrid — server authoritative, client display mirror — is what everyone does.

## Shared-script formulas: the compelling architectural move

Once you accept "both sides compute derived stats," the next question is: **how do you prevent the server and client formulas from drifting apart?**

Every MMO struggles with this. Patch notes say "DEX contributes 0.5% crit per point"; three patches later the server formula quietly changes; the client still says 0.5% per DEX; players datamine, find the mismatch, forums explode.

The elegant solution, and one Hybrasyl is uniquely positioned to pull off given its Lua scripting culture: **formulas ship as shared Lua scripts loaded by both server and client.**

```lua
-- formulas/combat.lua  (loaded by server AND client)
function calc_crit_chance(char)
  local base = 0.05
  local dex_bonus = char.dex * 0.005
  local gear = sum(char.equipment, function(e) return e.crit_rating or 0 end) / 10000
  local buffs = sum(char.buffs_active, function(b) return b.crit_bonus or 0 end)
  return math.min(1.0, base + dex_bonus + gear + buffs)
end
```

- Server calls `calc_crit_chance(char)` to resolve the actual crit roll during a swing.
- Client calls `calc_crit_chance(char)` (on its mirrored `char`) to render "Crit: 12.4%" on the extended stats panel.
- Values are **guaranteed to agree** because it's the same code running on the same inputs.
- Patch-note drift is impossible by construction — the script *is* the documentation.

This is also the **same abstraction** discussed separately for UI property subscription (the "can the UI poll via scripting like scripts can?" thread). Unified: scripts are the extensibility surface for both server-side derivations and client-side display, running in both locations.

## Migration path for the G and Shift+G panels

### Short-term (no server cooperation needed)

Replace the hand-drawn labels with data-driven `UILabel` controls, and add client-side derived-stat computation from the existing `AttributesArgs` + `EquipmentArgs` + `Inventory` + buff state already mirrored on the client.

Concretely:

1. Ship a modern `_nstatus` / `_nstatur` background (via `content_type: "ui_prefabs"` override when UI modernization ships, or a loose override in the interim) with the field-name text removed.
2. Add `UILabel` controls in `ExtendedStatsPanel` for field names (e.g. "AC", "DMG") alongside the existing value labels.
3. Add a derived-stats layer in `PlayerAttributes` or a new `DerivedStats` ViewModel that computes from primaries. Example methods:
   - `CalculateCritChance()`
   - `CalculateHitRate()`
   - `CalculateEffectiveDamage()`
   - `CalculateGearScore()`
   - `CalculateTotalBuffPower()`
4. Re-compute on primary-stat-change events; cache results; panels read cached values.
5. Panels render whatever fields their prefab declares, filled from this layer.

This gets Hybrasyl the "more fields" they want immediately, with the formulas living in C#. Formula drift vs. server is a known limitation documented inline.

### Medium-term (with shared-script system)

When Hybrasyl's scripting architecture supports client-loadable formula scripts:

1. Move the derived-stat formulas from C# to shared Lua scripts.
2. Server loads the same scripts for combat resolution.
3. Formula updates ship as script-bundle updates, server and client pick them up simultaneously.
4. Script-based formulas can be modded / plugin-extended without touching client C#.

This is the endgame architectural direction, dependent on:

- Lua runtime in the client (see plugin architecture direction in memory — scripting is already a planned extensibility surface).
- A shared-script distribution mechanism (likely part of the `.datf` pack system — packs can ship `scripts/*.lua`).
- Capability handshake so old servers fall back to the C# derivation.

### Long-term (property subscription for non-derivable fields)

For stats the client *cannot* compute (server-private state: dungeon-wide luck modifiers, event bonuses, hidden mechanics), use the `ScriptInvoke` / `ScriptSubscribe` property-subscription pipe from the stats-polling direction discussion. Panel declares the properties it wants; server runs a script; result streams back.

This is the fully-modern state: **primary stats push, derived stats local-compute, server-only stats script-subscribe.** Every stat in the client has a clean provenance.

## Martial Awareness — the canonical use case

Hybrasyl's Martial Awareness feature — a player-visible display of derived combat stats — is the most concrete justification for this direction. Under the current architecture it has nowhere natural to live: the stats panel is hand-drawn, `AttributesArgs` is fixed-shape, and there's no property pipe for arbitrary derived fields. Today it's necessarily bolted on with bespoke packets and custom UI.

Under the proposed architecture:

- Martial Awareness fields (attack speed, expected DPS, crit chance breakdown, mitigation rating, damage-by-element summaries) are **all derived from primaries** already on the client.
- Each field has a formula script. Server uses it for authoritative calculations where needed; client uses it for display.
- An extended stats panel (or a dedicated Martial Awareness panel) declares the fields it wants to show; the client derivation layer supplies the values.
- Adding a new Martial Awareness field is: write one formula, add one label. No packet changes, no Chaos.Networking bump, no server deploy coordination.

This is the feature that actually unlocks when the architecture moves.

## Open decisions

### D1 — Where do derived-stat formulas initially live? C# or Lua?

- **C# now, migrate to Lua later:** fast start, formulas next to existing client code. Requires re-implementing when shared-script system exists; formula drift vs. server in the interim.
- **Lua now:** requires client Lua runtime before anything else ships. Biggest blocker is "do we have a client Lua interpreter?" — plugin architecture plans include one but it's not built.

**DECISION:** [ ] **C# interim, Lua endgame.** Accept formula-drift risk in the short term; document which formulas are client-only so server can stay aligned manually until shared-script system exists.

### D2 — Dirty-flag granularity

Recompute derived stats when:

- **Any primary stat changes** (simple, slight over-recompute)
- **Only primaries that affect a specific derived stat** (efficient, requires per-formula dependency tracking)

Modern MMOs use the efficient version (dependency graphs). For a bridge client, the simple version is probably plenty — stat changes are rare enough that full recompute is cheap.

**DECISION:** [ ] **Full recompute on any primary change.** ~microseconds of work at human event rates. Revisit only if profiling shows a problem.

### D3 — Where does the derived-stats layer live?

- **Inside `PlayerAttributes`** — tight coupling to the primary state, simple access pattern
- **New `DerivedStats` ViewModel** — clean separation, derived-stats layer can subscribe to events from multiple sources (equipment, buffs, etc.)

Second option is cleaner as the derivation list grows beyond "just AttributesArgs fields."

**DECISION:** [ ] **New `DerivedStats` ViewModel** in `Chaos.Client/ViewModel/`. Subscribes to `PlayerAttributes.Changed`, `Equipment.Changed`, buff events. Exposes `CritChance`, `HitRate`, etc. as computed properties.

### D4 — How do panels declare which derived stats they show?

- **Hardcoded in each panel** (like `ExtendedStatsPanel` today calling `TrySetLabel(IDX_AC, attrs.Ac)`)
- **Declarative** — panel declares a list of `{labelName, formulaId, format}` triples; `DerivedStats` resolves them

The declarative version makes it trivial to add new fields without touching panel code. Pairs well with the Tier 2 UI modernization ("JSON layouts" in the UI direction doc) — panel's JSON spec includes field declarations.

**DECISION:** [ ] **Hardcoded for v1, declarative once UI layouts are JSON-based.** Don't invent a new declaration format just for stats; reuse whatever UI layout format lands.

### D5 — Formula authority when client-computed value disagrees with server roll

If the client displays "Crit: 12.4%" but server rolls at 11.8% (due to some buff the client hasn't learned about yet, or a latency-window equip change), what does the player see?

Standard MMO behavior: "eventual consistency" — client updates as server pushes primaries. Momentary disagreement is acceptable and invisible to the player in practice.

**DECISION:** [ ] **Accept momentary drift during network latency.** Don't show a "pending" marker or synchronize on every swing. Displayed value is best-effort current.

### D6 — Does Martial Awareness get its own panel or extend Shift+G?

- **Extend Shift+G** — fewer UI pieces, consistent "this is where stats live"
- **Dedicated panel** — more room for an expansive stat breakdown, doesn't crowd the basic stats

**DECISION:** Hybrasyl's call — flag as open until product direction weighs in. Either approach works on the proposed architecture.

## Out of scope for this direction

- **Authoritative combat calculation** stays server-side. Nothing in this doc proposes moving crit rolls, damage resolution, or status application to the client.
- **Full script runtime in the client.** That's a separate track (plugin architecture direction). This doc assumes it eventually exists but doesn't require it for Phase 1.
- **Server-side refactor of combat resolution.** Server already computes derived stats however it does today. This doc is about the client side; matching formulas is a soft coordination concern, not a refactor.
- **Replacement of `AttributesArgs`.** The legacy packet keeps working. Derived stats layer sits above it; new fields arrive either via script invoke or via extended `AttributesArgs` variant later.

## Relationship to other tracks

- **[UI modernization direction](ui-modernization-direction.md):** Tier 1 texture overrides enable the hand-drawn-label replacement; Tier 2 JSON layouts enable declarative field lists.
- **[Font modernization findings](font-modernization-findings.md):** unblocks text-box labels replacing baked-in PNG text (once the 12px slot constraint is decoupled).
- **[Additive modernization pattern](../C:/Users/tacol/.claude/projects/e--Dark-Ages-Dev-Repos-Chaos-Client/memory/additive_modernization_pattern.md) (memory):** legacy `AttributesArgs` keeps working, modern derivation layer adds alongside, script-invoke opcode adds further alongside. Classic additive.
- **Property subscription track (not yet doc'd):** companion to this — script-invoke pipe for stats the client genuinely can't derive. Worth writing when this one is acted on.

## Status

**Direction doc only.** Worth acting on once UI modernization starts or when Hybrasyl's server side wants to unblock Martial Awareness. No work underway. Approve the decisions inline when ready, then write an implementation plan.

# UI Asset Packs — Scoping

Scope for extending the `.datf` asset pack framework from single-image lookups (ability icons, nation badges) to full UI replacement, including pack-controlled panel layouts. Driver is the Extended Stats pane (Shift+G), which will be the pilot.

## Problem

- Current `.datf` packs cover two content types: `ability_icons`, `nation_badges`. Both are flat, single-image-per-ID lookups that fall back to legacy EPF/SPF per-asset. [asset-pack-format.md](asset-pack-format.md) describes v1.
- Legacy UI lives in `.txt` control files paired with `.spf`/`.epf` sheets, loaded through [ControlPrefab](../Chaos.Client.Data/Models/ControlPrefab.cs) and driven by [PrefabPanel](../Chaos.Client/Controls/Components/PrefabPanel.cs) subclasses. The `.txt` format is rigid: pixel-exact rects, DALib `Control` types, limited to 640×480 virtual resolution at 1× art.
- Modern UI ambitions — extended stats display, HUD redesigns, any new panel Hybrasyl adds — want 2×/4× PNG art, pack-controlled layout, and no `.txt` authoring. The prefab system is a bottleneck.
- Packs must remain artist-friendly (ZIP + PNG + JSON, open in any ZIP tool), per the v1 philosophy.

## Goals

1. **Tier 1 — more single-image content types.** Drop-in additions following the nation-badges shape: class badges, status-effect icons, portrait frames, HUD button faces, world-map markers. Each is ~150 LOC and zero risk.
2. **Tier 2 — stateful/multi-frame single sprites.** Generic button states (normal/pressed/disabled), animated UI sprites. First convention change: extend the naming scheme with a state/frame suffix.
3. **Tier 3 — pack-controlled panel layouts.** A new content type that carries both geometry and art for whole panels, replacing `ControlPrefabSet` as the layout source. Panels load from a pack first, fall back to legacy `.txt` prefabs when no pack is registered.

Non-goals for v1:

- Data binding in the manifest (no `"bind": "attributes.str"` expressions — C# panels keep binding responsibility).
- Event/action wiring in the manifest (buttons still need a C# handler).
- Replacing in-game 3D/tile/creature asset paths — those have their own roadmap (see `asset_format_direction` memory).

## Tier 1 — additional single-image content types

Mechanical. Each new type is a copy-paste of [NationBadgePack.cs](../Chaos.Client.Data/AssetPacks/NationBadgePack.cs) with a new prefix. Add a case in [AssetPackRegistry.RegisterByContentType](../Chaos.Client.Data/AssetPacks/AssetPackRegistry.cs). Update [asset-pack-format.md](asset-pack-format.md) with the new convention. Consumer renderer gets an `AssetPackRegistry.GetXxxPack()?.TryGetImage(...)` lookup with legacy fallback.

**Initial candidate types:**

| Content type | Legacy source | ID convention | Notes |
|---|---|---|---|
| `status_effect_icons` | `effecti.epf` / effect sprite sheets | `effect{id:D4}.png` | For buff/debuff bar |
| `portrait_frames` | Profile panel frame art | `frame{id:D4}.png` | Small set (~10 frames) |
| `class_badges` | `_nui_cls.spf` or similar | `class{id:D4}.png` | Pair with existing nation_badges |
| `worldmap_markers` | Parts of `townmap.epf` | `marker{id:D4}.png` | New UI surface |

Each is independent and shippable alone. No need to commit to all four upfront.

## Tier 2 — stateful / multi-frame sprites

First structural extension: a single ID can resolve to multiple PNGs. Two subcases:

**Button states** — discrete state enum, not a frame index:

```
btn_0001_normal.png
btn_0001_pressed.png
btn_0001_disabled.png
```

Lookup API takes `(int id, ButtonState state)` and resolves to the right filename. Missing states fall back to `normal` (which falls back to legacy).

**Animation frames** — ordered sequence, driven by time:

```
spinner_0001_000.png
spinner_0001_001.png
spinner_0001_002.png
```

Frame count is emergent from what's shipped — manifest declares `"frame_rate": 15` (or similar) but not the count.

Both subcases share one piece of new infrastructure: a multi-asset lookup returning an array/dictionary instead of a single SKImage. Ship with button states first (concrete consumer: HUD buttons), defer animated until needed.

## Tier 3 — pack-controlled panel layouts

### The shift from `ControlPrefabSet` to pack-authored panels

Today, [PrefabPanel](../Chaos.Client/Controls/Components/PrefabPanel.cs) loads a `ControlPrefabSet` (from a `.txt` + `.spf`/`.epf`) and subclasses call `CreateButton("OK")`/`CreateLabel("Title")`/etc. to selectively instantiate typed controls. Geometry and art come from the prefab; behavior and binding come from the C# subclass.

The tier 3 move: introduce `IPanelLayout` as the abstraction `PrefabPanel` depends on. Both `ControlPrefabSet` (legacy) and `UiPackPanelLayout` (modern) implement it. Panels construct against either source unchanged. Each `PrefabPanel` subclass stays as-is — it's the binding/behavior controller, not the layout source.

### Pack structure

One XML per panel, flat PNGs at archive root, named by convention. The `_manifest.json` stays JSON — it's a pack header, not content. The XAML-style split (JSON project file, XML views) keeps each format in its natural role:

```
hybui.datf
├── _manifest.json                   # pack header (JSON)
├── extstats.xml                     # layout for ExtendedStatsPanel
├── extstats_bg.png                  # compact background
├── extstats_expanded_bg.png         # expanded background
├── inventory.xml
├── inventory_bg.png
└── ...
```

Naming: `{panel_id}.xml` + `{panel_id}_bg.png` + `{panel_id}_{variant}_bg.png` + `{panel_id}_{control_name}_{state}.png`.

**Why XML for layouts (not JSON):** UI is inherently tree-structured (panels → variants → controls → states). XML's element/attribute split maps cleanly — `<label name="e_attack" align="right"/>` is more natural than the JSON equivalent, and it's the pattern every mainstream UI framework uses (XAML, Android, Qt `.ui`, HTML). XML also supports comments natively (useful for artists annotating variants), and Hybrasyl's server ecosystem already lives in XML via the `Hybrasyl.Xml` package, keeping Taliesin and the team's mental model consistent.

### Manifest additions

```json
{
  "schema_version": 2,
  "pack_id": "hybui-extstats-pilot",
  "pack_version": "0.1.0",
  "content_type": "ui_panels",
  "priority": 100,
  "covers": {
    "ui_panels": { "panel_ids": ["extstats"] }
  }
}
```

`schema_version` bumps to `2` because `ui_panels` introduces JSON-parsed layout files — a genuinely new capability. Older clients that only understand v1 skip the pack entirely (existing reject path).

`covers.ui_panels.panel_ids` is informational — actual coverage still emerges from which `{panel_id}.json` files ship.

### Layout XML schema

```xml
<?xml version="1.0" encoding="utf-8"?>
<panel id="extstats" layout-version="1">
  <anchor rect="0,0,160,100"/>

  <!-- Compact variant: shown in classic HUD mode (6 labels, narrow panel) -->
  <variant name="compact" background="extstats_bg.png">
    <label name="e_attack"  rect="10,10,60,14" align="right"/>
    <label name="e_defense" rect="10,26,60,14" align="right"/>
    <label name="e_AC"      rect="80,10,60,14" align="right"/>
    <label name="e_DMG"     rect="80,26,60,14" align="right"/>
  </variant>

  <!-- Expanded variant: shown on Shift+G toggle, includes opcode 0xFF stats -->
  <variant name="expanded" background="extstats_expanded_bg.png">
    <label name="e_attack"     rect="..." align="right"/>
    <label name="e_defense"    rect="..." align="right"/>
    <label name="e_magic"      rect="..." align="right"/>
    <label name="e_AC"         rect="..." align="right"/>
    <label name="e_DMG"        rect="..." align="right"/>
    <label name="e_HIT"        rect="..." align="right"/>
    <label name="e_crit"       rect="..." align="right"/>
    <label name="e_magiccrit"  rect="..." align="right"/>
    <label name="e_dodge"      rect="..." align="right"/>
    <label name="e_magicdodge" rect="..." align="right"/>
    <label name="e_mr"         rect="..." align="right"/>
  </variant>
</panel>
```

**Notes:**

- Rects are `x,y,w,h` in **logical pixels** (the panel's own coordinate system). Background PNGs at 2×/4× are point-filter downscaled; renderer honors the XML's logical rect, not the image's pixel dimensions.
- `<variant>` is the native home for Compact/Expanded. [ExtendedStatsPanel.ConfigureExpand](../Chaos.Client/Controls/World/Hud/Panel/ExtendedStatsPanel.cs) currently infers this from two different `ControlPrefabSet` sources (`_nstatur` vs `_nstatus`); under tier 3 it's one XML file with two `<variant>` children.
- Control element names mirror the `PrefabPanel.CreateXxx` catalog: `<label>`, `<button>`, `<image>`, `<textbox>`, `<progressbar>`.
- Per-type extensions land as attributes (e.g., `max-length="12"` on textboxes, `frames="12"` on progress bars).
- Parser: `System.Xml.Linq` (`XDocument`) — idiomatic modern .NET, simpler than `XmlReader` or `XmlSerializer` for a small hand-rolled schema. Zero new NuGet dependencies.

### XSD — worth it, but not for the pilot

Strictly required? No — a structured layout format needs schema validation either way (JSON would want JSON Schema for the same reasons). The real question is *when* to invest in the schema definition.

**Recommendation: ship the pilot without an XSD, add one in Phase 5.**

- **Phase 3/4 (infrastructure + pilot):** Validate imperatively in C# at load time. `XDocument.Parse` + walk the tree, throw descriptive errors for missing/malformed attributes. Costs ~50 LOC of validation logic and gives better error messages than XSD's generic complaints anyway. This is also what C# would do for JSON (reading attributes/elements via `.Attribute("name")?.Value` with a guard rail for `null`).
- **Phase 5 (final review + docs):** Author `ui-panel-layout.xsd` alongside the format doc. Ship it inside the client (embedded resource) and next to `asset-pack-format.md` for authors. Taliesin consumes it for autocomplete, artists get schema-aware editors (VS Code XML extension, Visual Studio, IntelliJ) for free. XSD is ~200 lines for the control-type-and-attribute surface.

**Downside to hand-rolled validation:** two sources of truth (the C# validator and the eventual XSD) can drift. Mitigate by generating integration tests that load sample `.xml` files through both paths and assert the same pass/fail outcomes. Small investment, catches drift automatically.

**Downside to XSD specifically (not JSON Schema):** the tooling varies by language. Python/JS tooling for JSON Schema tends to be richer than XSD. For Hybrasyl this is a non-issue — everything consuming the layout files is C# (client) or Taliesin (C#/.NET-native). If Hybrasyl ever grows a web-based layout tool, JSON Schema would be friendlier there, but that's a speculative future and not a reason to choose JSON today.

### Resolution strategy

- Pack authors draw at 2×/4× (e.g., 320×200 PNG for a 160×100 logical panel). Extra detail is free.
- Renderer draws the PNG scaled to the JSON's logical rect using **point filter** (pixel-art aesthetic preserved).
- Game still runs at 640×480 virtual resolution — the 2×/4× art is headroom for a future high-DPI pass, not a v1 ship change.

### Fallback behavior

When a panel loads:

1. Check if `AssetPackRegistry.GetUiPanelPack()?.HasLayout(panel_id)` — use pack if yes.
2. Otherwise load the legacy `ControlPrefabSet` via `DataContext.UserControls.Get(prefabName)`.
3. `PrefabPanel` constructor takes an `IPanelLayout` and doesn't care which source produced it.

Incremental adoption: a pack can ship `extstats.json` only, and every other panel keeps using legacy.

## Pilot: Extended Stats pane

Concrete walkthrough using today's [ExtendedStatsPanel.cs](../Chaos.Client/Controls/World/Hud/Panel/ExtendedStatsPanel.cs) as the reference consumer.

**Today:** Constructor takes `ControlPrefabSet statusPrefabSet` (for the "ExtraStatus" background + 6 compact labels), then `ConfigureExpand(ControlPrefabSet expandedPrefabSet)` adds the expanded layout.

**Under tier 3:**

1. Introduce `IPanelLayout` with methods: `TryGetRect(name)`, `TryGetImage(name, index?)`, `GetVariant(variantName)`.
2. Two implementations: `PrefabSetLayout` (wraps today's `ControlPrefabSet`) and `UiPackPanelLayout` (parses JSON).
3. `ExtendedStatsPanel` constructor takes a single `IPanelLayout` with two variants, replacing the two-`ControlPrefabSet` pattern entirely.
4. [WorldHudControl / LargeWorldHudControl] constructs the panel by calling a new factory:

```csharp
var layout = PanelLayoutFactory.Resolve("extstats");
var panel = new ExtendedStatsPanel(layout);
```

`PanelLayoutFactory.Resolve` tries the pack first, falls back to legacy `_nstatus`/`_nstatur`.

5. New values from opcode 0xFF (see [opcode-0xff-extended-stats.md](opcode-0xff-extended-stats.md)) drive the added expanded-only labels (`e_crit`, `e_dodge`, etc.) — these names live in the pack's `<variant name="expanded">` element and are missing from the legacy layout. Legacy users see the vanilla 6-label compact view; pack users see the full 11-label expanded view with raw float percentages.

**LOC estimate for pilot:**

| Component | LOC |
|---|---|
| `IPanelLayout` + two adapters | ~200 |
| `UiPanelPack` content type + parser | ~250 |
| `PanelLayoutFactory` + registry hook | ~50 |
| `PrefabPanel` refactor to take `IPanelLayout` | ~30 (internal plumbing) |
| `ExtendedStatsPanel` port (thin — mostly constructor swap) | ~20 |
| Registry + manifest v2 support | ~30 |
| **Total** | **~580** |

## Authoring tool implications (Taliesin)

Taliesin is the `.datf` authoring tool per memory. Tier 1–2 fit cleanly (it already handles single-PNG packs). Tier 3 needs:

- XML layout editor (visual drag/drop on a PNG canvas would be ideal; raw XML editing is MVP-acceptable — especially with XSD autocomplete in Phase 5).
- Variant support (compact/expanded switcher on the canvas).
- Control-type palette (label, button, image, textbox, progressbar).
- Export to `.datf` with the tier-3 naming convention.
- XSD-aware validation once the schema lands — matches Hybrasyl server's existing XML authoring flow.

Out of scope for this doc; flag for the Taliesin roadmap.

## Rollout phases

Each phase ends with a bug/regression review + architecture review per [CLAUDE.md](../CLAUDE.md) review policy before the next phase starts.

**Phase 1 — Tier 1 content types (~1 week).** Add 2–3 new single-image types (`status_effect_icons` first, as it has concrete UX value). No architectural change. Ship as incremental format doc updates.

**Phase 2 — Tier 2 button states (~1 week).** Extend the lookup API to return per-state textures. Pilot on one HUD button (e.g., the mail-pulse button). Update format doc with the state-suffix naming convention.

**Phase 3 — Tier 3 core infrastructure (~2 weeks).** `IPanelLayout`, two adapters, `UiPanelPack` parser, `PanelLayoutFactory`, `PrefabPanel` refactor. No new panels ported yet — just the plumbing. Unit-test with a fake pack.

**Phase 4 — Tier 3 Extended Stats pilot (~1 week).** Port `ExtendedStatsPanel` to the new layout source. Ship a pilot `.datf` pack containing `extstats.json` + backgrounds. Verify legacy fallback still works when the pack is removed.

**Phase 5 — Final review + docs + XSD.** Comprehensive review, update [asset-pack-format.md](asset-pack-format.md) with schema v2, write the pack-authoring guide for tier 3, author `ui-panel-layout.xsd` and ship it as an embedded resource + authoring-tool artifact. Add cross-validator tests to keep imperative C# validation and XSD in sync.

## Open questions

- **Hot-reload?** `AssetPackRegistry` is startup-scan-only today. Worth it for tier 3 dev ergonomics, or keep restart-to-reload?
- **Schema v2 split or unified?** Do `ability_icons` / `nation_badges` packs also bump to `schema_version: 2`, or does v2 only apply when `content_type: ui_panels`? Leaning toward: schema version is per-pack, v1 packs keep working forever, v2 packs get the superset of features.
- **Font packs?** Text in labels uses `Font` repository today (DALib fonts). Does a UI pack want to bring its own font? Probably defer — treat as a separate content type later.
- **Bundle content_type?** Future `bundle` type in the format doc suggests one `.datf` shipping icons + badges + panels together. Nice to have; not needed for the pilot.
- **Click/hover hit-testing for modernized panels:** does the pack declare hit regions, or does the C# panel keep control? Recommend: C# keeps control, same as today.

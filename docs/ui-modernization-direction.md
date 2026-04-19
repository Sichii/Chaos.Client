# UI Modernization Direction

Design doc for modernizing the Chaos.Client UI system (currently legacy `.txt` prefab + `.spf`/`.epf` image pipeline). Not an implementation plan — pending decisions are flagged inline. Execute against this doc when the track is prioritized.

## Motivation

The legacy UI pipeline conflates two concerns that modern authoring wants to separate:

1. **Visual reskin** — new button art, borders, backgrounds. The layout is correct; it just looks like 1998.
2. **Structural relayout** — different control arrangements, new panels, additional controls on existing panels.

Editing `.txt` files by hand to swap `.epf` frame indices is painful for case 1 and unscalable for case 2. The existing `PrefabPanel` subclass pattern handles structural UI well at the code level but requires a developer and a rebuild for every tweak.

Additionally: Hybrasyl's roadmap will eventually want to **add** new panels that have no legacy equivalent (custom crafting, mentor UI, etc.). Today that means writing a new `PrefabPanel` subclass + shipping a hand-built `.txt` — uncomfortable because the `.txt` format has no tooling and no reason to exist for a panel with no legacy counterpart.

## Current state

From [CLAUDE.md](../CLAUDE.md) and [controlFileList.txt](../controlFileList.txt):

- **`ControlPrefab`/`ControlPrefabSet`** wrap DALib `Control` definitions + pre-rendered `SKImage` arrays.
- **`.txt` + `.spf`/`.epf` pairs** in `setoa.dat` and related archives define every panel. ~40 panels (login screen, options, profile tabs, world HUD pieces, popups).
- **First control in each `.txt`** (the Anchor) defines panel bounds; subsequent controls are positioned relative to anchor.
- **`PrefabPanel.CreateButton/CreateImage/CreateLabel/CreateTextBox/CreateProgressBar`** selectively instantiates controls from prefab data by name.
- **Every panel class** is a `PrefabPanel` subclass that hardcodes a reference to a specific legacy `.txt`.

## Proposed direction: two-tier override layered on existing asset pack system

Extend the existing `.datf` pack system (see [asset-pack-format.md](asset-pack-format.md)) with two new content types. Each tier is independent and opt-in.

### Tier 1: `content_type: "ui_prefabs"` — texture overrides (the 80% case)

Per-panel, per-named-control texture replacement. Layout/coordinates are untouched — legacy `.txt` remains the source of truth for structure. Only image references are swapped.

```json
{
  "schema_version": 1,
  "pack_id": "hybrasyl-ui-reskin",
  "content_type": "ui_prefabs",
  "covers": {
    "ui_prefabs": {}
  },
  "overrides": {
    "_options": {
      "OkButton":     { "normal": "my_ok.png",     "pressed": "my_ok_p.png" },
      "CancelButton": { "normal": "my_cancel.png", "pressed": "my_cancel_p.png" },
      "Background":   { "image":  "my_options_bg.png" }
    },
    "_invag": {
      "SlotBackground": { "image": "my_slot.png" }
    }
  }
}
```

Runtime: `PrefabPanel.CreateButton("OkButton")` consults the registry before falling back to the legacy prefab texture. One modern-first dispatch insertion point in `PrefabPanel`'s create methods covers every consumer.

### Tier 2: `content_type: "ui_layouts"` — full layout replacement (the 20% case + new panels)

JSON layout definitions that fully replace legacy `.txt` for a panel, OR define entirely new panels that have no legacy counterpart.

```json
{
  "schema_version": 1,
  "pack_id": "hybrasyl-crafting-ui",
  "content_type": "ui_layouts",
  "covers": {
    "ui_layouts": {}
  },
  "panels": {
    "hyb_crafting": {
      "bounds": [0, 0, 320, 240],
      "controls": [
        { "type": "image",  "name": "Background",   "rect": [0,0,320,240], "texture": "bg.png" },
        { "type": "label",  "name": "Title",        "rect": [8,4,300,14], "text": "Crafting", "font": "default" },
        { "type": "button", "name": "CraftButton",  "rect": [260,210,50,24],
          "textures": { "normal": "craft.png", "hover": "craft_h.png", "pressed": "craft_p.png" } }
      ]
    }
  }
}
```

Runtime: `PrefabPanel` (or a new `JsonPanel` sibling) can be constructed from either a legacy `ControlPrefabSet` or a modern `UiLayout`. Same `CreateButton`/`CreateImage`/etc. API.

---

## Open decisions

### D1 — New-panel authoring: code-only, JSON-only, or both?

Today, new panels require a `PrefabPanel` subclass. If we accept Tier 2, a *future* new panel could be defined in pure JSON with no subclass. Tradeoff:

- **Code-only**: compile-time safe, refactor-safe, typical game-UI practice. Cannot hot-reload; requires developer.
- **JSON-only**: non-coder authoring, hot-reloadable, no compile step for UI tweaks. Event wiring (button click handlers) still has to happen somewhere — JSON declares *names*, code binds *behavior*.
- **Both**: JSON for layout, code for behavior. A `JsonPanel` base class is constructed from a layout name; subclasses wire events by looking up controls by name. Typical modern game UI pattern.

**DECISION:** [ ] Code-only / [ ] JSON-only / [ ] **Both (JSON layout + C# behavior subclass)** — this is the likely pick but call it out explicitly.

### D2 — Override granularity

What's the unit of an override?

- **Per-panel, per-control, per-state** (texture for `_options`/`OkButton`/`pressed`). Maximum flexibility, verbose manifest.
- **Per-panel, per-control** (all states for a control replaced as a set). Less verbose; a partial reskin forces shipping all states.
- **Per-panel sheet** (replace the whole `.spf`/`.epf` atlas the panel draws from). Matches legacy thinking but couples unrelated visuals.

**DECISION:** [ ] **Per-panel + per-control, state fields optional**. Ship normal only and hover/pressed fall back to legacy for that control. This is consistent with how ability-icon packs do partial replacement today.

### D3 — Legacy fallback granularity

If a Tier-1 pack overrides *some* controls on `_options` but not others:

- **All-or-nothing per panel** — any override on a panel means we use modern textures for everything; missing controls stay legacy (but rendered from the modern manifest's perspective).
- **Per-control mix** — overridden controls use modern textures; non-overridden controls use legacy. Panels can be half-modern, half-legacy.

Per-control mix is the obvious "correct" answer but may produce visual inconsistency if artists don't ship matching sets. All-or-nothing forces full coverage but complicates incremental shipping.

**DECISION:** [ ] **Per-control mix.** Consistent with the emergent-coverage pattern everywhere else. Document the visual-consistency authoring guidance clearly.

### D4 — Godot compatibility

Chaos.Client is a 2+ year bridge to a Godot-native client. Should Tier-2 JSON layouts be designed to be mechanically convertible to Godot `.tscn`?

- **Yes, design for it** — constrain the JSON schema to concepts that map cleanly to Godot `Control` nodes (`Button`, `TextureRect`, `Label`, `Container`). No MonoGame-specific idioms. Someone writes a one-time converter when Godot work starts.
- **No, don't bother** — MonoGame era only. Godot will have its own UI system and authoring approach. Don't prematurely constrain the format.

Leaning toward yes: the cost is low (keep schema to boring well-known control types), and it saves re-authoring UI twice.

**DECISION:** [ ] **Design for Godot-compatibility.** Limit Tier-2 control types to `image`, `button`, `label`, `textbox`, `progress`, `panel` — the universal set. Avoid MonoGame-specific features like SpriteBatch tricks in layout declarations.

### D5 — Hot reload

Should modern UI packs reload on file change during development?

- **Yes** — huge iteration-speed win for artists. Requires a file watcher and UI-tree rebuild path. Some complexity around in-flight control state (focused textbox, hovered button).
- **No** — restart client to see changes. Simpler. Matches current legacy pack behavior.

Probably start without hot reload and add it if iteration pain is real.

**DECISION:** [ ] **Start without hot reload.** Revisit after first real reskin work. Architect the `UiLayoutRegistry` so it's rebuildable at runtime even if we don't expose a trigger yet.

### D6 — Button states

Legacy `UIButton` has `NormalTexture`, `HoverTexture`, `PressedTexture`, `DisabledTexture`. Modern manifest schema for states:

- **Flat fields**: `{ "normal": "...", "hover": "...", "pressed": "...", "disabled": "..." }`
- **Nested dict**: `{ "states": { "normal": "...", "hover": "..." } }`

Flat is more ergonomic, nested is more future-proof (room for per-state tint/offset/sound).

**DECISION:** [ ] **Flat fields for v1.** Escape to nested dict in a v2 schema bump when a concrete need arrives.

### D7 — Coordinate system

Legacy panel positions are mostly absolute within the panel's anchor. For modern layouts:

- **Keep absolute**: `"rect": [x, y, w, h]` relative to panel top-left. Simple, matches current mental model.
- **Add anchoring**: `"anchor": "bottom-right"`, `"offset": [-10, -10]` — enables responsive-ish layouts. Overkill for a fixed 640×480 virtual resolution client.

**DECISION:** [ ] **Absolute only.** Virtual resolution is fixed; responsive layout isn't a real requirement.

### D8 — Fonts

Legacy panels reference fonts by name from `.fnt` files. Modern layouts may want to ship custom TTF/OTF fonts in a pack.

- **Reference legacy fonts only** in v1. `"font": "default"` / `"font": "tab_small"` / etc.
- **Allow modern font bundles**: pack ships a `.ttf`, layout references it by name. Requires a font-loading extension and a rendering layer integration.

**DECISION:** [ ] **Legacy fonts only for v1.** Font modernization is a separate track.

### D9 — Localization

UI modernization does *not* solve i18n. Modern layouts declare text literally (`"text": "Crafting"`). If/when localization lands, it layers over both legacy and modern UI through a string-key lookup — out of scope here.

**DECISION:** [ ] **Localization is a separate track.** Modern layouts use literal strings; i18n integration happens later.

### D10 — Event binding for Tier-2 new panels

A JSON panel declares buttons by name. Behavior still lives in C#. Binding options:

- **Convention: subclass `JsonPanel`, use `GetButton("CraftButton").Clicked += ...`**. Matches existing PrefabPanel pattern.
- **Declarative event names in JSON**: `"on_click": "OnCraftClicked"` with reflection lookup on a handler object. More data-driven but opens failure modes (typos, missing handlers, reflection perf).

**DECISION:** [ ] **Convention-based subclass with name lookup.** No reflection. C# stays the behavior authority.

### D11 — Pack distribution structure

Where do UI packs live relative to other `.datf` pack types?

- **Alongside all other packs** in the data folder (current pattern).
- **Subdirectory convention** e.g. `packs/ui/` for organization.

Unrelated to format; purely a filesystem convention. Aligns with the general "packs subfolder" direction flagged in an earlier design conversation.

**DECISION:** [ ] **Flat for now; move to `packs/ui/` when the general packs-subfolder reorg happens.** Defer to that cross-track decision.

---

## Implementation phasing (rough sketch, not a plan)

When this track is prioritized, implementation likely staged as:

1. **Tier 1 only — texture overrides.** Extends `AssetPackRegistry` with `UiPrefabPack` multi-pack list. `PrefabPanel.CreateButton/CreateImage` consult registry before falling back to legacy. Low risk, big artist-facing win.
2. **Review + stabilize Tier 1** before touching Tier 2. Let real reskin packs ship and surface pain points.
3. **Tier 2 — layout replacement, existing-panel mode only.** Modern JSON layout can replace a legacy panel's `.txt`. All current `PrefabPanel` subclasses keep working; each can be retargeted from legacy to modern layout.
4. **Tier 2 — new-panel mode.** `JsonPanel` base class supports panels with no legacy counterpart. Hybrasyl-specific panels (crafting, mentor, etc.) author straight into JSON.
5. **Retrospective.** Decide whether to deprecate legacy `.txt` for any panels that have shipped full modern replacements, or keep legacy as the always-available baseline.

## Out of scope

- **Hot reload** (see D5) — deferred until iteration pain is real.
- **Font modernization** (see D8) — separate track.
- **Localization** (see D9) — separate track.
- **SkiaSharp-rendered rich text, HTML, markdown rendering** — UI stays bitmap + simple label rendering. No layout engines.
- **Responsive / multi-resolution layouts** — virtual resolution is fixed.
- **Animation in UI** — tweens, transitions, animated sprites on controls. Future extension.
- **Accessibility metadata** — ARIA-equivalents, screen reader support. Hybrasyl-level concern, not in this doc.

## Relationship to other modernization tracks

- **Asset pack system** (`ability_icons`, `nation_badges`, `tiles`, `props`, `creatures`) — UI modernization is the same pattern: additive overrides via `.datf` packs, priority-based conflict resolution, legacy as fallback. No new infrastructure beyond two new `content_type` values.
- **Plugin architecture direction** (see memory) — if plugins ever want to define their own UI panels, Tier-2 JSON layouts + `JsonPanel` subclass is the natural shipping mechanism. Out-of-process plugins can't ship C# subclasses; they can ship JSON.
- **Godot endgame** (see D4) — Tier-2 schema designed for clean `.tscn` convertibility. Saves re-authoring UI twice.
- **Additive modernization pattern** (see memory) — this doc is a textbook application. No refactors to legacy, two new layers alongside.

## Status

**Direction doc only.** No work underway. Approve the decisions inline, then write an implementation plan when the track is prioritized.

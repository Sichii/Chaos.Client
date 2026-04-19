## Dialog Modernization Direction — Declarative Graphs, Legacy Coexistence, and Authoring Tooling

Design doc for modernizing how NPC (and reactor / sign / item) dialogs are authored, shipped, and rendered. Captures the "declarative dialog graph alongside the legacy `DialogSequence` chain" pattern and the implications for the Chaos.Client UI, the Hybrasyl server, and the Hybrasyl authoring toolchain. Not an implementation plan — decisions flagged inline.

## Motivation

Legacy Dark Ages / Hybrasyl dialogs are **imperatively constructed in Lua** against server-side `DialogSequence` / `Dialog` / `DialogOption` objects. A single non-trivial NPC ends up as dozens of lines of Lua that:

- Instantiate `Dialog` objects one at a time in the script
- Wire options together by mutating fields on already-constructed sequences
- Hard-code `JumpDialog` / `Next` / `Prev` references by string name
- Embed conditional branching as Lua `if` blocks between construction calls
- Interleave display text, game-state mutations, and control flow with no structural separation

This is the "insanity" the user referenced. It has three compounding costs:

1. **Authoring cost** — writers must be Lua programmers, and even experienced Lua programmers lose the plot on complex dialog trees. There is no graph view — the graph only exists in the author's head.
2. **Review cost** — diffs against imperative Lua construction are painful. Reviewers can't tell a behavior change from a refactor.
3. **Tooling cost** — no editor can meaningfully operate on a blob of Lua. Creidhne can manage NPCs, but dialogs attached to those NPCs are opaque `.lua` files.

The legacy format isn't going away soon — every existing NPC on every Hybrasyl world is written this way. So the modernization has to be **additive**: new dialogs in a new format, old dialogs untouched, both formats dispatched at runtime.

## The pattern: declarative dialog graph

The modern replacement is a **declarative node graph** — a JSON (or YAML) document that enumerates nodes, text, options, and transitions, with scripting hooks for the dynamic bits (conditions, state mutations, computed text).

```json
{
  "id": "blacksmith.greeting",
  "entry": "intro",
  "sprite": 134,
  "nodes": {
    "intro": {
      "text": "Well met, traveller. What brings you to the forge?",
      "options": [
        { "text": "I need a weapon.",     "target": "weapons" },
        { "text": "Tell me about yourself.", "target": "lore",
          "visible_if": "player.level >= 5" },
        { "text": "Farewell.",             "target": "$exit" }
      ]
    },
    "weapons": {
      "text": "Aye, I've fine steel for them as can pay. Browse the wares?",
      "on_enter": "npc:open_shop('blacksmith_weapons')",
      "options": [
        { "text": "Back.", "target": "intro" }
      ]
    },
    "lore": {
      "text": "$(script:blacksmith.lore_text)",
      "options": [
        { "text": "Back.", "target": "intro" }
      ]
    }
  }
}
```

Properties:

- **The graph is a document.** It can be diffed, reviewed, statically validated, and opened in a node editor. The structure is explicit, not emergent.
- **Control flow is declarative.** `target`, `visible_if`, `on_enter`, `on_exit` — no imperative construction.
- **Dynamic content goes through script.** When text needs to be computed (`"You have $(player.gold) gold"`) or a branch depends on game state, the script-invoke pattern from the stats direction doc handles it. The graph names a script symbol; the runtime resolves it. The graph itself stays data.
- **Every modern dialogue system looks like this.** Twine, Ink, Yarn, Dialogic, Articy:Draft, and TalkerMakerDeluxe are all variations on the same theme. It is the accepted shape.

## The runtime pipeline

```
┌─────────────────────┐    ┌───────────────────────┐    ┌─────────────────────┐
│ Creidhne authoring  │ -> │ dialog graph JSON     │ -> │ server world lib    │
│ (node graph UI)     │    │ (committed to repo)   │    │ (loaded at startup) │
└─────────────────────┘    └───────────────────────┘    └──────────┬──────────┘
                                                                   │
                                                                   v
                               ┌────────────────────────────────────────────┐
                               │ Player triggers dialog (click NPC, etc.)   │
                               │ Server decides: legacy sequence or graph?  │
                               └────────────┬───────────────────────────────┘
                                            │
                          ┌─────────────────┴─────────────────┐
                          v                                   v
              ┌─────────────────────┐              ┌───────────────────────┐
              │ Legacy path         │              │ Modern path           │
              │ 0x30 DisplayDialog  │              │ DialogGraph opcode    │
              │ (unchanged)         │              │ or ScriptInvoke blob  │
              └──────────┬──────────┘              └───────────┬───────────┘
                         v                                     v
              ┌─────────────────────┐              ┌───────────────────────┐
              │ Client renders via  │              │ Client renders via    │
              │ NpcSessionControl   │              │ same NpcSessionControl│
              │ + legacy menu state │              │ driven by graph VM    │
              └─────────────────────┘              └───────────────────────┘
```

The key observation: **the client's dialog UI doesn't need to know which format authored the interaction.** `NpcSessionControl`, `FramedDialogPanelBase`, `DialogOptionPanel`, `MenuListPanel`, `DialogTextEntryPanel`, and the shop/exchange variants are all *presentations of a dialog state*. Whether that state came from a legacy `DialogSequence` packet or a modern graph walker is an implementation detail below the UI layer.

This is the same shape as the ability-icons pilot (legacy EPF icons and modern PNG overrides feed `IconTexture` through one draw path) and the tiles/props pack pilot (legacy tileset and modern atlas both hit `MapRenderer` through one lookup). The pattern is: **new data layer, shared presentation layer.**

## Migration path

### Phase 0 — Schema ratification (design-only, no code)

Decide the document shape. Strawman above. Open questions:

- **D1. JSON or YAML?** JSON wins on tooling (every editor, every diff viewer, native in the Electron stack Creidhne uses). YAML wins on handwritten readability. Given Creidhne will be the primary authoring path and hand-editing is a fallback, lean JSON.
- **D2. Node ID scheme.** Flat string IDs in a single `nodes` map (shown above), or nested by category? Flat is simpler and matches how Twine/Ink/Yarn work. Recommend flat.
- **D3. Scripting hook surface.** Which symbols does a dialog graph get to call? Minimum: `visible_if` (predicate), `on_enter` / `on_exit` (side effects), `$(expr)` text interpolation. Should also be able to **mutate dialog-local state** (a per-session key/value bag visible as `session.foo` in expressions) for "the player already answered the riddle" patterns without polluting player state.
- **D4. Special targets.** Need `$exit` (close dialog), `$back` (pop), `$menu` (jump to a named sub-menu). Should there be `$sequence:other_npc.greeting` for cross-graph references, or must those go through an explicit `start_dialog` script call? Recommend explicit script call — keeps graph references to inside-the-graph only.
- **D5. Version field.** `"schema_version": 1` on every graph, same as `.datf` manifests. Cheap insurance.

### Phase 1 — Runtime: client-side graph renderer

The client is the easy side because the UI already exists. Needed work:

1. Add a client-side `DialogGraph` model + `DialogGraphWalker` VM that tracks current node, session state, and option visibility.
2. Add a packet (new opcode or `ScriptInvoke` channel — see D6) that carries a graph document or a delta to the walker state.
3. Route the walker's current-node display through the existing `NpcSessionControl` + subpanel stack — same controls the legacy dispatch uses today.
4. Legacy path untouched. Opcode 0x30 `DisplayDialog` still routes to `NpcInteraction` ViewModel and `NpcSessionControl` via its current handler.

**D6. Transport: new opcode or ScriptInvoke?** Two options:

- **New typed opcode** (e.g. `0xB0 DialogGraphOpen`, `0xB1 DialogGraphAdvance`, `0xB2 DialogGraphClose`). Traditional, typed, easy to debug. Adds to the opcode surface that's already on the chopping block per the Chaos.Networking discussion.
- **ScriptInvoke channel**: graph documents and walker deltas travel over the generic script-invoke pipe discussed in the stats direction doc. No new opcode. Aligns with the "replace Chaos.Networking with scripting-over-packets" thread.

Recommend ScriptInvoke if that infrastructure lands first; a new opcode if dialogs need to ship before the script-invoke channel is ready. Either is a wrapper over the same `DialogGraphWalker` VM — swappable.

### Phase 2 — Server-side graph loader + evaluator

Server reads `dialogs/*.json` from the world library at startup, registers graphs by ID, and at dispatch time chooses:

- If the NPC's `Dialog` Lua registers a legacy `DialogSequence`, dispatch the legacy path (unchanged).
- If the NPC's XML points at a `DialogGraph` ID, instantiate a walker server-side, send the open event to the client, and advance the walker as options come back.

Legacy NPCs keep working. New NPCs reference a graph. Mixed NPCs are fine — an NPC can have a legacy shop dialog and a modern quest dialog; the dispatch key is the graph ID the player's interaction resolves to.

### Phase 3 — Creidhne authoring UI

Add a **Dialogs** page to Creidhne. Features (initial scope):

- Graph list view, filter by NPC association, library-wide "used by" lookup.
- Node graph canvas (React-Flow or similar) — drag nodes, wire port-to-port, edit text inline.
- Script-hook editing surface — `visible_if`, `on_enter`, `on_exit` as small Lua-stub fields with autocomplete against the Lua stubs library Creidhne already ships.
- Save/load against the same `world/` library folder Creidhne and Taliesin already share. One JSON file per graph. Index in `.creidhne/index.json` for cross-reference.
- XSD-equivalent validation — the schema decided in Phase 0 becomes a JSON Schema that Creidhne validates against before save.

**Editor decision: Creidhne is the authoring home.** NPCs already live there, dialogs hang off NPCs, and the Electron/React/MUI stack is already set up. TalkerMakerDeluxe is a valuable **UX reference** — it has already solved node-graph editing, port wiring, and canvas pan/zoom in a Dark-Ages-adjacent context. Borrow the interaction model, not the codebase. Retooling a WPF .NET app into the JS/TS toolchain would be fighting the stack; porting the UX patterns into a React-Flow-based Creidhne page is straightforward.

- **D7. React-Flow vs custom canvas.** React-Flow is the obvious starting point — handles panning, zooming, edge routing, minimap. Custom canvas gives more control but is a 2-3 week detour.
- **D8. Auto-layout on first open.** Graphs authored hand-JSON will have no node positions. Need a sensible default layout (dagre, elk, or similar) that Creidhne applies if positions are missing, then persists once the author arranges.
- **D9. Lua-in-field editing.** Inline Monaco editor (same as Creidhne's other Lua fields) vs modal dialog vs plain textarea. Recommend inline Monaco for consistency with existing Creidhne Lua editing.

### Phase 4 — Migration (opportunistic, never forced)

No sweeping legacy migration. The new format exists; new NPCs are authored in it; old NPCs stay as Lua. When an old NPC needs substantial dialog changes, that's when someone rewrites it as a graph — the rewrite is how you know it's worth the effort.

Eventually some fraction of legacy dialogs get migrated by attrition. The rest stay until the legacy path is deprecated (a decision that can wait years — the legacy path is *correct*, just painful to author).

## Open questions — decisions to make before implementation

- **D10. Conditional option visibility vs disabled-with-reason.** Should an option that fails `visible_if` be hidden, or shown greyed-out with a hover tooltip ("Requires level 5")? Legacy behavior is hidden. Modern MMOs mostly show greyed-out because players hate "why doesn't this NPC offer me the quest?" mysteries. Recommend greyed-out with optional tooltip, but the graph author should be able to opt into hidden per option.
- **D11. Rich-text support.** Legacy dialog text is plain ASCII + Korean through the legacy font stack. Modern graphs could support `{color:red}`, `{item:longsword}` inline tokens for item-link display, player-name interpolation, etc. This opens the same can-of-worms as the chat system direction — recommend deferring to the chat modernization pattern (whatever that ends up as) so dialogs and chat use one text-markup language.
- **D12. Localization.** Legacy Hybrasyl has `localization_strings` XML (managed by Creidhne). Graphs could either inline strings directly (simple) or reference localization keys (`"text_key": "blacksmith.intro"`). Localization keys are the right answer long-term, but inline strings are fine for v1 since no one is translating Hybrasyl today.
- **D13. Versioning on the wire.** If a player has a dialog open when the server reloads a modified graph, what happens? Simplest answer: close the dialog on graph-version change. Most correct answer: keep the old walker running against the old graph until it closes. Recommend close-on-change for v1.

## How this interacts with other direction docs

- **Scripting-over-packets (stats-display-direction):** D6 above depends on the ScriptInvoke channel. If scripting-over-packets ships first, dialog graphs ride the same pipe. If dialogs ship first, dialogs get a typed opcode and scripting-over-packets joins it later.
- **UI modernization (ui-modernization-direction):** `NpcSessionControl` and its subpanels are candidates for the `ui_prefabs` / `ui_layouts` override track. The dialog renderer is unchanged by this work — it's the textbox frame / option-row visuals that get modernized through the UI track, independent of whether the content is legacy or graph-sourced.
- **Chat system direction:** D11 rich-text punts to whatever markup language the chat modernization settles on. Resolving chat markup first avoids two different markup dialects in the client.
- **Additive modernization pattern:** Dialog modernization is a canonical instance — new data layer (`DialogGraph`), new transport (opcode or ScriptInvoke), shared presentation layer (`NpcSessionControl`), no legacy refactor.

## Summary of decisions to flag

| # | Decision | Current lean |
|---|----------|--------------|
| D1 | JSON or YAML document format | JSON |
| D2 | Flat or nested node IDs | Flat |
| D3 | Scripting hook surface | `visible_if`, `on_enter`, `on_exit`, `$(expr)`, `session.*` local state |
| D4 | Special jump targets | `$exit`, `$back`, `$menu`; no cross-graph references (use script call) |
| D5 | Schema version field | Yes, `"schema_version": 1` |
| D6 | Transport: new opcode or ScriptInvoke | Prefer ScriptInvoke if available, else typed opcode |
| D7 | Creidhne canvas library | React-Flow |
| D8 | Auto-layout algorithm | dagre or elk, persist once arranged |
| D9 | Lua field editor | Inline Monaco (match existing Creidhne UX) |
| D10 | Conditional options: hidden or greyed-out | Greyed-out with tooltip, opt-in to hidden per option |
| D11 | Rich-text markup in dialog text | Defer to chat modernization |
| D12 | Localization: inline or key-referenced | Inline for v1, key-referenced once localization is real |
| D13 | Wire behavior on graph reload | Close open dialogs on version change |

**Editor decision is settled, not flagged:** Creidhne is the authoring home, TalkerMakerDeluxe is the UX reference.

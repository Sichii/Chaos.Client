# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Chaos.Client is a Dark Ages MMORPG client built in C# (.NET 10.0) using MonoGame for windowing/graphics and a local DALib fork for Dark Ages file format handling. Targets the Chaos-Server private server. Licensed under AGPL-3.0-or-later. v0.1.0.

## Build & Run

```bash
dotnet build Chaos.Client.slnx
dotnet run --project Chaos.Client/Chaos.Client.csproj
```

No test projects exist currently.

## Solution Structure

```
Chaos.Client.slnx (.NET 10.0, C# 14)
├── Chaos.Client               — MonoGame Game class, screens, UI controls, systems, entry point
├── Chaos.Client.Data           — Asset repositories, DALib integration, archive loading, caching
├── Chaos.Client.Rendering      — Texture conversion, sprite renderers, map rendering, text, camera
├── Chaos.Client.Networking     — TCP client, crypto, packet framing, connection state machine
└── DALib (project ref)         — Local fork at ../dalib/DALib/ — Dark Ages file format support
```

**Dependency flow:** Data <- Rendering <- Client, Networking <- Client

## Related Repositories

| Path | Description |
|------|-------------|
| `D:\repos\Sichii\Chaos-Server` | Chaos-Server private server. Contains all `Chaos.*` namespaces (Chaos.Networking, Chaos.DarkAges, etc.) |
| `D:\repos\Sichii\ChaosAssetManager` | Asset rendering reference app |
| `../dalib/DALib/` | Local DALib fork (project ref) |

## Key Dependencies

| Package | Purpose |
|---------|---------|
| DALib (project ref, ../dalib/) | Dark Ages file format support, SkiaSharp rendering |
| MonoGame.Framework.DesktopGL 3.8.4.1 | Cross-platform graphics/windowing |
| Chaos.Networking 1.10.0-preview | Complete protocol library: packet converters, crypto, opcodes, args types |
| Chaos.Common 1.10.0-preview | Shared extension methods (NuGet) |
| Chaos.DarkAges 1.10.0-preview | Dark Ages protocol types (NuGet) |
| Chaos.Geometry 1.10.0-preview | Geometry types -- rectangles, points (NuGet) |
| Chaos.Pathfinding 1.10.0-preview | A* pathfinding (NuGet) |
| Microsoft.Extensions.Caching.Memory 10.0.5 | MemoryCache infrastructure |
| NAudio 2.3.0 | MP3 decoding for sound playback |

## Build Configuration

Centralized in `Directory.Build.props`: C# 14, net10.0, nullable enabled, implicit usings, TieredPGO + TieredCompilation (+ QuickJit) enabled, WarningLevel 4, EnforceCodeStyleInBuild. Package versions managed centrally in `Directory.Packages.props`. Versioning via Nerdbank.GitVersioning.

## Architecture

### Data Layer (`Chaos.Client.Data`)
- **`DataContext`** -- Static singleton exposing all repositories via `Initialize()`.
- **`DatArchives`** -- Static holder for 21 game data archives loaded at startup via memory-mapped files.
- **`RepositoryBase`** -- Abstract base with MemoryCache (15-min sliding expiration). Uses `GetOrCreate<T>(key, factory)`.
- **11 repositories:** AislingDrawData, CreatureSprite, Effects, Font, LightMask, LocalPlayerSettings, MapFile, MetaFile, PanelSprite, Tile, UiComponent.
- **`ControlPrefab`/`ControlPrefabSet`** -- Wraps DALib Control definitions + pre-rendered SKImage arrays. First control (Anchor) defines panel bounds.
- Control file catalog in `controlFileList.txt` at solution root.

### Rendering Layer (`Chaos.Client.Rendering`)
- **`TextureConverter`** -- DALib `SKImage` -> MonoGame `Texture2D` (RGBA8888 premul). Also `LoadSpfTexture()`, `LoadEpfTextures()`, `RenderSprite()`.
- **`Camera`** -- Isometric camera: `WorldToScreen`, `ScreenToWorld`, `TileToWorld`, `WorldToTile`, `GetVisibleTileBounds()`.
- **`MapRenderer`** -- Background + foreground tile rendering. `DrawBackground()`, `DrawForegroundTile()`, `PreloadMapTiles()`.
- **`TextRenderer`** -- SkiaSharp text rendering: `RenderText()`, `RenderWrappedText()`, `MeasureWidth()`, `WrapText()`.
- **`UiRenderer`** -- UI panel rendering utilities.
- **`DarknessRenderer`** -- Light/darkness overlay.
- **`TabMapRenderer`** -- Mini-map rendering.
- **`SilhouetteRenderer`** -- Silhouette effect for blocked entities.
- **`PaletteCyclingManager`** -- Animated palette shimmer effects.
- **`FontAtlas`** -- Font glyph atlas management.
- **`CreatureRenderer`/`AislingRenderer`/`EffectRenderer`/`ItemRenderer`** -- Per-frame texture caches. `Clear()` on map change.
- **`LegendColors`** -- Named color constants for UI text. Initialized at startup.
- **`LightSource`** -- Light source model for darkness system.
- **`RenderHelper`** -- Shared rendering utility methods.
- **`TextureAtlas`/`AtlasHelper`/`CachedTexture2D`** -- Grid/Shelf packing for performance optimization.
- **`SpriteAnimation`/`SpriteFrame`** -- Frame array with `GetFrame(index)`, timing, additive blending.
- **`EntityHitBox`** -- Hit testing geometry for clickable entities.
- **Asset pipeline:** `DatArchives -> Repository -> Palettized<T> -> DALib Graphics.RenderXxx() -> SKImage -> TextureConverter.ToTexture2D() -> Texture2D -> SpriteBatch.Draw()`

### Networking Layer (`Chaos.Client.Networking`)
- **`GameClient`** -- Low-level TCP: crypto, packet framing (0xAA + 2-byte BE length), sequence tracking, `InboundQueue` via `DrainPackets()`. Auto-responds to HeartBeat/SynchronizeTicks.
- **`ConnectionManager`** -- State machine (Disconnected->Connecting->Lobby->Login->World), array-indexed handler dispatch (60+ handlers), 48+ events. Full lobby/login/world-entry flows. Player action methods, communication, NPC/dialog, requests.
- **`ServerTableData`** -- Zlib-compressed server list parser.
- Full protocol spec in `Chaos.Client.Networking/NETWORKING_SPEC.md`.

### Client Project (`Chaos.Client`) Internal Organization

```
Chaos.Client/
├── ChaosGame.cs              — MonoGame Game class, entry point
├── GlobalSettings.cs         — Static config (ClientVersion, DataPath, LobbyHost/Port)
├── InputBuffer.cs            — Event-driven input capture and buffering
├── Collections/              — WorldState, CircularBuffer
├── Models/                   — WorldEntity, Animation, EntityRemovalAnimation, EntityHighlight, SlotDragPayload, PathfindingState, etc.
├── ViewModel/                — Authoritative state classes owned by WorldState
├── Systems/                  — AnimationSystem, CastingSystem, ChatSystem, SoundSystem, Pathfinder, ClientSettings, MachineIdentity
├── Screens/                  — IScreen, ScreenManager, LobbyLoginScreen, WorldScreen (7 partial files)
├── Rendering/                — EntityOverlayManager, WorldDebugRenderer
├── Controls/                 — Full UI control hierarchy (see UI Control System below)
├── Definitions/              — Delegates, Enums, DoorTable, InputEvents, TextColors
├── Extensions/               — DirectionExtensions, RectangleExtensions, UIElementExtensions
└── Utilities/                — Clipboard, DialogFrame, SlideAnimator
```

### Screen System
- **`IScreen`/`ScreenManager`** -- Stack-based screen management.
- **`LobbyLoginScreen`** -- Full login flow: lobby connect, server select, login, character creation, transition to world.
- **`WorldScreen`** -- Main game screen, split into 7 partial class files:
  - `WorldScreen.cs` -- Base class, fields, construction
  - `WorldScreen.Draw.cs` -- Render logic (diagonal stripe entity interleaving, overlays)
  - `WorldScreen.Update.cs` -- Game logic update
  - `WorldScreen.InputHandlers.cs` -- Keyboard/mouse input (movement, hotkeys, pathfinding, click-to-interact)
  - `WorldScreen.ServerHandlers.cs` -- Network packet handler subscriptions
  - `WorldScreen.Wiring.cs` -- Event subscription setup
  - `WorldScreen.Map.cs` -- Map management

### UI Control System

**Component Primitives (`Controls/Components/`):** UIElement, UIPanel, UIButton, UITextBox, UIImage, UIAnimatedImage, UILabel, UIProgressBar, TextElement, PrefabPanel, ExpandablePanel, PromptControl, AlphaScreenPane.

**Login Flow (`Controls/LobbyLogin/`):** LobbyLoginControl, LoginControl, ServerSelectControl, CharacterCreationControl, EulaNoticeControl, PasswordChangeControl.

**Generic Controls (`Controls/Generic/`):** OkPopupMessageControl, TextPopupControl, ScrollBarControl, DebugOverlay.

**World HUD (`Controls/World/Hud/`):** IWorldHud interface, WorldHudControl (classic compact HUD), LargeWorldHudControl (expanded HUD), OrangeBarControl, EffectBarControl/EffectSlotControl.

**HUD Panels (`Hud/Panel/`):** PanelBase, InventoryPanel, SkillBookPanel, SpellBookPanel, ToolsPanel, ChatPanel, StatsPanel, ExtendedStatsPanel, SystemMessagePanel. Slots: PanelSlot, AbilitySlotBase, SkillSlot, SpellSlot.

**Self Profile (`Popups/Profile/`):** SelfProfileTabControl with Equipment/Legend/AbilityMetadata/Events/Family/Blank tabs, SelfProfileTextEditorControl, AbilityMetadataDetailsControl/AbilityMetadataEntryControl, EventMetadataDetailsControl/EventMetadataEntryControl, LegendMarkControl. **Other Profile:** OtherProfileTabControl (Equipment via _nui_eqa + Legend tabs), OtherProfileEquipmentTab.

**Options (`Popups/Options/`):** MainOptionsControl, MacrosListControl, SettingsControl, FriendsListControl.

**Popups (`Popups/`):** AislingContextMenu, AmountControl, ChantEditControl, GroupRecruitPanel, GroupTab/GroupTabControl, HotkeyHelpControl, ItemTooltipControl, NotepadControl, SocialStatusControl, TownMapControl. Subdirectories: `Boards/` (BoardListControl, ArticleListControl/ArticleReadControl/ArticleSendControl, MailListControl/MailReadControl/MailSendControl), `Dialog/` (NpcSessionControl, MerchantBrowserPanel, DialogTextEntryPanel, MenuTextEntryPanel, OptionMenuPanel, ProtectedEntryPanel), `Exchange/` (ExchangeControl/ExchangeItemControl), `WorldList/` (WorldListControl/WorldListEntryControl).

**Viewport Overlays (`ViewPort/`):** ChatBubble, HealthBar, LoadingBar/MapLoadingBar, WorldMap/WorldMapNode, ChantOverlay.

### Game Systems (`Chaos.Client/Systems/`)
- **`AnimationSystem`** -- Pure methods for walk/body/creature animations, frame calculation, walk offset lerp.
- **`CastingSystem`** -- Spell targeting + chant management.
- **`ChatSystem`** -- Chat message handling and routing.
- **`SoundSystem`** -- NAudio MP3->PCM, cached playback, music looping.
- **`Pathfinder`** -- A* pathfinding algorithm.
- **`MachineIdentity`** -- Machine-specific identification for the client.
- **`ClientSettings`** -- Static class. Persistent user settings. Access via `ClientSettings.SoundVolume`, etc.
- **`GlobalSettings`** -- Static config: ClientVersion (741), DataPath, LobbyHost/Port.

### World State & Models
- **`WorldState`** (`Collections/`) -- Static class. Entity tracking, sorted rendering, active effects, all ViewModel state. Access via `WorldState.Inventory`, `WorldState.Attributes`, etc.
- **`WorldEntity`** (`Models/`) -- Full entity data bag: position, direction, appearance, animation state, emotes.
- **Other models:** `Animation`, `EntityRemovalAnimation`, `EntityHighlight`, `SlotDragPayload`, `PathfindingState`, `TileClickTracker`, `MailEntry`, `FriendEntry`, `LegendMarkEntry`, `WorldListEntry`.

### ViewModel (`Chaos.Client/ViewModel/`)
Authoritative state objects exposed as static properties on WorldState, updated by server packets:
- **`PlayerAttributes`** -- Stats, HP/MP, experience.
- **`Inventory`** -- Items and gold.
- **`SkillBook`/`SpellBook`** -- Skills/spells with cooldown timers.
- **`Equipment`** -- Equipped items.
- **`Chat`** -- Chat and orange bar messages.
- **`Exchange`** -- Trade state.
- **`Board`** -- Bulletin board / mail state.
- **`GroupState`/`GroupInvite`** -- Party/group membership.
- **`NpcInteraction`** -- Dialog/menu state.
- **`UserOptions`** -- Server-sent user option flags.
- **`WorldList`** -- Online players list.

### Entry Point
- **`ChaosGame : Game`** -- 640x480 MonoGame window. Owns ConnectionManager, all renderers, SoundSystem, InputBuffer, ScreenManager. Global entity event wiring at construction. WorldState and ClientSettings are static classes (not owned by ChaosGame).
- **`InputBuffer`** -- Event-driven input: `WasKeyPressed()`, `IsKeyHeld()`, `TextInput`, mouse state. Per-frame freeze.

## C# Coding Standards

- Target: .NET 10.0, C# 14 language version
- Nullable reference types enabled, implicit usings enabled
- Write high-verbosity code: descriptive names, explicit types, early returns
- Handle edge cases first
- Keep comments concise, explain "why" not "what"
- Follow existing patterns in neighboring code
- Respect package versions pinned in `Directory.Packages.props`

## Conventions

### Naming
- **Private fields:** PascalCase, no prefix -- e.g. `private readonly Lock SendLock = new();`
- **No backing fields:** Use auto-properties with `field` keyword, `private set`, or `init` instead of manual backing fields
- **Constants:** UPPER_SNAKE_CASE -- e.g. `private const int RECEIVE_BUFFER_SIZE = ...;`
- Fields may share a name with their type -- e.g. `private Tileset Tileset`, `private Socket? Socket`

### Concurrency
- Use `Lock` with `EnterScope()` instead of the `lock` keyword -- e.g. `using var scope = SendLock.EnterScope();`

### Packet Dispatch
- Use array-indexed handler dispatch (not switch-case) for opcode routing, matching Chaos-Server's pattern
- Delegate arrays sized `byte.MaxValue + 1`, indexed by opcode byte, registered via `IndexHandlers()`

### UI Patterns
- All UI panels derive from `PrefabPanel` (for prefab-based layouts) or `UIPanel` (for manual layouts)
- `PrefabPanel` provides `CreateButton`/`CreateImage`/`CreateLabel`/`CreateTextBox`/`CreateProgressBar` to selectively create controls from prefab data. Panels explicitly create only the controls they need (no auto-populate).
- Popup panels use `Show()`/`Hide()` for visibility and are children of the WorldScreen Root panel
- HUD has two implementations behind `IWorldHud`: `WorldHudControl` (classic compact) and `LargeWorldHudControl` (expanded)
- HUD tab panels share the center-bottom area via `ShowTab(HudTab)` -- only one visible at a time
- World controls organized into subdirectories: `Hud/`, `Hud/Panel/`, `Hud/Panel/Slots/`, `Popups/`, `Popups/Boards/`, `Popups/Dialog/`, `Popups/Exchange/`, `Popups/Options/`, `Popups/Profile/`, `Popups/WorldList/`, `ViewPort/`
- Hotkeys: A=Inventory, S=Skills, D=Spells, Shift+S/D=Alt panels, F=Chat, Shift+F=MessageHistory, G=Stats, Shift+G=ExtendedStats, H=Tools, F9=Ignore, Tab=TabMap, F1=Help, F3=Macros, F4=Settings, F5=Refresh, F7=Mail, F8=Group, F10=Friends
- Grid panels use `PanelBase` -> `PanelSlot` with slot number overlays and cooldown rendering
- Server-driven UI: many panels (exchange, dialog, equipment, profile) are populated by server packets, not client state
- Emote hotkeys: Ctrl+1-0/- (BodyAnimation 9-19), Ctrl+Alt+1-0/- (23-33), Alt+1-0/- (34-44)
- Slot hotkeys: 1-9, 0, -, = -> UseItem/UseSkill/UseSpell depending on active panel

### Architecture Patterns
- **Screens own controls:** WorldScreen creates and manages all world UI controls as children of its Root UIPanel
- **WorldScreen partial classes:** Split by concern (Draw, Update, Input, ServerHandlers, Wiring, Map) for maintainability
- **Events bridge network to UI:** ConnectionManager fires events -> WorldScreen subscribes -> creates/updates/shows controls
- **ViewModel state:** WorldState is a static class exposing ViewModel objects (Inventory, SkillBook, etc.) updated by server packets. Controls access state directly via `WorldState.Xxx` -- no constructor injection needed.
- **Cache-on-demand:** All renderers cache textures lazily and clear on map change
- **Data bag entities:** WorldEntity holds all state; AnimationSystem provides pure functions
- **Separation:** Rendering layer has no dependency on Networking; coordination happens in Client project
- **Global entity wiring:** ChaosGame wires entity tracking events at construction (before WorldScreen exists)
- **Pathfinding:** Right-click A* to tile/entity, entity following with auto-assail, arrow/spacebar cancels
- **Casting flow:** CastingSystem coordinates targeting -> UseSpellOnTarget and chant progress

### Other
- Case-insensitive string operations: `StartsWithI`, `ContainsI`, `EqualsI`, `ReplaceI`
- Thread-safe cache access via `MemoryCacheExtensions.SafeGetOrCreate<T>`
- Repository `Get` methods return null on failure (try-catch pattern)
- UI controls use `NeverRemove` cache priority; other assets use sliding expiration
- Disposable cached objects are disposed via post-eviction callbacks

## Review Policy

Notable refactors or changes must have at minimum:
1. **Bug/regression review** -- A team member reviews the changes for correctness, edge cases, and regressions.
2. **Architecture/design review** -- A separate team member reviews for consistency with the current architecture, adherence to established patterns, and reasonable optimizations.

### Plan Workflow

When writing any implementation plan, each plan must include:
- **Phase-level review gates** -- After each phase/milestone, include a review step that performs both bug/regression review and architecture/design review of the changes made in that phase before proceeding to the next.
- **Final review** -- After full implementation is complete, include a comprehensive review step covering the entire changeset for correctness, regressions, architectural consistency, and adherence to established patterns.
- **Execution via project lead** -- When an implementation plan is approved, send the full plan to the project-lead agent for orchestration. The project lead breaks the plan into tasks and assigns work to the appropriate specialist agents.

## Guardrails

- Do not introduce interactive prompts in scripts or commands
- Do not add commentary inside code solely to explain actions
- Prefer semantic code search over full directory scans
- Avoid exception swallowing -- use guard checks (`TryGetValue`, bounds checks, null checks) instead of try-catch for control flow. Prefer `archive.TryGetValue` + `FromEntry` over `FromArchive` wrapped in try-catch, `lookup.Palettes.TryGetValue` over `lookup.GetPaletteForId` in try-catch, etc.
- Every implementation plan must include review gates after each phase and a final review after full implementation. Do not proceed to the next phase without completing both bug/regression and architecture/design review of the current phase.
- When an implementation plan is approved, send it to the project-lead agent for orchestration. Do not assign work to specialist agents directly -- the project lead coordinates all work assignment.

## Control File Reference

Control files (`.txt` + `.spf`/`.epf`) define UI panel layouts. Loaded via `DataContext.UserControls.Get("_name")` -> `ControlPrefabSet`. Full catalog of all control files, their image references, consuming classes, format specification, and ControlType enum is in `controlFileList.txt` at solution root.

### Pattern: Building a Panel from a ControlPrefabSet
```csharp
// Extend PrefabPanel -- constructor handles anchor dimensions, centering, and background
public sealed class MyPanel : PrefabPanel
{
    public UIButton? OkButton { get; }
    public UIButton? CancelButton { get; }

    public MyPanel(GraphicsDevice device) : base(device, "_name")
    {
        // Create controls by name from the prefab definition
        OkButton = CreateButton("OK");
        CancelButton = CreateButton("Cancel");
        var title = CreateLabel("Title", TextAlignment.Center);
        var icon = CreateImage("Icon");
        var inputRect = GetRect("InputArea");  // rect-only lookup, no child created
    }
}
```

## Isometric Rendering Reference

Tile dimensions: 56x27 pixels, half-tile: 28x14 (from `DALib.Definitions.CONSTANTS`).

```
Tile -> Pixel (from Graphics.RenderMap):
  initialDrawX = (mapHeight - 1) * 28
  For each tile (x, y):
    pixelX = initialDrawX + x * 28    (initialDrawX decrements by 28 each y row)
    pixelY = initialDrawY + x * 14    (initialDrawY increments by 14 each y row)

Foreground tile positioning:
  lfgDrawX = same as bgDrawX
  lfgDrawY = bgDrawY + (x+1) * 14 - image.Height + 14  (bottom-aligned)
  Only render if tileIndex.IsRenderedTileIndex() -> (index > 10012) || ((index % 10000) > 12)

Draw order (painter's algorithm -- diagonal stripe):
  1. Background tiles (floor) -- y-major, x-minor order
  2. Foreground tiles + Entities -- diagonal stripe (depth = x+y ascending), X ascending within stripe
  3. Effects -- ground-targeted in stripe pass, entity-targeted after entity
  4. Tab map overlay -- on top of world, under HUD
  5. UI overlay -- separate SpriteBatch pass, no camera transform
```

## DALib Key Types (local fork at ../dalib/DALib/)

- **`MapFile`** -- `Tiles[x,y]` returns `MapTile` with `.Background`, `.LeftForeground`, `.RightForeground`
- **`Tileset`** -- `Collection<Tile>`, indexed by background tile ID
- **`Tile`** -- 56x27 palettized pixel data
- **`HpfFile`** -- Foreground tile, 28px wide, variable height
- **`Palette`** -- 256 SKColors. `Dye(colorTableEntry)` returns new palette with dye colors at index 98+.
- **`PaletteLookup`** -- Maps tile IDs to palettes via PaletteTable. Khan archives use `KhanPalOverrideType.Male`/`.Female`.
- **`ColorTable`** -- Dye color table from `.tbl` files. `ColorTableEntry` has `Colors[]` (6 SKColors for palette dye slots).
- **`EpfFile/MpfFile/EfaFile`** -- Sprite/animation formats with frame collections
- **`Palettized<T>`** -- Generic wrapper: `.Entity` + `.Palette`, implements IDisposable
- **`DataArchive`** -- DAT file container. `archive["filename"]` or `archive.TryGetValue(name, out entry)`

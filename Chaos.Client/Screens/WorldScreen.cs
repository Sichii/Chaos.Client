#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Controls.World;
using Chaos.Client.Data;
using Chaos.Client.Definitions;
using Chaos.Client.Models;
using Chaos.Client.Rendering;
using Chaos.Client.Systems;
using Chaos.Client.Systems.Animation;
using Chaos.DarkAges.Definitions;
using Chaos.Extensions.Common;
using Chaos.Geometry.Abstractions.Definitions;
using Chaos.Networking.Entities.Server;
using DALib.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using BoardOrResponseType = Chaos.DarkAges.Definitions.BoardOrResponseType;
using BoardRequestType = Chaos.DarkAges.Definitions.BoardRequestType;
using ClientGroupSwitch = Chaos.DarkAges.Definitions.ClientGroupSwitch;
using ServerGroupSwitch = Chaos.DarkAges.Definitions.ServerGroupSwitch;
using CONSTANTS = DALib.Definitions.CONSTANTS;
using DialogArgsType = Chaos.DarkAges.Definitions.DialogArgsType;
using ExchangeRequestType = Chaos.DarkAges.Definitions.ExchangeRequestType;
using ExchangeResponseType = Chaos.DarkAges.Definitions.ExchangeResponseType;
using MarkColor = Chaos.DarkAges.Definitions.MarkColor;
using NpcEntityType = Chaos.DarkAges.Definitions.EntityType;
using PanelType = Chaos.DarkAges.Definitions.PanelType;
using PublicMessageType = Chaos.DarkAges.Definitions.PublicMessageType;
using ServerMessageType = Chaos.DarkAges.Definitions.ServerMessageType;
using UserOption = Chaos.DarkAges.Definitions.UserOption;
#endregion

namespace Chaos.Client.Screens;

/// <summary>
///     Main game screen that renders the current map with the camera centered on the player. Activated after world entry
///     completes (all essential packets received). Receives map data from the server via ConnectionManager events. Handles
///     entity rendering with diagonal stripe draw ordering for correct isometric occlusion.
/// </summary>
public sealed class WorldScreen : IScreen
{
    private const int HALF_TILE_WIDTH = CONSTANTS.HALF_TILE_WIDTH;
    private const int HALF_TILE_HEIGHT = CONSTANTS.HALF_TILE_HEIGHT;

    // Walk queue: when walk animation is >= 75% complete, one walk can be queued
    private const float WALK_QUEUE_THRESHOLD = 0.75f;

    // Spacebar (assail) repeat interval when held
    private const float SPACEBAR_INTERVAL_MS = 100f;

    // Additive tint for entity hover highlight — legend.pal index 0x28, BGR bytes (CB,00,17) → RGB(23, 0, 203)
    private static readonly Color HIGHLIGHT_TINT = new(23, 0, 203);

    // Aisling body anchor within the padded composite canvas.
    // Canvas is padded by 27px on each side for weapon/accessory layers, so body center shifts right.
    private const int BODY_CENTER_X = 28 + 27;
    private const int BODY_CENTER_Y = 70;
    private const float DOUBLE_CLICK_MS = 300f;

    // Aisling texture cache: keyed by entity ID, invalidated when appearance/frame/suffix changes
    private readonly Dictionary<uint, AislingCacheEntry> AislingCache = new();

    // Active chat bubbles: keyed by entity ID, one per entity (new message replaces old)
    private readonly Dictionary<uint, ChatBubble> ChatBubbles = new();

    // Ground item texture cache: keyed by item sprite ID
    private readonly Dictionary<int, Texture2D?> GroundItemCache = new();
    private Camera Camera = null!;
    private ContextMenu ContextMenu = null!;
    private GraphicsDevice Device = null!;
    private ExchangeControl Exchange = null!;
    private FriendsListControl FriendsList = null!;

    private ChaosGame Game = null!;
    private GroupControl GroupPanel = null!;
    private uint? HighlightedEntityId;
    private uint? HighlightTintedEntityId;
    private Texture2D? HighlightTintedTexture;
    private HotkeyHelpControl HotkeyHelp = null!;
    private MainGameHudControl Hud = null!;
    private int LastClickTileX;
    private int LastClickTileY;
    private float LastClickTimer = DOUBLE_CLICK_MS;
    private MacroMenuControl MacroMenu = null!;
    private MailListControl MailList = null!;
    private MailReadControl MailRead = null!;
    private MailSendControl MailSend = null!;
    private MainOptionsControl MainOptions = null!;

    private MapFile? MapFile;
    private bool MapPreloaded;
    private MapRenderer MapRenderer = null!;

    // Overlay panels (rendered on top of HUD)
    private NpcDialogControl NpcDialog = null!;
    private Direction? QueuedWalkDirection;
    private RasterizerState ScissorRasterizerState = null!;
    private SettingsControl SettingsDialog = null!;
    private float SpacebarTimer;
    private SelfProfileTabControl StatusBook = null!;
    private TextPopupControl TextPopup = null!;

    // Tile cursor: dashed ellipse drawn on the hovered tile
    private Texture2D? TileCursorTexture;
    private WorldListControl WorldList = null!;

    /// <inheritdoc />
    public UIPanel? Root { get; private set; }

    /// <inheritdoc />
    public void Dispose() { }

    /// <inheritdoc />
    public void Draw(SpriteBatch spriteBatch, GameTime gameTime)
    {
        // Pass 1: World rendering — clipped to the HUD viewport area, camera transform
        if (MapFile is not null && MapPreloaded)
        {
            var viewportRect = Hud.ViewportBounds;
            Device.ScissorRectangle = viewportRect;

            var transform = Matrix.CreateTranslation(viewportRect.X, viewportRect.Y, 0);

            // Background tiles + tile cursor: batched (many draws, no blend changes)
            spriteBatch.Begin(samplerState: GlobalSettings.Sampler, rasterizerState: ScissorRasterizerState, transformMatrix: transform);
            MapRenderer.DrawBackground(spriteBatch, MapFile, Camera);
            DrawTileCursor(spriteBatch);
            spriteBatch.End();

            // Foreground, entities, effects: immediate mode (per-stripe ordering, blend switches for additive effects)
            spriteBatch.Begin(
                SpriteSortMode.Immediate,
                BlendState.AlphaBlend,
                GlobalSettings.Sampler,
                null,
                ScissorRasterizerState,
                null,
                transform);
            DrawForegroundAndEntities(spriteBatch);
            DrawChatBubbles(spriteBatch);
            spriteBatch.End();

            // Debug overlay: entity hitboxes, tile grid, etc.
            if (DebugOverlay.IsActive)
            {
                spriteBatch.Begin(
                    SpriteSortMode.Immediate,
                    BlendState.AlphaBlend,
                    GlobalSettings.Sampler,
                    null,
                    ScissorRasterizerState,
                    null,
                    transform);
                DrawWorldDebug(spriteBatch);
                spriteBatch.End();
            }
        }

        // Pass 2: UI overlay — full screen, no transform
        spriteBatch.Begin(samplerState: GlobalSettings.Sampler);
        Root!.Draw(spriteBatch);
        spriteBatch.End();
    }

    /// <inheritdoc />
    public void Initialize(ChaosGame game)
    {
        Game = game;

        // Map assembly events
        Game.Connection.OnMapInfo += HandleMapInfo;
        Game.Connection.OnMapData += HandleMapData;
        Game.Connection.OnMapLoadComplete += HandleMapLoadComplete;
        Game.Connection.OnLocationChanged += HandleLocationChanged;

        // Entity events
        // WorldState updates (entity add/remove/walk/turn) are wired in ChaosGame so they
        // work during world entry before this screen exists. We subscribe here only for
        // screen-specific side effects (HUD updates, cache cleanup).
        Game.Connection.OnDisplayAisling += HandleDisplayAisling;
        Game.Connection.OnRemoveEntity += HandleRemoveEntity;
        Game.Connection.OnClientWalkResponse += HandleClientWalkResponse;

        // HUD data events
        Game.Connection.OnAttributes += HandleAttributes;

        // Chat events
        Game.Connection.OnDisplayPublicMessage += HandleDisplayPublicMessage;
        Game.Connection.OnServerMessage += HandleServerMessage;

        // Inventory/Skill/Spell pane events
        Game.Connection.OnAddItemToPane += HandleAddItemToPane;
        Game.Connection.OnRemoveItemFromPane += HandleRemoveItemFromPane;
        Game.Connection.OnAddSkillToPane += HandleAddSkillToPane;
        Game.Connection.OnRemoveSkillFromPane += HandleRemoveSkillFromPane;
        Game.Connection.OnAddSpellToPane += HandleAddSpellToPane;
        Game.Connection.OnRemoveSpellFromPane += HandleRemoveSpellFromPane;

        // NPC dialog events
        Game.Connection.OnDisplayDialog += HandleDisplayDialog;
        Game.Connection.OnDisplayMenu += HandleDisplayMenu;

        // Equipment events
        Game.Connection.OnEquipment += HandleEquipment;
        Game.Connection.OnDisplayUnequip += HandleDisplayUnequip;

        // Cooldown
        Game.Connection.OnCooldown += HandleCooldown;

        // Refresh response
        Game.Connection.OnRefreshResponse += HandleRefreshResponse;

        // World list (user list)
        Game.Connection.OnWorldList += HandleWorldList;

        // Exchange / trade
        Game.Connection.OnDisplayExchange += HandleDisplayExchange;

        // Board / bulletin
        Game.Connection.OnDisplayBoard += HandleDisplayBoard;

        // Group invite
        Game.Connection.OnDisplayGroupInvite += HandleDisplayGroupInvite;

        // Profiles
        Game.Connection.OnSelfProfile += HandleSelfProfile;
        Game.Connection.OnOtherProfile += HandleOtherProfile;

        // Animations / effects / sound
        Game.Connection.OnBodyAnimation += HandleBodyAnimation;
        Game.Connection.OnAnimation += HandleAnimation;
        Game.Connection.OnSound += HandleSound;

        // Map transitions
        Game.Connection.OnMapChangePending += HandleMapChangePending;

        // Logout
        Game.Connection.OnExitResponse += HandleExitResponse;

        // Health bars
        Game.Connection.OnHealthBar += HandleHealthBar;

        // Light level
        Game.Connection.OnLightLevel += HandleLightLevel;

        // Notepad popups
        Game.Connection.OnDisplayReadonlyNotepad += HandleDisplayReadonlyNotepad;
        Game.Connection.OnDisplayEditableNotepad += HandleDisplayEditableNotepad;
    }

    /// <inheritdoc />
    public void LoadContent(GraphicsDevice graphicsDevice)
    {
        Device = graphicsDevice;

        // HUD defines the viewport bounds for world rendering
        Hud = new MainGameHudControl(graphicsDevice);

        var viewport = Hud.ViewportBounds;

        Camera = new Camera(viewport.Width, viewport.Height)
        {
            Offset = new Vector2(-28, 24)
        };
        MapRenderer = new MapRenderer();

        ScissorRasterizerState = new RasterizerState
        {
            ScissorTestEnable = true
        };

        TileCursorTexture = CreateTileCursorTexture(graphicsDevice);

        // Overlay panels — ZIndex: -2 sub-panels, -1 slide panels, 0 standard (default), 1 popups, 2 context menu
        NpcDialog = new NpcDialogControl(graphicsDevice);
        WireNpcDialog();

        MainOptions = new MainOptionsControl(graphicsDevice)
        {
            ZIndex = -1
        };
        MainOptions.SetViewportBounds(Hud.ViewportBounds);
        WireOptionsDialog();

        // Sub-panels slide out from MainOptions' left edge, render behind it
        var optionsAnchorX = Hud.ViewportBounds.X + Hud.ViewportBounds.Width - MainOptions.Width + 10;
        var optionsAnchorY = Hud.ViewportBounds.Y;

        SettingsDialog = new SettingsControl(graphicsDevice)
        {
            ZIndex = -2
        };
        SettingsDialog.SetSlideAnchor(optionsAnchorX, optionsAnchorY);

        SettingsDialog.OnSettingToggled += (index, _) =>
        {
            var option = (UserOption)(index + 1);
            Game.Connection.SendOptionToggle(option);
        };

        SettingsDialog.OnLocalSettingToggled += (index, value) =>
        {
            switch (index)
            {
                case 6:
                    Game.Settings.GroupObjectOption = value;

                    break;
                case 8:
                    Game.Settings.ScrollLevel = value ? 1 : 0;

                    break;
                case 9:
                    Game.Settings.SkillSpellSelectByToggle = value;

                    break;
                case 10:
                    Game.Settings.UserClickMode = value ? 1 : 0;

                    break;
                case 11:
                    Game.Settings.MonsterSayRecordMode = value;

                    break;
                case 12:
                    Game.Settings.GroupAnswer = value;

                    break;
            }

            Game.Settings.Save();
        };

        // Apply saved local settings to the settings dialog
        SettingsDialog.SetSettingValue(6, Game.Settings.GroupObjectOption);
        SettingsDialog.SetSettingValue(8, Game.Settings.ScrollLevel > 0);
        SettingsDialog.SetSettingValue(9, Game.Settings.SkillSpellSelectByToggle);
        SettingsDialog.SetSettingValue(10, Game.Settings.UserClickMode > 0);
        SettingsDialog.SetSettingValue(11, Game.Settings.MonsterSayRecordMode);
        SettingsDialog.SetSettingValue(12, Game.Settings.GroupAnswer);

        MacroMenu = new MacroMenuControl(graphicsDevice)
        {
            ZIndex = -2
        };
        MacroMenu.SetSlideAnchor(optionsAnchorX, optionsAnchorY);

        HotkeyHelp = new HotkeyHelpControl(graphicsDevice);

        GroupPanel = new GroupControl(graphicsDevice);

        GroupPanel.OnInvite += () =>
        {
            // Open chat in "Group: " mode for typing an invite target name
            FocusChat("Group invite: ", new Color(154, 205, 50));
        };

        WorldList = new WorldListControl(graphicsDevice)
        {
            ZIndex = -1
        };
        WorldList.SetViewportBounds(Hud.ViewportBounds);

        FriendsList = new FriendsListControl(graphicsDevice)
        {
            ZIndex = -2
        };
        FriendsList.SetSlideAnchor(optionsAnchorX, optionsAnchorY);

        Exchange = new ExchangeControl(graphicsDevice);
        MailList = new MailListControl(graphicsDevice);
        MailRead = new MailReadControl(graphicsDevice);
        MailSend = new MailSendControl(graphicsDevice);
        WireExchange();
        WireMailControls();

        StatusBook = new SelfProfileTabControl(graphicsDevice);

        TextPopup = new TextPopupControl(graphicsDevice)
        {
            ZIndex = 1
        };

        ContextMenu = new ContextMenu(graphicsDevice)
        {
            ZIndex = 2
        };

        Root = new UIPanel
        {
            Name = "WorldRoot",
            Width = ChaosGame.VIRTUAL_WIDTH,
            Height = ChaosGame.VIRTUAL_HEIGHT
        };
        Root.AddChild(Hud);
        Root.AddChild(NpcDialog);
        Root.AddChild(MainOptions);
        Root.AddChild(SettingsDialog);
        Root.AddChild(MacroMenu);
        Root.AddChild(HotkeyHelp);
        Root.AddChild(GroupPanel);
        Root.AddChild(WorldList);
        Root.AddChild(FriendsList);
        Root.AddChild(Exchange);
        Root.AddChild(MailList);
        Root.AddChild(MailRead);
        Root.AddChild(MailSend);
        Root.AddChild(StatusBook);
        Root.AddChild(TextPopup);
        Root.AddChild(ContextMenu);

        // Wire HUD buttons
        if (Hud.OptionButton is not null)
            Hud.OptionButton.OnClick += () => MainOptions.Show();

        if (Hud.HelpButton is not null)
            Hud.HelpButton.OnClick += () => HotkeyHelp.Show();

        if (Hud.GroupButton is not null)
            Hud.GroupButton.OnClick += () => GroupPanel.Show();

        if (Hud.UsersButton is not null)
            Hud.UsersButton.OnClick += () =>
            {
                if (WorldList.Visible)
                {
                    WorldList.Hide();

                    return;
                }

                WorldList.Show(new List<WorldListEntry>(), 0);
                Game.Connection.RequestWorldList();
            };

        if (Hud.BulletinButton is not null)
            Hud.BulletinButton.OnClick += () => Game.Connection.SendBoardInteraction(BoardRequestType.BoardList);

        if (Hud.LegendButton is not null)
            Hud.LegendButton.OnClick += () => Game.Connection.RequestSelfProfile();

        if (Hud.TownMapButton is not null)
            Hud.TownMapButton.OnClick += () => Hud.ShowOrangeBarMessage("Town map is not yet implemented.");

        if (Hud.ExpandButton is not null)
            Hud.ExpandButton.OnClick += () => Hud.ShowOrangeBarMessage("Hud expant is not yet implmented.");

        // Set the player entity ID and server name from the connection
        Game.World.PlayerEntityId = Game.Connection.AislingId;
        Hud.SetServerName(Game.Connection.ServerName);

        // If we already have map info from the connection (world entry completed before screen switch),
        // build the map file and try to preload
        TryBuildInitialMap();

        // Apply initial attributes if already received during world entry
        if (Game.Connection.Attributes is { } attrs)
            HandleAttributes(attrs);

        // Apply initial inventory/skill/spell state (received during world entry before this screen existed)
        foreach ((var slot, (var sprite, var name)) in Game.Connection.InventorySlots)
        {
            Hud.Inventory.SetSlot(slot, sprite);
            Hud.Inventory.SetSlotName(slot, name);
        }

        foreach ((var slot, (var sprite, var name)) in Game.Connection.SkillSlots)
        {
            Hud.SkillBook.SetSlot(slot, sprite);
            Hud.SkillBook.SetSlotName(slot, name);
            Hud.SkillBookAlt.SetSlot(slot, sprite);
            Hud.SkillBookAlt.SetSlotName(slot, name);
        }

        foreach ((var slot, (var sprite, var name)) in Game.Connection.SpellSlots)
        {
            Hud.SpellBook.SetSlot(slot, sprite);
            Hud.SpellBook.SetSlotName(slot, name);
            Hud.SpellBookAlt.SetSlot(slot, sprite);
            Hud.SpellBookAlt.SetSlotName(slot, name);
        }

        // Request current server settings (populates setting names/values)
        Game.Connection.SendOptionToggle(UserOption.Request);

        // Wire panel click-to-use events
        Hud.Inventory.OnSlotClicked += HandleInventorySlotClicked;
        Hud.Inventory.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.Inventory, s, t);
        Hud.Inventory.OnSlotDroppedOutside += HandleInventoryDropInViewport;
        Hud.SkillBook.OnSlotClicked += HandleSkillSlotClicked;
        Hud.SkillBook.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.SkillBook, s, t);
        Hud.SkillBookAlt.OnSlotClicked += HandleSkillSlotClicked;
        Hud.SkillBookAlt.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.SkillBook, s, t);
        Hud.SpellBook.OnSlotClicked += HandleSpellSlotClicked;
        Hud.SpellBook.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.SpellBook, s, t);
        Hud.SpellBookAlt.OnSlotClicked += HandleSpellSlotClicked;
        Hud.SpellBookAlt.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.SpellBook, s, t);

        // Wire hover description for all grid panels
        Hud.Inventory.OnSlotHovered += Hud.SetDescription;
        Hud.SkillBook.OnSlotHovered += Hud.SetDescription;
        Hud.SkillBookAlt.OnSlotHovered += Hud.SetDescription;
        Hud.SpellBook.OnSlotHovered += Hud.SetDescription;
        Hud.SpellBookAlt.OnSlotHovered += Hud.SetDescription;

        // Apply player name from entity if already tracked
        var player = Game.World.GetPlayerEntity();

        if (player is not null && (player.Name.Length > 0))
            Hud.SetPlayerName(player.Name);
    }

    /// <inheritdoc />
    public void UnloadContent()
    {
        Game.Connection.OnMapInfo -= HandleMapInfo;
        Game.Connection.OnMapData -= HandleMapData;
        Game.Connection.OnMapLoadComplete -= HandleMapLoadComplete;
        Game.Connection.OnLocationChanged -= HandleLocationChanged;
        Game.Connection.OnDisplayAisling -= HandleDisplayAisling;
        Game.Connection.OnRemoveEntity -= HandleRemoveEntity;
        Game.Connection.OnClientWalkResponse -= HandleClientWalkResponse;
        Game.Connection.OnAttributes -= HandleAttributes;
        Game.Connection.OnDisplayPublicMessage -= HandleDisplayPublicMessage;
        Game.Connection.OnServerMessage -= HandleServerMessage;
        Game.Connection.OnAddItemToPane -= HandleAddItemToPane;
        Game.Connection.OnRemoveItemFromPane -= HandleRemoveItemFromPane;
        Game.Connection.OnAddSkillToPane -= HandleAddSkillToPane;
        Game.Connection.OnRemoveSkillFromPane -= HandleRemoveSkillFromPane;
        Game.Connection.OnAddSpellToPane -= HandleAddSpellToPane;
        Game.Connection.OnRemoveSpellFromPane -= HandleRemoveSpellFromPane;
        Game.Connection.OnDisplayDialog -= HandleDisplayDialog;
        Game.Connection.OnDisplayMenu -= HandleDisplayMenu;
        Game.Connection.OnEquipment -= HandleEquipment;
        Game.Connection.OnDisplayUnequip -= HandleDisplayUnequip;
        Game.Connection.OnCooldown -= HandleCooldown;
        Game.Connection.OnRefreshResponse -= HandleRefreshResponse;
        Game.Connection.OnWorldList -= HandleWorldList;
        Game.Connection.OnDisplayExchange -= HandleDisplayExchange;
        Game.Connection.OnDisplayBoard -= HandleDisplayBoard;
        Game.Connection.OnDisplayGroupInvite -= HandleDisplayGroupInvite;
        Game.Connection.OnSelfProfile -= HandleSelfProfile;
        Game.Connection.OnOtherProfile -= HandleOtherProfile;
        Game.Connection.OnBodyAnimation -= HandleBodyAnimation;
        Game.Connection.OnAnimation -= HandleAnimation;
        Game.Connection.OnSound -= HandleSound;
        Game.Connection.OnMapChangePending -= HandleMapChangePending;
        Game.Connection.OnExitResponse -= HandleExitResponse;
        Game.Connection.OnHealthBar -= HandleHealthBar;
        Game.Connection.OnLightLevel -= HandleLightLevel;
        Game.Connection.OnDisplayReadonlyNotepad -= HandleDisplayReadonlyNotepad;
        Game.Connection.OnDisplayEditableNotepad -= HandleDisplayEditableNotepad;

        // Unwire panel click-to-use events
        Hud.Inventory.OnSlotClicked -= HandleInventorySlotClicked;
        Hud.SkillBook.OnSlotClicked -= HandleSkillSlotClicked;
        Hud.SkillBookAlt.OnSlotClicked -= HandleSkillSlotClicked;
        Hud.SpellBook.OnSlotClicked -= HandleSpellSlotClicked;
        Hud.SpellBookAlt.OnSlotClicked -= HandleSpellSlotClicked;

        MapRenderer.Dispose();
        ScissorRasterizerState.Dispose();
        Root?.Dispose();
        ClearAislingCache();
        ClearGroundItemCache();
        ClearChatBubbles();
    }

    /// <inheritdoc />
    public void Update(GameTime gameTime)
    {
        var input = Game.Input;
        LastClickTimer += (float)gameTime.ElapsedGameTime.TotalMilliseconds;
        var elapsedMs = (float)gameTime.ElapsedGameTime.TotalMilliseconds;

        // Advance entity animations and active effects
        var smoothScroll = Game.Settings.ScrollLevel > 0;
        var player = Game.World.GetPlayerEntity();

        foreach (var entity in Game.World.GetSortedEntities())
        {
            // All entities step discretely by default. Player gets smooth lerp only if setting enabled.
            var isSmooth = (entity == player) && smoothScroll;
            AnimationManager.Advance(entity, elapsedMs, isSmooth);
        }

        Game.World.UpdateEffects(elapsedMs);

        // Execute queued walk when player becomes idle after walk animation.
        // Sets MovementHandled to prevent the input check from also walking this frame.
        var movementHandled = false;

        if (player is not null && (player.AnimState == EntityAnimState.Idle) && QueuedWalkDirection.HasValue)
        {
            var queuedDir = QueuedWalkDirection.Value;
            QueuedWalkDirection = null;

            if (player.Direction != queuedDir)
            {
                Game.Connection.Turn(queuedDir);
                player.Direction = queuedDir;
            } else
                Game.Connection.Walk(queuedDir);

            movementHandled = true;
        }

        // Camera follows player's visual position (tile + walk interpolation offset)
        FollowPlayerCamera();

        // Overlay panels get first priority for input
        if (NpcDialog.Visible)
        {
            NpcDialog.Update(gameTime, input);

            return;
        }

        if (MainOptions.Visible)
        {
            // Sub-panels slide out from MainOptions — update them alongside it
            // If a sub-panel is open, it gets input priority (consumes Escape before MainOptions)
            var subPanelOpen = false;

            if (MacroMenu.Visible)
            {
                MacroMenu.Update(gameTime, input);
                subPanelOpen = true;
            } else if (SettingsDialog.Visible)
            {
                SettingsDialog.Update(gameTime, input);
                subPanelOpen = true;
            } else if (FriendsList.Visible)
            {
                FriendsList.Update(gameTime, input);
                subPanelOpen = true;
            }

            if (!subPanelOpen)
                MainOptions.Update(gameTime, input);

            return;
        }

        if (SettingsDialog.Visible)
        {
            SettingsDialog.Update(gameTime, input);

            return;
        }

        if (MacroMenu.Visible)
        {
            MacroMenu.Update(gameTime, input);

            return;
        }

        if (HotkeyHelp.Visible)
        {
            HotkeyHelp.Update(gameTime, input);

            return;
        }

        if (GroupPanel.Visible)
        {
            GroupPanel.Update(gameTime, input);

            return;
        }

        if (WorldList.Visible)
        {
            WorldList.Update(gameTime, input);

            return;
        }

        if (FriendsList.Visible)
        {
            FriendsList.Update(gameTime, input);

            return;
        }

        if (Exchange.Visible)
        {
            Exchange.Update(gameTime, input);

            return;
        }

        if (MailRead.Visible)
        {
            MailRead.Update(gameTime, input);

            return;
        }

        if (MailSend.Visible)
        {
            MailSend.Update(gameTime, input);

            return;
        }

        if (MailList.Visible)
        {
            MailList.Update(gameTime, input);

            return;
        }

        if (TextPopup.Visible)
        {
            TextPopup.Update(gameTime, input);

            return;
        }

        if (StatusBook.Visible)
        {
            StatusBook.Update(gameTime, input);

            return;
        }

        // Context menu gets priority when visible
        if (ContextMenu.Visible)
        {
            ContextMenu.Update(gameTime, input);

            return;
        }

        // Escape — close overlays, unfocus chat
        if (input.WasKeyPressed(Keys.Escape))
            if (Hud.ChatInput.IsFocused)
                UnfocusChat();

        // Enter — toggle chat focus / send message
        if (input.WasKeyPressed(Keys.Enter))
        {
            if (Hud.ChatInput.IsFocused)
            {
                var message = Hud.ChatInput.Text.Trim();
                var prefix = Hud.ChatInput.Prefix;

                // Whisper phase 1: "to []? " → user entered a target name, transition to phase 2
                if ((prefix == "to []? ") && (message.Length > 0))
                {
                    Hud.ChatInput.Prefix = $"-> {message}: ";
                    Hud.ChatInput.Text = string.Empty;
                } else
                {
                    DispatchChatMessage(message);
                    Hud.ChatInput.Text = string.Empty;
                    UnfocusChat();
                }
            } else
                FocusChat($"{Hud.PlayerName}: ", Color.White);
        }

        // Hotkeys and movement — only when chat is not focused
        if (!Hud.ChatInput.IsFocused)
        {
            // Shout hotkey (!) — opens chat in shout mode
            if (input.WasKeyPressed(Keys.D1) && (input.IsKeyHeld(Keys.LeftShift) || input.IsKeyHeld(Keys.RightShift)))
            {
                FocusChat($"{Hud.PlayerName}! ", Color.Yellow);

                return;
            }

            // Whisper hotkey (") — opens chat in whisper target mode
            if (input.WasKeyPressed(Keys.OemQuotes) && (input.IsKeyHeld(Keys.LeftShift) || input.IsKeyHeld(Keys.RightShift)))
            {
                FocusChat("to []? ", new Color(100, 149, 237));

                return;
            }

            // Tab panel switching
            var shift = input.IsKeyHeld(Keys.LeftShift) || input.IsKeyHeld(Keys.RightShift);

            if (input.WasKeyPressed(Keys.A))
            {
                if (Hud.ActiveTab == HudTab.Inventory)
                    Game.Connection.RequestSelfProfile();
                else
                    Hud.ShowTab(HudTab.Inventory);
            } else if (input.WasKeyPressed(Keys.S))
                Hud.ShowTab(shift ? HudTab.SkillsAlt : HudTab.Skills);
            else if (input.WasKeyPressed(Keys.D))
                Hud.ShowTab(shift ? HudTab.SpellsAlt : HudTab.Spells);
            else if (input.WasKeyPressed(Keys.F))
                Hud.ShowTab(shift ? HudTab.MessageHistory : HudTab.Chat);
            else if (input.WasKeyPressed(Keys.G))
                Hud.ShowTab(shift ? HudTab.ExtendedStats : HudTab.Stats);
            else if (input.WasKeyPressed(Keys.H))
                Hud.ShowTab(HudTab.Tools);
            else if (input.WasKeyPressed(Keys.T))
                if (!Hud.ChatInput.IsFocused)
                    FocusChat(string.Empty, Color.White);

            // F1 — hotkey help
            // F1 — help merchant (server-side)
            if (input.WasKeyPressed(Keys.F1))
                Game.Connection.ClickEntity(uint.MaxValue);

            // F3 — macro menu
            if (input.WasKeyPressed(Keys.F3))
                MacroMenu.Show();

            // F4 — settings
            if (input.WasKeyPressed(Keys.F4))
                SettingsDialog.Show();

            // F5 — refresh
            if (input.WasKeyPressed(Keys.F5))
                Game.Connection.RequestRefresh();

            // F7 — mail
            if (input.WasKeyPressed(Keys.F7))
                Game.Connection.SendBoardInteraction(BoardRequestType.ViewBoard);

            // F8 — group panel
            if (input.WasKeyPressed(Keys.F8))
                GroupPanel.Show();

            // F9 — focus chat input
            if (input.WasKeyPressed(Keys.F9))
                if (!Hud.ChatInput.IsFocused)
                    FocusChat(string.Empty, Color.White);

            // F10 — friends list
            if (input.WasKeyPressed(Keys.F10))
                FriendsList.Show();

            // Spacebar — assail (repeats while held)
            SpacebarTimer -= elapsedMs;

            if (input.IsKeyHeld(Keys.Space) && (SpacebarTimer <= 0))
            {
                Game.Connection.Spacebar();
                SpacebarTimer = SPACEBAR_INTERVAL_MS;
            } else if (!input.IsKeyHeld(Keys.Space))
                SpacebarTimer = 0;

            // B — pick up item from under player, or from the tile in front
            if (input.WasKeyPressed(Keys.B))
                TryPickupItem();

            // Slot hotkeys: 1-9, 0, -, = → use slot 1-12 of the active panel
            HandleSlotHotkeys(input);

            // Click handling — left click in viewport area
            if (input.WasLeftButtonPressed)
                HandleWorldClick(input.MouseX, input.MouseY);

            // Right-click — context menu on world entities
            if (input.WasRightButtonPressed)
                HandleWorldRightClick(input.MouseX, input.MouseY);

            // Player movement — event-driven, matching the original client's WM_KEYDOWN handling.
            // Each key press/repeat goes through the same turn-vs-walk decision:
            // - Same direction as facing → walk (if idle) or queue (if walk anim >= 75%)
            // - Different direction → turn only (if idle)
            // OS key repeat when holding produces repeated WasKeyPressed events.
            Direction? direction = null;

            if (input.WasKeyPressed(Keys.Up))
                direction = Direction.Up;
            else if (input.WasKeyPressed(Keys.Right))
                direction = Direction.Right;
            else if (input.WasKeyPressed(Keys.Down))
                direction = Direction.Down;
            else if (input.WasKeyPressed(Keys.Left))
                direction = Direction.Left;

            if (direction.HasValue && player is not null && !movementHandled)
            {
                if (player.AnimState == EntityAnimState.Idle)
                {
                    if (player.Direction != direction.Value)
                    {
                        // Different direction → turn only
                        Game.Connection.Turn(direction.Value);
                        player.Direction = direction.Value;
                    } else
                    {
                        // Same direction + idle → walk immediately
                        Game.Connection.Walk(direction.Value);
                        QueuedWalkDirection = null;
                    }
                } else if (player.AnimState == EntityAnimState.Walking)
                {
                    // During walk animation: queue one walk if >= 75% complete
                    var totalDuration = player.AnimFrameCount * player.AnimFrameIntervalMs;
                    var progress = totalDuration > 0 ? player.AnimElapsedMs / totalDuration : 1f;

                    if (progress >= WALK_QUEUE_THRESHOLD)
                        QueuedWalkDirection = direction.Value;
                }
            }

            // Clear queue when all arrow keys released
            if (!input.IsKeyHeld(Keys.Up) && !input.IsKeyHeld(Keys.Right) && !input.IsKeyHeld(Keys.Down) && !input.IsKeyHeld(Keys.Left))
                QueuedWalkDirection = null;
        }

        Hud.Update(gameTime, input);

        // Track highlighted entity when dragging a panel item over the world viewport
        UpdateDragHighlight(input);

        // Update chat bubbles — tick timers and remove expired
        UpdateChatBubbles((float)gameTime.ElapsedGameTime.TotalMilliseconds);

        // Detect shout-to-group transition: user typed "!" while in shout mode (prefix ends with "! ")
        // This matches the original DA convention where "!!" activates group chat
        if (Hud.ChatInput.IsFocused
            && Hud.ChatInput.Prefix.EndsWithI("! ")
            && !Hud.ChatInput.Prefix.StartsWithI("Group")
            && (Hud.ChatInput.Text == "!"))
            FocusChat("Group: ", new Color(154, 205, 50));
    }

    #region Exchange Wiring
    private void WireExchange()
    {
        Exchange.OnOk += () => Game.Connection.SendExchangeInteraction(ExchangeRequestType.Accept, Exchange.OtherUserId);

        Exchange.OnCancel += () =>
        {
            Game.Connection.SendExchangeInteraction(ExchangeRequestType.Cancel, Exchange.OtherUserId);
            Exchange.CloseExchange();
        };
    }
    #endregion

    #region Mail Wiring
    private void WireMailControls()
    {
        // Mail list events
        MailList.OnViewPost += postId =>
        {
            Game.Connection.SendBoardInteraction(BoardRequestType.ViewPost, MailList.BoardId, postId);
        };

        MailList.OnNewMail += () =>
        {
            MailList.Hide();
            MailSend.BoardId = MailList.BoardId;
            MailSend.ShowCompose();
        };

        MailList.OnDeletePost += postId => Game.Connection.SendBoardInteraction(BoardRequestType.Delete, MailList.BoardId, postId);

        MailList.OnReplyPost += postId => Game.Connection.SendBoardInteraction(BoardRequestType.ViewPost, MailList.BoardId, postId);

        // Mail read events
        MailRead.OnClose += () =>
        {
            MailRead.Hide();

            // Re-request the mail list to refresh
            Game.Connection.SendBoardInteraction(BoardRequestType.ViewBoard, MailRead.BoardId);
        };

        MailRead.OnReplyPost += _ =>
        {
            MailRead.Hide();
            MailSend.BoardId = MailRead.BoardId;
            MailSend.ShowCompose(MailRead.CurrentAuthor);
        };

        MailRead.OnDeletePost += postId =>
        {
            Game.Connection.SendBoardInteraction(BoardRequestType.Delete, MailRead.BoardId, postId);
            MailRead.Hide();
        };

        MailRead.OnNewMail += () =>
        {
            MailRead.Hide();
            MailSend.BoardId = MailRead.BoardId;
            MailSend.ShowCompose();
        };

        MailRead.OnPrev += () =>
        {
            // Request previous post
            var prevId = (short)(MailRead.CurrentPostId - 1);
            Game.Connection.SendBoardInteraction(BoardRequestType.ViewPost, MailRead.BoardId, prevId);
        };

        MailRead.OnNext += () =>
        {
            var nextId = (short)(MailRead.CurrentPostId + 1);
            Game.Connection.SendBoardInteraction(BoardRequestType.ViewPost, MailRead.BoardId, nextId);
        };

        // Mail send events
        MailSend.OnSend += (recipient, subject, body) =>
        {
            Game.Connection.SendBoardInteraction(
                BoardRequestType.SendMail,
                MailSend.BoardId,
                to: recipient,
                subject: subject,
                message: body);

            MailSend.Hide();
        };

        MailSend.OnCancel += () => MailSend.Hide();

        // Wire mail button on HUD
        if (Hud.MailButton is not null)
            Hud.MailButton.OnClick += () =>
            {
                // Request the mail board (boardId 0 = personal mail)
                Game.Connection.SendBoardInteraction(BoardRequestType.ViewBoard);
            };
    }
    #endregion

    #region NPC Dialog Wiring
    private void WireNpcDialog()
    {
        NpcDialog.OnClose += () =>
        {
            if (NpcDialog.SourceId is { } sourceId)
                Game.Connection.SendDialogResponse(
                    NpcEntityType.Creature,
                    sourceId,
                    NpcDialog.PursuitId,
                    0); // dialogId 0 = close
        };

        NpcDialog.OnNext += () =>
        {
            if (NpcDialog.SourceId is { } sourceId)
                Game.Connection.SendDialogResponse(
                    NpcEntityType.Creature,
                    sourceId,
                    NpcDialog.PursuitId,
                    (ushort)(NpcDialog.DialogId + 1));
        };

        NpcDialog.OnPrevious += () =>
        {
            if (NpcDialog.SourceId is { } sourceId)
                Game.Connection.SendDialogResponse(
                    NpcEntityType.Creature,
                    sourceId,
                    NpcDialog.PursuitId,
                    (ushort)(NpcDialog.DialogId - 1));
        };

        NpcDialog.OnOptionSelected += optionIndex =>
        {
            if (NpcDialog.SourceId is { } sourceId)
                Game.Connection.SendDialogResponse(
                    NpcEntityType.Creature,
                    sourceId,
                    NpcDialog.PursuitId,
                    (ushort)(NpcDialog.DialogId + 1),
                    DialogArgsType.MenuResponse,
                    (byte)(optionIndex + 1));
        };

        NpcDialog.OnTextSubmit += text =>
        {
            if (NpcDialog.SourceId is { } sourceId)
                Game.Connection.SendDialogResponse(
                    NpcEntityType.Creature,
                    sourceId,
                    NpcDialog.PursuitId,
                    (ushort)(NpcDialog.DialogId + 1),
                    DialogArgsType.TextResponse,
                    args: [text]);
        };
    }
    #endregion

    private record struct AislingCacheEntry(
        AislingAppearance Appearance,
        int FrameIndex,
        bool Flip,
        string AnimSuffix,
        Texture2D? Texture);

    #region Diagonal Stripe Rendering
    /// <summary>
    ///     Iterates foreground tiles, entities, and effects in diagonal stripe order (depth = x+y ascending). Per stripe draw
    ///     order: ground items → aislings → creatures → ground effects → entity effects → foreground tiles. Within each
    ///     category, entities draw in list order (arrival order — later arrivals on top).
    /// </summary>
    private void DrawForegroundAndEntities(SpriteBatch spriteBatch)
    {
        if (MapFile is null)
            return;

        var sortedEntities = Game.World.GetSortedEntities();

        (var fgMinX, var fgMinY, var fgMaxX, var fgMaxY) = Camera.GetVisibleTileBounds(
            MapFile.Width,
            MapFile.Height,
            MapRenderer.ForegroundExtraMargin);

        var minDepth = fgMinX + fgMinY;
        var maxDepth = fgMaxX + fgMaxY;
        var entityIndex = 0;
        var entityCount = sortedEntities.Count;

        // Skip entities before the visible depth range
        while ((entityIndex < entityCount) && (sortedEntities[entityIndex].SortDepth < minDepth))
            entityIndex++;

        for (var depth = minDepth; depth <= maxDepth; depth++)
        {
            // Collect entities at this depth stripe
            var stripeStart = entityIndex;

            while ((entityIndex < entityCount) && (sortedEntities[entityIndex].SortDepth == depth))
                entityIndex++;

            var stripeEnd = entityIndex;

            // 1. Ground items
            for (var i = stripeStart; i < stripeEnd; i++)
                if (sortedEntities[i].Type == ClientEntityType.GroundItem)
                    DrawEntity(spriteBatch, sortedEntities[i]);

            // 2. Aislings
            for (var i = stripeStart; i < stripeEnd; i++)
                if (sortedEntities[i].Type == ClientEntityType.Aisling)
                    DrawEntity(spriteBatch, sortedEntities[i]);

            // 3. Creatures
            for (var i = stripeStart; i < stripeEnd; i++)
                if (sortedEntities[i].Type == ClientEntityType.Creature)
                    DrawEntity(spriteBatch, sortedEntities[i]);

            // 4. Ground-targeted effects
            DrawGroundEffectsAtDepth(spriteBatch, depth);

            // 5. Entity-attached effects
            for (var i = stripeStart; i < stripeEnd; i++)
                DrawEntityEffects(spriteBatch, sortedEntities[i]);

            // 6. Foreground tiles (on top — trees, buildings occlude entities behind them)
            var tileXStart = Math.Max(fgMinX, depth - fgMaxY);
            var tileXEnd = Math.Min(fgMaxX, depth - fgMinY);

            for (var tileX = tileXStart; tileX <= tileXEnd; tileX++)
                MapRenderer.DrawForegroundTile(
                    spriteBatch,
                    MapFile,
                    Camera,
                    tileX,
                    depth - tileX);
        }
    }

    private void DrawGroundEffectsAtDepth(SpriteBatch spriteBatch, int depth)
    {
        foreach (var effect in Game.World.ActiveEffects)
        {
            if (effect.TargetEntityId.HasValue || effect.IsComplete)
                continue;

            if (!effect.TileX.HasValue || !effect.TileY.HasValue)
                continue;

            if ((effect.TileX.Value + effect.TileY.Value) != depth)
                continue;

            var tileWorld = Camera.TileToWorld(effect.TileX.Value, effect.TileY.Value, MapFile!.Height);

            DrawSingleEffect(
                spriteBatch,
                effect,
                tileWorld.X + HALF_TILE_WIDTH,
                tileWorld.Y + HALF_TILE_HEIGHT,
                Vector2.Zero);
        }
    }

    private void DrawSingleEffect(
        SpriteBatch spriteBatch,
        ActiveEffect effect,
        float tileCenterX,
        float tileCenterY,
        Vector2 visualOffset)
    {
        var spriteFrame = Game.EffectRenderer.GetFrame(Device, effect.EffectId, effect.CurrentFrame);

        if (spriteFrame is null)
            return;

        var frame = spriteFrame.Value;
        var drawX = tileCenterX + visualOffset.X - frame.CenterX + Math.Min(0, (int)frame.Left);
        var drawY = tileCenterY + visualOffset.Y - frame.CenterY + Math.Min(0, (int)frame.Top);
        var screenPos = Camera.WorldToScreen(new Vector2(drawX, drawY));

        // In immediate mode, blend state can be changed directly between draws
        if (effect.IsAdditive)
            Device.BlendState = BlendState.Additive;

        spriteBatch.Draw(frame.Texture, screenPos, Color.White);

        if (effect.IsAdditive)
            Device.BlendState = BlendState.AlphaBlend;
    }
    #endregion

    #region Options Dialog Wiring
    private void WireOptionsDialog()
    {
        MainOptions.OnMacro += () => ToggleSubPanel(MacroMenu);
        MainOptions.OnSettings += () => ToggleSubPanel(SettingsDialog);
        MainOptions.OnFriends += () => ToggleSubPanel(FriendsList);

        MainOptions.OnExit += () => Game.Connection.RequestExit();

        MainOptions.OnSoundVolumeChanged += volume =>
        {
            Game.SoundManager.SetSoundVolume(volume);
            Game.Settings.SoundVolume = volume;
            Game.Settings.Save();
        };

        MainOptions.OnMusicVolumeChanged += volume =>
        {
            Game.Settings.MusicVolume = volume;
            Game.Settings.Save();
        };

        // Apply saved volume settings
        MainOptions.SetSoundVolume(Game.Settings.SoundVolume);
        MainOptions.SetMusicVolume(Game.Settings.MusicVolume);
        Game.SoundManager.SetSoundVolume(Game.Settings.SoundVolume);
    }

    private static void ToggleSubPanel(PrefabPanel panel)
    {
        if (panel.Visible)
            panel.Hide();
        else if (panel is MacroMenuControl macro)
            macro.SlideIn();
        else if (panel is SettingsControl settings)
            settings.SlideIn();
        else if (panel is FriendsListControl friends)
            friends.SlideIn();
    }
    #endregion

    #region Entity Rendering
    private void DrawEntity(SpriteBatch spriteBatch, WorldEntity entity)
    {
        if (MapFile is null)
            return;

        var tileWorldPos = Camera.TileToWorld(entity.TileX, entity.TileY, MapFile.Height);
        var tileCenterX = tileWorldPos.X + HALF_TILE_WIDTH;
        var tileCenterY = tileWorldPos.Y + HALF_TILE_HEIGHT;

        switch (entity.Type)
        {
            case ClientEntityType.Aisling:
                DrawAisling(
                    spriteBatch,
                    entity,
                    tileCenterX,
                    tileCenterY);

                break;

            case ClientEntityType.Creature:
                DrawCreature(
                    spriteBatch,
                    entity,
                    tileCenterX,
                    tileCenterY);

                break;

            case ClientEntityType.GroundItem:
                DrawGroundItem(
                    spriteBatch,
                    entity,
                    tileCenterX,
                    tileCenterY);

                break;
        }
    }

    private void DrawCreature(
        SpriteBatch spriteBatch,
        WorldEntity entity,
        float tileCenterX,
        float tileCenterY)
    {
        var creatureRenderer = Game.CreatureRenderer;
        var animInfo = creatureRenderer.GetAnimInfo(entity.SpriteId);

        if (animInfo is null)
            return;

        var info = animInfo.Value;
        (var frameIndex, var flip) = AnimationManager.GetCreatureFrame(entity, in info);

        var spriteFrame = creatureRenderer.GetFrame(Device, entity.SpriteId, frameIndex);

        if (spriteFrame is null)
            return;

        var frame = spriteFrame.Value;

        // CenterX/CenterY in sprite-space. Convert to texture-space by subtracting Min(0, Left/Top)
        // (when Left/Top are negative, the rendered image has no padding — center shifts right/down).
        var texCenterX = frame.CenterX - Math.Min(0, (int)frame.Left);
        var texCenterY = frame.CenterY - Math.Min(0, (int)frame.Top);

        // When flipped, mirror the X anchor within the texture
        var anchorX = flip ? frame.Texture.Width - texCenterX : texCenterX;

        var drawX = tileCenterX + entity.VisualOffset.X - anchorX;
        var drawY = tileCenterY + entity.VisualOffset.Y - texCenterY;
        var screenPos = Camera.WorldToScreen(new Vector2(drawX, drawY));

        var effects = flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

        var drawTexture = HighlightedEntityId == entity.Id ? GetOrCreateTintedTexture(frame.Texture, entity.Id) : frame.Texture;

        spriteBatch.Draw(
            drawTexture,
            screenPos,
            null,
            Color.White,
            0f,
            Vector2.Zero,
            1f,
            effects,
            0f);
    }

    private void DrawAisling(
        SpriteBatch spriteBatch,
        WorldEntity entity,
        float tileCenterX,
        float tileCenterY)
    {
        // Morphed aislings (creature form) render as creatures
        if (entity.Appearance is null && (entity.SpriteId > 0))
        {
            DrawCreature(
                spriteBatch,
                entity,
                tileCenterX,
                tileCenterY);

            return;
        }

        if (entity.Appearance is null)
            return;

        var appearance = entity.Appearance.Value;
        (var frameIndex, var flip, var animSuffix) = AnimationManager.GetAislingFrame(entity);

        // Check cache — re-render if appearance, frame, flip, or animation suffix changed
        if (!AislingCache.TryGetValue(entity.Id, out var cached)
            || (cached.Appearance != appearance)
            || (cached.FrameIndex != frameIndex)
            || (cached.Flip != flip)
            || (cached.AnimSuffix != animSuffix))
        {
            cached.Texture?.Dispose();

            var texture = Game.AislingRenderer.Render(
                Device,
                in appearance,
                frameIndex,
                animSuffix,
                flip);

            if (texture is null)
                return;

            cached = new AislingCacheEntry(
                appearance,
                frameIndex,
                flip,
                animSuffix,
                texture);
            AislingCache[entity.Id] = cached;
        }

        // Position: tile center minus body center, plus visual offset for walk interpolation
        var drawX = tileCenterX + entity.VisualOffset.X - BODY_CENTER_X;
        var drawY = tileCenterY + entity.VisualOffset.Y - BODY_CENTER_Y;
        var screenPos = Camera.WorldToScreen(new Vector2(drawX, drawY));

        var drawTexture = HighlightedEntityId == entity.Id ? GetOrCreateTintedTexture(cached.Texture, entity.Id) : cached.Texture;

        spriteBatch.Draw(drawTexture, screenPos, Color.White);
    }

    /// <summary>
    ///     Creates a CPU-side tinted copy of a texture by adding a flat color to every non-transparent pixel with per-channel
    ///     saturation. Matches the original DA client's surface modification approach.
    /// </summary>
    private Texture2D CreateTintedTexture(Texture2D source)
    {
        var pixels = new Color[source.Width * source.Height];
        source.GetData(pixels);

        var tintR = HIGHLIGHT_TINT.R;
        var tintG = HIGHLIGHT_TINT.G;
        var tintB = HIGHLIGHT_TINT.B;

        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];

            if (p.A == 0)
                continue;

            pixels[i] = new Color(
                Math.Min(p.R + tintR, 255),
                Math.Min(p.G + tintG, 255),
                Math.Min(p.B + tintB, 255),
                p.A);
        }

        var tinted = new Texture2D(Device, source.Width, source.Height);
        tinted.SetData(pixels);

        return tinted;
    }

    /// <summary>
    ///     Returns a tinted texture for the given source, caching it for the current highlighted entity. Regenerates when the
    ///     entity or source texture changes.
    /// </summary>
    private Texture2D GetOrCreateTintedTexture(Texture2D source, uint entityId)
    {
        if (HighlightTintedTexture is not null && (HighlightTintedEntityId == entityId))
            return HighlightTintedTexture;

        HighlightTintedTexture?.Dispose();
        HighlightTintedTexture = CreateTintedTexture(source);
        HighlightTintedEntityId = entityId;

        return HighlightTintedTexture;
    }

    private void ClearHighlightCache()
    {
        HighlightTintedTexture?.Dispose();
        HighlightTintedTexture = null;
        HighlightTintedEntityId = null;
    }

    private void DrawEntityEffects(SpriteBatch spriteBatch, WorldEntity entity)
    {
        if (MapFile is null)
            return;

        var tileWorldPos = Camera.TileToWorld(entity.TileX, entity.TileY, MapFile.Height);
        var tileCenterX = tileWorldPos.X + HALF_TILE_WIDTH;
        var tileCenterY = tileWorldPos.Y + HALF_TILE_HEIGHT;

        foreach (var effect in Game.World.ActiveEffects)
        {
            if ((effect.TargetEntityId != entity.Id) || effect.IsComplete)
                continue;

            DrawSingleEffect(
                spriteBatch,
                effect,
                tileCenterX,
                tileCenterY,
                entity.VisualOffset);
        }
    }

    private void DrawGroundItem(
        SpriteBatch spriteBatch,
        WorldEntity entity,
        float tileCenterX,
        float tileCenterY)
    {
        var spriteId = (int)entity.SpriteId;

        if (!GroundItemCache.TryGetValue(spriteId, out var texture))
        {
            texture = LoadGroundItemTexture(spriteId);
            GroundItemCache[spriteId] = texture;
        }

        if (texture is null)
            return;

        // Center the item sprite on the tile
        var drawX = tileCenterX - texture.Width / 2f;
        var drawY = tileCenterY - texture.Height / 2f;
        var screenPos = Camera.WorldToScreen(new Vector2(drawX, drawY));

        spriteBatch.Draw(texture, screenPos, Color.White);
    }

    private void UpdateChatBubbles(float deltaMs)
    {
        List<uint>? expired = null;

        foreach ((var entityId, var bubble) in ChatBubbles)
        {
            bubble.Update(deltaMs);

            if (bubble.IsExpired)
                (expired ??= []).Add(entityId);
        }

        if (expired is not null)
            foreach (var id in expired)
            {
                ChatBubbles[id]
                    .Dispose();
                ChatBubbles.Remove(id);
            }
    }

    private void DrawChatBubbles(SpriteBatch spriteBatch)
    {
        if (MapFile is null)
            return;

        foreach ((var entityId, var bubble) in ChatBubbles)
        {
            var entity = Game.World.GetEntity(entityId);

            if (entity is null)
                continue;

            var tileWorld = Camera.TileToWorld(entity.TileX, entity.TileY, MapFile.Height);
            var tileCenterX = tileWorld.X + HALF_TILE_WIDTH;
            var tileCenterY = tileWorld.Y + HALF_TILE_HEIGHT;

            // Position bubble centered above the entity's head
            var entityWorldX = tileCenterX + entity.VisualOffset.X;
            var entityWorldY = tileCenterY + entity.VisualOffset.Y - 60;

            var bubbleX = entityWorldX - bubble.Width / 2f;
            var bubbleY = entityWorldY - bubble.Height;

            var screenPos = Camera.WorldToScreen(new Vector2(bubbleX, bubbleY));

            spriteBatch.Draw(bubble.Texture, new Vector2((int)screenPos.X, (int)screenPos.Y), Color.White);
        }
    }

    private void DrawWorldDebug(SpriteBatch spriteBatch)
    {
        if (MapFile is null)
            return;

        var pixel = UIElement.GetPixel(Device);

        // Foreground tile hitboxes (doors, interactive objects)
        (var fgMinX, var fgMinY, var fgMaxX, var fgMaxY) = Camera.GetVisibleTileBounds(
            MapFile.Width,
            MapFile.Height,
            MapRenderer.ForegroundExtraMargin);

        for (var tileY = fgMinY; tileY <= fgMaxY; tileY++)
            for (var tileX = fgMinX; tileX <= fgMaxX; tileX++)
            {
                var tile = MapFile.Tiles[tileX, tileY];

                if ((tile.LeftForeground == 0) && (tile.RightForeground == 0))
                    continue;

                var tileWorld = Camera.TileToWorld(tileX, tileY, MapFile.Height);
                var topLeft = Camera.WorldToScreen(new Vector2(tileWorld.X, tileWorld.Y));

                var tileRect = new Rectangle(
                    (int)topLeft.X,
                    (int)topLeft.Y,
                    HALF_TILE_WIDTH * 2,
                    HALF_TILE_HEIGHT * 2);

                spriteBatch.Draw(
                    pixel,
                    new Rectangle(
                        tileRect.X,
                        tileRect.Y,
                        tileRect.Width,
                        1),
                    Color.Cyan * 0.3f);

                spriteBatch.Draw(
                    pixel,
                    new Rectangle(
                        tileRect.X,
                        tileRect.Bottom - 1,
                        tileRect.Width,
                        1),
                    Color.Cyan * 0.3f);

                spriteBatch.Draw(
                    pixel,
                    new Rectangle(
                        tileRect.X,
                        tileRect.Y,
                        1,
                        tileRect.Height),
                    Color.Cyan * 0.3f);

                spriteBatch.Draw(
                    pixel,
                    new Rectangle(
                        tileRect.Right - 1,
                        tileRect.Y,
                        1,
                        tileRect.Height),
                    Color.Cyan * 0.3f);
            }

        // Entity hitboxes
        foreach (var entity in Game.World.GetSortedEntities())
        {
            var tileWorld = Camera.TileToWorld(entity.TileX, entity.TileY, MapFile.Height);
            var tileCenterX = tileWorld.X + HALF_TILE_WIDTH;
            var tileCenterY = tileWorld.Y + HALF_TILE_HEIGHT;

            // Draw tile hitbox (the isometric diamond as a screen-space rect)
            var topLeft = Camera.WorldToScreen(new Vector2(tileWorld.X, tileWorld.Y));

            var tileRect = new Rectangle(
                (int)topLeft.X,
                (int)topLeft.Y,
                HALF_TILE_WIDTH * 2,
                HALF_TILE_HEIGHT * 2);

            var color = entity.Type switch
            {
                ClientEntityType.Aisling    => Color.Lime,
                ClientEntityType.Creature   => Color.Red,
                ClientEntityType.GroundItem => Color.Yellow,
                _                           => Color.White
            };

            // Draw border
            spriteBatch.Draw(
                pixel,
                new Rectangle(
                    tileRect.X,
                    tileRect.Y,
                    tileRect.Width,
                    1),
                color * 0.6f);

            spriteBatch.Draw(
                pixel,
                new Rectangle(
                    tileRect.X,
                    tileRect.Bottom - 1,
                    tileRect.Width,
                    1),
                color * 0.6f);

            spriteBatch.Draw(
                pixel,
                new Rectangle(
                    tileRect.X,
                    tileRect.Y,
                    1,
                    tileRect.Height),
                color * 0.6f);

            spriteBatch.Draw(
                pixel,
                new Rectangle(
                    tileRect.Right - 1,
                    tileRect.Y,
                    1,
                    tileRect.Height),
                color * 0.6f);

            // Draw fill
            spriteBatch.Draw(pixel, tileRect, color * 0.15f);

            // Entity name/info label
            var label = $"{entity.Name} [{entity.Id}] ({entity.TileX},{entity.TileY})";
            var labelTexture = TextRenderer.RenderText(Device, label, color);
            var labelPos = Camera.WorldToScreen(new Vector2(tileCenterX - labelTexture.Width / 2f, tileWorld.Y - 12));
            spriteBatch.Draw(labelTexture, labelPos, Color.White);
            labelTexture.Dispose();
        }

        // Draw player position crosshair
        var player = Game.World.GetPlayerEntity();

        if (player is not null)
        {
            var playerWorld = Camera.TileToWorld(player.TileX, player.TileY, MapFile.Height);

            var playerCenter = Camera.WorldToScreen(
                new Vector2(
                    playerWorld.X + HALF_TILE_WIDTH + player.VisualOffset.X,
                    playerWorld.Y + HALF_TILE_HEIGHT + player.VisualOffset.Y));

            spriteBatch.Draw(
                pixel,
                new Rectangle(
                    (int)playerCenter.X - 5,
                    (int)playerCenter.Y,
                    11,
                    1),
                Color.White);

            spriteBatch.Draw(
                pixel,
                new Rectangle(
                    (int)playerCenter.X,
                    (int)playerCenter.Y - 5,
                    1,
                    11),
                Color.White);
        }

        // Mouse hover tile highlight
        var input = Game.Input;
        (var hoverTileX, var hoverTileY) = ScreenToTile(input.MouseX, input.MouseY);

        if ((hoverTileX >= 0) && (hoverTileX < MapFile.Width) && (hoverTileY >= 0) && (hoverTileY < MapFile.Height))
        {
            var hoverWorld = Camera.TileToWorld(hoverTileX, hoverTileY, MapFile.Height);
            var hoverScreen = Camera.WorldToScreen(new Vector2(hoverWorld.X, hoverWorld.Y));

            var hoverRect = new Rectangle(
                (int)hoverScreen.X,
                (int)hoverScreen.Y,
                HALF_TILE_WIDTH * 2,
                HALF_TILE_HEIGHT * 2);

            spriteBatch.Draw(pixel, hoverRect, Color.Magenta * 0.3f);

            spriteBatch.Draw(
                pixel,
                new Rectangle(
                    hoverRect.X,
                    hoverRect.Y,
                    hoverRect.Width,
                    1),
                Color.Magenta);

            spriteBatch.Draw(
                pixel,
                new Rectangle(
                    hoverRect.X,
                    hoverRect.Bottom - 1,
                    hoverRect.Width,
                    1),
                Color.Magenta);

            spriteBatch.Draw(
                pixel,
                new Rectangle(
                    hoverRect.X,
                    hoverRect.Y,
                    1,
                    hoverRect.Height),
                Color.Magenta);

            spriteBatch.Draw(
                pixel,
                new Rectangle(
                    hoverRect.Right - 1,
                    hoverRect.Y,
                    1,
                    hoverRect.Height),
                Color.Magenta);
        }
    }

    /// <summary>
    ///     Creates a texture containing a dashed ellipse inscribed in the isometric tile diamond. Gaps at the 4 cardinal
    ///     directions (top, right, bottom, left of the ellipse).
    /// </summary>
    private static Texture2D CreateTileCursorTexture(GraphicsDevice device)
    {
        const int WIDTH = HALF_TILE_WIDTH * 2; // 56
        const int HEIGHT = HALF_TILE_HEIGHT * 2; // 28

        var pixels = new Color[WIDTH * HEIGHT];
        var color = new Color(247, 142, 24);

        var cx = WIDTH / 2;
        var cy = HEIGHT / 2;

        // Top-right quarter only.
        // These are offsets from the center.
        // Tweak these until the shape matches exactly how you want.
        Span<Point> quarter =
        [
            new(-6, -8),
            new(-7, -8),
            new(-8, -8),
            new(-9, -8),
            new(-10, -8),
            new(-11, -7),
            new(-12, -7),
            new(-13, -6),
            new(-14, -6),
            new(-15, -5),
            new(-16, -5),
            new(-17, -4),
            new(-17, -3)
        ];

        foreach (var p in quarter)
            ProjectQuads(
                pixels,
                WIDTH,
                HEIGHT,
                cx,
                cy,
                p.X,
                p.Y,
                color);

        var texture = new Texture2D(device, WIDTH, HEIGHT);
        texture.SetData(pixels);

        return texture;
    }

    private static void ProjectQuads(
        Color[] pixels,
        int width,
        int height,
        int cx,
        int cy,
        int dx,
        int dy,
        Color color)
    {
        SetPixel(
            pixels,
            width,
            height,
            cx + dx,
            cy + dy,
            color); // top-right

        SetPixel(
            pixels,
            width,
            height,
            cx - dx,
            cy + dy,
            color); // top-left

        SetPixel(
            pixels,
            width,
            height,
            cx + dx,
            cy - dy,
            color); // bottom-right

        SetPixel(
            pixels,
            width,
            height,
            cx - dx,
            cy - dy,
            color); // bottom-left
    }

    private static void SetPixel(
        Color[] pixels,
        int width,
        int height,
        int x,
        int y,
        Color color)
    {
        if (((uint)x < width) && ((uint)y < height))
            pixels[y * width + x] = color;
    }

    private void DrawTileCursor(SpriteBatch spriteBatch)
    {
        if (MapFile is null || TileCursorTexture is null)
            return;

        var input = Game.Input;
        var viewport = Hud.ViewportBounds;

        // Only draw when mouse is within the world viewport
        if ((input.MouseX < viewport.X)
            || (input.MouseX >= (viewport.X + viewport.Width))
            || (input.MouseY < viewport.Y)
            || (input.MouseY >= (viewport.Y + viewport.Height)))
            return;

        (var tileX, var tileY) = ScreenToTile(input.MouseX, input.MouseY);

        if ((tileX < 0) || (tileX >= MapFile.Width) || (tileY < 0) || (tileY >= MapFile.Height))
            return;

        var tileWorld = Camera.TileToWorld(tileX, tileY, MapFile.Height);
        var tileScreen = Camera.WorldToScreen(new Vector2(tileWorld.X, tileWorld.Y));

        spriteBatch.Draw(TileCursorTexture, new Vector2((int)tileScreen.X, (int)tileScreen.Y), Color.White);
    }

    private Texture2D? LoadGroundItemTexture(int spriteId)
        => TextureConverter.RenderSprite(Device, DataContext.PanelItems.GetPanelItemSprite(spriteId));
    #endregion

    #region Map Assembly
    private void TryBuildInitialMap()
    {
        var mapInfo = Game.Connection.MapInfo;

        if (mapInfo is null)
            return;

        // Load full map from local .map files (server-sent MapData may have already
        // been consumed before this screen was created during world entry)
        MapFile = LoadMapFile(mapInfo.MapId, mapInfo.Width, mapInfo.Height);
        MapPreloaded = false;

        Hud.SetZoneName(mapInfo.Name);

        // Preload tiles immediately if we have the map
        if (MapFile is not null)
        {
            MapRenderer.PreloadMapTiles(Device, MapFile);
            MapPreloaded = true;
        }

        FollowPlayerCamera();
    }

    private void HandleMapInfo(MapInfoArgs args)
    {
        // New map — dispose old caches, load fresh MapFile from local files
        MapRenderer.Dispose();
        MapRenderer = new MapRenderer();
        MapFile = LoadMapFile(args.MapId, args.Width, args.Height);
        MapPreloaded = false;

        // Clear entity state and caches for the new map
        Game.World.Clear();
        Game.CreatureRenderer.Clear();
        Game.AislingRenderer.ClearCache();
        ClearAislingCache();
        ClearGroundItemCache();
        ClearChatBubbles();

        Hud.SetZoneName(args.Name);
    }

    private void HandleMapData(MapDataArgs args)
    {
        if (MapFile is null)
            return;

        var y = args.CurrentYIndex;

        if (y >= MapFile.Height)
            return;

        // Each tile is 6 bytes: bg(2 BE), lfg(2 BE), rfg(2 BE)
        var data = args.MapData;
        var tileCount = Math.Min(data.Length / 6, MapFile.Width);

        for (var x = 0; x < tileCount; x++)
        {
            var offset = x * 6;
            var background = (short)((data[offset] << 8) | data[offset + 1]);
            var leftForeground = (short)((data[offset + 2] << 8) | data[offset + 3]);
            var rightForeground = (short)((data[offset + 4] << 8) | data[offset + 5]);

            MapFile.Tiles[x, y] = new MapTile
            {
                Background = background,
                LeftForeground = leftForeground,
                RightForeground = rightForeground
            };
        }
    }

    private void HandleMapLoadComplete()
    {
        if (MapFile is null)
            return;

        if (!MapPreloaded)
        {
            MapRenderer.PreloadMapTiles(Device, MapFile);
            MapPreloaded = true;
        }

        FollowPlayerCamera();
    }

    private static MapFile? LoadMapFile(int mapId, int width, int height)
    {
        var key = $"lod{mapId}";

        return DataContext.MapsFiles.GetMapFile(key, width, height);
    }

    private void HandleLocationChanged(int x, int y) => Hud.SetCoords(x, y);

    /// <summary>
    ///     Updates camera position to follow the player entity's visual position, including walk interpolation offset. In
    ///     rough scroll mode, only updates at fixed intervals for a choppier look.
    /// </summary>
    private void FollowPlayerCamera()
    {
        if (MapFile is null)
            return;

        var player = Game.World.GetPlayerEntity();

        if (player is null)
            return;

        var tileWorld = Camera.TileToWorld(player.TileX, player.TileY, MapFile.Height);
        Camera.Position = tileWorld + player.VisualOffset;
    }
    #endregion

    #region Entity Events
    private void HandleDisplayAisling(DisplayAislingArgs args)
    {
        // Update player name in HUD when the player's own aisling is displayed
        if (args.Id == Game.Connection.AislingId)
            Hud.SetPlayerName(args.Name);
    }

    private void HandleRemoveEntity(uint id)
    {
        if (AislingCache.TryGetValue(id, out var entry))
        {
            entry.Texture?.Dispose();
            AislingCache.Remove(id);
        }
    }

    private void HandleClientWalkResponse(Direction direction, int oldX, int oldY)
    {
        // Update HUD coords — camera following is handled in Update via FollowPlayerCamera
        var player = Game.World.GetPlayerEntity();

        if (player is not null)
            Hud.SetCoords(player.TileX, player.TileY);
    }

    private void HandleAttributes(AttributesArgs args) => Hud.UpdateAttributes(args);

    private void HandleDisplayPublicMessage(DisplayPublicMessageArgs args)
    {
        var color = args.PublicMessageType switch
        {
            PublicMessageType.Shout => Color.Yellow,
            PublicMessageType.Chant => new Color(135, 206, 250),
            _                       => Color.White
        };

        Hud.AddChatMessage(args.Message, color);

        // Create chat bubble above the speaking entity
        var isShout = args.PublicMessageType == PublicMessageType.Shout;

        if (ChatBubbles.TryGetValue(args.SourceId, out var existing))
            existing.Dispose();

        ChatBubbles[args.SourceId] = ChatBubble.Create(
            Device,
            args.SourceId,
            args.Message,
            isShout);
    }

    private void HandleServerMessage(ServerMessageArgs args)
    {
        switch (args.ServerMessageType)
        {
            case ServerMessageType.Whisper:
                Hud.AddChatMessage(args.Message, new Color(100, 149, 237));

                break;

            case ServerMessageType.GroupChat:
                Hud.AddChatMessage(args.Message, new Color(154, 205, 50));

                break;

            case ServerMessageType.GuildChat:
                Hud.AddChatMessage(args.Message, new Color(128, 128, 0));

                break;

            case ServerMessageType.OrangeBar1
                 or ServerMessageType.OrangeBar2
                 or ServerMessageType.ActiveMessage
                 or ServerMessageType.OrangeBar3
                 or ServerMessageType.AdminMessage
                 or ServerMessageType.OrangeBar5:
                Hud.ShowOrangeBarMessage(args.Message);

                break;

            case ServerMessageType.PersistentMessage:
                Hud.ShowPersistentMessage(args.Message);

                break;

            case ServerMessageType.ScrollWindow:
                TextPopup.Show(args.Message);

                break;

            case ServerMessageType.NonScrollWindow:
                TextPopup.Show(args.Message, PopupStyle.NonScroll);

                break;

            case ServerMessageType.WoodenBoard:
                TextPopup.Show(args.Message, PopupStyle.Wooden);

                break;

            case ServerMessageType.UserOptions:
                ParseUserOptions(args.Message);

                break;

            case ServerMessageType.ClosePopup:
                TextPopup.Hide();

                break;

            default:
                Hud.ShowOrangeBarMessage(args.Message);

                break;
        }
    }

    /// <summary>
    ///     Parses the server's UserOptions response. Format is tab-delimited entries, each formatted as
    ///     "{optionNum}{description,-25}:{ON/OFF,-3}". A full request response has a leading "0" prefix before all entries.
    /// </summary>
    private void ParseUserOptions(string message)
    {
        var entries = message.Split('\t', StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in entries)
        {
            if (entry.Length < 2)
                continue;

            // First char is option number (1-based), or '0' for the leading prefix
            var numChar = entry[0];

            if (numChar == '0')
                continue;

            if (!char.IsDigit(numChar))
                continue;

            var optionIndex = numChar - '1';

            if (optionIndex is < 0 or >= 13)
                continue;

            // Parse "description   :ON " or "description   :OFF"
            var colonIdx = entry.LastIndexOf(':');

            if (colonIdx < 1)
                continue;

            var name = entry[1..colonIdx]
                .TrimEnd();

            var stateStr = entry[(colonIdx + 1)..]
                .Trim();
            var isOn = stateStr.StartsWith("ON", StringComparison.OrdinalIgnoreCase);

            SettingsDialog.SetSettingName(optionIndex, name);
            SettingsDialog.SetSettingValue(optionIndex, isOn);
        }
    }

    private void HandleAddItemToPane(AddItemToPaneArgs args)
    {
        Hud.Inventory.SetSlot(args.Item.Slot, args.Item.Sprite);
        Hud.Inventory.SetSlotName(args.Item.Slot, Game.Connection.InventorySlots[args.Item.Slot].Name);
    }

    private void HandleRemoveItemFromPane(RemoveItemFromPaneArgs args) => Hud.Inventory.ClearSlot(args.Slot);

    private void HandleAddSkillToPane(AddSkillToPaneArgs args)
    {
        Hud.SkillBook.SetSlot(args.Skill.Slot, args.Skill.Sprite);
        Hud.SkillBook.SetSlotName(args.Skill.Slot, args.Skill.PanelName);
        Hud.SkillBookAlt.SetSlot(args.Skill.Slot, args.Skill.Sprite);
        Hud.SkillBookAlt.SetSlotName(args.Skill.Slot, args.Skill.PanelName);
    }

    private void HandleRemoveSkillFromPane(RemoveSkillFromPaneArgs args)
    {
        Hud.SkillBook.ClearSlot(args.Slot);
        Hud.SkillBookAlt.ClearSlot(args.Slot);
    }

    private void HandleAddSpellToPane(AddSpellToPaneArgs args)
    {
        Hud.SpellBook.SetSlot(args.Spell.Slot, args.Spell.Sprite);
        Hud.SpellBook.SetSlotName(args.Spell.Slot, args.Spell.PanelName);
        Hud.SpellBookAlt.SetSlot(args.Spell.Slot, args.Spell.Sprite);
        Hud.SpellBookAlt.SetSlotName(args.Spell.Slot, args.Spell.PanelName);
    }

    private void HandleRemoveSpellFromPane(RemoveSpellFromPaneArgs args)
    {
        Hud.SpellBook.ClearSlot(args.Slot);
        Hud.SpellBookAlt.ClearSlot(args.Slot);
    }

    private void HandleInventorySlotClicked(byte slot) => Game.Connection.UseItem(slot);

    private void HandleSkillSlotClicked(byte slot) => Game.Connection.UseSkill(slot);

    private void HandleSpellSlotClicked(byte slot) => Game.Connection.UseSpell(slot);

    private void HandleInventoryDropInViewport(byte slot, int mouseX, int mouseY)
    {
        var viewport = Hud.ViewportBounds;

        // Only drop if released within the world viewport
        if ((mouseX < viewport.X)
            || (mouseX >= (viewport.X + viewport.Width))
            || (mouseY < viewport.Y)
            || (mouseY >= (viewport.Y + viewport.Height)))
            return;

        if (MapFile is null)
            return;

        // Check if dropped on an entity (give item to NPC/player)
        (var tileX, var tileY) = ScreenToTile(mouseX, mouseY);
        var entity = Game.World.GetEntityAt(tileX, tileY);

        if (entity is not null && entity.Type is ClientEntityType.Aisling or ClientEntityType.Creature)
            Game.Connection.DropItemOnCreature(slot, entity.Id);
        else

            // Drop on ground at the target tile
            Game.Connection.DropItem(slot, tileX, tileY);
    }

    private void HandleDisplayDialog(DisplayDialogArgs args)
    {
        // CloseDialog type means hide the NPC dialog
        if (args.DialogType == DialogType.CloseDialog)
        {
            NpcDialog.Hide();

            return;
        }

        NpcDialog.ShowDialog(args);
    }

    private void HandleDisplayMenu(DisplayMenuArgs args) => NpcDialog.ShowMenu(args);

    private void HandleEquipment(EquipmentArgs args) => StatusBook.SetEquipmentSlot(args.Slot, args.Item.Sprite);

    private void HandleDisplayUnequip(DisplayUnequipArgs args) => StatusBook.ClearEquipmentSlot(args.EquipmentSlot);

    private void ShowStatusBook()
    {
        StatusBook.RefreshEquipment(Game.Connection.Equipment);

        if (Game.Connection.Attributes is { } attrs)
            StatusBook.UpdateEquipmentStats(
                attrs.Str,
                attrs.Int,
                attrs.Wis,
                attrs.Con,
                attrs.Dex,
                attrs.Ac);

        StatusBook.Show();
    }

    private void HandleCooldown(CooldownArgs args)
    {
        if (args.IsSkill)
        {
            Hud.SkillBook.SetCooldown(args.Slot, args.CooldownSecs);
            Hud.SkillBookAlt.SetCooldown(args.Slot, args.CooldownSecs);
        } else
        {
            Hud.SpellBook.SetCooldown(args.Slot, args.CooldownSecs);
            Hud.SpellBookAlt.SetCooldown(args.Slot, args.CooldownSecs);
        }
    }

    private void HandleRefreshResponse()
        =>

            // Server acknowledged the refresh request — re-center camera
            FollowPlayerCamera();

    private void HandleWorldList(WorldListArgs args)
    {
        var entries = args.CountryList
                          .Select(m => new WorldListEntry(
                              m.Name,
                              m.Title,
                              m.BaseClass,
                              m.IsMaster,
                              m.IsGuilded,
                              m.Color))
                          .ToList();

        WorldList.Show(entries, args.WorldMemberCount, Hud.PlayerName);
    }

    private void HandleSlotHotkeys(InputBuffer input)
    {
        var slot = -1;

        if (input.WasKeyPressed(Keys.D1))
            slot = 1;
        else if (input.WasKeyPressed(Keys.D2))
            slot = 2;
        else if (input.WasKeyPressed(Keys.D3))
            slot = 3;
        else if (input.WasKeyPressed(Keys.D4))
            slot = 4;
        else if (input.WasKeyPressed(Keys.D5))
            slot = 5;
        else if (input.WasKeyPressed(Keys.D6))
            slot = 6;
        else if (input.WasKeyPressed(Keys.D7))
            slot = 7;
        else if (input.WasKeyPressed(Keys.D8))
            slot = 8;
        else if (input.WasKeyPressed(Keys.D9))
            slot = 9;
        else if (input.WasKeyPressed(Keys.D0))
            slot = 10;
        else if (input.WasKeyPressed(Keys.OemMinus))
            slot = 11;
        else if (input.WasKeyPressed(Keys.OemPlus))
            slot = 12;

        if (slot < 0)
            return;

        var byteSlot = (byte)slot;

        switch (Hud.ActiveTab)
        {
            case HudTab.Inventory:
                Game.Connection.UseItem(byteSlot);

                break;

            case HudTab.Skills:
                Game.Connection.UseSkill(byteSlot);

                break;

            case HudTab.SkillsAlt:
                Game.Connection.UseSkill((byte)(byteSlot + 36));

                break;

            case HudTab.Spells:
                Game.Connection.UseSpell(byteSlot);

                break;

            case HudTab.SpellsAlt:
                Game.Connection.UseSpell((byte)(byteSlot + 36));

                break;

            case HudTab.Tools:
                // TODO: left half = world skills (slots 73-78), right half = world spells (slots 73-78)
                break;

            case HudTab.Chat:
            case HudTab.Stats:
            case HudTab.ExtendedStats:
                // TODO: chat macros (slots 1-12)
                break;
        }
    }

    private void FocusChat(string prefix, Color textColor)
    {
        Hud.ChatInput.IsFocused = true;
        Hud.ChatInput.Prefix = prefix;
        Hud.ChatInput.TextColor = textColor;
        Hud.SetDescription(null);

        Hud.ChatInput.FocusedBackgroundColor = new Color(
            0,
            0,
            0,
            128);
    }

    private void UnfocusChat()
    {
        Hud.ChatInput.IsFocused = false;
        Hud.ChatInput.Text = string.Empty;
        Hud.ChatInput.Prefix = string.Empty;
        Hud.ChatInput.TextColor = Color.White;
        Hud.ChatInput.FocusedBackgroundColor = null;
    }

    private void DispatchChatMessage(string message)
    {
        var prefix = Hud.ChatInput.Prefix;

        if (prefix.StartsWithI("Group invite"))
            Game.Connection.SendGroupInvite(ClientGroupSwitch.TryInvite, message);
        else if (prefix.StartsWithI("Group"))
            Game.Connection.SendGroupMessage(message);
        else if (prefix.EndsWithI("! "))
            Game.Connection.SendShout(message);
        else if (prefix.StartsWithI("-> ") && prefix.EndsWithI(": "))
        {
            // Whisper phase 2: prefix is "-> targetName: "
            var targetName = prefix[3..^2];
            Game.Connection.SendWhisper(targetName, message);
        } else
            Game.Connection.SendPublicMessage(message);
    }

    private void ClearAislingCache()
    {
        foreach (var entry in AislingCache.Values)
            entry.Texture?.Dispose();

        AislingCache.Clear();
    }

    private void ClearGroundItemCache()
    {
        foreach (var texture in GroundItemCache.Values)
            texture?.Dispose();

        GroundItemCache.Clear();
    }

    private void ClearChatBubbles()
    {
        foreach (var bubble in ChatBubbles.Values)
            bubble.Dispose();

        ChatBubbles.Clear();
    }
    #endregion

    #region Click Handling
    /// <summary>
    ///     Handles left-click within the viewport area — picks the entity at the click position.
    /// </summary>
    private void UpdateDragHighlight(InputBuffer input)
    {
        // Check if any panel is actively dragging
        var isDragging = Hud.Inventory.IsDragging
                         || Hud.SkillBook.IsDragging
                         || Hud.SkillBookAlt.IsDragging
                         || Hud.SpellBook.IsDragging
                         || Hud.SpellBookAlt.IsDragging;

        if (!isDragging || MapFile is null)
        {
            if (HighlightedEntityId is not null)
                ClearHighlightCache();

            HighlightedEntityId = null;

            return;
        }

        (var tileX, var tileY) = ScreenToTile(input.MouseX, input.MouseY);
        var entity = Game.World.GetEntityAt(tileX, tileY);

        uint? newHighlight = entity?.Type is ClientEntityType.Aisling or ClientEntityType.Creature ? entity.Id : null;

        if (newHighlight != HighlightedEntityId)
            ClearHighlightCache();

        HighlightedEntityId = newHighlight;
    }

    /// <summary>
    ///     Converts screen mouse coordinates to tile coordinates, accounting for the HUD viewport offset. The world is
    ///     rendered with a translation matrix for the viewport origin, so mouse coords must be adjusted to match.
    /// </summary>
    private (int TileX, int TileY) ScreenToTile(int mouseX, int mouseY)
    {
        var viewport = Hud.ViewportBounds;
        var worldPos = Camera.ScreenToWorld(new Vector2(mouseX - viewport.X, mouseY - viewport.Y));
        var tile = Camera.WorldToTile(worldPos.X, worldPos.Y, MapFile!.Height);

        return (tile.X, tile.Y);
    }

    private void TryPickupItem()
    {
        var player = Game.World.GetPlayerEntity();

        if (player is null)
            return;

        var slot = Game.Connection.GetFirstEmptyInventorySlot();

        if (slot == 0)
            return;

        // First try the player's own tile
        if (Game.World.HasGroundItemAt(player.TileX, player.TileY))
        {
            Game.Connection.PickupItem(player.TileX, player.TileY, slot);

            return;
        }

        // Then try the tile in front (direction the player is facing)
        (var dx, var dy) = player.Direction switch
        {
            Direction.Up    => (0, -1),
            Direction.Right => (1, 0),
            Direction.Down  => (0, 1),
            Direction.Left  => (-1, 0),
            _               => (0, 0)
        };

        var frontX = player.TileX + dx;
        var frontY = player.TileY + dy;

        if (Game.World.HasGroundItemAt(frontX, frontY))
            Game.Connection.PickupItem(frontX, frontY, slot);
    }

    private void HandleWorldClick(int mouseX, int mouseY)
    {
        if (MapFile is null)
            return;

        var viewport = Hud.ViewportBounds;

        if ((mouseX < viewport.X)
            || (mouseX >= (viewport.X + viewport.Width))
            || (mouseY < viewport.Y)
            || (mouseY >= (viewport.Y + viewport.Height)))
            return;

        (var tileX, var tileY) = ScreenToTile(mouseX, mouseY);

        // Check for double-click (same tile within time window)
        var isDoubleClick = (LastClickTimer < DOUBLE_CLICK_MS) && (tileX == LastClickTileX) && (tileY == LastClickTileY);

        LastClickTimer = 0;
        LastClickTileX = tileX;
        LastClickTileY = tileY;

        if (isDoubleClick)
        {
            // Double-click: interact with entities
            var entity = Game.World.GetEntityAt(tileX, tileY);

            if (entity is not null)
            {
                if (entity.Type == ClientEntityType.GroundItem)
                {
                    var firstEmptySlot = Game.Connection.GetFirstEmptyInventorySlot();
                    Game.Connection.PickupItem(tileX, tileY, firstEmptySlot);
                } else
                    Game.Connection.ClickEntity(entity.Id);
            }
        } else
        {
            // Single click: tile interaction (doors, reactor tiles)
            Game.Connection.ClickTile(tileX, tileY);
        }
    }

    private void HandleWorldRightClick(int mouseX, int mouseY)
    {
        if (MapFile is null)
            return;

        var viewport = Hud.ViewportBounds;

        if ((mouseX < viewport.X)
            || (mouseX >= (viewport.X + viewport.Width))
            || (mouseY < viewport.Y)
            || (mouseY >= (viewport.Y + viewport.Height)))
            return;

        (var tileX, var tileY) = ScreenToTile(mouseX, mouseY);

        var entity = Game.World.GetEntityAt(tileX, tileY);

        if (entity is null)
            return;

        if ((entity.Type == ClientEntityType.Aisling) && (entity.Id != Game.Connection.AislingId))
        {
            var name = entity.Name;

            ContextMenu.Show(
                mouseX,
                mouseY,
                ("Whisper", () => FocusChat($"-> {name}: ", new Color(100, 149, 237))),
                ("Click", () => Game.Connection.ClickEntity(entity.Id)));
        }
    }
    #endregion

    #region Server Event Handlers
    private void HandleDisplayExchange(DisplayExchangeArgs args)
    {
        switch (args.ExchangeResponseType)
        {
            case ExchangeResponseType.StartExchange:
                if (args.OtherUserId.HasValue)
                    Exchange.StartExchange(args.OtherUserId.Value, args.OtherUserName);

                break;

            case ExchangeResponseType.AddItem:
                if (args is { RightSide: not null, ItemSprite: not null, ExchangeIndex: not null })
                    Exchange.AddItem(
                        args.RightSide.Value,
                        args.ExchangeIndex.Value,
                        args.ItemSprite.Value,
                        args.ItemName);

                break;

            case ExchangeResponseType.SetGold:
                if (args is { RightSide: not null, GoldAmount: not null })
                    Exchange.SetGold(args.RightSide.Value, args.GoldAmount.Value);

                break;

            case ExchangeResponseType.Cancel:
                Exchange.CloseExchange();

                if (args.Message is not null)
                    Hud.ShowOrangeBarMessage(args.Message);

                break;

            case ExchangeResponseType.Accept:
                if (args.PersistExchange == true)
                    Exchange.CloseExchange();
                else
                    Exchange.ShowOtherAccepted();

                if (args.Message is not null)
                    Hud.ShowOrangeBarMessage(args.Message);

                break;
        }
    }

    private void HandleDisplayBoard(DisplayBoardArgs args)
    {
        switch (args.Type)
        {
            case BoardOrResponseType.PublicBoard:
            case BoardOrResponseType.MailBoard:
                if (args.Board is { } board)
                {
                    var entries = board.Posts
                                       .Select(p => new MailEntry(
                                           p.PostId,
                                           p.Author,
                                           p.MonthOfYear,
                                           p.DayOfMonth,
                                           p.Subject,
                                           p.IsHighlighted))
                                       .ToList();

                    MailList.ShowMailList(board.BoardId, entries);
                }

                break;

            case BoardOrResponseType.PublicPost:
            case BoardOrResponseType.MailPost:
                if (args.Post is { } post)
                {
                    MailRead.BoardId = MailList.BoardId;

                    MailRead.ShowMail(
                        post.PostId,
                        post.Author,
                        post.MonthOfYear,
                        post.DayOfMonth,
                        post.Subject,
                        post.Message,
                        args.EnablePrevBtn);
                }

                break;

            case BoardOrResponseType.SubmitPostResponse:
            case BoardOrResponseType.DeletePostResponse:
            case BoardOrResponseType.HighlightPostResponse:
                if (args.ResponseMessage is not null)
                    Hud.ShowOrangeBarMessage(args.ResponseMessage);

                break;
        }
    }

    private void HandleDisplayGroupInvite(DisplayGroupInviteArgs args)
    {
        var sourceName = args.SourceName;

        switch (args.ServerGroupSwitch)
        {
            case ServerGroupSwitch.Invite:
            {
                Hud.ShowOrangeBarMessage($"{sourceName} invites you to join a group.");

                // Show accept/decline context menu at center of viewport
                var vp = Hud.ViewportBounds;
                var menuX = vp.X + vp.Width / 2;
                var menuY = vp.Y + vp.Height / 2;

                ContextMenu.Show(
                    menuX,
                    menuY,
                    ($"Accept {sourceName}'s invite", () => Game.Connection.SendGroupInvite(ClientGroupSwitch.AcceptInvite, sourceName)),
                    ("Decline", () => { }));

                break;
            }

            case ServerGroupSwitch.RequestToJoin:
            {
                Hud.ShowOrangeBarMessage($"{sourceName} wants to join your group.");

                var vp = Hud.ViewportBounds;
                var menuX = vp.X + vp.Width / 2;
                var menuY = vp.Y + vp.Height / 2;

                ContextMenu.Show(
                    menuX,
                    menuY,
                    ($"Accept {sourceName}", () => Game.Connection.SendGroupInvite(ClientGroupSwitch.AcceptInvite, sourceName)),
                    ("Decline", () => { }));

                break;
            }

            case ServerGroupSwitch.ShowGroupBox:
            {
                // TODO: Show the group recruitment box UI with args.GroupBoxInfo
                Hud.ShowOrangeBarMessage($"{sourceName} has an open group box.");

                break;
            }
        }
    }

    private void HandleSelfProfile(SelfProfileArgs args)
    {
        // Populate and show the status book
        StatusBook.SetPlayerInfo(
            Hud.PlayerName,
            args.DisplayClass,
            args.GuildName ?? string.Empty,
            args.GuildRank ?? string.Empty,
            args.Title ?? string.Empty);

        // Legend marks
        var marks = args.LegendMarks
                        .Select(m => new LegendMarkEntry(
                            m.Text,
                            MapMarkColor(m.Color),
                            (byte)m.Icon,
                            m.Key))
                        .ToList();

        StatusBook.SetLegendMarks(marks);

        // Family info
        StatusBook.SetFamilyInfo(args.Name, args.SpouseName ?? string.Empty);

        // Paperdoll — render the player's full aisling at south-facing idle
        var playerEntity = Game.World.GetPlayerEntity();

        if (playerEntity?.Appearance is { } appearance)
            StatusBook.SetPaperdoll(Game.AislingRenderer, in appearance);

        // Group open state
        StatusBook.SetGroupOpen(args.GroupOpen);

        // Group members — parse GroupString into member names for the group panel
        if (!string.IsNullOrEmpty(args.GroupString))
        {
            var members = ParseGroupString(args.GroupString);
            GroupPanel.SetMembers(members);
        } else
            GroupPanel.ClearGroup();

        ShowStatusBook();
    }

    private void HandleOtherProfile(OtherProfileArgs args)
        =>

            // For now, show other player info as an orange bar message
            Hud.ShowOrangeBarMessage($"Viewing profile of {args.Name}");

    private void HandleBodyAnimation(BodyAnimationArgs args)
    {
        var entity = Game.World.GetEntity(args.SourceId);

        if (entity is null)
            return;

        // Creatures use their MpfFile attack frame counts; aislings use EPF suffix-based frame counts
        if (entity.Type == ClientEntityType.Creature)
        {
            var animInfo = Game.CreatureRenderer.GetAnimInfo(entity.SpriteId);

            if (animInfo is { } info)
                AnimationManager.StartCreatureBodyAnimation(
                    entity,
                    args.BodyAnimation,
                    args.AnimationSpeed,
                    in info);
        } else
            AnimationManager.StartBodyAnimation(entity, args.BodyAnimation, args.AnimationSpeed);

        if (args.Sound.HasValue)
            Game.SoundManager.PlaySound(args.Sound.Value);
    }

    private void HandleAnimation(AnimationArgs args)
    {
        // Ground-targeted effect
        if (args.TargetPoint.HasValue && (args.TargetAnimation > 0))
            CreateEffect(args.TargetAnimation, targetTileX: args.TargetPoint.Value.X, targetTileY: args.TargetPoint.Value.Y);

        // Entity-targeted effect on target
        if (args.TargetId is > 0 && (args.TargetAnimation > 0))
            CreateEffect(args.TargetAnimation, args.TargetId.Value);

        // Source-side effect (caster visual)
        if (args.SourceId is > 0 && (args.SourceAnimation > 0))
            CreateEffect(args.SourceAnimation, args.SourceId.Value);
    }

    private void CreateEffect(
        int effectId,
        uint? targetEntityId = null,
        int? targetTileX = null,
        int? targetTileY = null)
    {
        var info = Game.EffectRenderer.GetEffectInfo(Device, effectId);

        if (info is null)
            return;

        (var frameCount, var frameIntervalMs, var isAdditive) = info.Value;

        // Cancel any existing effect on the same entity — only one effect per entity at a time
        if (targetEntityId.HasValue)
            Game.World.ActiveEffects.RemoveAll(e => e.TargetEntityId == targetEntityId);

        Game.World.ActiveEffects.Add(
            new ActiveEffect
            {
                EffectId = effectId,
                TargetEntityId = targetEntityId,
                TileX = targetTileX,
                TileY = targetTileY,
                FrameCount = frameCount,
                FrameIntervalMs = frameIntervalMs > 0 ? frameIntervalMs : 50,
                IsAdditive = isAdditive
            });
    }

    private void HandleSound(SoundArgs args)
    {
        if (args.IsMusic)
            Game.SoundManager.PlayMusic(args.Sound);
        else
            Game.SoundManager.PlaySound(args.Sound);
    }

    /// <summary>
    ///     Parses the server's GroupString format into a list of member names. Format: "Group members\n* Leader\n  Member2\n
    ///     Member3\nTotal N"
    /// </summary>
    private static List<string> ParseGroupString(string groupString)
    {
        var members = new List<string>();

        foreach (var line in groupString.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("Group members", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("Total ", StringComparison.OrdinalIgnoreCase)
                || (trimmed.Length == 0))
                continue;

            // Leader lines start with "* ", member lines start with "  "
            if (trimmed.StartsWith("* "))
                members.Add(trimmed[2..]);
            else
                members.Add(trimmed);
        }

        return members;
    }

    private static Color MapMarkColor(MarkColor color)
        => color switch
        {
            MarkColor.White       => Color.White,
            MarkColor.LightOrange => new Color(255, 200, 100),
            MarkColor.LightYellow => new Color(255, 255, 150),
            MarkColor.Yellow      => Color.Yellow,
            MarkColor.LightGreen  => new Color(150, 255, 150),
            MarkColor.Blue        => new Color(100, 149, 237),
            MarkColor.Cyan        => new Color(0, 200, 200),
            MarkColor.LightPink   => new Color(255, 150, 200),
            MarkColor.DarkPurple  => new Color(150, 100, 200),
            MarkColor.Pink        => new Color(255, 182, 193),
            MarkColor.Red         => Color.Red,
            MarkColor.Orange      => Color.Orange,
            MarkColor.Green       => new Color(100, 255, 100),
            MarkColor.Brown       => new Color(180, 120, 60),
            _                     => Color.White
        };

    private void HandleMapChangePending()
        =>

            // Clear caches in preparation for a map change
            MapPreloaded = false;

    private void HandleExitResponse(ExitResponseArgs args)
    {
        if (args.ExitConfirmed)
            Game.Exit();
    }

    private void HandleHealthBar(HealthBarArgs args)
    {
        // TODO: render HP bar above entity
    }

    private void HandleLightLevel(LightLevelArgs args)
    {
        // TODO: apply darkness overlay based on light level
    }

    private void HandleDisplayReadonlyNotepad(DisplayReadonlyNotepadArgs args) => TextPopup.Show(args.Message);

    private void HandleDisplayEditableNotepad(DisplayEditableNotepadArgs args) => TextPopup.Show(args.Message);
    #endregion
}
#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Controls.World.Hud;
using Chaos.Client.Controls.World.Hud.Panel;
using Chaos.Client.Controls.World.Hud.Panel.Slots;
using Chaos.Client.Controls.World.Hud.SelfProfile;
using Chaos.Client.Controls.World.Options;
using Chaos.Client.Controls.World.Popups;
using Chaos.Client.Controls.World.ViewPort;
using Chaos.Client.Data;
using Chaos.Client.Data.Definitions;
using Chaos.Client.Data.Models;
using Chaos.Client.Data.Utilities;
using Chaos.Client.Extensions;
using Chaos.Client.Models;
using Chaos.Client.Networking;
using Chaos.Client.Rendering;
using Chaos.Client.Systems;
using Chaos.Client.Systems.Animation;
using Chaos.DarkAges.Definitions;
using Chaos.Extensions.Common;
using Chaos.Geometry.Abstractions;
using Chaos.Geometry.Abstractions.Definitions;
using Chaos.Networking.Entities.Server;
using Chaos.Pathfinding;
using DALib.Data;
using DALib.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using TileFlags = DALib.Definitions.TileFlags;
#endregion

namespace Chaos.Client.Screens;

/// <summary>
///     Main game screen that renders the current map with the camera centered on the player. Activated after world entry
///     completes (all essential packets received). Receives map data from the server via ConnectionManager events. Handles
///     entity rendering with diagonal stripe draw ordering for correct isometric occlusion.
/// </summary>
public sealed class WorldScreen : IScreen
{
    // Walk queue: when walk animation is >= 75% complete, one walk can be queued
    private const float WALK_QUEUE_THRESHOLD = 0.75f;

    // Spacebar (assail) repeat interval when held
    private const float SPACEBAR_INTERVAL_MS = 100f;

    // Aisling body anchor within the padded composite canvas.
    // Canvas is padded by 27px on each side for weapon/accessory layers, so body center shifts right.
    private const int BODY_CENTER_X = 28 + 27;
    private const int BODY_CENTER_Y = 70;

    // Entity hitbox dimensions (screen pixels)
    private const int HITBOX_WIDTH = 28;
    private const int HITBOX_HEIGHT = 60;

    // Health bar Y offset from entity tile center (higher = further above entity)
    private const int HEALTH_BAR_Y_OFFSET = 61;

    // Name tag Y offset from entity tile center (above health bars)
    private const int NAME_TAG_Y_OFFSET = 72;
    private static readonly Color NAME_TAG_SHADOW_COLOR = new(20, 20, 20);

    private const string SPOUSE_PREFIX = "Spouse: ";
    private const string GROUP_MEMBERS_PREFIX = "Group members";

    // Screen blend: output = src + dst * (1 - src) per channel. Used for SelfAlpha EFA effects.
    private static readonly BlendState ScreenBlendState = new()
    {
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.InverseSourceColor,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.InverseSourceAlpha
    };

    // Aisling draw data cache: keyed by entity ID, invalidated when appearance/frame/suffix changes.
    // Individual layer textures are owned by AislingRenderer.LayerTextureCache — this just tracks which draw data is current.
    private readonly Dictionary<uint, AislingDrawDataEntry> AislingCache = new();

    private readonly CastingManager CastingManager = new();

    // Active chant overlays: keyed by entity ID, new chant replaces old
    private readonly Dictionary<uint, ChantOverlay> ChantOverlays = new();

    // Active chat bubbles: keyed by entity ID, one per entity (new message replaces old)
    private readonly Dictionary<uint, ChatBubble> ChatBubbles = new();

    // Cached debug label textures: keyed by entity ID, re-rendered only when name/position changes
    private readonly Dictionary<uint, CachedText> DebugLabelCache = new();

    // Draw-pass hitbox list: rebuilt every frame during entity rendering, in draw order (back-to-front)
    private readonly List<EntityHitBox> EntityHitBoxes = new(256);

    // Active health bars: keyed by entity ID, reset on each HealthBar packet
    private readonly Dictionary<uint, HealthBar> HealthBars = new();
    private readonly EntityHighlightState Highlight = new();

    // Cached name tag textures: keyed by entity ID, re-rendered only when name/color changes
    private readonly Dictionary<uint, CachedText> NameTagCache = new();
    private readonly PathfindingState Pathfinding = new();
    private readonly List<(CachedText Text, Vector2 Position)> PendingDebugLabels = new();

    private Camera Camera = null!;
    private ChantEditControl ChantEdit = null!;
    private ContextMenu ContextMenu = null!;
    private short CurrentMapId;

    private DarknessRenderer DarknessRenderer = null!;
    private GraphicsDevice Device = null!;
    private ExchangeControl Exchange = null!;
    private byte? ExchangeAmountSlot;
    private FpsCounter FpsCounter = null!;
    private FriendsListControl FriendsList = null!;

    private ChaosGame Game = null!;
    private GoldExchangeControl GoldDrop = null!;
    private GroupControl GroupPanel = null!;
    private HotkeyHelpControl HotkeyHelp = null!;
    private PanelSlot? HoveredInventorySlot;
    private bool IsGameMaster;
    private ItemTooltipControl ItemTooltip = null!;
    private LargeWorldHudControl LargeHud = null!;
    private TileClickTracker LeftClickTracker = new();

    // True while awaiting a paginated board response (append instead of replace)
    private bool LoadingMoreBoardPosts;
    private MacroMenuControl MacroMenu = null!;
    private MailListControl MailList = null!;
    private MailReadControl MailRead = null!;
    private MailSendControl MailSend = null!;
    private MainOptionsControl MainOptions = null!;

    private MapFile? MapFile;
    private Pathfinder? MapPathfinder;
    private bool MapPreloaded;
    private MapRenderer MapRenderer = null!;

    // Overlay panels (rendered on top of HUD)
    private MerchantDialogControl MerchantDialog = null!;
    private NotepadControl Notepad = null!;
    private NpcDialogControl NpcDialog = null!;
    private OtherProfileControl OtherProfile = null!;
    private bool PendingLoginSwitch;
    private Direction? QueuedWalkDirection;
    private TileClickTracker RightClickTracker = new();
    private RasterizerState ScissorRasterizerState = null!;

    // True when the client explicitly requested its own profile — prevents unsolicited SelfProfile packets from opening the panel
    private bool SelfProfileRequested;
    private SettingsControl SettingsDialog = null!;
    private SilhouetteRenderer SilhouetteRenderer = null!;
    private WorldHudControl SmallHud = null!;
    private SocialStatusControl SocialStatusPicker = null!;
    private float SpacebarTimer;
    private SelfProfileTabControl StatusBook = null!;
    private TabMapRenderer TabMapRenderer = null!;
    private bool TabMapVisible;
    private TextPopupControl TextPopup = null!;
    private Texture2D? TileCursorDragTexture;

    // Tile cursor: dashed ellipse drawn on the hovered tile
    private Texture2D? TileCursorTexture;
    private IWorldHud WorldHud = null!;
    private WorldListControl WorldList = null!;
    private WorldMap WorldMap = null!;

    /// <inheritdoc />
    public UIPanel? Root { get; private set; }

    /// <inheritdoc />
    public void Dispose() { }

    /// <inheritdoc />
    public void Draw(SpriteBatch spriteBatch, GameTime gameTime)
    {
        // Pre-composite transparent aislings into per-entity RTs before world drawing
        if (MapFile is not null && MapPreloaded)
        {
            SilhouetteRenderer.Clear();

            var player = Game.World.GetPlayerEntity();

            // Collect silhouette entries (any entity type)
            if (player is not null)
                SilhouetteRenderer.AddSilhouette(new SilhouetteRenderer.SilhouetteEntry(player));

            // Collect transparent aisling entries (need per-entity compositing for uniform alpha)
            foreach (var entity in Game.World.GetSortedEntities())
                if ((entity.Type == ClientEntityType.Aisling) && entity.IsTransparent)
                    SilhouetteRenderer.AddTransparent(
                        entity,
                        Game.AislingRenderer,
                        Highlight.ShowTintHighlight && (Highlight.HoveredEntityId == entity.Id));

            SilhouetteRenderer.PreRenderTransparents(Game.AislingRenderer);

            // Pre-render silhouettes into a screen-sized RT (must happen before main RT drawing starts,
            // because RT switching discards the main RT's contents)
            SilhouetteRenderer.PreRenderSilhouettes(batch =>
            {
                batch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, GlobalSettings.Sampler);

                foreach (var entry in SilhouetteRenderer.SilhouetteEntries)
                    DrawEntity(batch, entry.Entity);

                batch.End();
            });
        }

        // Pass 1: World rendering — clipped to the HUD viewport area, camera transform
        if (MapFile is not null && MapPreloaded)
        {
            var viewportRect = WorldHud.ViewportBounds;
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
            SilhouetteRenderer.DrawSilhouettes(spriteBatch);
            DrawChantOverlays(spriteBatch);
            DrawHealthBars(spriteBatch);
            DrawNameTags(spriteBatch);
            DrawChatBubbles(spriteBatch);
            spriteBatch.End();

            // Darkness overlay — drawn over the world in screen space (no camera transform)
            if (DarknessRenderer.IsActive)
            {
                var viewport = WorldHud.ViewportBounds;
                DarknessRenderer.Update(Camera, viewport);

                spriteBatch.Begin(
                    blendState: BlendState.NonPremultiplied,
                    samplerState: GlobalSettings.Sampler,
                    rasterizerState: ScissorRasterizerState);
                DarknessRenderer.Draw(spriteBatch, viewport);
                spriteBatch.End();
            }

            // Snapshot draw count before debug draws so the reported count excludes debug visualizations
            DebugOverlay.SnapshotDrawCount();

            // Debug overlay: entity hitboxes, tile grid, etc.
            if (DebugOverlay.IsActive)
            {
                spriteBatch.Begin(
                    SpriteSortMode.Deferred,
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

        // Tab map overlay — drawn on top of world, under HUD
        // TabMapRenderer manages its own SpriteBatch Begin/End blocks (stencil passes for entity overlap)
        if (TabMapVisible && MapFile is not null)
        {
            var viewport = WorldHud.ViewportBounds;
            var player = Game.World.GetPlayerEntity();
            var px = player?.TileX ?? 0;
            var py = player?.TileY ?? 0;

            TabMapRenderer.Draw(
                spriteBatch,
                Device,
                viewport,
                px,
                py,
                Game.World.GetSortedEntities(),
                Game.World.PlayerEntityId);
        }

        // Pass 2: UI overlay — full screen, no transform
        spriteBatch.Begin(samplerState: GlobalSettings.Sampler);
        Root!.Draw(spriteBatch);
        DrawDragIcon(spriteBatch);
        spriteBatch.End();
    }

    /// <inheritdoc />
    public void Initialize(ChaosGame game)
    {
        Game = game;

        // Player identity
        Game.Connection.OnUserId += HandleUserId;

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

        // NPC dialog/menu
        Game.World.NpcInteraction.DialogChanged += HandleDialogChanged;
        Game.World.NpcInteraction.MenuChanged += HandleMenuChanged;

        // Refresh response
        Game.Connection.OnRefreshResponse += HandleRefreshResponse;

        Game.World.Exchange.AmountRequested += HandleExchangeAmountRequested;

        // Board — subscribe to state events
        Game.World.Board.PostListChanged += HandleBoardPostListChanged;
        Game.World.Board.PostViewed += HandleBoardPostViewed;
        Game.World.Board.BoardListReceived += HandleBoardListReceived;
        Game.World.Board.ResponseReceived += msg => Game.World.Chat.AddOrangeBarMessage(msg);

        // Group invite — subscribe to state event
        Game.World.GroupInvite.Received += HandleGroupInviteReceived;

        // Profiles
        Game.Connection.OnEditableProfileRequest += HandleEditableProfileRequest;
        Game.Connection.OnSelfProfile += HandleSelfProfile;
        Game.Connection.OnOtherProfile += HandleOtherProfile;

        // Animations / effects / sound
        Game.Connection.OnBodyAnimation += HandleBodyAnimation;
        Game.Connection.OnAnimation += HandleAnimation;
        Game.Connection.OnSound += HandleSound;
        Game.Connection.OnCancelCasting += CastingManager.Reset;

        // Map transitions
        Game.Connection.OnMapChangePending += HandleMapChangePending;

        // Logout
        Game.Connection.OnExitResponse += HandleExitResponse;
        Game.Connection.StateChanged += HandleStateChanged;

        // Health bars
        Game.Connection.OnHealthBar += HandleHealthBar;

        // Status effects
        Game.Connection.OnEffect += HandleEffect;

        // Light level
        Game.Connection.OnLightLevel += HandleLightLevel;

        // Notepad popups
        Game.Connection.OnDisplayReadonlyNotepad += HandleDisplayReadonlyNotepad;
        Game.Connection.OnDisplayEditableNotepad += HandleDisplayEditableNotepad;

        // World map
        Game.Connection.OnWorldMap += HandleWorldMap;

        // Doors
        Game.Connection.OnDoor += HandleDoor;
    }

    /// <inheritdoc />
    public void LoadContent(GraphicsDevice graphicsDevice)
    {
        Device = graphicsDevice;

        // Create both HUD layouts — '/' key swaps between them
        // ZIndex=-1 so HUD frames render behind all popup panels
        SmallHud = new WorldHudControl(Game.World)
        {
            ZIndex = -1
        };

        LargeHud = new LargeWorldHudControl(Game.World)
        {
            Visible = false,
            ZIndex = -1
        };
        WorldHud = SmallHud;

        var viewport = WorldHud.ViewportBounds;

        Camera = new Camera(viewport.Width, viewport.Height)
        {
            Offset = new Vector2(-28, 24)
        };
        MapRenderer = new MapRenderer();
        TabMapRenderer = new TabMapRenderer();
        SilhouetteRenderer = new SilhouetteRenderer(graphicsDevice);
        DarknessRenderer = new DarknessRenderer(graphicsDevice);

        ScissorRasterizerState = new RasterizerState
        {
            ScissorTestEnable = true
        };

        TileCursorTexture = CreateTileCursorTexture(graphicsDevice, new Color(247, 142, 24));
        TileCursorDragTexture = CreateTileCursorTexture(graphicsDevice, new Color(100, 149, 237));

        // Overlay panels — ZIndex: -2 sub-panels, -1 slide panels, 0 standard (default), 1 popups, 2 context menu
        NpcDialog = new NpcDialogControl();
        WireNpcDialog();

        MerchantDialog = new MerchantDialogControl();
        WireMerchantDialog();

        MainOptions = new MainOptionsControl
        {
            ZIndex = -1
        };
        MainOptions.SetViewportBounds(WorldHud.ViewportBounds);
        WireOptionsDialog();

        // Sub-panels slide out from MainOptions' left edge, render behind it
        var optionsAnchorX = WorldHud.ViewportBounds.X + WorldHud.ViewportBounds.Width - MainOptions.Width + 10;
        var optionsAnchorY = WorldHud.ViewportBounds.Y;

        SettingsDialog = new SettingsControl
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
                    Game.Settings.AutoAcceptGroupInvites = value;

                    break;
                case 8:
                    Game.Settings.ScrollLevel = value ? 1 : 0;

                    break;
                case 9:
                    Game.Settings.UseShiftKeyForAltPanels = value;

                    break;
                case 10:
                    Game.Settings.EnableProfileClick = value ? 1 : 0;

                    break;
                case 11:
                    Game.Settings.RecordNpcChat = value;

                    break;
                case 12:
                    // Server-authoritative — send toggle, server responds with updated profile
                    Game.Connection.ToggleGroup();

                    return;
            }

            Game.Settings.Save();
        };

        // Apply saved local settings to the settings dialog
        SettingsDialog.SetSettingValue(6, Game.Settings.AutoAcceptGroupInvites);
        SettingsDialog.SetSettingValue(8, Game.Settings.ScrollLevel > 0);
        SettingsDialog.SetSettingValue(9, Game.Settings.UseShiftKeyForAltPanels);
        SettingsDialog.SetSettingValue(10, Game.Settings.EnableProfileClick > 0);
        SettingsDialog.SetSettingValue(11, Game.Settings.RecordNpcChat);
        SettingsDialog.SetSettingValue(12, Game.Settings.GroupOpen);

        MacroMenu = new MacroMenuControl
        {
            ZIndex = -2
        };
        MacroMenu.SetSlideAnchor(optionsAnchorX, optionsAnchorY);
        MacroMenu.OnOk += SavePlayerMacros;

        HotkeyHelp = new HotkeyHelpControl();

        GroupPanel = new GroupControl();

        GroupPanel.OnInvite += () =>
        {
            // Open chat in "Group: " mode for typing an invite target name
            FocusChat("Group invite: ", new Color(154, 205, 50));
        };

        WorldList = new WorldListControl(Game.World.WorldList)
        {
            ZIndex = -1
        };
        WorldList.SetViewportBounds(WorldHud.ViewportBounds);

        FriendsList = new FriendsListControl
        {
            ZIndex = -2
        };
        FriendsList.SetSlideAnchor(optionsAnchorX, optionsAnchorY);
        FriendsList.OnOk += SavePlayerFriendList;

        Exchange = new ExchangeControl(Game.World.Exchange, WorldHud.ViewportBounds);

        GoldDrop = new GoldExchangeControl
        {
            ZIndex = 2
        };

        GoldDrop.OnConfirm += amount =>
        {
            if (ExchangeAmountSlot.HasValue)
            {
                // Exchange stackable item amount response
                Game.Connection.SendExchangeInteraction(
                    ExchangeRequestType.AddStackableItem,
                    Exchange.OtherUserId,
                    ExchangeAmountSlot.Value,
                    (byte)Math.Min(amount, byte.MaxValue));

                ExchangeAmountSlot = null;
            } else if (Exchange.Visible && (GoldDrop.TargetEntityId == Exchange.OtherUserId))

                // Exchange gold setting
                Game.Connection.SendExchangeInteraction(ExchangeRequestType.SetGold, Exchange.OtherUserId, goldAmount: (int)amount);
            else if (GoldDrop.TargetEntityId.HasValue)
                Game.Connection.DropGoldOnCreature((int)amount, GoldDrop.TargetEntityId.Value);
            else
                Game.Connection.DropGold((int)amount, GoldDrop.TargetTileX, GoldDrop.TargetTileY);
        };

        MailList = new MailListControl();
        MailRead = new MailReadControl();
        MailSend = new MailSendControl();
        WireExchange();
        WireMailControls();

        StatusBook = new SelfProfileTabControl(Game.World.Equipment)
        {
            ZIndex = 2
        };

        StatusBook.OnUnequip += slot => Game.Connection.Unequip(slot);
        StatusBook.OnClose += SavePlayerFamilyList;

        StatusBook.OnGroupToggled += () => Game.Connection.ToggleGroup();

        SocialStatusPicker = new SocialStatusControl();

        SocialStatusPicker.OnStatusSelected += status =>
        {
            Game.Connection.SendSocialStatus(status);
            StatusBook.SetEmoticonState((byte)status, status.ToString());
        };

        TextPopup = new TextPopupControl
        {
            ZIndex = 2
        };

        Notepad = new NotepadControl
        {
            ZIndex = 2
        };
        Notepad.OnSave += (slot, text) => Game.Connection.SendSetNotepad(slot, text);

        OtherProfile = new OtherProfileControl
        {
            ZIndex = 2
        };

        ChantEdit = new ChantEditControl
        {
            ZIndex = 2
        };
        ChantEdit.OnChantSet += HandleChantSet;

        WorldMap = new WorldMap(Game.Connection)
        {
            ZIndex = 2
        };

        ContextMenu = new ContextMenu
        {
            ZIndex = 3
        };

        ItemTooltip = new ItemTooltipControl
        {
            ZIndex = 3
        };

        Root = new UIPanel
        {
            Name = "WorldRoot",
            Width = ChaosGame.VIRTUAL_WIDTH,
            Height = ChaosGame.VIRTUAL_HEIGHT
        };
        Root.AddChild(SmallHud);
        Root.AddChild(LargeHud);
        Root.AddChild(ItemTooltip);
        Root.AddChild(NpcDialog);
        Root.AddChild(MerchantDialog);
        Root.AddChild(MainOptions);
        Root.AddChild(SettingsDialog);
        Root.AddChild(MacroMenu);
        Root.AddChild(HotkeyHelp);
        Root.AddChild(GroupPanel);
        Root.AddChild(WorldList);
        Root.AddChild(FriendsList);
        Root.AddChild(Exchange);
        Root.AddChild(GoldDrop);
        Root.AddChild(MailList);
        Root.AddChild(MailRead);
        Root.AddChild(MailSend);
        Root.AddChild(StatusBook);
        Root.AddChild(OtherProfile);
        Root.AddChild(TextPopup);
        Root.AddChild(Notepad);
        Root.AddChild(ChantEdit);
        Root.AddChild(WorldMap);
        Root.AddChild(SocialStatusPicker);
        Root.AddChild(ContextMenu);

        FpsCounter = new FpsCounter
        {
            X = 5,
            Y = 5,
            ZIndex = 100
        };
        Root.AddChild(FpsCounter);

        WireHudPanels(SmallHud);
        WireHudPanels(LargeHud);

        // Request current server settings (populates setting names/values)
        Game.Connection.SendOptionToggle(UserOption.Request);

        // Build UI atlas after all HUD controls are constructed
        UiRenderer.Instance?.BuildAtlas();
    }

    /// <inheritdoc />
    public void UnloadContent()
    {
        Game.Connection.OnUserId -= HandleUserId;
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
        Game.World.NpcInteraction.DialogChanged -= HandleDialogChanged;
        Game.World.NpcInteraction.MenuChanged -= HandleMenuChanged;
        Game.Connection.OnRefreshResponse -= HandleRefreshResponse;
        Game.World.Exchange.AmountRequested -= HandleExchangeAmountRequested;
        Game.World.Board.PostListChanged -= HandleBoardPostListChanged;
        Game.World.Board.PostViewed -= HandleBoardPostViewed;
        Game.World.Board.BoardListReceived -= HandleBoardListReceived;
        Game.World.GroupInvite.Received -= HandleGroupInviteReceived;
        Game.Connection.OnEditableProfileRequest -= HandleEditableProfileRequest;
        Game.Connection.OnSelfProfile -= HandleSelfProfile;
        Game.Connection.OnOtherProfile -= HandleOtherProfile;
        Game.Connection.OnBodyAnimation -= HandleBodyAnimation;
        Game.Connection.OnAnimation -= HandleAnimation;
        Game.Connection.OnSound -= HandleSound;
        Game.Connection.OnCancelCasting -= CastingManager.Reset;
        Game.Connection.OnMapChangePending -= HandleMapChangePending;
        Game.Connection.OnExitResponse -= HandleExitResponse;
        Game.Connection.StateChanged -= HandleStateChanged;
        Game.Connection.OnHealthBar -= HandleHealthBar;
        Game.Connection.OnEffect -= HandleEffect;
        Game.Connection.OnLightLevel -= HandleLightLevel;
        Game.Connection.OnDisplayReadonlyNotepad -= HandleDisplayReadonlyNotepad;
        Game.Connection.OnDisplayEditableNotepad -= HandleDisplayEditableNotepad;
        Game.Connection.OnWorldMap -= HandleWorldMap;
        Game.Connection.OnDoor -= HandleDoor;

        // Unwire panel click-to-use events
        WorldHud.Inventory.OnSlotClicked -= HandleInventorySlotClicked;
        WorldHud.SkillBook.OnSlotClicked -= HandleSkillSlotClicked;
        WorldHud.SkillBookAlt.OnSlotClicked -= HandleSkillSlotClicked;
        WorldHud.SpellBook.OnSlotClicked -= HandleSpellSlotClicked;
        WorldHud.SpellBookAlt.OnSlotClicked -= HandleSpellSlotClicked;

        MapRenderer.Dispose();
        TabMapRenderer.Dispose();
        ScissorRasterizerState.Dispose();
        DarknessRenderer.Dispose();
        SilhouetteRenderer.Dispose();
        Root?.Dispose();
        ClearAislingCache();
        Game.ItemRenderer.Clear();
        ClearChatBubbles();
        ClearHealthBars();
        ClearChantOverlays();
        ClearNameTagCache();
        ClearDebugLabelCache();
    }

    /// <inheritdoc />
    public void Update(GameTime gameTime)
    {
        if (PendingLoginSwitch)
        {
            PendingLoginSwitch = false;
            Game.Screens.Switch(new LobbyLoginScreen(true));

            return;
        }

        var input = Game.Input;
        var elapsedMs = (float)gameTime.ElapsedGameTime.TotalMilliseconds;
        LeftClickTracker.Tick(elapsedMs);
        RightClickTracker.Tick(elapsedMs);

        // Advance entity animations and active effects
        var smoothScroll = Game.Settings.ScrollLevel > 0;
        var player = Game.World.GetPlayerEntity();

        foreach (var entity in Game.World.GetSortedEntities())
        {
            // All entities step discretely by default. Player gets smooth lerp only if setting enabled.
            var isSmooth = (entity == player) && smoothScroll;
            AnimationManager.Advance(entity, elapsedMs, isSmooth);

            // Tick emote overlay timer and cycle animated emote frames
            if (entity.ActiveEmoteFrame >= 0)
            {
                entity.EmoteElapsedMs += elapsedMs;
                entity.EmoteRemainingMs -= elapsedMs;

                if (entity.EmoteRemainingMs <= 0)
                {
                    entity.ActiveEmoteFrame = -1;
                    entity.EmoteFrameCount = 0;
                } else if (entity.EmoteFrameCount > 1)
                {
                    var frameDuration = entity.EmoteDurationMs / entity.EmoteFrameCount;
                    var frameIndex = (int)(entity.EmoteElapsedMs / frameDuration) % entity.EmoteFrameCount;
                    entity.ActiveEmoteFrame = entity.EmoteStartFrame + frameIndex;
                }
            }
        }

        Game.World.UpdateEffects(elapsedMs);

        // Execute queued walk when player becomes idle after walk animation.
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
                PredictAndWalk(player, queuedDir);

            movementHandled = true;
        }

        // Execute next pathfinding step when player becomes idle
        if (!movementHandled && player is not null && (player.AnimState == EntityAnimState.Idle))
        {
            if (Pathfinding.Path is { Count: > 0 })
            {
                // If chasing an entity that no longer exists, stop
                if (Pathfinding.TargetEntityId.HasValue && Game.World.GetEntity(Pathfinding.TargetEntityId.Value) is null)
                    Pathfinding.Clear();
                else
                {
                    var nextPoint = Pathfinding.Path.Pop();
                    var dx = nextPoint.X - player.TileX;
                    var dy = nextPoint.Y - player.TileY;

                    var pathDir = (dx, dy) switch
                    {
                        (0, -1) => Direction.Up,
                        (1, 0)  => Direction.Right,
                        (0, 1)  => Direction.Down,
                        (-1, 0) => Direction.Left,
                        _       => (Direction?)null
                    };

                    if (pathDir.HasValue)
                    {
                        if (player.Direction != pathDir.Value)
                        {
                            Game.Connection.Turn(pathDir.Value);
                            player.Direction = pathDir.Value;
                        }

                        PredictAndWalk(player, pathDir.Value);
                        movementHandled = true;
                    } else
                        Pathfinding.Clear();
                }
            } else if (Pathfinding.TargetEntityId.HasValue)
            {
                // Path exhausted with entity target — check if adjacent and assail, or re-pathfind
                var target = Game.World.GetEntity(Pathfinding.TargetEntityId.Value);

                if (target is null)
                    Pathfinding.Clear();
                else if (IsAdjacent(
                             player.TileX,
                             player.TileY,
                             target.TileX,
                             target.TileY))
                {
                    // Adjacent — turn toward target and assail
                    var faceDir = DirectionToward(
                        player.TileX,
                        player.TileY,
                        target.TileX,
                        target.TileY);

                    if (faceDir.HasValue && (player.Direction != faceDir.Value))
                    {
                        Game.Connection.Turn(faceDir.Value);
                        player.Direction = faceDir.Value;
                    }

                    Game.Connection.Spacebar();
                    Pathfinding.Clear();
                    movementHandled = true;
                } else
                {
                    // Entity moved — re-pathfind on 100ms timer
                    Pathfinding.RetargetTimer += elapsedMs;

                    if (Pathfinding.RetargetTimer >= 100f)
                    {
                        Pathfinding.RetargetTimer = 0;
                        PathfindToEntity(player, target);

                        if (Pathfinding.Path is null)
                            Pathfinding.Clear();
                    }
                }
            }
        }

        // Tick re-pathfind timer while walking toward an entity target
        if (Pathfinding.TargetEntityId.HasValue && player is not null && (player.AnimState == EntityAnimState.Walking))
            Pathfinding.RetargetTimer += elapsedMs;

        // Camera follows player's visual position (tile + walk interpolation offset)
        FollowPlayerCamera();

        // Overlay panels get first priority for input
        if (NpcDialog.Visible)
        {
            NpcDialog.Update(gameTime, input);

            return;
        }

        if (MerchantDialog.Visible)
        {
            MerchantDialog.Update(gameTime, input);

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

        if (ChantEdit.Visible)
        {
            ChantEdit.Update(gameTime, input);

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

        if (WorldHud.Prompt.Visible)
        {
            WorldHud.Prompt.Update(gameTime, input);

            return;
        }

        if (GoldDrop.Visible)
        {
            GoldDrop.Update(gameTime, input);

            return;
        }

        if (Exchange.Visible)
        {
            ((UIPanel)WorldHud).Update(gameTime, input);
            Exchange.Update(gameTime, input);

            // Allow setting gold by clicking MyMoney area — shows gold amount popup
            if (input.WasLeftButtonPressed && Exchange.IsMyMoneyClicked(input.MouseX, input.MouseY))
            {
                ExchangeAmountSlot = null;
                GoldDrop.ShowForTarget(Exchange.OtherUserId, 0, 0);
            }

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

        if (Notepad.Visible)
        {
            Notepad.Update(gameTime, input);

            return;
        }

        if (StatusBook.Visible)
        {
            // HUD panels still receive input while the status book is open (drag-and-drop)
            ((UIPanel)WorldHud).Update(gameTime, input);
            StatusBook.Update(gameTime, input);

            return;
        }

        if (OtherProfile.Visible)
        {
            OtherProfile.Update(gameTime, input);

            return;
        }

        if (WorldMap.Visible)
        {
            WorldMap.Update(gameTime, input);

            return;
        }

        if (SocialStatusPicker.Visible)
        {
            SocialStatusPicker.Update(gameTime, input);

            return;
        }

        // Context menu gets priority when visible
        if (ContextMenu.Visible)
        {
            ContextMenu.Update(gameTime, input);

            return;
        }

        // Track which entity the mouse is hovering over (for name tags, tint highlight, targeting)
        var hoverEntity = GetEntityAtScreen(input.MouseX, input.MouseY);

        var newHoveredId = hoverEntity is not null && hoverEntity.Type is ClientEntityType.Aisling or ClientEntityType.Creature
            ? hoverEntity.Id
            : (uint?)null;

        if (newHoveredId != Highlight.HoveredEntityId)
            ClearHighlightCache();

        Highlight.HoveredEntityId = newHoveredId;

        // Tint highlight only shows during spell targeting or item dragging
        Highlight.ShowTintHighlight = CastingManager.IsTargeting || GetDraggingPanel() is not null;

        // Tick casting timer (chant lines are sent on a 1-second interval)
        CastingManager.Update(elapsedMs, Game.Connection);

        // Cast mode — blocks all other input, only allows target selection or cancel
        if (CastingManager.IsTargeting)
        {
            if (input.WasKeyPressed(Keys.Escape))
            {
                CastingManager.Reset();

                return;
            }

            // Click to select target, or cancel if clicking on nothing
            if (input.WasLeftButtonPressed)
            {
                if (hoverEntity is not null && hoverEntity.Type is ClientEntityType.Aisling or ClientEntityType.Creature)
                    CastingManager.SelectTarget(
                        hoverEntity.Id,
                        hoverEntity.TileX,
                        hoverEntity.TileY,
                        Game.Connection);
                else
                    CastingManager.Reset();
            }

            ((UIPanel)WorldHud).Update(gameTime, input);

            return;
        }

        // Escape — close overlays, unfocus chat
        if (input.WasKeyPressed(Keys.Escape))
            if (WorldHud.ChatInput.IsFocused)
                UnfocusChat();

        // Enter — toggle chat focus / send message
        if (input.WasKeyPressed(Keys.Enter))
        {
            if (WorldHud.ChatInput.IsFocused)
            {
                var message = WorldHud.ChatInput.Text.Trim();
                var prefix = WorldHud.ChatInput.Prefix;

                // Whisper phase 1: "to []? " → user entered a target name, transition to phase 2
                if ((prefix == "to []? ") && (message.Length > 0))
                {
                    WorldHud.ChatInput.Prefix = $"-> {message}: ";
                    WorldHud.ChatInput.Text = string.Empty;
                } else
                {
                    DispatchChatMessage(message);
                    WorldHud.ChatInput.Text = string.Empty;
                    UnfocusChat();
                }
            } else
                FocusChat($"{WorldHud.PlayerName}: ", Color.White);
        }

        // Hotkeys and movement — only when chat is not focused
        if (!WorldHud.ChatInput.IsFocused)
        {
            // Shout hotkey (!) — opens chat in shout mode
            if (input.WasKeyPressed(Keys.D1) && (input.IsKeyHeld(Keys.LeftShift) || input.IsKeyHeld(Keys.RightShift)))
            {
                FocusChat($"{WorldHud.PlayerName}! ", Color.Yellow);

                return;
            }

            // Whisper hotkey (") — opens chat in whisper target mode
            if (input.WasKeyPressed(Keys.OemQuotes) && (input.IsKeyHeld(Keys.LeftShift) || input.IsKeyHeld(Keys.RightShift)))
            {
                FocusChat("to []? ", new Color(100, 149, 237));

                return;
            }

            // Tab panel switching — blocked while dragging the orange bar
            var shift = input.IsKeyHeld(Keys.LeftShift) || input.IsKeyHeld(Keys.RightShift);

            if (WorldHud.IsOrangeBarDragging)
            {
                // Suppress all panel switching / expand while dragging
            } else if (input.WasKeyPressed(Keys.A))
            {
                if (shift)
                {
                    if (WorldHud.ActiveTab != HudTab.Inventory)
                        WorldHud.ShowTab(HudTab.Inventory);

                    WorldHud.ToggleExpand();
                } else if (WorldHud.ActiveTab == HudTab.Inventory)
                {
                    SelfProfileRequested = true;
                    Game.Connection.RequestSelfProfile();
                } else
                    WorldHud.ShowTab(HudTab.Inventory);
            } else if (input.WasKeyPressed(Keys.S))
                WorldHud.ShowTab(shift ? HudTab.SkillsAlt : HudTab.Skills);
            else if (input.WasKeyPressed(Keys.D))
                WorldHud.ShowTab(shift ? HudTab.SpellsAlt : HudTab.Spells);
            else if (input.WasKeyPressed(Keys.F))
                WorldHud.ShowTab(shift ? HudTab.MessageHistory : HudTab.Chat);
            else if (input.WasKeyPressed(Keys.G))
                WorldHud.ShowTab(shift ? HudTab.ExtendedStats : HudTab.Stats);
            else if (input.WasKeyPressed(Keys.H))
                WorldHud.ShowTab(HudTab.Tools);

            // Tab — toggle tab map overlay
            if (input.WasKeyPressed(Keys.Tab))
                TabMapVisible = !TabMapVisible;

            // PageUp/PageDown — tab map zoom
            if (TabMapVisible)
            {
                if (input.WasKeyPressed(Keys.PageUp))
                    TabMapRenderer.ZoomIn();

                if (input.WasKeyPressed(Keys.PageDown))
                    TabMapRenderer.ZoomOut();
            }

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
                if (!WorldHud.ChatInput.IsFocused)
                    FocusChat(string.Empty, Color.White);

            // F10 — friends list
            if (input.WasKeyPressed(Keys.F10))
                FriendsList.Show();

            // / — swap HUD layout (small ↔ large)
            if (input.WasKeyPressed(Keys.OemQuestion) && !shift)
                SwapHudLayout();

            // Spacebar — assail (repeats while held)
            SpacebarTimer -= elapsedMs;

            if (input.IsKeyHeld(Keys.Space) && (SpacebarTimer <= 0))
            {
                Game.Connection.Spacebar();
                SpacebarTimer = SPACEBAR_INTERVAL_MS;
                Pathfinding.Clear();
            } else if (!input.IsKeyHeld(Keys.Space))
                SpacebarTimer = 0;

            // B — pick up item from under player, or from the tile in front
            if (input.WasKeyPressed(Keys.B))
                TryPickupItem();

            // Emote hotkeys: Ctrl/Alt/Ctrl+Alt + number row → body animations 9-44
            if (HandleEmoteHotkeys(input))
                return;

            // Slot hotkeys: 1-9, 0, -, = → use slot 1-12 of the active panel
            HandleSlotHotkeys(input);

            // Click handling — left click in viewport area
            if (input.WasLeftButtonPressed)
            {
                // Ctrl+click — context menu on aisling entities
                if (input.IsKeyHeld(Keys.LeftControl) || input.IsKeyHeld(Keys.RightControl))
                    HandleCtrlClick(input.MouseX, input.MouseY);

                // Alt+click on self — open self profile
                else if (input.IsKeyHeld(Keys.LeftAlt) || input.IsKeyHeld(Keys.RightAlt))
                {
                    var altEntity = GetEntityAtScreen(input.MouseX, input.MouseY);

                    if (altEntity is not null && (altEntity.Id == Game.Connection.AislingId))
                    {
                        SelfProfileRequested = true;
                        Game.Connection.RequestSelfProfile();
                    } else
                        HandleWorldClick(input.MouseX, input.MouseY);
                } else
                    HandleWorldClick(input.MouseX, input.MouseY);
            }

            // Right-click — pathfind to clicked tile
            if (input.WasRightButtonPressed)
                HandleWorldRightClick(input.MouseX, input.MouseY);

            // Player movement — each WasKeyPressed (initial press + OS key repeat) goes through:
            // - Idle → walk (with client-side prediction) or turn immediately
            // - Walking at >= 75% → queue one walk
            Direction? direction = null;

            if (input.WasKeyPressed(Keys.Up))
                direction = Direction.Up;
            else if (input.WasKeyPressed(Keys.Right))
                direction = Direction.Right;
            else if (input.WasKeyPressed(Keys.Down))
                direction = Direction.Down;
            else if (input.WasKeyPressed(Keys.Left))
                direction = Direction.Left;

            // Arrow key press cancels any active pathfinding
            if (direction.HasValue)
                Pathfinding.Clear();

            if (direction.HasValue && player is not null && !movementHandled)
            {
                if (player.AnimState == EntityAnimState.Idle)
                {
                    if (player.Direction != direction.Value)
                    {
                        Game.Connection.Turn(direction.Value);
                        player.Direction = direction.Value;
                    } else
                    {
                        PredictAndWalk(player, direction.Value);
                        QueuedWalkDirection = null;
                    }
                } else if (player.AnimState == EntityAnimState.Walking)
                {
                    // Must match AdvanceWalk's totalDuration formula: frameCount * interval
                    var totalDuration = Math.Max(1f, player.AnimFrameCount * player.AnimFrameIntervalMs);
                    var progress = player.AnimElapsedMs / totalDuration;

                    if (progress >= WALK_QUEUE_THRESHOLD)
                        QueuedWalkDirection = direction.Value;
                }
            }
        }

        ((UIPanel)WorldHud).Update(gameTime, input);

        // Update inventory tooltip position to follow cursor
        if (HoveredInventorySlot is not null && ItemTooltip.Visible)
        {
            var rightX = input.MouseX + 15;

            ItemTooltip.X = (rightX + ItemTooltip.Width) <= ChaosGame.VIRTUAL_WIDTH ? rightX : input.MouseX - ItemTooltip.Width;

            ItemTooltip.Y = Math.Clamp(input.MouseY + 15, 0, ChaosGame.VIRTUAL_HEIGHT - ItemTooltip.Height);
        }

        // Track highlighted entity when dragging a panel item over the world viewport
        UpdateDragHighlight(input);

        // Update chat bubbles and health bars — tick timers and remove expired
        UpdateChatBubbles(gameTime, input);
        UpdateChantOverlays(gameTime, input);
        UpdateHealthBars(gameTime);

        // Player silhouette is pre-rendered at the start of Draw
    }

    #region Exchange Wiring
    private void WireExchange()
    {
        Exchange.OnOk += () => Game.Connection.SendExchangeInteraction(ExchangeRequestType.Accept, Exchange.OtherUserId);

        Exchange.OnCancel += () =>
        {
            Game.Connection.SendExchangeInteraction(ExchangeRequestType.Cancel, Exchange.OtherUserId);
            Game.World.Exchange.Close();
        };
    }
    #endregion

    #region Mail Wiring
    private void WireMailControls()
    {
        // Mail list events
        MailList.OnViewPost += postId =>
        {
            Game.Connection.SendBoardInteraction(
                BoardRequestType.ViewPost,
                MailList.BoardId,
                postId,
                controls: BoardControls.RequestPost);
        };

        MailList.OnNewMail += () =>
        {
            MailList.Hide();
            MailSend.BoardId = MailList.BoardId;
            MailSend.IsPublicBoard = MailList.IsPublicBoard;
            MailSend.ShowCompose();
        };

        MailList.OnDeletePost += postId => Game.Connection.SendBoardInteraction(BoardRequestType.Delete, MailList.BoardId, postId);

        MailList.OnReplyPost += postId => Game.Connection.SendBoardInteraction(
            BoardRequestType.ViewPost,
            MailList.BoardId,
            postId,
            controls: BoardControls.RequestPost);

        // Pagination: load next page of posts starting from the last visible post
        MailList.OnLoadMorePosts += lastPostId =>
        {
            LoadingMoreBoardPosts = true;
            Game.Connection.SendBoardInteraction(BoardRequestType.ViewBoard, MailList.BoardId, startPostId: lastPostId);
        };

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
            MailSend.IsPublicBoard = MailRead.IsPublicBoard;
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
            MailSend.IsPublicBoard = MailRead.IsPublicBoard;
            MailSend.ShowCompose();
        };

        MailRead.OnPrev += () =>
        {
            // Server expects postId+1 for PreviousPage (it decrements internally)
            var prevId = (short)(MailRead.CurrentPostId + 1);

            Game.Connection.SendBoardInteraction(
                BoardRequestType.ViewPost,
                MailRead.BoardId,
                prevId,
                controls: BoardControls.PreviousPage);
        };

        MailRead.OnNext += () =>
        {
            // Server expects postId-1 for NextPage (it increments internally)
            var nextId = (short)(MailRead.CurrentPostId - 1);

            Game.Connection.SendBoardInteraction(
                BoardRequestType.ViewPost,
                MailRead.BoardId,
                nextId,
                controls: BoardControls.NextPage);
        };

        // Mail send events
        MailSend.OnSend += (recipient, subject, body) =>
        {
            if (MailSend.IsPublicBoard)
                Game.Connection.SendBoardInteraction(
                    BoardRequestType.NewPost,
                    MailSend.BoardId,
                    subject: subject,
                    message: body);
            else
                Game.Connection.SendBoardInteraction(
                    BoardRequestType.SendMail,
                    MailSend.BoardId,
                    to: recipient,
                    subject: subject,
                    message: body);

            MailSend.Hide();
        };

        MailSend.OnCancel += () => MailSend.Hide();
    }
    #endregion

    #region Merchant Dialog Wiring
    private void WireMerchantDialog()
    {
        MerchantDialog.OnClose += () =>
        {
            if (MerchantDialog.SourceId is { } sourceId)
                Game.Connection.SendMenuResponse(MerchantDialog.SourceEntityType, sourceId, MerchantDialog.PursuitId);
        };

        MerchantDialog.OnItemSelected += selectedIndex =>
        {
            if (MerchantDialog.SourceId is not { } sourceId)
                return;

            var slot = MerchantDialog.GetEntrySlot(selectedIndex);

            if (slot is null)
                return;

            // ShowPlayerItems/ShowPlayerSkills/ShowPlayerSpells send the slot byte
            // ShowItems/ShowSkills/ShowSpells send the name as an arg
            if (MerchantDialog.CurrentMenuType is MenuType.ShowPlayerItems or MenuType.ShowPlayerSkills or MenuType.ShowPlayerSpells)
                Game.Connection.SendMenuResponse(
                    MerchantDialog.SourceEntityType,
                    sourceId,
                    MerchantDialog.PursuitId,
                    slot.Value);
            else
            {
                var name = MerchantDialog.GetEntryName(selectedIndex);

                if (name is not null)
                    Game.Connection.SendMenuResponse(
                        MerchantDialog.SourceEntityType,
                        sourceId,
                        MerchantDialog.PursuitId,
                        args: [name]);
            }
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
                    NpcDialog.SourceEntityType,
                    sourceId,
                    NpcDialog.PursuitId,
                    0); // dialogId 0 = close
        };

        NpcDialog.OnNext += () =>
        {
            if (NpcDialog.SourceId is { } sourceId)
                Game.Connection.SendDialogResponse(
                    NpcDialog.SourceEntityType,
                    sourceId,
                    NpcDialog.PursuitId,
                    (ushort)(NpcDialog.DialogId + 1));
        };

        NpcDialog.OnPrevious += () =>
        {
            if (NpcDialog.SourceId is { } sourceId)
                Game.Connection.SendDialogResponse(
                    NpcDialog.SourceEntityType,
                    sourceId,
                    NpcDialog.PursuitId,
                    (ushort)(NpcDialog.DialogId - 1));
        };

        NpcDialog.OnOptionSelected += optionIndex =>
        {
            if (NpcDialog.SourceId is not { } sourceId)
                return;

            if (NpcDialog.IsMenuMode)
            {
                // Menu responses use MenuInteraction opcode (0x39) with the option's pursuit ID
                var pursuitId = NpcDialog.GetOptionPursuitId(optionIndex);

                Game.Connection.SendMenuResponse(NpcDialog.SourceEntityType, sourceId, pursuitId);
            } else
            {
                // Dialog responses use DialogInteraction opcode (0x3A) with option index
                Game.Connection.SendDialogResponse(
                    NpcDialog.SourceEntityType,
                    sourceId,
                    NpcDialog.PursuitId,
                    (ushort)(NpcDialog.DialogId + 1),
                    DialogArgsType.MenuResponse,
                    (byte)(optionIndex + 1));
            }
        };

        NpcDialog.OnTextSubmit += text =>
        {
            if (NpcDialog.SourceId is not { } sourceId)
                return;

            if (NpcDialog.IsMenuMode)
            {
                // Menu text responses use MenuInteraction opcode (0x39)
                Game.Connection.SendMenuResponse(
                    NpcDialog.SourceEntityType,
                    sourceId,
                    NpcDialog.PursuitId,
                    args: [text]);
            } else
            {
                // Dialog text responses use DialogInteraction opcode (0x3A)
                Game.Connection.SendDialogResponse(
                    NpcDialog.SourceEntityType,
                    sourceId,
                    NpcDialog.PursuitId,
                    (ushort)(NpcDialog.DialogId + 1),
                    DialogArgsType.TextResponse,
                    args: [text]);
            }
        };
    }
    #endregion

    private record struct AislingDrawDataEntry(
        AislingAppearance Appearance,
        int FrameIndex,
        bool Flip,
        bool IsFrontFacing,
        string AnimSuffix,
        int EmotionFrame,
        AislingDrawData? DrawData);

    #region HUD Panel Wiring
    private void WireHudPanels(IWorldHud hud)
    {
        // Layout/expand
        if (hud.ChangeLayoutButton is not null)
            hud.ChangeLayoutButton.OnClick += SwapHudLayout;

        if (hud.ExpandButton is not null)
            hud.ExpandButton.OnClick += () => hud.ToggleExpand();

        // Action buttons
        if (hud.OptionButton is not null)
            hud.OptionButton.OnClick += () => MainOptions.Show();

        if (hud.HelpButton is not null)
            hud.HelpButton.OnClick += () => HotkeyHelp.Show();

        if (hud.GroupButton is not null)
            hud.GroupButton.OnClick += () => GroupPanel.Show();

        if (hud.UsersButton is not null)
            hud.UsersButton.OnClick += () =>
            {
                if (WorldList.Visible)
                {
                    WorldList.Hide();

                    return;
                }

                WorldList.Show(new List<WorldListEntry>(), 0);
                Game.Connection.RequestWorldList();
            };

        if (hud.BulletinButton is not null)
            hud.BulletinButton.OnClick += () => Game.Connection.SendBoardInteraction(BoardRequestType.BoardList);

        if (hud.LegendButton is not null)
            hud.LegendButton.OnClick += () =>
            {
                SelfProfileRequested = true;
                Game.Connection.RequestSelfProfile();
            };

        if (hud.TownMapButton is not null)
            hud.TownMapButton.OnClick += () =>
            {
                if (WorldMap.Visible)
                    WorldMap.HideMap();
            };

        if (hud.EmoteButton is not null)
            hud.EmoteButton.OnClick += () =>
            {
                if (SocialStatusPicker.Visible)
                {
                    SocialStatusPicker.Visible = false;

                    return;
                }

                SocialStatusPicker.X = hud.EmoteButton!.ScreenX - SocialStatusPicker.Width / 2 + hud.EmoteButton.Width / 2;
                SocialStatusPicker.Y = hud.EmoteButton.ScreenY - SocialStatusPicker.Height - 2;

                if (SocialStatusPicker.X < 0)
                    SocialStatusPicker.X = 0;

                if ((SocialStatusPicker.X + SocialStatusPicker.Width) > 640)
                    SocialStatusPicker.X = 640 - SocialStatusPicker.Width;

                SocialStatusPicker.Show();
            };

        if (hud.MailButton is not null)
            hud.MailButton.OnClick += () => Game.Connection.SendBoardInteraction(BoardRequestType.ViewBoard);

        // Slot events
        hud.Inventory.OnSlotClicked += HandleInventorySlotClicked;
        hud.Inventory.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.Inventory, s, t);
        hud.Inventory.OnSlotDroppedOutside += HandleInventoryDropInViewport;
        hud.SkillBook.OnSlotClicked += HandleSkillSlotClicked;
        hud.SkillBook.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.SkillBook, s, t);
        hud.SkillBookAlt.OnSlotClicked += HandleSkillSlotClicked;
        hud.SkillBookAlt.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.SkillBook, s, t);
        hud.SpellBook.OnSlotClicked += HandleSpellSlotClicked;
        hud.SpellBook.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.SpellBook, s, t);
        hud.SpellBookAlt.OnSlotClicked += HandleSpellSlotClicked;
        hud.SpellBookAlt.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.SpellBook, s, t);

        WireAbilityRightClicks(hud.SkillBook);
        WireAbilityRightClicks(hud.SkillBookAlt);
        WireAbilityRightClicks(hud.SpellBook);
        WireAbilityRightClicks(hud.SpellBookAlt);

        hud.Inventory.OnSlotHoverEnter += HandleInventoryHoverEnter;
        hud.Inventory.OnSlotHoverExit += HandleInventoryHoverExit;

        foreach (var panel in new PanelBase[]
                 {
                     hud.Inventory,
                     hud.SkillBook,
                     hud.SkillBookAlt,
                     hud.SpellBook,
                     hud.SpellBookAlt
                 })
        {
            panel.OnSlotHoverEnter += slot => WorldHud.SetDescription(slot.SlotName);
            panel.OnSlotHoverExit += () => WorldHud.SetDescription(null);
        }
    }

    private void SwapHudLayout()
    {
        WorldHud.Inventory.ForceHoverExit();

        var activeTab = WorldHud.ActiveTab;

        ((UIPanel)WorldHud).Visible = false;
        WorldHud = WorldHud == SmallHud ? LargeHud : SmallHud;
        ((UIPanel)WorldHud).Visible = true;
        WorldHud.ShowTab(activeTab);

        var viewport = WorldHud.ViewportBounds;
        Camera.Resize(viewport.Width, viewport.Height);
        WorldList.SetViewportBounds(viewport);

        FollowPlayerCamera();
    }

    /// <summary>
    ///     Calls an action on all HUD instances so both stay in sync regardless of which is visible.
    /// </summary>
    private void UpdateHuds(Action<IWorldHud> action)
    {
        action(SmallHud);
        action(LargeHud);
    }
    #endregion

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

        EntityHitBoxes.Clear();

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

            // 4. Dying creature dissolves
            DrawDyingEffectsAtDepth(spriteBatch, depth);

            // 5. Ground-targeted effects
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

    private void DrawDyingEffectsAtDepth(SpriteBatch spriteBatch, int depth)
    {
        if (MapFile is null)
            return;

        foreach (var dying in Game.World.DyingEffects)
        {
            if (dying.IsComplete || ((dying.TileX + dying.TileY) != depth))
                continue;

            var tileWorld = Camera.TileToWorld(dying.TileX, dying.TileY, MapFile.Height);
            var tileCenterX = tileWorld.X + DaLibConstants.HALF_TILE_WIDTH;
            var tileCenterY = tileWorld.Y + DaLibConstants.HALF_TILE_HEIGHT;

            var texCenterX = dying.CenterX - Math.Min(0, (int)dying.Left);
            var texCenterY = dying.CenterY - Math.Min(0, (int)dying.Top);

            var anchorX = dying.Flip ? dying.Texture.Width - texCenterX : texCenterX;

            var drawX = tileCenterX - anchorX;
            var drawY = tileCenterY - texCenterY;
            var screenPos = Camera.WorldToScreen(new Vector2(drawX, drawY));

            var effects = dying.Flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            spriteBatch.Draw(
                dying.Texture,
                screenPos,
                null,
                Color.White * dying.Alpha,
                0f,
                Vector2.Zero,
                1f,
                effects,
                0f);
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
                tileWorld.X + DaLibConstants.HALF_TILE_WIDTH,
                tileWorld.Y + DaLibConstants.HALF_TILE_HEIGHT,
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
        var spriteFrame = Game.EffectRenderer.GetFrame(effect.EffectId, effect.CurrentFrame);

        if (spriteFrame is null)
            return;

        var frame = spriteFrame.Value;
        var drawX = tileCenterX + visualOffset.X - frame.CenterX + Math.Min(0, (int)frame.Left);
        var drawY = tileCenterY + visualOffset.Y - frame.CenterY + Math.Min(0, (int)frame.Top);
        var screenPos = Camera.WorldToScreen(new Vector2(drawX, drawY));

        // In immediate mode, blend state can be changed directly between draws
        if (effect.BlendMode != EffectBlendMode.Normal)
            Device.BlendState = effect.BlendMode switch
            {
                EffectBlendMode.Additive  => BlendState.Additive,
                EffectBlendMode.SelfAlpha => ScreenBlendState,
                _                         => BlendState.AlphaBlend
            };

        spriteBatch.Draw(frame.Texture, screenPos, Color.White);

        if (effect.BlendMode != EffectBlendMode.Normal)
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
            Game.SoundManager.SetMusicVolume(volume);
            Game.Settings.MusicVolume = volume;
            Game.Settings.Save();
        };

        // Apply saved volume settings
        MainOptions.SetSoundVolume(Game.Settings.SoundVolume);
        MainOptions.SetMusicVolume(Game.Settings.MusicVolume);
        Game.SoundManager.SetSoundVolume(Game.Settings.SoundVolume);
        Game.SoundManager.SetMusicVolume(Game.Settings.MusicVolume);
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
        var tileCenterX = tileWorldPos.X + DaLibConstants.HALF_TILE_WIDTH;
        var tileCenterY = tileWorldPos.Y + DaLibConstants.HALF_TILE_HEIGHT;

        var textureBottom = 0;

        switch (entity.Type)
        {
            case ClientEntityType.Aisling:
                textureBottom = DrawAisling(
                    spriteBatch,
                    entity,
                    tileCenterX,
                    tileCenterY);

                break;

            case ClientEntityType.Creature:
                textureBottom = DrawCreature(
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

                return; // Ground items don't get hitboxes
        }

        if (textureBottom <= 0)
            return;

        // Hitbox: 28px wide centered on tile screen X, 60px tall bottom-aligned to texture bottom
        var tileScreenPos = Camera.WorldToScreen(new Vector2(tileCenterX + entity.VisualOffset.X, tileCenterY + entity.VisualOffset.Y));
        var hitboxX = (int)tileScreenPos.X - HITBOX_WIDTH / 2;
        var hitboxY = textureBottom - HITBOX_HEIGHT;

        EntityHitBoxes.Add(
            new EntityHitBox(
                entity.Id,
                new Rectangle(
                    hitboxX,
                    hitboxY,
                    HITBOX_WIDTH,
                    HITBOX_HEIGHT)));
    }

    /// <summary>
    ///     Draws a creature entity. Returns the screen-space Y of the texture bottom edge, or 0 if not drawn.
    /// </summary>
    private int DrawCreature(
        SpriteBatch spriteBatch,
        WorldEntity entity,
        float tileCenterX,
        float tileCenterY)
    {
        var creatureRenderer = Game.CreatureRenderer;
        var animInfo = creatureRenderer.GetAnimInfo(entity.SpriteId);

        if (animInfo is null)
            return 0;

        var info = animInfo.Value;
        (var frameIndex, var flip) = AnimationManager.GetCreatureFrame(entity, in info);

        var spriteFrame = creatureRenderer.GetFrame(entity.SpriteId, frameIndex);

        if (spriteFrame is null)
            return 0;

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

        var shouldTint = Highlight.ShowTintHighlight && (Highlight.HoveredEntityId == entity.Id);
        var drawTexture = shouldTint ? GetOrCreateTintedTexture(frame.Texture, entity.Id) : frame.Texture;

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

        return (int)screenPos.Y + frame.Texture.Height;
    }

    /// <summary>
    ///     Draws an aisling entity. Returns the screen-space Y of the texture bottom edge, or 0 if not drawn.
    /// </summary>
    private int DrawAisling(
        SpriteBatch spriteBatch,
        WorldEntity entity,
        float tileCenterX,
        float tileCenterY)
    {
        // Morphed aislings (creature form) render as creatures
        if (entity.Appearance is null && (entity.SpriteId > 0))
            return DrawCreature(
                spriteBatch,
                entity,
                tileCenterX,
                tileCenterY);

        if (entity.Appearance is null)
            return 0;

        var appearance = entity.Appearance.Value;
        (var frameIndex, var flip, var animSuffix, var isFrontFacing) = AnimationManager.GetAislingFrame(entity);

        var emotionFrame = entity.ActiveEmoteFrame;

        // Check cache — re-resolve layer frames if appearance, frame, flip, animation suffix, or emotion changed.
        // Individual layer textures are cached globally in AislingRenderer — this just tracks which draw data is current.
        if (!AislingCache.TryGetValue(entity.Id, out var cached)
            || (cached.Appearance != appearance)
            || (cached.FrameIndex != frameIndex)
            || (cached.Flip != flip)
            || (cached.IsFrontFacing != isFrontFacing)
            || (cached.AnimSuffix != animSuffix)
            || (cached.EmotionFrame != emotionFrame))
        {
            var drawData = Game.AislingRenderer.GetLayerFrames(
                in appearance,
                frameIndex,
                animSuffix,
                flip,
                isFrontFacing,
                emotionFrame);

            if (!drawData.Layers[(int)LayerSlot.Body].HasValue)
                return 0;

            cached = new AislingDrawDataEntry(
                appearance,
                frameIndex,
                flip,
                isFrontFacing,
                animSuffix,
                emotionFrame,
                drawData);
            AislingCache[entity.Id] = cached;
        }

        var cachedDrawData = cached.DrawData!;

        // Base position: composite canvas origin relative to tile center
        var baseX = tileCenterX + entity.VisualOffset.X - BODY_CENTER_X;
        var baseY = tileCenterY + entity.VisualOffset.Y - BODY_CENTER_Y;
        var flipPivot = AislingRenderer.BODY_CENTER_X + AislingRenderer.LAYER_OFFSET_PADDING;

        var isHighlighted = Highlight.ShowTintHighlight && (Highlight.HoveredEntityId == entity.Id);

        // Transparent aislings use the pre-composited render target for uniform alpha (no layer bleed-through)
        if (entity.IsTransparent
            && SilhouetteRenderer.DrawTransparentEntity(
                spriteBatch,
                entity,
                Camera,
                MapFile!.Height,
                BODY_CENTER_X,
                BODY_CENTER_Y))
        {
            var bodyScreenPos2 = Camera.WorldToScreen(new Vector2(baseX, baseY));

            return (int)(bodyScreenPos2.Y + AislingRenderer.COMPOSITE_HEIGHT);
        }

        // Draw each layer in order
        foreach (var slot in cachedDrawData.DrawOrder)
        {
            if (cachedDrawData.Layers[(int)slot] is not { } layer)
                continue;

            var layerOffsetX = AislingRenderer.GetLayerOffsetX(layer.TypeLetter) + AislingRenderer.LAYER_OFFSET_PADDING;

            float compositeX;

            if (cachedDrawData.FlipHorizontal)
                compositeX = 2 * flipPivot - layerOffsetX - layer.Texture.Width;
            else
                compositeX = layerOffsetX;

            var worldPos = new Vector2(baseX + compositeX, baseY);
            var screenPos = Camera.WorldToScreen(worldPos);
            var effects = cachedDrawData.FlipHorizontal ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            var drawTexture = isHighlighted ? Game.AislingRenderer.GetOrCreateTintedTexture(layer.Texture) : layer.Texture;

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

        // Return bottom edge for hitbox calculation (body layer defines the aisling's visual bounds)
        var bodyScreenPos = Camera.WorldToScreen(new Vector2(baseX, baseY));

        return (int)bodyScreenPos.Y + AislingRenderer.COMPOSITE_HEIGHT;
    }

    /// <summary>
    ///     Creates a CPU-side tinted copy of a texture using the original DA client's highlight color transform.
    /// </summary>
    private Texture2D CreateTintedTexture(Texture2D source) => TextureConverter.CreateTintedTexture(source);

    /// <summary>
    ///     Returns a tinted texture for the given source, caching it for the current highlighted entity. Regenerates when the
    ///     entity or source texture changes.
    /// </summary>
    private Texture2D? GetOrCreateTintedTexture(Texture2D source, uint entityId)
        => Highlight.GetOrCreateTinted(source, entityId, CreateTintedTexture);

    private void ClearHighlightCache()
    {
        Highlight.ClearTint();
        Game.AislingRenderer.ClearTintedCache();
    }

    private void DrawEntityEffects(SpriteBatch spriteBatch, WorldEntity entity)
    {
        if (MapFile is null)
            return;

        var tileWorldPos = Camera.TileToWorld(entity.TileX, entity.TileY, MapFile.Height);
        var tileCenterX = tileWorldPos.X + DaLibConstants.HALF_TILE_WIDTH;
        var tileCenterY = tileWorldPos.Y + DaLibConstants.HALF_TILE_HEIGHT;

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
        var sprite = Game.ItemRenderer.GetSprite(entity.SpriteId, entity.ItemColor);

        if (sprite is null)
            return;

        var texture = sprite.Value.Texture;

        // Center the visual content (not the canvas) on the tile
        // The texture includes Left/Top transparent padding from SimpleRender,
        // so the content center is at (Left + PixelWidth/2, Top + PixelHeight/2)
        var contentWidth = texture.Width - sprite.Value.FrameLeft;
        var contentHeight = texture.Height - sprite.Value.FrameTop;
        var contentCenterX = sprite.Value.FrameLeft + contentWidth / 2f;
        var contentCenterY = sprite.Value.FrameTop + contentHeight / 2f;
        var drawX = tileCenterX - contentCenterX;
        var drawY = tileCenterY - contentCenterY;
        var screenPos = Camera.WorldToScreen(new Vector2(drawX, drawY));

        spriteBatch.Draw(texture, screenPos, Color.White);
    }

    private void UpdateChatBubbles(GameTime gameTime, InputBuffer input)
    {
        List<uint>? expired = null;

        foreach ((var entityId, var bubble) in ChatBubbles)
        {
            bubble.Update(gameTime, input);

            if (bubble.IsExpired)
            {
                (expired ??= []).Add(entityId);

                continue;
            }

            if (MapFile is null)
                continue;

            var entity = Game.World.GetEntity(entityId);

            if (entity is null)
                continue;

            var tileWorld = Camera.TileToWorld(entity.TileX, entity.TileY, MapFile.Height);
            var tileCenterX = tileWorld.X + DaLibConstants.HALF_TILE_WIDTH;
            var tileCenterY = tileWorld.Y + DaLibConstants.HALF_TILE_HEIGHT;

            var entityWorldX = tileCenterX + entity.VisualOffset.X;
            var entityWorldY = tileCenterY + entity.VisualOffset.Y - 64;

            var bubbleX = entityWorldX - bubble.Width / 2f;
            var bubbleY = entityWorldY - bubble.Height;

            var screenPos = Camera.WorldToScreen(new Vector2(bubbleX, bubbleY));
            bubble.X = (int)screenPos.X;
            bubble.Y = (int)screenPos.Y;
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
        foreach (var bubble in ChatBubbles.Values)
            bubble.Draw(spriteBatch);
    }

    private void UpdateHealthBars(GameTime gameTime)
    {
        List<uint>? expired = null;

        foreach ((var entityId, var bar) in HealthBars)
        {
            bar.Update(gameTime, Game.Input);

            if (bar.IsExpired)
            {
                (expired ??= []).Add(entityId);

                continue;
            }

            if (MapFile is null)
                continue;

            var entity = Game.World.GetEntity(entityId);

            if (entity is null)
            {
                (expired ??= []).Add(entityId);

                continue;
            }

            var tileWorld = Camera.TileToWorld(entity.TileX, entity.TileY, MapFile.Height);
            var entityWorldX = tileWorld.X + DaLibConstants.HALF_TILE_WIDTH + entity.VisualOffset.X;
            var entityWorldY = tileWorld.Y + DaLibConstants.HALF_TILE_HEIGHT + entity.VisualOffset.Y - HEALTH_BAR_Y_OFFSET;

            var screenPos = Camera.WorldToScreen(new Vector2(entityWorldX - bar.Width / 2f, entityWorldY));
            bar.X = (int)screenPos.X + 1;
            bar.Y = (int)screenPos.Y;
        }

        if (expired is not null)
            foreach (var id in expired)
            {
                HealthBars[id]
                    .Dispose();
                HealthBars.Remove(id);
            }
    }

    private void DrawHealthBars(SpriteBatch spriteBatch)
    {
        foreach (var bar in HealthBars.Values)
            bar.Draw(spriteBatch);
    }

    private void DrawNameTags(SpriteBatch spriteBatch)
    {
        if (MapFile is null)
            return;

        var entities = Game.World.GetSortedEntities();

        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];

            if (entity.Type != ClientEntityType.Aisling)
                continue;

            if (string.IsNullOrEmpty(entity.Name))
                continue;

            // NeutralHover/FriendlyHover: only show on hover, and not during targeting/dragging
            var isHoverOnly = entity.NameTagStyle is NameTagStyle.NeutralHover or NameTagStyle.FriendlyHover;

            if (isHoverOnly && (Highlight.ShowTintHighlight || (Highlight.HoveredEntityId != entity.Id)))
                continue;

            var nameColor = entity.NameTagStyle switch
            {
                NameTagStyle.Hostile       => new Color(255, 128, 0),
                NameTagStyle.FriendlyHover => Color.LimeGreen,
                _                          => Color.White
            };

            if (!NameTagCache.TryGetValue(entity.Id, out var cachedText))
            {
                cachedText = new CachedText();
                NameTagCache[entity.Id] = cachedText;
            }

            cachedText.UpdateShadowed(entity.Name, nameColor, NAME_TAG_SHADOW_COLOR);

            if (cachedText.Texture is null)
                continue;

            var tileWorld = Camera.TileToWorld(entity.TileX, entity.TileY, MapFile.Height);
            var entityWorldX = tileWorld.X + DaLibConstants.HALF_TILE_WIDTH + entity.VisualOffset.X;
            var entityWorldY = tileWorld.Y + DaLibConstants.HALF_TILE_HEIGHT + entity.VisualOffset.Y - NAME_TAG_Y_OFFSET;
            var screenPos = Camera.WorldToScreen(new Vector2(entityWorldX - cachedText.Texture.Width / 2f, entityWorldY));

            cachedText.Draw(spriteBatch, screenPos);
        }
    }

    private void UpdateChantOverlays(GameTime gameTime, InputBuffer input)
    {
        List<uint>? expired = null;

        foreach ((var entityId, var overlay) in ChantOverlays)
        {
            overlay.Update(gameTime, input);

            if (overlay.IsExpired)
            {
                (expired ??= []).Add(entityId);

                continue;
            }

            if (MapFile is null)
                continue;

            var entity = Game.World.GetEntity(entityId);

            if (entity is null)
                continue;

            var tileWorld = Camera.TileToWorld(entity.TileX, entity.TileY, MapFile.Height);
            var tileCenterX = tileWorld.X + DaLibConstants.HALF_TILE_WIDTH;
            var tileCenterY = tileWorld.Y + DaLibConstants.HALF_TILE_HEIGHT;

            var entityWorldX = tileCenterX + entity.VisualOffset.X;
            var entityWorldY = tileCenterY + entity.VisualOffset.Y - 60;

            var overlayX = entityWorldX - overlay.Width / 2f;
            var overlayY = entityWorldY - overlay.Height;

            var screenPos = Camera.WorldToScreen(new Vector2(overlayX, overlayY));
            overlay.X = (int)screenPos.X;
            overlay.Y = (int)screenPos.Y;
        }

        if (expired is not null)
            foreach (var id in expired)
            {
                ChantOverlays[id]
                    .Dispose();
                ChantOverlays.Remove(id);
            }
    }

    private void DrawChantOverlays(SpriteBatch spriteBatch)
    {
        foreach (var overlay in ChantOverlays.Values)
            overlay.Draw(spriteBatch);
    }

    private void DrawWorldDebug(SpriteBatch spriteBatch)
    {
        if (MapFile is null)
            return;

        PendingDebugLabels.Clear();

        var pixel = UIElement.GetPixel();

        // Foreground tile hitboxes (doors, interactive objects)
        (var fgMinX, var fgMinY, var fgMaxX, var fgMaxY) = Camera.GetVisibleTileBounds(
            MapFile.Width,
            MapFile.Height,
            MapRenderer.ForegroundExtraMargin);

        for (var tileY = fgMinY; tileY <= fgMaxY; tileY++)
            for (var tileX = fgMinX; tileX <= fgMaxX; tileX++)
            {
                var tile = MapFile.Tiles[tileX, tileY];

                if (tile is { LeftForeground: 0, RightForeground: 0 })
                    continue;

                var tileWorld = Camera.TileToWorld(tileX, tileY, MapFile.Height);
                var topLeft = Camera.WorldToScreen(new Vector2(tileWorld.X, tileWorld.Y));

                var tileRect = new Rectangle(
                    (int)topLeft.X,
                    (int)topLeft.Y,
                    DaLibConstants.HALF_TILE_WIDTH * 2,
                    DaLibConstants.HALF_TILE_HEIGHT * 2);

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
            var tileCenterX = tileWorld.X + DaLibConstants.HALF_TILE_WIDTH;

            // Draw tile hitbox (the isometric diamond as a screen-space rect)
            var topLeft = Camera.WorldToScreen(new Vector2(tileWorld.X, tileWorld.Y));

            var tileRect = new Rectangle(
                (int)topLeft.X,
                (int)topLeft.Y,
                DaLibConstants.HALF_TILE_WIDTH * 2,
                DaLibConstants.HALF_TILE_HEIGHT * 2);

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

            // Entity name/info label (cached, deferred to draw after all pixel-texture geometry)
            var label = $"{entity.Name} [{entity.Id}] ({entity.TileX},{entity.TileY})";

            if (!DebugLabelCache.TryGetValue(entity.Id, out var cachedLabel))
            {
                cachedLabel = new CachedText();
                DebugLabelCache[entity.Id] = cachedLabel;
            }

            cachedLabel.Update(label, color);

            if (cachedLabel.Texture is not null)
            {
                var labelPos = Camera.WorldToScreen(new Vector2(tileCenterX - cachedLabel.Texture.Width / 2f, tileWorld.Y - 12));
                PendingDebugLabels.Add((cachedLabel, labelPos));
            }
        }

        // Draw player position crosshair
        var player = Game.World.GetPlayerEntity();

        if (player is not null)
        {
            var playerWorld = Camera.TileToWorld(player.TileX, player.TileY, MapFile.Height);

            var playerCenter = Camera.WorldToScreen(
                new Vector2(
                    playerWorld.X + DaLibConstants.HALF_TILE_WIDTH + player.VisualOffset.X,
                    playerWorld.Y + DaLibConstants.HALF_TILE_HEIGHT + player.VisualOffset.Y));

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
                DaLibConstants.HALF_TILE_WIDTH * 2,
                DaLibConstants.HALF_TILE_HEIGHT * 2);

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

        // Entity hitboxes (the click-detection rects, not the tile rects)
        foreach (var hitbox in EntityHitBoxes)
        {
            var rect = hitbox.ScreenRect;

            spriteBatch.Draw(
                pixel,
                new Rectangle(
                    rect.X,
                    rect.Y,
                    rect.Width,
                    1),
                Color.Orange * 0.8f);

            spriteBatch.Draw(
                pixel,
                new Rectangle(
                    rect.X,
                    rect.Bottom - 1,
                    rect.Width,
                    1),
                Color.Orange * 0.8f);

            spriteBatch.Draw(
                pixel,
                new Rectangle(
                    rect.X,
                    rect.Y,
                    1,
                    rect.Height),
                Color.Orange * 0.8f);

            spriteBatch.Draw(
                pixel,
                new Rectangle(
                    rect.Right - 1,
                    rect.Y,
                    1,
                    rect.Height),
                Color.Orange * 0.8f);
        }

        // Deferred entity debug labels — drawn after all pixel-texture geometry to minimize batch breaks
        foreach ((var text, var pos) in PendingDebugLabels)
            text.Draw(spriteBatch, pos);
    }

    /// <summary>
    ///     Creates a texture containing a dashed ellipse inscribed in the isometric tile diamond. Gaps at the 4 cardinal
    ///     directions (top, right, bottom, left of the ellipse).
    /// </summary>
    private static Texture2D CreateTileCursorTexture(GraphicsDevice device, Color color)
    {
        const int WIDTH = DaLibConstants.HALF_TILE_WIDTH * 2; // 56
        const int HEIGHT = DaLibConstants.HALF_TILE_HEIGHT * 2; // 28

        var pixels = new Color[WIDTH * HEIGHT];

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

    private PanelBase? GetDraggingPanel()
    {
        if (WorldHud.Inventory.IsDragging)
            return WorldHud.Inventory;

        if (WorldHud.SkillBook.IsDragging)
            return WorldHud.SkillBook;

        if (WorldHud.SkillBookAlt.IsDragging)
            return WorldHud.SkillBookAlt;

        if (WorldHud.SpellBook.IsDragging)
            return WorldHud.SpellBook;

        if (WorldHud.SpellBookAlt.IsDragging)
            return WorldHud.SpellBookAlt;

        return null;
    }

    private void DrawDragIcon(SpriteBatch spriteBatch)
    {
        var dragging = GetDraggingPanel();

        if (dragging?.DragTexture is not { } icon)
            return;

        spriteBatch.Draw(icon, new Vector2(dragging.DragX - icon.Width / 2.0f, dragging.DragY - icon.Height / 2.0f), Color.White * 0.7f);
    }

    private void DrawTileCursor(SpriteBatch spriteBatch)
    {
        if (MapFile is null || TileCursorTexture is null)
            return;

        var input = Game.Input;
        var viewport = WorldHud.ViewportBounds;

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

        var cursorTexture = GetDraggingPanel() is not null ? TileCursorDragTexture : TileCursorTexture;
        spriteBatch.Draw(cursorTexture!, new Vector2((int)tileScreen.X, (int)tileScreen.Y), Color.White);
    }
    #endregion

    #region Map Assembly
    private void HandleUserId(uint id) => Game.World.PlayerEntityId = id;

    private void HandleMapInfo(MapInfoArgs args)
    {
        // Same map (refresh) — skip expensive teardown, just clear transient entity state
        if ((args.MapId == CurrentMapId) && MapFile is not null)
        {
            ClearTransientState();
            UpdateHuds(h => h.SetZoneName(args.Name));

            return;
        }

        // New map — dispose old caches, load fresh MapFile from local files
        MapRenderer.Dispose();
        MapRenderer = new MapRenderer();
        MapFile = LoadMapFile(args.MapId, args.Width, args.Height);
        MapPreloaded = false;
        CurrentMapId = args.MapId;

        // Clear entity + renderer caches for the new map
        ClearTransientState();
        Game.CreatureRenderer.Clear();
        Game.AislingRenderer.ClearCache();
        Game.AislingRenderer.ClearLayerCache();
        Game.ItemRenderer.Clear();

        // Reset darkness state and load HEA light map for the new map
        DarknessRenderer.OnMapChanged(args.MapId);

        UpdateHuds(h => h.SetZoneName(args.Name));
    }

    private void ClearTransientState()
    {
        Game.World.Clear();
        ClearAislingCache();
        ClearChatBubbles();
        ClearHealthBars();
        ClearChantOverlays();
        ClearNameTagCache();
        ClearDebugLabelCache();
        NpcDialog.Hide();
        MerchantDialog.Hide();
        Pathfinding.Clear();
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
            TabMapRenderer.Generate(Device, MapFile);
            MapPathfinder = BuildPathfinder(MapFile);
            MapPreloaded = true;
        }

        FollowPlayerCamera();
    }

    private static Pathfinder BuildPathfinder(MapFile mapFile)
    {
        var sotpData = DataContext.Tiles.SotpData;
        var walls = new List<IPoint>();

        for (var y = 0; y < mapFile.Height; y++)
            for (var x = 0; x < mapFile.Width; x++)
            {
                var tile = mapFile.Tiles[x, y];

                if (IsTileWall(tile.LeftForeground, sotpData) || IsTileWall(tile.RightForeground, sotpData))
                    walls.Add(new Geometry.Point(x, y));
            }

        return new Pathfinder(
            new GridDetails
            {
                Width = mapFile.Width,
                Height = mapFile.Height,
                Walls = walls,
                BlockingReactors = []
            });
    }

    private bool TileHasForeground(int tileX, int tileY)
    {
        if (MapFile is null)
            return false;

        if ((tileX < 0) || (tileY < 0) || (tileX >= MapFile.Width) || (tileY >= MapFile.Height))
            return false;

        var tile = MapFile.Tiles[tileX, tileY];

        return tile.LeftForeground.IsRenderedTileIndex() || tile.RightForeground.IsRenderedTileIndex();
    }

    private static bool IsTileWall(int fgIndex, byte[] sotpData)
    {
        if (fgIndex <= 0)
            return false;

        var sotpIndex = fgIndex - 1;

        if (sotpIndex >= sotpData.Length)
            return false;

        return ((TileFlags)sotpData[sotpIndex]).HasFlag(TileFlags.Wall);
    }

    private bool IsTilePassable(int tileX, int tileY)
    {
        if (MapFile is null)
            return true;

        // Check wall tiles (foreground SOTP data)
        var tile = MapFile.Tiles[tileX, tileY];
        var sotpData = DataContext.Tiles.SotpData;

        if (IsTileWall(tile.LeftForeground, sotpData) || IsTileWall(tile.RightForeground, sotpData))
            return false;

        // Check entities at the destination tile
        if (Game.World.HasBlockingEntityAt(tileX, tileY, Game.World.PlayerEntityId))
            return false;

        return true;
    }

    private static MapFile? LoadMapFile(int mapId, int width, int height)
    {
        var key = $"lod{mapId}";

        return DataContext.MapsFiles.GetMapFile(key, width, height);
    }

    private void HandleLocationChanged(int x, int y) => UpdateHuds(h => h.SetCoords(x, y));

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
        {
            UpdateHuds(h => h.SetPlayerName(args.Name));
            WorldList.PlayerName = args.Name;
            Exchange.PlayerName = args.Name;
            UpdateHuds(h => h.SetServerName(Game.Connection.ServerName));
            DataContext.PlayerData.Initialize(args.Name);
            LoadPlayerFamilyList();
            LoadPlayerFriendList();
            LoadPlayerMacros();
            Game.World.ReloadChants();
        }

        // Check for idle animation ("04") frames on this aisling's body
        var entity = Game.World.GetEntity(args.Id);

        if (entity?.Appearance is { } appearance)
        {
            entity.IdleAnimFrameCount = Game.AislingRenderer.GetIdleAnimFrameCount(in appearance);

            // Start idle cycling if entity is currently idle
            if (entity.AnimState == EntityAnimState.Idle)
                AnimationManager.ResetToIdle(entity);
        }
    }

    private void HandleRemoveEntity(uint id)
    {
        // Capture creature sprite for death dissolve before removing from WorldState
        var entity = Game.World.GetEntity(id);

        if (entity is { Type: ClientEntityType.Creature })
            CreateDyingEffect(entity);

        // Clean up aisling draw data cache (layer textures are owned by AislingRenderer)
        AislingCache.Remove(id);

        // Clean up cached name tag texture
        if (NameTagCache.Remove(id, out var nameTag))
            nameTag.Dispose();

        // Clean up cached debug label texture
        if (DebugLabelCache.Remove(id, out var debugLabel))
            debugLabel.Dispose();

        // Remove entity from WorldState (ChaosGame skips removal when WorldScreen is active)
        Game.World.RemoveEntity(id);
    }

    private void CreateDyingEffect(WorldEntity entity)
    {
        var creatureRenderer = Game.CreatureRenderer;
        var animInfo = creatureRenderer.GetAnimInfo(entity.SpriteId);

        if (animInfo is null)
            return;

        var info = animInfo.Value;
        (var frameIndex, var flip) = AnimationManager.GetCreatureFrame(entity, in info);

        var spriteFrame = creatureRenderer.GetFrame(entity.SpriteId, frameIndex);

        if (spriteFrame is null)
            return;

        var frame = spriteFrame.Value;

        var dyingEffect = new DyingEffect(
            Device,
            frame.Texture,
            entity.TileX,
            entity.TileY,
            frame.CenterX,
            frame.CenterY,
            frame.Left,
            frame.Top,
            flip);

        Game.World.DyingEffects.Add(dyingEffect);
    }

    /// <summary>
    ///     Client-side prediction: sends Walk packet and immediately starts the walk animation locally without waiting for
    ///     server confirmation. The server response reconciles position if needed.
    /// </summary>
    private void PredictAndWalk(WorldEntity player, Direction direction)
    {
        // Bounds check — don't walk off the map edge
        (var dx, var dy) = direction.ToTileOffset();
        var newX = player.TileX + dx;
        var newY = player.TileY + dy;

        if (MapFile is null || (newX < 0) || (newY < 0) || (newX >= MapFile.Width) || (newY >= MapFile.Height))
            return;

        // Collision check — GM bypasses all collision
        if (!IsGameMaster && !IsTilePassable(newX, newY))
            return;

        Game.Connection.Walk(direction);

        // Predict position locally
        player.TileX = newX;
        player.TileY = newY;

        var walkFrames = player.UsesCreatureWalkTiming ? Game.CreatureRenderer.GetWalkFrameCount(player.SpriteId) : null;

        AnimationManager.StartWalk(
            player,
            direction,
            player.UsesCreatureWalkTiming,
            true,
            walkFrames);
        UpdateHuds(h => h.SetCoords(player.TileX, player.TileY));
    }

    private void HandleClientWalkResponse(Direction direction, int oldX, int oldY)
    {
        // Server confirmation — position was already predicted locally by PredictAndWalk.
        // Reconcile if the server position differs from our prediction.
        var player = Game.World.GetPlayerEntity();

        if (player is null)
            return;

        (var dx, var dy) = direction.ToTileOffset();
        var serverX = oldX + dx;
        var serverY = oldY + dy;

        // If prediction was wrong (e.g. server denied the walk), snap to server position and cancel pathfinding
        if ((player.TileX != serverX) || (player.TileY != serverY))
        {
            player.TileX = serverX;
            player.TileY = serverY;
            UpdateHuds(h => h.SetCoords(serverX, serverY));
            Pathfinding.Clear();
        }
    }

    private void HandleAttributes(AttributesArgs args)
    {
        IsGameMaster = args.StatUpdateType.HasFlag(StatUpdateType.GameMasterA) || args.StatUpdateType.HasFlag(StatUpdateType.GameMasterB);
    }

    private void HandleDisplayPublicMessage(DisplayPublicMessageArgs args)
    {
        var entityExists = Game.World.GetEntity(args.SourceId) is not null;

        if (args.PublicMessageType == PublicMessageType.Chant)
        {
            // Chant: plain blue text above entity (no bubble, no chat panel)
            if (ChantOverlays.TryGetValue(args.SourceId, out var existingChant))
            {
                existingChant.Dispose();
                ChantOverlays.Remove(args.SourceId);
            }

            // Blank chant clears the overlay without creating a new one
            if (entityExists && !string.IsNullOrEmpty(args.Message))
            {
                // Chant replaces any active chat bubble
                if (ChatBubbles.TryGetValue(args.SourceId, out var existingBubble))
                {
                    existingBubble.Dispose();
                    ChatBubbles.Remove(args.SourceId);
                }

                ChantOverlays[args.SourceId] = ChantOverlay.Create(args.SourceId, args.Message);
            }

            return;
        }

        var color = args.PublicMessageType switch
        {
            PublicMessageType.Shout => Color.Yellow,
            _                       => Color.White
        };

        Game.World.Chat.AddMessage(args.Message, color);

        if (!entityExists)
            return;

        var isShout = args.PublicMessageType == PublicMessageType.Shout;

        // Chat bubble replaces any active chant overlay
        if (ChantOverlays.TryGetValue(args.SourceId, out var existingChantForBubble))
        {
            existingChantForBubble.Dispose();
            ChantOverlays.Remove(args.SourceId);
        }

        if (ChatBubbles.TryGetValue(args.SourceId, out var existing))
            existing.Dispose();

        ChatBubbles[args.SourceId] = ChatBubble.Create(args.SourceId, args.Message, isShout);
    }

    private void HandleServerMessage(ServerMessageArgs args)
    {
        switch (args.ServerMessageType)
        {
            case ServerMessageType.Whisper:
                Game.World.Chat.AddMessage(args.Message, new Color(100, 149, 237));

                break;

            case ServerMessageType.GroupChat:
                Game.World.Chat.AddMessage(args.Message, new Color(154, 205, 50));

                break;

            case ServerMessageType.GuildChat:
                Game.World.Chat.AddMessage(args.Message, new Color(128, 128, 0));

                break;

            case ServerMessageType.OrangeBar1
                 or ServerMessageType.OrangeBar2
                 or ServerMessageType.ActiveMessage
                 or ServerMessageType.OrangeBar3
                 or ServerMessageType.AdminMessage
                 or ServerMessageType.OrangeBar5:
                Game.World.Chat.AddOrangeBarMessage(args.Message);

                break;

            case ServerMessageType.PersistentMessage:
                UpdateHuds(h => h.ShowPersistentMessage(args.Message));

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
                Game.World.Chat.AddOrangeBarMessage(args.Message);

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
            var isOn = stateStr.StartsWithI("ON");

            SettingsDialog.SetSettingName(optionIndex, name);
            SettingsDialog.SetSettingValue(optionIndex, isOn);
        }
    }

    private void HandleInventorySlotClicked(byte slot) => Game.Connection.UseItem(slot);

    private void HandleInventoryHoverEnter(PanelSlot slot)
    {
        HoveredInventorySlot = slot;

        ItemTooltip.Show(
            slot.SlotName ?? string.Empty,
            slot.CurrentDurability,
            slot.MaxDurability,
            Game.Input.MouseX + 15,
            Game.Input.MouseY + 15);
    }

    private void HandleInventoryHoverExit()
    {
        HoveredInventorySlot = null;
        ItemTooltip.Hide();
    }

    private void HandleSkillSlotClicked(byte slot)
    {
        var skillSlot = WorldHud.SkillBook.GetSkillSlot(slot) ?? WorldHud.SkillBookAlt.GetSkillSlot(slot);

        if (skillSlot is not null && (skillSlot.CooldownPercent > 0))
            return;

        // Send chant line if one is set for this skill
        if (skillSlot is not null && !string.IsNullOrEmpty(skillSlot.Chant))
            Game.Connection.SendChant(skillSlot.Chant);

        Game.Connection.UseSkill(slot);
    }

    private void HandleSpellSlotClicked(byte slot)
    {
        // Determine which panel the slot came from
        var spellSlot = WorldHud.ActiveTab switch
        {
            HudTab.Spells    => WorldHud.SpellBook.GetSpellSlot(slot),
            HudTab.SpellsAlt => WorldHud.SpellBookAlt.GetSpellSlot(slot),
            _                => WorldHud.SpellBook.GetSpellSlot(slot) ?? WorldHud.SpellBookAlt.GetSpellSlot(slot)
        };

        if (spellSlot is null || string.IsNullOrEmpty(spellSlot.AbilityName))
            return;

        if (spellSlot.CooldownPercent > 0)
            return;

        // NoTarget spells cast immediately (no cast mode)
        if (spellSlot.SpellType == SpellType.NoTarget)
        {
            if (spellSlot.CastLines == 0)
                Game.Connection.UseSpell(slot);
            else
            {
                // NoTarget with lines: begin chant sequence targeting self
                CastingManager.BeginTargeting(spellSlot);

                var player = Game.World.GetPlayerEntity();

                CastingManager.SelectTarget(
                    Game.Connection.AislingId,
                    player?.TileX ?? 0,
                    player?.TileY ?? 0,
                    Game.Connection);
            }

            return;
        }

        // Enter cast mode — wait for target selection
        CastingManager.BeginTargeting(spellSlot);
    }

    private void HandleInventoryDropInViewport(byte slot, int mouseX, int mouseY)
    {
        // Dropped onto an equipment slot — equip the item
        if ((slot != 0) && StatusBook.Visible && StatusBook.ContainsEquipmentSlotPoint(mouseX, mouseY))
        {
            Game.Connection.UseItem(slot);

            return;
        }

        var viewport = WorldHud.ViewportBounds;

        // Only drop if released within the world viewport
        if ((mouseX < viewport.X)
            || (mouseX >= (viewport.X + viewport.Width))
            || (mouseY < viewport.Y)
            || (mouseY >= (viewport.Y + viewport.Height)))
            return;

        if (MapFile is null)
            return;

        // Check if dropped on an entity (give item/gold to NPC/player) — skip self (drop on ground instead)
        (var tileX, var tileY) = ScreenToTile(mouseX, mouseY);
        var entity = Game.World.GetEntityAt(tileX, tileY);

        var droppedOnEntity = entity is not null
                              && entity.Type is ClientEntityType.Aisling or ClientEntityType.Creature
                              && (entity.Id != Game.Connection.AislingId);

        // Gold bag (slot 0) — show the gold amount popup
        if (slot == 0)
        {
            GoldDrop.Y = viewport.Y + (viewport.Height - GoldDrop.Height) / 2;
            GoldDrop.ShowForTarget(droppedOnEntity ? entity!.Id : null, tileX, tileY);

            return;
        }

        if (droppedOnEntity)
        {
            Game.Connection.DropItemOnCreature(slot, entity!.Id);

            return;
        }

        // Stackable items — prompt for count before dropping
        var invSlot = Game.World.Inventory.GetSlot(slot);

        if (invSlot.Stackable)
        {
            WorldHud.Prompt.ShowPrompt($"Number of items to drop [ 0 - {(int)invSlot.Count} ]: ");

            var capturedSlot = slot;
            var capturedX = tileX;
            var capturedY = tileY;

            void OnPromptConfirm(string text)
            {
                WorldHud.Prompt.OnConfirm -= OnPromptConfirm;

                if (int.TryParse(text, out var count) && (count > 0))
                    Game.Connection.DropItem(
                        capturedSlot,
                        capturedX,
                        capturedY,
                        count);
            }

            WorldHud.Prompt.OnConfirm += OnPromptConfirm;

            return;
        }

        Game.Connection.DropItem(slot, tileX, tileY);
    }

    private void HandleDialogChanged()
    {
        var dialog = Game.World.NpcInteraction.CurrentDialog;

        // No dialog or CloseDialog type means hide all panels
        if (dialog is null || (dialog.DialogType == DialogType.CloseDialog))
        {
            NpcDialog.Hide();
            MerchantDialog.Hide();

            return;
        }

        // Dialog replaces any open merchant panel
        MerchantDialog.Hide();
        NpcDialog.ShowDialog(dialog);
        RenderNpcPortrait();
    }

    private void HandleMenuChanged()
    {
        var menu = Game.World.NpcInteraction.CurrentMenu;

        if (menu is null)
            return;

        if (menu.MenuType is MenuType.ShowItems
                             or MenuType.ShowPlayerItems
                             or MenuType.ShowSkills
                             or MenuType.ShowSpells
                             or MenuType.ShowPlayerSkills
                             or MenuType.ShowPlayerSpells)
        {
            NpcDialog.Hide();
            MerchantDialog.ShowMerchant(menu, Game.Connection);

            return;
        }

        MerchantDialog.Hide();
        NpcDialog.ShowMenu(menu);
        RenderNpcPortrait();
    }

    private void RenderNpcPortrait()
    {
        if (!NpcDialog.ShouldIllustrate || (NpcDialog.PortraitSpriteId == 0))
        {
            NpcDialog.SetPortrait(null);

            return;
        }

        // Render creature standing frame at direction 0 (front-facing)
        var animInfo = Game.CreatureRenderer.GetAnimInfo(NpcDialog.PortraitSpriteId);

        if (animInfo is null)
        {
            NpcDialog.SetPortrait(null);

            return;
        }

        var standingFrame = Game.CreatureRenderer.GetFrame(NpcDialog.PortraitSpriteId, animInfo.Value.StandingFrameIndex);

        NpcDialog.SetPortrait(standingFrame?.Texture);
    }

    private void ShowStatusBook()
    {
        StatusBook.RefreshEquipment();

        if (Game.World.Attributes.Current is { } attrs)
            StatusBook.UpdateEquipmentStats(
                attrs.Str,
                attrs.Int,
                attrs.Wis,
                attrs.Con,
                attrs.Dex,
                attrs.Ac);

        StatusBook.SwitchTab(StatusBookTab.Equipment);
        StatusBook.Show();
    }

    private void HandleRefreshResponse()
        =>

            // Server acknowledged the refresh request — re-center camera
            FollowPlayerCamera();

    private static readonly Keys[] EmoteKeys =
    [
        Keys.D1,
        Keys.D2,
        Keys.D3,
        Keys.D4,
        Keys.D5,
        Keys.D6,
        Keys.D7,
        Keys.D8,
        Keys.D9,
        Keys.D0,
        Keys.OemMinus
    ];

    // Ctrl+key emotes: 9-17 then 21-22 (skips 18-20 which don't exist in BodyAnimation)
    private static readonly BodyAnimation[] CtrlEmotes =
    [
        BodyAnimation.Smile,
        BodyAnimation.Cry,
        BodyAnimation.Frown,
        BodyAnimation.Wink,
        BodyAnimation.Surprise,
        BodyAnimation.Tongue,
        BodyAnimation.Pleasant,
        BodyAnimation.Snore,
        BodyAnimation.Mouth,
        BodyAnimation.BlowKiss,
        BodyAnimation.Wave
    ];

    private bool HandleEmoteHotkeys(InputBuffer input)
    {
        var ctrl = input.IsKeyHeld(Keys.LeftControl) || input.IsKeyHeld(Keys.RightControl);
        var alt = input.IsKeyHeld(Keys.LeftAlt) || input.IsKeyHeld(Keys.RightAlt);

        if (!ctrl && !alt)
            return false;

        var keyIndex = -1;

        for (var i = 0; i < EmoteKeys.Length; i++)
            if (input.WasKeyPressed(EmoteKeys[i]))
            {
                keyIndex = i;

                break;
            }

        if (keyIndex < 0)
            return false;

        BodyAnimation bodyAnimation;

        if (ctrl && !alt)
            bodyAnimation = CtrlEmotes[keyIndex];
        else if (ctrl && alt)
            bodyAnimation = (BodyAnimation)(23 + keyIndex);
        else
            bodyAnimation = (BodyAnimation)(34 + keyIndex);

        Game.Connection.SendEmote(bodyAnimation);

        return true;
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

        switch (WorldHud.ActiveTab)
        {
            case HudTab.Inventory:
                Game.Connection.UseItem(byteSlot);

                break;

            case HudTab.Skills:
                HandleSkillSlotClicked(byteSlot);

                break;

            case HudTab.SkillsAlt:
                HandleSkillSlotClicked((byte)(byteSlot + 36));

                break;

            case HudTab.Spells:
                HandleSpellSlotClicked(byteSlot);

                break;

            case HudTab.SpellsAlt:
                HandleSpellSlotClicked((byte)(byteSlot + 36));

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
        WorldHud.ChatInput.FocusedBackgroundColor = new Color(
            0,
            0,
            0,
            128);
        WorldHud.ChatInput.IsFocused = true;
        WorldHud.ChatInput.Prefix = prefix;
        WorldHud.ChatInput.TextColor = textColor;
        WorldHud.SetDescription(null);
    }

    private void UnfocusChat()
    {
        WorldHud.ChatInput.IsFocused = false;
        WorldHud.ChatInput.Text = string.Empty;
        WorldHud.ChatInput.Prefix = string.Empty;
        WorldHud.ChatInput.TextColor = Color.White;
    }

    private void DispatchChatMessage(string message)
    {
        var prefix = WorldHud.ChatInput.Prefix;

        if (prefix.EndsWithI("! "))
            Game.Connection.SendShout(message);
        else if (prefix.StartsWithI("-> ") && prefix.EndsWithI(": "))
        {
            // Whisper phase 2: prefix is "-> targetName: "
            var targetName = prefix[3..^2];
            Game.Connection.SendWhisper(targetName, message);
        } else if (TryDispatchChatCommand(message))
        {
            // Handled as a slash command
        } else
            Game.Connection.SendPublicMessage(message);
    }

    /// <summary>
    ///     Handles slash commands typed in the chat input. Returns true if the message was a recognized command.
    /// </summary>
    private bool TryDispatchChatCommand(string message)
    {
        if (!message.StartsWith('/'))
            return false;

        // /ignore (no args) — show ignore list
        if (message.EqualsI("/ignore"))
        {
            Game.Connection.SendIgnoreRequest();

            return true;
        }

        // /ignore Name — add to ignore list
        if (message.StartsWithI("/ignore "))
        {
            var name = message[8..]
                .Trim();

            if (name.Length > 0)
                Game.Connection.SendAddIgnore(name);

            return true;
        }

        // /unignore Name — remove from ignore list
        if (message.StartsWithI("/unignore "))
        {
            var name = message[10..]
                .Trim();

            if (name.Length > 0)
                Game.Connection.SendRemoveIgnore(name);

            return true;
        }

        return false;
    }

    private void ClearAislingCache()
        =>

            // Layer textures are owned by AislingRenderer.LayerTextureCache — just clear the draw data references
            AislingCache.Clear();

    private void ClearChatBubbles()
    {
        foreach (var bubble in ChatBubbles.Values)
            bubble.Dispose();

        ChatBubbles.Clear();
    }

    private void ClearHealthBars()
    {
        foreach (var bar in HealthBars.Values)
            bar.Dispose();

        HealthBars.Clear();
    }

    private void ClearChantOverlays()
    {
        foreach (var overlay in ChantOverlays.Values)
            overlay.Dispose();

        ChantOverlays.Clear();
    }

    private void ClearNameTagCache()
    {
        foreach (var cached in NameTagCache.Values)
            cached.Dispose();

        NameTagCache.Clear();
    }

    private void ClearDebugLabelCache()
    {
        foreach (var cached in DebugLabelCache.Values)
            cached.Dispose();

        DebugLabelCache.Clear();
    }

    private void LoadPlayerFamilyList()
    {
        var family = DataContext.PlayerData.LoadFamilyList();
        StatusBook.SetFamilyMembers(family);
    }

    private void SavePlayerFamilyList()
    {
        var family = StatusBook.GetFamilyMembers();

        if (family is not null)
            DataContext.PlayerData.SaveFamilyList(family);
    }

    private void LoadPlayerFriendList()
    {
        var names = DataContext.PlayerData.LoadFriendList();

        var entries = names.Select(n => new FriendEntry(n, false))
                           .ToList();
        FriendsList.SetFriends(entries);
    }

    private void SavePlayerFriendList()
    {
        var names = FriendsList.GetFriendNames();
        DataContext.PlayerData.SaveFriendList(names);
    }

    private void LoadPlayerMacros()
    {
        var macros = DataContext.PlayerData.LoadMacros();

        for (var i = 0; i < macros.Length; i++)
            MacroMenu.SetMacro(i, $"F{(i < 9 ? i + 5 : 0)}", macros[i]);
    }

    private void SavePlayerMacros()
    {
        var macros = MacroMenu.GetMacroValues();
        DataContext.PlayerData.SaveMacros(macros);
    }

    private void WireAbilityRightClicks(PanelBase panel)
    {
        for (byte i = 1; i <= 36; i++)
            if (panel.GetSlotControl(i) is AbilitySlotControl ability)
                ability.OnRightClick += OpenChantEdit;
    }

    private void OpenChantEdit(byte slot)
    {
        // Determine which panel this slot belongs to based on active tab
        AbilitySlotControl? abilitySlot = WorldHud.ActiveTab switch
        {
            HudTab.Skills    => WorldHud.SkillBook.GetSkillSlot(slot),
            HudTab.SkillsAlt => WorldHud.SkillBookAlt.GetSkillSlot(slot),
            HudTab.Spells    => WorldHud.SpellBook.GetSpellSlot(slot),
            HudTab.SpellsAlt => WorldHud.SpellBookAlt.GetSpellSlot(slot),
            _                => null
        };

        if (abilitySlot is null || string.IsNullOrEmpty(abilitySlot.AbilityName))
            return;

        var isSpell = abilitySlot is SpellSlot;

        string[] currentChants;
        int lineCount;

        if (abilitySlot is SpellSlot spell)
        {
            currentChants = spell.Chants;
            lineCount = spell.CastLines;
        } else if (abilitySlot is SkillSlot skill)
        {
            currentChants = [skill.Chant];
            lineCount = 1;
        } else
            return;

        ChantEdit.Show(
            slot,
            abilitySlot.AbilityName,
            abilitySlot.AbilityLevel ?? string.Empty,
            abilitySlot.NormalTexture,
            currentChants,
            lineCount,
            isSpell);
    }

    private void HandleChantSet(byte slot, string[] chantLines, bool isSpell)
    {
        if (isSpell)
        {
            foreach (var panel in new[]
                     {
                         WorldHud.SpellBook,
                         WorldHud.SpellBookAlt
                     })
            {
                var spellSlot = panel.GetSpellSlot(slot);

                if (spellSlot is null)
                    continue;

                for (var i = 0; i < Math.Min(chantLines.Length, spellSlot.Chants.Length); i++)
                    spellSlot.Chants[i] = chantLines[i];
            }

            SaveSpellChants();
            Game.World.ReloadChants();
        } else
        {
            SaveSkillChants();
            Game.World.ReloadChants();
        }
    }

    private void SaveSkillChants()
    {
        var entries = new List<SkillChantEntry>();

        for (byte i = 1; i <= 89; i++)
        {
            var slot = WorldHud.SkillBook.GetSkillSlot(i);

            if (slot is null || string.IsNullOrEmpty(slot.AbilityName))
                continue;

            entries.Add(
                new SkillChantEntry
                {
                    Name = slot.AbilityName,
                    Chant = slot.Chant
                });
        }

        DataContext.PlayerData.SaveSkillChants(entries);
    }

    private void SaveSpellChants()
    {
        var entries = new List<SpellChantEntry>();

        for (byte i = 1; i <= 89; i++)
        {
            var slot = WorldHud.SpellBook.GetSpellSlot(i);

            if (slot is null || string.IsNullOrEmpty(slot.AbilityName))
                continue;

            var entry = new SpellChantEntry
            {
                Name = slot.AbilityName
            };
            Array.Copy(slot.Chants, entry.Chants, 10);
            entries.Add(entry);
        }

        DataContext.PlayerData.SaveSpellChants(entries);
    }
    #endregion

    #region Click Handling
    /// <summary>
    ///     Handles left-click within the viewport area — picks the entity at the click position.
    /// </summary>
    private void UpdateDragHighlight(InputBuffer input)
    {
        if (GetDraggingPanel() is null || MapFile is null)
            return;

        var entity = GetEntityAtScreen(input.MouseX, input.MouseY);

        // When dragging inventory items, don't highlight the player (drop goes to ground instead)
        var isItemDrag = WorldHud.Inventory.IsDragging;
        var playerId = Game.Connection.AislingId;

        uint? newHighlight
            = entity?.Type is ClientEntityType.Aisling or ClientEntityType.Creature && !(isItemDrag && (entity.Id == playerId))
                ? entity.Id
                : null;

        if (newHighlight != Highlight.HoveredEntityId)
            ClearHighlightCache();

        Highlight.HoveredEntityId = newHighlight;
    }

    /// <summary>
    ///     Converts screen mouse coordinates to tile coordinates, accounting for the HUD viewport offset. The world is
    ///     rendered with a translation matrix for the viewport origin, so mouse coords must be adjusted to match.
    /// </summary>
    private WorldEntity? GetEntityAtScreen(int mouseX, int mouseY)
    {
        if (MapFile is null)
            return null;

        // Iterate hitboxes back-to-front (last drawn = closest to camera = highest priority)
        for (var i = EntityHitBoxes.Count - 1; i >= 0; i--)
        {
            var hitbox = EntityHitBoxes[i];

            if (hitbox.ScreenRect.Contains(mouseX, mouseY))
                return Game.World.GetEntity(hitbox.EntityId);
        }

        // Fallback: tile-based lookup for ground items
        (var tileX, var tileY) = ScreenToTile(mouseX, mouseY);

        return Game.World.GetGroundItemAt(tileX, tileY);
    }

    private (int TileX, int TileY) ScreenToTile(int mouseX, int mouseY)
    {
        var viewport = WorldHud.ViewportBounds;
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
        (var dx, var dy) = player.Direction.ToTileOffset();
        var frontX = player.TileX + dx;
        var frontY = player.TileY + dy;

        if (Game.World.HasGroundItemAt(frontX, frontY))
            Game.Connection.PickupItem(frontX, frontY, slot);
    }

    private void HandleWorldClick(int mouseX, int mouseY)
    {
        if (MapFile is null)
            return;

        var viewport = WorldHud.ViewportBounds;

        if ((mouseX < viewport.X)
            || (mouseX >= (viewport.X + viewport.Width))
            || (mouseY < viewport.Y)
            || (mouseY >= (viewport.Y + viewport.Height)))
            return;

        (var tileX, var tileY) = ScreenToTile(mouseX, mouseY);

        // Check for double-click (same tile within time window)
        var isDoubleClick = LeftClickTracker.Click(tileX, tileY);

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
        } else if (TileHasForeground(tileX, tileY))
        {
            // Single click: tile interaction (doors, reactor tiles) — only if foreground exists
            Game.Connection.ClickTile(tileX, tileY);
        }
    }

    private void HandleCtrlClick(int mouseX, int mouseY)
    {
        if (MapFile is null)
            return;

        var viewport = WorldHud.ViewportBounds;

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
                ("Click", () => Game.Connection.ClickEntity(entity.Id)),
                ("Ignore", () => Game.Connection.SendAddIgnore(name)));
        }
    }

    private void HandleWorldRightClick(int mouseX, int mouseY)
    {
        if (MapFile is null || MapPathfinder is null)
            return;

        var viewport = WorldHud.ViewportBounds;

        if ((mouseX < viewport.X)
            || (mouseX >= (viewport.X + viewport.Width))
            || (mouseY < viewport.Y)
            || (mouseY >= (viewport.Y + viewport.Height)))
            return;

        var player = Game.World.GetPlayerEntity();

        if (player is null)
            return;

        (var tileX, var tileY) = ScreenToTile(mouseX, mouseY);

        // Clamp to map bounds
        tileX = Math.Clamp(tileX, 0, MapFile.Width - 1);
        tileY = Math.Clamp(tileY, 0, MapFile.Height - 1);

        var isDoubleRightClick = RightClickTracker.Click(tileX, tileY);

        // Don't pathfind to current position
        if ((tileX == player.TileX) && (tileY == player.TileY))
        {
            Pathfinding.Clear();

            return;
        }

        // Double right-click on entity — follow and assail
        if (isDoubleRightClick)
        {
            var entity = Game.World.GetEntityAt(tileX, tileY);

            if (entity is not null && entity.Type is ClientEntityType.Aisling or ClientEntityType.Creature)
            {
                Pathfinding.SetEntityTarget(entity.Id);
                PathfindToEntity(player, entity);

                return;
            }
        }

        // Single right-click — pathfind to ground tile
        Pathfinding.TargetEntityId = null;
        PathfindToTile(player, tileX, tileY);
    }

    private void PathfindToTile(WorldEntity player, int tileX, int tileY)
    {
        if (MapPathfinder is null)
            return;

        var blockedPoints = Game.World.GetBlockedPoints();

        var path = MapPathfinder.FindPath(
            new Geometry.Point(player.TileX, player.TileY),
            new Geometry.Point(tileX, tileY),
            new PathOptions
            {
                BlockedPoints = blockedPoints,
                LimitRadius = null
            });

        Pathfinding.Path = path.Count > 0 ? path : null;
    }

    private void PathfindToEntity(WorldEntity player, WorldEntity target)
    {
        if (MapPathfinder is null)
            return;

        // Already adjacent — no pathfinding needed
        if (IsAdjacent(
                player.TileX,
                player.TileY,
                target.TileX,
                target.TileY))
        {
            Pathfinding.Path = null;

            return;
        }

        // Find path to the best adjacent tile around the target
        var blockedPoints = Game.World.GetBlockedPoints();
        Stack<IPoint>? bestPath = null;

        ReadOnlySpan<(int Dx, int Dy)> adjacentOffsets =
        [
            (0, -1),
            (1, 0),
            (0, 1),
            (-1, 0)
        ];

        foreach ((var dx, var dy) in adjacentOffsets)
        {
            var adjX = target.TileX + dx;
            var adjY = target.TileY + dy;

            if ((adjX == player.TileX) && (adjY == player.TileY))
            {
                // Already adjacent
                Pathfinding.Clear();

                return;
            }

            var path = MapPathfinder.FindPath(
                new Geometry.Point(player.TileX, player.TileY),
                new Geometry.Point(adjX, adjY),
                new PathOptions
                {
                    BlockedPoints = blockedPoints,
                    LimitRadius = null
                });

            if ((path.Count > 0) && (bestPath is null || (path.Count < bestPath.Count)))
                bestPath = path;
        }

        Pathfinding.Path = bestPath;
    }

    private static bool IsAdjacent(
        int x1,
        int y1,
        int x2,
        int y2)
        => (Math.Abs(x1 - x2) + Math.Abs(y1 - y2)) == 1;

    private static Direction? DirectionToward(
        int fromX,
        int fromY,
        int toX,
        int toY)
        => (toX - fromX, toY - fromY) switch
        {
            (0, -1) => Direction.Up,
            (1, 0)  => Direction.Right,
            (0, 1)  => Direction.Down,
            (-1, 0) => Direction.Left,
            _       => null
        };
    #endregion

    #region Server Event Handlers
    private void HandleExchangeAmountRequested(byte fromSlot)
    {
        ExchangeAmountSlot = fromSlot;
        GoldDrop.ShowForTarget(Exchange.OtherUserId, 0, 0);
    }

    private void HandleBoardListReceived()
    {
        var boards = Game.World.Board.AvailableBoards;

        if (boards is { Count: > 0 })
        {
            var targetBoard = boards.FirstOrDefault(b => b.BoardId > 0) ?? boards.First();
            Game.Connection.SendBoardInteraction(BoardRequestType.ViewBoard, targetBoard.BoardId);
        }
    }

    private void HandleBoardPostListChanged()
    {
        var board = Game.World.Board;

        // Paginated append: same board, already visible, triggered by "Load More"
        if (LoadingMoreBoardPosts && MailList.Visible && (MailList.BoardId == board.BoardId))
            MailList.AppendEntries(board.Posts.ToList());
        else
            MailList.ShowMailList(board.BoardId, board.Posts.ToList(), board.IsPublicBoard);

        LoadingMoreBoardPosts = false;
    }

    private void HandleBoardPostViewed()
    {
        var post = Game.World.Board.CurrentPost;

        if (post is not { } p)
            return;

        MailRead.BoardId = Game.World.Board.BoardId;
        MailRead.IsPublicBoard = Game.World.Board.IsPublicBoard;

        MailRead.ShowMail(
            p.PostId,
            p.Author,
            p.MonthOfYear,
            p.DayOfMonth,
            p.Subject,
            p.Message,
            Game.World.Board.EnablePrevButton);
    }

    private void HandleGroupInviteReceived()
    {
        var invite = Game.World.GroupInvite.Current;

        if (invite is null)
            return;

        var sourceName = invite.SourceName;

        switch (invite.ServerGroupSwitch)
        {
            case ServerGroupSwitch.Invite:
            {
                Game.World.Chat.AddOrangeBarMessage($"{sourceName} invites you to join a group.");

                var vp = WorldHud.ViewportBounds;
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
                Game.World.Chat.AddOrangeBarMessage($"{sourceName} wants to join your group.");

                var vp = WorldHud.ViewportBounds;
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
                Game.World.Chat.AddOrangeBarMessage($"{sourceName} has an open group box.");

                break;
            }
        }
    }

    private void HandleEditableProfileRequest()
    {
        var name = Game.Connection.AislingName;
        var portrait = LoadPortraitFile(name);
        var profileText = LoadProfileText(name);

        Game.Connection.SendEditableProfile(portrait, profileText);
    }

    private static byte[] LoadPortraitFile(string name)
    {
        if (string.IsNullOrEmpty(name))
            return [];

        var jpgPath = Path.Combine(GlobalSettings.DataPath, $"{name}.jpg");

        if (File.Exists(jpgPath))
            return File.ReadAllBytes(jpgPath);

        var noExtPath = Path.Combine(GlobalSettings.DataPath, name);

        if (File.Exists(noExtPath))
            return File.ReadAllBytes(noExtPath);

        return [];
    }

    private static string LoadProfileText(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        var profilePath = Path.Combine(GlobalSettings.DataPath, $"{name}.txt");

        return File.Exists(profilePath) ? File.ReadAllText(profilePath) : string.Empty;
    }

    private void HandleSelfProfile(SelfProfileArgs args)
    {
        // Social status display
        var status = SocialStatusPicker.CurrentStatus;
        StatusBook.SetEmoticonState((byte)status, status.ToString());

        // Populate and show the status book
        StatusBook.SetPlayerInfo(
            WorldHud.PlayerName,
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
        LoadPlayerFamilyList();

        // Paperdoll — render the player's full aisling at south-facing idle
        var playerEntity = Game.World.GetPlayerEntity();

        if (playerEntity?.Appearance is { } appearance)
            StatusBook.SetPaperdoll(Game.AislingRenderer, in appearance);

        // Group open state — server is source of truth, sync all UI
        StatusBook.SetGroupOpen(args.GroupOpen);
        SettingsDialog.SetSettingValue(12, args.GroupOpen);

        // Group members — parse GroupString into member names for the group panel
        if (!string.IsNullOrEmpty(args.GroupString))
        {
            if (args.GroupString.StartsWithI(GROUP_MEMBERS_PREFIX))
            {
                var members = ParseGroupString(args.GroupString);
                GroupPanel.SetMembers(members);
            } else if (args.GroupString.StartsWithI(SPOUSE_PREFIX))
            {
                var spouseName = args.GroupString[SPOUSE_PREFIX.Length..]
                                     .Trim();
                StatusBook.SetFamilyInfo(args.Name, spouseName);
                GroupPanel.ClearGroup();
            } else
                GroupPanel.ClearGroup();
        } else
            GroupPanel.ClearGroup();

        if (SelfProfileRequested)
        {
            SelfProfileRequested = false;
            ShowStatusBook();
        }
    }

    private void HandleOtherProfile(OtherProfileArgs args)
    {
        var marks = args.LegendMarks
                        .Select(m => new LegendMarkEntry(
                            m.Text,
                            MapMarkColor(m.Color),
                            (byte)m.Icon,
                            m.Key))
                        .ToList();

        OtherProfile.Show(
            args.Name,
            args.DisplayClass,
            args.GuildName,
            args.GuildRank,
            args.Title,
            args.GroupOpen,
            marks,
            args.ProfileText);
    }

    private void HandleBodyAnimation(BodyAnimationArgs args)
    {
        var entity = Game.World.GetEntity(args.SourceId);

        if (entity is null)
            return;

        // Emotes are body animations — ignore if any body anim or emote overlay is already playing
        if ((entity.AnimState == EntityAnimState.BodyAnim) || (entity.ActiveEmoteFrame >= 0))
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
        {
            (var suffix, var framesPerDir, _, _) = AnimationManager.ResolveBodyAnimParams(args.BodyAnimation);

            if (framesPerDir > 0)
            {
                // Has body animation frames — skip if armor doesn't support it (exempt "03" peasant anims)
                if (entity.Appearance.HasValue
                    && (suffix != AnimationManager.PEASANT_ANIM_SUFFIX)
                    && !Game.AislingRenderer.HasArmorAnimation(entity.Appearance.Value, suffix))
                    return;

                AnimationManager.StartBodyAnimation(entity, args.BodyAnimation, args.AnimationSpeed);
            } else if (DataUtilities.IsEmote(args.BodyAnimation))
            {
                // Emote overlay — face/bubble icon composited into the aisling sprite
                (var startFrame, var frameCount, var durationMs) = AnimationManager.ResolveEmoteFrames(args.BodyAnimation);

                if (startFrame >= 0)
                {
                    entity.EmoteStartFrame = startFrame;
                    entity.EmoteFrameCount = frameCount;
                    entity.ActiveEmoteFrame = startFrame;
                    entity.EmoteDurationMs = durationMs;
                    entity.EmoteElapsedMs = 0;
                    entity.EmoteRemainingMs = durationMs;
                }
            }
        }

        if (args.Sound.HasValue)
            Game.SoundManager.PlaySound(args.Sound.Value);
    }

    private void HandleAnimation(AnimationArgs args)
    {
        // Ground-targeted effect
        if (args is { TargetPoint: not null, TargetAnimation: > 0 })
            CreateEffect(
                args.TargetAnimation,
                args.AnimationSpeed,
                targetTileX: args.TargetPoint.Value.X,
                targetTileY: args.TargetPoint.Value.Y);

        // Entity-targeted effect on target
        if (args is { TargetId: > 0, TargetAnimation: > 0 })
            CreateEffect(args.TargetAnimation, args.AnimationSpeed, args.TargetId.Value);

        // Source-side effect (caster visual)
        if (args is { SourceId: > 0, SourceAnimation: > 0 })
            CreateEffect(args.SourceAnimation, args.AnimationSpeed, args.SourceId.Value);
    }

    private void CreateEffect(
        int effectId,
        ushort animationSpeed,
        uint? targetEntityId = null,
        int? targetTileX = null,
        int? targetTileY = null)
    {
        var info = Game.EffectRenderer.GetEffectInfo(effectId);

        if (info is null)
            return;

        (var frameCount, var fileIntervalMs, var isEfa, var blendMode) = info.Value;

        // EFA effects use the interval from the file; EPF effects use the packet's animation speed
        float frameIntervalMs = isEfa
            ? fileIntervalMs > 0 ? fileIntervalMs : 50
            : animationSpeed > 0
                ? animationSpeed
                : 50;

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
                FrameIntervalMs = frameIntervalMs,
                BlendMode = blendMode
            });
    }

    private void HandleSound(SoundArgs args)
    {
        if (args.IsMusic)
            Game.SoundManager.PlayMusic(args.Sound);
        else
            Game.SoundManager.PlaySound(args.Sound);
    }

    private void HandleWorldMap(WorldMapArgs args) => WorldMap.Show(args);

    private void HandleDoor(DoorArgs args)
    {
        if (MapFile is null)
            return;

        foreach (var door in args.Doors)
        {
            if ((door.X < 0) || (door.X >= MapFile.Width) || (door.Y < 0) || (door.Y >= MapFile.Height))
                continue;

            var tile = MapFile.Tiles[door.X, door.Y];

            if (door.Closed)
            {
                // Restore closed tile: find the open tile currently set and swap it back
                var closedLeft = DoorTileTable.GetClosedTileId(tile.LeftForeground);
                var closedRight = DoorTileTable.GetClosedTileId(tile.RightForeground);

                if (closedLeft.HasValue)
                    tile.LeftForeground = closedLeft.Value;

                if (closedRight.HasValue)
                    tile.RightForeground = closedRight.Value;
            } else
            {
                // Open door: find the closed tile and swap to open
                var openLeft = DoorTileTable.GetOpenTileId(tile.LeftForeground);
                var openRight = DoorTileTable.GetOpenTileId(tile.RightForeground);

                if (openLeft.HasValue)
                    tile.LeftForeground = openLeft.Value;

                if (openRight.HasValue)
                    tile.RightForeground = openRight.Value;
            }
        }
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

            if (trimmed.StartsWithI("Group members") || trimmed.StartsWithI("Total ") || (trimmed.Length == 0))
                continue;

            // Leader lines start with "* ", member lines start with "  "
            if (trimmed.StartsWithI("* "))
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
    {
        MapPreloaded = false;
        QueuedWalkDirection = null;
        Pathfinding.Clear();

        Game.SoundManager.StopMusic();
        WorldMap.HideMap();
    }

    private void HandleExitResponse(ExitResponseArgs args)
    {
        // Server confirmed exit — send the actual logout (isRequest=false triggers server-side redirect to login)
        if (args.ExitConfirmed)
            Game.Connection.RequestExit(false);
    }

    private void HandleStateChanged(ConnectionState oldState, ConnectionState newState)
    {
        // Server redirected us back to login (e.g., after logout)
        // State transitions go World → Connecting → Login, so just check for Login arrival
        if (newState == ConnectionState.Login)
            PendingLoginSwitch = true;
    }

    private void HandleEffect(EffectArgs args) => WorldHud.EffectBar.SetEffect(args.EffectIcon, args.EffectColor);

    private void HandleHealthBar(HealthBarArgs args)
    {
        if (HealthBars.TryGetValue(args.SourceId, out var existing))
            existing.Reset(args.HealthPercent);
        else
            HealthBars[args.SourceId] = new HealthBar(args.SourceId, args.HealthPercent);

        if (args.Sound.HasValue)
            Game.SoundManager.PlaySound(args.Sound.Value);
    }

    private void HandleLightLevel(LightLevelArgs args) => DarknessRenderer.OnLightLevel(args);

    private void HandleDisplayReadonlyNotepad(DisplayReadonlyNotepadArgs args)
        => Notepad.ShowReadonly(args.Width, args.Height, args.Message);

    private void HandleDisplayEditableNotepad(DisplayEditableNotepadArgs args)
        => Notepad.ShowEditable(
            args.Slot,
            args.Width,
            args.Height,
            args.Message);
    #endregion
}
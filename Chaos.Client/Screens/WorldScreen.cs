#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Controls.World.Hud;
using Chaos.Client.Controls.World.Hud.Panel.Slots;
using Chaos.Client.Controls.World.Popups;
using Chaos.Client.Controls.World.Popups.Boards;
using Chaos.Client.Controls.World.Popups.Dialog;
using Chaos.Client.Controls.World.Popups.Exchange;
using Chaos.Client.Controls.World.Popups.Options;
using Chaos.Client.Controls.World.Popups.Profile;
using Chaos.Client.Controls.World.Popups.WorldList;
using Chaos.Client.Controls.World.ViewPort;
using Chaos.Client.Extensions;
using Chaos.Client.Models;
using Chaos.Client.Rendering.Models;
using Chaos.Client.Systems;
using Chaos.Client.ViewModel;
using Chaos.DarkAges.Definitions;
using Chaos.Geometry.Abstractions.Definitions;
using DALib.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pathfinder = Chaos.Pathfinding.Pathfinder;
#endregion

namespace Chaos.Client.Screens;

public sealed partial class WorldScreen : IScreen
{
    // Walk queue: when walk animation is >= 75% complete, one walk can be queued
    private const float WALK_QUEUE_THRESHOLD = 0.75f;

    // Spacebar (assail) repeat interval when held
    private const float SPACEBAR_INTERVAL_MS = 100f;

    // Aisling body anchor within the padded composite canvas — matches AislingRenderer.CANVAS_CENTER_X/Y.
    private const int BODY_CENTER_X = AislingRenderer.CANVAS_CENTER_X;
    private const int BODY_CENTER_Y = AislingRenderer.CANVAS_CENTER_Y;

    // Entity hitbox dimensions (screen pixels)
    private const int HITBOX_WIDTH = 28;
    private const int HITBOX_HEIGHT = 60;

    private const string SPOUSE_PREFIX = "Spouse: ";
    private const string GROUP_MEMBERS_PREFIX = "Group members";

    private readonly CastingSystem CastingSystem = new();

    private readonly WorldDebugRenderer DebugRenderer = new();

    // Draw-pass hitbox list: rebuilt every frame during entity rendering, in draw order (back-to-front)
    private readonly List<EntityHitBox> EntityHitBoxes = new(256);

    // Set of entity IDs currently highlighted as group members (auto-expires after 1000ms)
    private readonly HashSet<uint> GroupHighlightedIds = [];
    private readonly EntityHighlightState Highlight = new();
    private readonly EntityOverlayManager Overlays = new();
    private readonly PathfindingState Pathfinding = new();
    private readonly Queue<PendingWalk> PendingWalks = new();

    private AbilityDetailControl AbilityDetail = null!;
    private AislingPopupMenu AislingPopup = null!;

    private int AnimationTick;
    private ArticleListControl ArticleList = null!;
    private ArticleReadControl ArticleRead = null!;
    private ArticleSendControl ArticleSend = null!;

    // Board/mail controls — 7 instances for 7 prefabs
    private bool AwaitingMapData;
    private BoardListControl BoardList = null!;
    private Camera Camera = null!;
    private ChantEditControl ChantEdit = null!;
    private ChatSystem Chat = null!;
    private ContextMenu ContextMenu = null!;
    private MapFlags CurrentMapFlags;
    private short CurrentMapId;

    private DarknessRenderer DarknessRenderer = null!;
    private OkPopupMessageControl DeleteConfirm = null!;
    private GraphicsDevice Device = null!;
    private OkPopupMessageControl DisconnectPopup = null!;

    // Event detail popup (from Events tab)
    private EventDetailControl EventDetail = null!;
    private ExchangeControl Exchange = null!;
    private byte? ExchangeAmountSlot;

    private FriendsListControl FriendsList = null!;

    private ChaosGame Game = null!;
    private GoldExchangeControl GoldDrop = null!;

    // True when J was pressed — the next SelfProfile response triggers group highlighting instead of opening the panel
    private bool GroupHighlightRequested;
    private float GroupHighlightTimer;
    private GroupControl GroupPanel = null!;
    private HotkeyHelpControl HotkeyHelp = null!;
    private PanelSlot? HoveredInventorySlot;
    private bool IsGameMaster;
    private ItemTooltipControl ItemTooltip = null!;
    private LargeWorldHudControl LargeHud = null!;
    private TileClickTracker LeftClickTracker = new();
    private LightSource[] LightSourceBuffer = new LightSource[16];

    // True while awaiting a paginated board response (append instead of replace)
    private bool LoadingMoreBoardPosts;
    private MacroMenuControl MacroMenu = null!;
    private MailListControl MailList = null!;
    private MailReadControl MailRead = null!;
    private MailSendControl MailSend = null!;
    private MainOptionsControl MainOptions = null!;
    private MapFile? MapFile;
    private MapLoadingBar MapLoading = null!;
    private Pathfinder? MapPathfinder;
    private bool MapPreloaded;
    private MapRenderer MapRenderer = null!;

    // Overlay panels (rendered on top of HUD)
    private NotepadControl Notepad = null!;
    private NpcSessionControl NpcSession = null!;
    private OtherProfileTabControl OtherProfile = null!;
    private Action? PendingDeleteAction;
    private bool PendingLoginSwitch;
    private byte[] PlayerPortrait = [];
    private ProfileTextEditorControl ProfileTextEditor = null!;
    private Direction? QueuedWalkDirection;
    private bool RedirectInProgress;
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
    private TabMapEntity[] TabMapEntities = [];
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
    public void Initialize(ChaosGame game)
    {
        Game = game;
        Chat = new ChatSystem(game.Connection, () => WorldHud);

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
        WorldState.NpcInteraction.DialogChanged += HandleDialogChanged;
        WorldState.NpcInteraction.MenuChanged += HandleMenuChanged;

        // Refresh response
        Game.Connection.OnRefreshResponse += HandleRefreshResponse;

        WorldState.Exchange.AmountRequested += HandleExchangeAmountRequested;

        // Board — subscribe to state events
        WorldState.Board.PostListChanged += HandleBoardPostListChanged;
        WorldState.Board.PostViewed += HandleBoardPostViewed;
        WorldState.Board.BoardListReceived += HandleBoardListReceived;
        WorldState.Board.ResponseReceived += msg => WorldState.Chat.AddOrangeBarMessage(msg);

        // Group invite — subscribe to state event
        WorldState.GroupInvite.Received += HandleGroupInviteReceived;

        // Profiles
        Game.Connection.OnEditableProfileRequest += HandleEditableProfileRequest;
        Game.Connection.OnSelfProfile += HandleSelfProfile;
        Game.Connection.OnOtherProfile += HandleOtherProfile;

        // Animations / effects / sound
        Game.Connection.OnBodyAnimation += HandleBodyAnimation;
        Game.Connection.OnAnimation += HandleAnimation;
        Game.Connection.OnSound += HandleSound;
        Game.Connection.OnCancelCasting += CastingSystem.Reset;

        // Map transitions
        Game.Connection.OnMapChangePending += HandleMapChangePending;

        // Logout / disconnect
        Game.Connection.OnExitResponse += HandleExitResponse;
        Game.Connection.StateChanged += HandleStateChanged;
        Game.Connection.OnRedirectReceived += _ => RedirectInProgress = true;

        // Health bars
        Game.Connection.OnHealthBar += HandleHealthBar;

        // Status effects
        Game.Connection.OnEffect += HandleEffect;

        // Light level
        Game.Connection.OnLightLevel += HandleLightLevel;

        // Metadata sync — reload metadata consumers after server handshake completes
        Game.OnMetaDataSyncComplete += HandleMetaDataSyncComplete;

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
        SmallHud = new WorldHudControl
        {
            ZIndex = -1
        };

        LargeHud = new LargeWorldHudControl
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
        NpcSession = new NpcSessionControl();
        WireNpcSession();

        MainOptions = new MainOptionsControl
        {
            ZIndex = -2
        };
        MainOptions.SetViewportBounds(WorldHud.ViewportBounds);
        WireOptionsDialog();

        // Sub-panels slide out from MainOptions' left edge, render behind it
        var optionsAnchorX = WorldHud.ViewportBounds.X + WorldHud.ViewportBounds.Width - MainOptions.Width + 10;
        var optionsAnchorY = WorldHud.ViewportBounds.Y;

        // Initialize client-local settings into UserOptions from persisted config
        var userOptions = WorldState.UserOptions;
        userOptions.SetValue(6, ClientSettings.AutoAcceptGroupInvites);
        userOptions.SetValue(8, ClientSettings.ScrollLevel > 0);
        userOptions.SetValue(9, ClientSettings.UseShiftKeyForAltPanels);
        userOptions.SetValue(10, ClientSettings.EnableProfileClick);
        userOptions.SetValue(11, ClientSettings.RecordNpcChat);
        userOptions.SetValue(12, ClientSettings.GroupOpen);

        // Route user-initiated toggles to server or local persistence
        userOptions.SettingToggled += (index, value) =>
        {
            if (UserOptions.IsServerSetting(index))
            {
                var option = (UserOption)(index + 1);
                Game.Connection.SendOptionToggle(option);
            } else
            {
                switch (index)
                {
                    case 6:
                        ClientSettings.AutoAcceptGroupInvites = value;

                        break;
                    case 8:
                        ClientSettings.ScrollLevel = value ? 1 : 0;

                        break;
                    case 9:
                        ClientSettings.UseShiftKeyForAltPanels = value;

                        break;
                    case 10:
                        ClientSettings.EnableProfileClick = value;

                        break;
                    case 11:
                        ClientSettings.RecordNpcChat = value;

                        break;
                    case 12:
                        // Server-authoritative — send toggle, server responds with updated profile
                        Game.Connection.ToggleGroup();

                        return;
                }

                ClientSettings.Save();
            }
        };

        SettingsDialog = new SettingsControl(userOptions)
        {
            ZIndex = -3
        };
        SettingsDialog.SetSlideAnchor(optionsAnchorX, optionsAnchorY);

        SettingsDialog.VisibilityChanged += visible =>
        {
            if (visible)
                Game.Connection.SendOptionToggle(UserOption.Request);
        };

        MacroMenu = new MacroMenuControl
        {
            ZIndex = -3
        };
        MacroMenu.SetSlideAnchor(optionsAnchorX, optionsAnchorY);

        HotkeyHelp = new HotkeyHelpControl();

        GroupPanel = new GroupControl();
        GroupPanel.OnKick += name => Game.Connection.SendGroupInvite(ClientGroupSwitch.TryInvite, name);

        WorldList = new WorldListControl
        {
            ZIndex = -2
        };
        WorldList.SetViewportBounds(WorldHud.ViewportBounds);

        FriendsList = new FriendsListControl
        {
            ZIndex = -3
        };
        FriendsList.SetSlideAnchor(optionsAnchorX, optionsAnchorY);
        FriendsList.OnOk += SavePlayerFriendList;

        Exchange = new ExchangeControl(WorldHud.ViewportBounds);

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

        BoardList = new BoardListControl
        {
            ZIndex = -2
        };

        ArticleList = new ArticleListControl
        {
            ZIndex = -2
        };

        ArticleRead = new ArticleReadControl
        {
            ZIndex = -2
        };

        ArticleSend = new ArticleSendControl
        {
            ZIndex = -2
        };

        MailList = new MailListControl
        {
            ZIndex = -2
        };

        MailRead = new MailReadControl
        {
            ZIndex = -2
        };

        MailSend = new MailSendControl
        {
            ZIndex = -2
        };
        DeleteConfirm = new OkPopupMessageControl(true);

        DisconnectPopup = new OkPopupMessageControl(true)
        {
            ZIndex = 10
        };

        DisconnectPopup.OnOk += () =>
        {
            DisconnectPopup.Hide();
            Game.Screens.Switch(new LobbyLoginScreen());
        };
        DisconnectPopup.OnCancel += () => Game.Exit();

        var boardViewport = WorldHud.ViewportBounds;
        BoardList.SetViewportBounds(boardViewport);
        ArticleList.SetViewportBounds(boardViewport);
        ArticleRead.SetViewportBounds(boardViewport);
        ArticleSend.SetViewportBounds(boardViewport);
        MailList.SetViewportBounds(boardViewport);
        MailRead.SetViewportBounds(boardViewport);
        MailSend.SetViewportBounds(boardViewport);

        WireExchange();
        WireMailControls();

        StatusBook = new SelfProfileTabControl
        {
            ZIndex = 2
        };

        StatusBook.OnUnequip += slot => Game.Connection.Unequip(slot);
        StatusBook.OnClose += SavePlayerFamilyList;
        StatusBook.OnClose += () => SaveProfileText(StatusBook.GetProfileText());

        StatusBook.OnGroupToggled += () => Game.Connection.ToggleGroup();

        StatusBook.OnProfileTextClicked += () =>
        {
            ProfileTextEditor.Show(StatusBook.GetProfileText());
        };

        StatusBook.OnAbilityDetailRequested += entry =>
        {
            AbilityDetail.ShowEntry(entry, WorldHud.ViewportBounds);
        };
        StatusBook.OnEventDetailRequested += (entry, state) => EventDetail.ShowEntry(entry, state, WorldHud.ViewportBounds);

        ProfileTextEditor = new ProfileTextEditorControl
        {
            ZIndex = 3
        };

        ProfileTextEditor.OnSave += text =>
        {
            StatusBook.SetProfileText(text);
            SaveProfileText(text);
        };

        AbilityDetail = new AbilityDetailControl
        {
            ZIndex = 3
        };

        EventDetail = new EventDetailControl
        {
            ZIndex = 3
        };

        SocialStatusPicker = new SocialStatusControl();

        SocialStatusPicker.OnStatusSelected += status =>
        {
            Game.Connection.SendSocialStatus(status);
            StatusBook.SetEmoticonState((byte)status, status.ToString());

            var emoteIcon = UiRenderer.Instance?.GetSpfTexture("_nemots.spf", (int)status);

            if (emoteIcon is not null)
                UpdateHuds(h =>
                {
                    if (h.EmoteButton is not null)
                        h.EmoteButton.NormalTexture = emoteIcon;
                });
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

        OtherProfile = new OtherProfileTabControl
        {
            ZIndex = 2
        };
        OtherProfile.OnGroupInviteRequested += name => Game.Connection.SendGroupInvite(ClientGroupSwitch.TryInvite, name);

        ChantEdit = new ChantEditControl
        {
            ZIndex = 2
        };
        ChantEdit.OnChantSet += HandleChantSet;

        WorldMap = new WorldMap(Game.Connection)
        {
            ZIndex = 2
        };

        MapLoading = new MapLoadingBar
        {
            ZIndex = 5
        };
        MapLoading.CenterIn(viewport);

        AislingPopup = new AislingPopupMenu
        {
            ZIndex = 3
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
        Root.AddChild(NpcSession);
        Root.AddChild(ItemTooltip);
        Root.AddChild(MainOptions);
        Root.AddChild(SettingsDialog);
        Root.AddChild(MacroMenu);
        Root.AddChild(HotkeyHelp);
        Root.AddChild(GroupPanel);
        Root.AddChild(WorldList);
        Root.AddChild(FriendsList);
        Root.AddChild(Exchange);
        Root.AddChild(GoldDrop);
        Root.AddChild(BoardList);
        Root.AddChild(ArticleList);
        Root.AddChild(ArticleRead);
        Root.AddChild(ArticleSend);
        Root.AddChild(MailList);
        Root.AddChild(MailRead);
        Root.AddChild(MailSend);
        Root.AddChild(DeleteConfirm);
        Root.AddChild(StatusBook);
        Root.AddChild(ProfileTextEditor);
        Root.AddChild(AbilityDetail);
        Root.AddChild(EventDetail);
        Root.AddChild(OtherProfile);
        Root.AddChild(TextPopup);
        Root.AddChild(Notepad);
        Root.AddChild(ChantEdit);
        Root.AddChild(WorldMap);
        Root.AddChild(SocialStatusPicker);
        Root.AddChild(AislingPopup);
        Root.AddChild(ContextMenu);
        Root.AddChild(MapLoading);
        Root.AddChild(DisconnectPopup);

        WireHudPanels(SmallHud);
        WireHudPanels(LargeHud);

        // Build UI atlas after all HUD controls are constructed
        UiRenderer.Instance?.BuildAtlas();

        // Load local portrait and profile text from file
        var playerName = Game.Connection.AislingName;
        PlayerPortrait = LoadPortraitFile(playerName);
        StatusBook.SetProfileText(LoadProfileText(playerName));
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
        WorldState.NpcInteraction.DialogChanged -= HandleDialogChanged;
        WorldState.NpcInteraction.MenuChanged -= HandleMenuChanged;
        Game.Connection.OnRefreshResponse -= HandleRefreshResponse;
        WorldState.Exchange.AmountRequested -= HandleExchangeAmountRequested;
        WorldState.Board.PostListChanged -= HandleBoardPostListChanged;
        WorldState.Board.PostViewed -= HandleBoardPostViewed;
        WorldState.Board.BoardListReceived -= HandleBoardListReceived;
        WorldState.Board.SessionClosed -= HideAllBoardControls;
        WorldState.GroupInvite.Received -= HandleGroupInviteReceived;
        Game.Connection.OnEditableProfileRequest -= HandleEditableProfileRequest;
        Game.Connection.OnSelfProfile -= HandleSelfProfile;
        Game.Connection.OnOtherProfile -= HandleOtherProfile;
        Game.Connection.OnBodyAnimation -= HandleBodyAnimation;
        Game.Connection.OnAnimation -= HandleAnimation;
        Game.Connection.OnSound -= HandleSound;
        Game.Connection.OnCancelCasting -= CastingSystem.Reset;
        Game.Connection.OnMapChangePending -= HandleMapChangePending;
        Game.Connection.OnExitResponse -= HandleExitResponse;
        Game.Connection.StateChanged -= HandleStateChanged;
        Game.Connection.OnHealthBar -= HandleHealthBar;
        Game.Connection.OnEffect -= HandleEffect;
        Game.Connection.OnLightLevel -= HandleLightLevel;
        Game.OnMetaDataSyncComplete -= HandleMetaDataSyncComplete;
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
        Game.AislingRenderer.ClearCompositeCache();
        Game.AislingRenderer.ClearGroupTintCache();
        Game.CreatureRenderer.ClearTintCaches();
        Game.ItemRenderer.Clear();
        Overlays.Clear();
        DebugRenderer.Clear();
    }

    private readonly record struct PendingWalk(
        int FromX,
        int FromY,
        int ToX,
        int ToY,
        Direction Direction);
}
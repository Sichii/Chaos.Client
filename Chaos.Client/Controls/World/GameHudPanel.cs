#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Rendering;
using Chaos.Networking.Entities.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Main game HUD frame loaded from _nbk_s.txt (small/normal layout). Defines viewport bounds, chat area, inventory
///     area, HP/MP orbs, info text fields, and all action buttons. Manages the shared "center bottom" tab area where
///     Inventory/Skills/Spells/Chat/Stats panels swap.
/// </summary>
public class GameHudPanel : PrefabPanel
{
    private readonly UILabel CoordsLabel;

    // HP/MP numeric displays
    private readonly UILabel HpNumLabel;

    // HP/MP orbs
    private readonly OrbDisplay HpOrb;
    private readonly UILabel MpNumLabel;
    private readonly OrbDisplay MpOrb;

    // Orange bar
    private readonly OrangeBarControl OrangeBar;
    private readonly UILabel? PersistentMessageLabel;

    // Persistent message — EmoticonDialog panel, white text, no auto-expire, cleared by server
    private readonly UIPanel? PersistentMessagePanel;

    // Info text
    private readonly UILabel PlayerNameLabel;

    // Tab panel system — shared center-bottom area
    private readonly UIPanel?[] TabPanels = new UIPanel?[Enum.GetValues<HudTab>()
                                                             .Length];

    private readonly UILabel WeightLabel;
    private readonly UILabel ZoneNameLabel;
    public HudTab ActiveTab { get; private set; } = HudTab.Inventory;
    public ChatPanel ChatDisplay { get; private set; } = null!;
    public ExtendedStatsControl ExtendedStatsPanel { get; private set; } = null!;
    public InventoryPanel Inventory { get; private set; } = null!;
    public SkillBookPanel SkillBook { get; private set; } = null!;
    public SkillBookPanel SkillBookAlt { get; private set; } = null!;
    public SpellBookPanel SpellBook { get; private set; } = null!;
    public SpellBookPanel SpellBookAlt { get; private set; } = null!;
    public StatsControl StatsPanel { get; private set; } = null!;
    public UIButton? BulletinButton { get; }
    public UIButton? ChangeLayoutButton { get; }

    // Chat
    public UITextBox ChatInput { get; }
    public UIButton? ExpandButton { get; }
    public UIButton? GroupButton { get; }
    public UIButton? GroupIndicator { get; }

    // Buttons — lower right
    public UIButton? HelpButton { get; }

    // Inventory
    public Rectangle InventoryBounds { get; }

    // Inventory tab buttons
    public UIButton?[] InventoryTabButtons { get; } = new UIButton?[6];
    public UIButton? LegendButton { get; }

    // Buttons — notification indicators
    public UIButton? MailButton { get; }

    // Buttons — right side
    public UIButton? OptionButton { get; }
    public UIButton? ScreenshotButton { get; }
    public UIButton? TownMapButton { get; }

    public UIButton? UsersButton { get; }

    // Viewport — the area where the game world renders
    public Rectangle ViewportBounds { get; }

    public GameHudPanel(GraphicsDevice device)
        : base(device, "_nbk_s", false)
    {
        Name = "GameHud";

        // Viewport rect — where the game world renders
        ViewportBounds = GetRect("MAP");

        if (ViewportBounds == Rectangle.Empty)
            ViewportBounds = GetRect("EMPTY");

        // Chat input textbox (SAY) — reduce padding so prefix aligns flush
        ChatInput = CreateTextBox("SAY", 255)!;
        ChatInput.PaddingX = 1;
        ChatInput.PaddingY = 1;

        // Inventory area
        InventoryBounds = GetRect("InventoryRect");

        // HP/MP orbs
        HpOrb = new OrbDisplay(device, PrefabSet, "ORB_HP");
        MpOrb = new OrbDisplay(device, PrefabSet, "ORB_MP");

        // HP/MP numeric text
        HpNumLabel = CreateLabel("NUM_HP", TextAlignment.Right)!;
        MpNumLabel = CreateLabel("NUM_MP", TextAlignment.Right)!;

        // Info text areas
        PlayerNameLabel = CreateLabel("SZ_ID", TextAlignment.Center)!;
        ZoneNameLabel = CreateLabel("SZ_ZONE", TextAlignment.Center)!;
        WeightLabel = CreateLabel("SZ_WEIGHT", TextAlignment.Center)!;
        CoordsLabel = CreateLabel("SZ_XY")!;

        // Buttons — right side
        OptionButton = CreateButton("BTN_OPTION");
        BulletinButton = CreateButton("BTN_BULLETIN");
        UsersButton = CreateButton("BTN_USERS");
        ExpandButton = CreateButton("BTN_EXPAND");
        ChangeLayoutButton = CreateButton("BTN_CHANGELAYOUT");

        // Buttons — lower right
        HelpButton = CreateButton("BTN_HELP");
        LegendButton = CreateButton("BTN_LEGEND");
        TownMapButton = CreateButton("BTN_TOWNMAP");
        GroupButton = CreateButton("BTN_GROUP");

        // Notification indicators
        MailButton = CreateButton("CMail");
        GroupIndicator = CreateButton("CGroup");
        ScreenshotButton = CreateButton("CShot");

        // Conditional buttons (may not exist in all client versions)
        CreateButton("BTN_SETTING");
        CreateButton("BTN_SCREENSHOT");

        // Inventory tab buttons (BTN_INV0 through BTN_INV5)
        for (var i = 0; i < 6; i++)
            InventoryTabButtons[i] = CreateButton($"BTN_INV{i}");

        // Persistent message panel — uses EmoticonDialog from lback.txt + Description from lemot.txt
        var lbackPrefab = DataContext.UserControls.Get("lback");
        var lemotPrefab = DataContext.UserControls.Get("lemot");

        if ((lbackPrefab?.Contains("EmoticonDialog") == true) && lemotPrefab is not null)
        {
            var dialogPrefab = lbackPrefab["EmoticonDialog"];
            var dialogRect = dialogPrefab.Control.Rect!.Value;

            PersistentMessagePanel = new UIPanel
            {
                Name = "PersistentMessage",
                X = (int)dialogRect.Left,
                Y = (int)dialogRect.Top,
                Width = (int)dialogRect.Width,
                Height = (int)dialogRect.Height,
                Visible = false
            };

            if (dialogPrefab.Images.Count > 0)
                PersistentMessagePanel.Background = TextureConverter.ToTexture2D(device, dialogPrefab.Images[0]);

            var descRect = GetRect(lemotPrefab, "Description");

            if (descRect != Rectangle.Empty)
            {
                PersistentMessageLabel = new UILabel(Device)
                {
                    Name = "PersistentText",
                    X = descRect.X,
                    Y = descRect.Y,
                    Width = descRect.Width,
                    Height = descRect.Height,
                    Alignment = TextAlignment.Center
                };

                PersistentMessagePanel.AddChild(PersistentMessageLabel);
            }

            AddChild(PersistentMessagePanel);
        }

        // Tab panels — shared center-bottom area
        CreateTabPanels();

        // Orange bar — created here, added as child after tab panels so it draws on top
        OrangeBar = new OrangeBarControl(device, PrefabSet);

        // Orange bar drawn after tab panels so it renders on top
        AddChild(OrangeBar);
    }

    public override void Dispose()
    {
        HpOrb.Dispose();
        MpOrb.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        var sx = ScreenX;
        var sy = ScreenY;

        // HP/MP orbs
        HpOrb.Draw(spriteBatch, sx, sy);
        MpOrb.Draw(spriteBatch, sx, sy);
    }

    #region Helpers
    private static int GetTabButtonIndex(HudTab tab)
        => tab switch
        {
            HudTab.Inventory                     => 0,
            HudTab.Skills or HudTab.SkillsAlt    => 1,
            HudTab.Spells or HudTab.SpellsAlt    => 2,
            HudTab.Chat                          => 3,
            HudTab.Stats or HudTab.ExtendedStats => 4,
            HudTab.Tools                         => 5,
            _                                    => -1
        };
    #endregion

    #region Tab Panel Management
    /// <summary>
    ///     Switches the active tab in the shared center-bottom area. Hides the current panel and shows the new one.
    /// </summary>
    public void ShowTab(HudTab tab)
    {
        // Hide ALL unique panels to prevent stale visibility
        foreach (var panel in TabPanels)
            if (panel is not null)
                panel.Visible = false;

        // Deselect old tab button
        var oldButtonIndex = GetTabButtonIndex(ActiveTab);

        if ((oldButtonIndex >= 0) && (oldButtonIndex < InventoryTabButtons.Length))
            if (InventoryTabButtons[oldButtonIndex] is { } oldBtn)
                oldBtn.IsSelected = false;

        ActiveTab = tab;

        var next = TabPanels[(int)tab];

        if (next is not null)
            next.Visible = true;

        // Select new tab button
        var newButtonIndex = GetTabButtonIndex(tab);

        if ((newButtonIndex >= 0) && (newButtonIndex < InventoryTabButtons.Length))
            if (InventoryTabButtons[newButtonIndex] is { } newBtn)
                newBtn.IsSelected = true;
    }

    /// <summary>
    ///     Updates both HUD orb/number displays and the stats tab panels.
    /// </summary>
    public void UpdateAttributes(AttributesArgs attrs)
    {
        UpdateHp((int)attrs.CurrentHp, (int)attrs.MaximumHp);
        UpdateMp((int)attrs.CurrentMp, (int)attrs.MaximumMp);
        SetWeight(attrs.CurrentWeight, attrs.MaxWeight);

        StatsPanel.UpdateAttributes(attrs);
        ExtendedStatsPanel.UpdateAttributes(attrs);
    }

    private void CreateTabPanels()
    {
        // Tab panels are placed at InventoryBounds (absolute screen position from InventoryRect control).
        var tabRect = InventoryBounds;

        // Inventory (A)
        Inventory = new InventoryPanel(Device, PrefabSet);
        RegisterTab(HudTab.Inventory, Inventory, tabRect);

        // Skills (S) / Skills Alt (Shift+S)
        SkillBook = new SkillBookPanel(Device, PrefabSet);
        SkillBookAlt = new SkillBookPanel(Device, PrefabSet, true);
        RegisterTab(HudTab.Skills, SkillBook, tabRect);
        RegisterTab(HudTab.SkillsAlt, SkillBookAlt, tabRect);

        // Spells (D) / Spells Alt (Shift+D)
        SpellBook = new SpellBookPanel(Device, PrefabSet);
        SpellBookAlt = new SpellBookPanel(Device, PrefabSet, true);
        RegisterTab(HudTab.Spells, SpellBook, tabRect);
        RegisterTab(HudTab.SpellsAlt, SpellBookAlt, tabRect);

        // Chat (F)
        var chatDisplayBounds = GetRect("ChattingRect");
        ChatDisplay = new ChatPanel(Device, chatDisplayBounds);
        RegisterTab(HudTab.Chat, ChatDisplay, tabRect);

        // Stats (G) / Extended Stats (Shift+G) — both load from _nstatus prefab
        var statusPrefabSet = DataContext.UserControls.Get("_nstatus")!;
        StatsPanel = new StatsControl(Device);
        ExtendedStatsPanel = new ExtendedStatsControl(Device, statusPrefabSet);
        RegisterTab(HudTab.Stats, StatsPanel, tabRect);
        RegisterTab(HudTab.ExtendedStats, ExtendedStatsPanel, tabRect);

        // Tools (H)
        RegisterTab(HudTab.Tools, new ToolsPanel(Device, PrefabSet), tabRect);

        // Wire tab button clicks: BTN_INV0=A, BTN_INV1=S, BTN_INV2=D, BTN_INV3=F, BTN_INV4=G, BTN_INV5=H
        HudTab[] tabMapping =
        [
            HudTab.Inventory,
            HudTab.Skills,
            HudTab.Spells,
            HudTab.Chat,
            HudTab.Stats,
            HudTab.Tools
        ];

        for (var i = 0; i < InventoryTabButtons.Length; i++)
            if (InventoryTabButtons[i] is not null && (i < tabMapping.Length))
            {
                var tab = tabMapping[i];
                InventoryTabButtons[i]!.OnClick += () => ShowTab(tab);
            }

        // Default to inventory visible
        ShowTab(HudTab.Inventory);
    }

    private void RegisterTab(HudTab tab, UIPanel panel, Rectangle tabRect)
    {
        panel.X = tabRect.X;
        panel.Y = tabRect.Y;
        panel.Visible = false;
        TabPanels[(int)tab] = panel;
        AddChild(panel);
    }
    #endregion

    #region Public Methods
    public void UpdateHp(int current, int max)
    {
        HpNumLabel.SetText($"{current}");
        HpOrb.UpdateValue(current, max);
    }

    public void UpdateMp(int current, int max)
    {
        MpNumLabel.SetText($"{current}");
        MpOrb.UpdateValue(current, max);
    }

    public string PlayerName { get; private set; } = string.Empty;

    public void SetPlayerName(string name)
    {
        PlayerName = name;
        PlayerNameLabel.SetText(name);
    }

    public void SetZoneName(string zone) => ZoneNameLabel.SetText(zone);

    public void SetWeight(int current, int max) => WeightLabel.SetText($"{current}/{max}");

    public void SetCoords(int x, int y) => CoordsLabel.SetText($"{x},{y}");

    public void AddChatMessage(string text, Color color) => ChatDisplay.AddMessage(text, color);

    public void ShowOrangeBarMessage(string text) => OrangeBar.ShowMessage(text);

    /// <summary>
    ///     Displays a persistent message in the EmoticonDialog panel. Remains until the server sends an empty string to clear
    ///     it.
    /// </summary>
    public void ShowPersistentMessage(string text)
    {
        if (PersistentMessagePanel is null || PersistentMessageLabel is null)
            return;

        if (string.IsNullOrEmpty(text))
            PersistentMessagePanel.Visible = false;
        else
        {
            PersistentMessageLabel.SetText(text, Color.White);
            PersistentMessagePanel.Visible = true;
        }
    }
    #endregion
}
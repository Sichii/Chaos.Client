#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.World.Hud.Panel;
using Chaos.Client.Controls.World.ViewPort;
using Chaos.Client.Data;
using Chaos.Client.ViewModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud;

/// <summary>
///     Main game HUD frame loaded from _nbk_s.txt (small/normal layout). Defines viewport bounds, chat area, inventory
///     area, HP/MP orbs, info text fields, and all action buttons. Manages the shared "center bottom" tab area where
///     Inventory/Skills/Spells/Chat/Stats panels swap.
/// </summary>
public sealed class WorldHudControl : PrefabPanel, IWorldHud
{
    private readonly PlayerAttributes AttributesState;
    private readonly UILabel CoordsLabel;
    private readonly UILabel? DescriptionLabel;

    //hp/mp numeric displays
    private readonly UILabel HpNumLabel;

    //hp/mp orbs
    private readonly UIProgressBar HpOrb;
    private readonly UILabel MpNumLabel;
    private readonly UIProgressBar MpOrb;

    //orange bar
    private readonly OrangeBarControl OrangeBar;
    private readonly PersistentMessageControl PersistentMessage;
    private readonly SystemMessagePaneControl SystemMessagePane;

    //info text
    private readonly UILabel PlayerNameLabel;

    private readonly UILabel? ServerNameLabel;

    //tab panel system — shared center-bottom area
    private readonly UIPanel?[] TabPanels = new UIPanel?[Enum.GetValues<HudTab>()
                                                             .Length];

    private readonly UILabel WeightLabel;
    private readonly UILabel ZoneNameLabel;
    public HudTab ActiveTab { get; private set; } = HudTab.Inventory;
    public ChatPanel ChatDisplay { get; private set; } = null!;
    public SystemMessagePanel MessageHistory { get; private set; } = null!;
    public ExtendedStatsPanel ExtendedStatsPanel { get; private set; } = null!;
    public InventoryPanel Inventory { get; private set; } = null!;
    public SkillBookPanel SkillBook { get; private set; } = null!;
    public SkillBookPanel SkillBookAlt { get; private set; } = null!;
    public SpellBookPanel SpellBook { get; private set; } = null!;
    public SpellBookPanel SpellBookAlt { get; private set; } = null!;
    public StatsPanel StatsPanel { get; private set; } = null!;
    public UIButton? BulletinButton { get; }
    public UIButton? ChangeLayoutButton { get; }

    //chat
    public ChatInputControl ChatInput { get; }

    public EffectBarControl EffectBar { get; }
    public UIButton? EmoteButton { get; }
    public UIButton? ExpandButton { get; }
    public UIButton? GroupButton { get; }
    public UIButton? GroupIndicator { get; }

    //buttons — lower right
    public UIButton? HelpButton { get; }

    //inventory
    public Rectangle InventoryBounds { get; }

    //inventory tab buttons
    public UIButton?[] InventoryTabButtons { get; } = new UIButton?[6];
    public UIButton? LegendButton { get; }

    //buttons — notification indicators
    public UIButton? MailButton { get; }

    //buttons — right side
    public UIButton? OptionButton { get; }
    public UIButton? ScreenshotButton { get; }
    public UIButton? SettingsButton { get; }
    public UIButton? TownMapButton { get; }

    public UIButton? UsersButton { get; }

    //viewport — the area where the game world renders
    public Rectangle ViewportBounds { get; }

    public WorldHudControl(InputBuffer input, string prefabName = "_nbk_s")
        : base(prefabName, false)
    {
        Name = "GameHud";
        IsPassThrough = true;

        //viewport rect — where the game world renders
        ViewportBounds = GetRect("MAP");

        if (ViewportBounds == Rectangle.Empty)
            ViewportBounds = GetRect("EMPTY");

        //chat input control (say)
        ChatInput = new ChatInputControl(PrefabSet, input);
        AddChild(ChatInput);

        ChatInput.FocusChanged += focused =>
        {
            if (focused)
                DescriptionLabel?.Text = string.Empty;
        };

        //inventory area
        InventoryBounds = GetRect("InventoryRect");

        //hp/mp orbs
        HpOrb = CreateProgressBar("ORB_HP")!;
        MpOrb = CreateProgressBar("ORB_MP")!;

        //hp/mp numeric text — zindex=1 so labels always render above overlapping orbs after re-sorts
        HpNumLabel = CreateLabel("NUM_HP", HorizontalAlignment.Right)!;
        HpNumLabel.ZIndex = 1;
        MpNumLabel = CreateLabel("NUM_MP", HorizontalAlignment.Right)!;
        MpNumLabel.ZIndex = 1;

        //info text areas
        PlayerNameLabel = CreateLabel("SZ_ID", HorizontalAlignment.Center)!;
        ZoneNameLabel = CreateLabel("SZ_ZONE", HorizontalAlignment.Center)!;
        ZoneNameLabel.ForegroundColor = LegendColors.White;
        WeightLabel = CreateLabel("SZ_WEIGHT", HorizontalAlignment.Center)!;
        CoordsLabel = CreateLabel("SZ_XY", HorizontalAlignment.Center)!;
        ServerNameLabel = CreateLabel("SZ_SERVER", HorizontalAlignment.Center);
        DescriptionLabel = CreateLabel("SZ_DESCRIPTION");

        //effect bar — right side, above the option button
        EffectBar = new EffectBarControl
        {
            X = 618,
            Y = 2
        };
        AddChild(EffectBar);

        //buttons — right side
        OptionButton = CreateButton("BTN_OPTION");
        BulletinButton = CreateButton("BTN_BULLETIN");
        UsersButton = CreateButton("BTN_USERS");
        ExpandButton = CreateButton("BTN_EXPAND");
        ChangeLayoutButton = CreateButton("BTN_CHANGELAYOUT");

        //buttons — lower right
        HelpButton = CreateButton("BTN_HELP");
        LegendButton = CreateButton("BTN_LEGEND");
        TownMapButton = CreateButton("BTN_TOWNMAP");
        GroupButton = CreateButton("BTN_GROUP");

        //notification indicators
        MailButton = CreateButton("CMail");
        GroupIndicator = CreateButton("CGroup");
        ScreenshotButton = CreateButton("CShot");

        //emote/status button
        EmoteButton = CreateButton("BTN_EMOT");

        //conditional buttons (may not exist in all client versions)
        SettingsButton = CreateButton("BTN_SETTING");
        CreateButton("BTN_SCREENSHOT");

        //inventory tab buttons (btn_inv0 through btn_inv5)
        for (var i = 0; i < 6; i++)
            InventoryTabButtons[i] = CreateButton($"BTN_INV{i}");

        //persistent message — floating text, top-right of viewport
        PersistentMessage = new PersistentMessageControl(ViewportBounds);
        AddChild(PersistentMessage);

        //system message pane — floating text, top-left of viewport
        SystemMessagePane = new SystemMessagePaneControl(ViewportBounds);
        AddChild(SystemMessagePane);

        //resolve inventory background textures from prefab for tab panels
        var cache = UiRenderer.Instance!;
        var invBgTexture = GetPrefabTexture(cache, "InventoryBackground");
        var invBgExpandedTexture = GetPrefabTexture(cache, "InventoryBackgroundExpanded");
        var livingBgTexture = GetPrefabTexture(cache, "LivingInventoryBackground");

        //orange bar — created before tab panels (messagehistorypanel needs its history list),
        //but added as child after so it draws on top
        OrangeBar = new OrangeBarControl(PrefabSet);

        //tab panels — shared center-bottom area
        CreateTabPanels(invBgTexture, invBgExpandedTexture, livingBgTexture);

        //orange bar renders above collapsed tab panels (z=0) but below expanded ones (z=10)
        OrangeBar.ZIndex = 1;
        AddChild(OrangeBar);

        //subscribe to attributes changes for hp/mp/weight/stats
        AttributesState = WorldState.Attributes;
        AttributesState.Changed += OnAttributesChanged;

        if (AttributesState.Current is not null)
            OnAttributesChanged();
    }

    public override void Dispose()
    {
        AttributesState.Changed -= OnAttributesChanged;

        base.Dispose();
    }

    private void OnAttributesChanged()
    {
        if (AttributesState.Current is not { } attrs)
            return;

        UpdateHp((int)attrs.CurrentHp, (int)attrs.MaximumHp);
        UpdateMp((int)attrs.CurrentMp, (int)attrs.MaximumMp);
        SetWeight(attrs.CurrentWeight, attrs.MaxWeight);
        StatsPanel.UpdateAttributes(attrs);
        ExtendedStatsPanel.UpdateAttributes(attrs);
    }

    #region Helpers
    private static int GetTabButtonIndex(HudTab tab)
        => tab switch
        {
            HudTab.Inventory                     => 0,
            HudTab.Skills or HudTab.SkillsAlt    => 1,
            HudTab.Spells or HudTab.SpellsAlt    => 2,
            HudTab.Chat or HudTab.MessageHistory => 3,
            HudTab.Stats or HudTab.ExtendedStats => 4,
            HudTab.Tools                         => 5,
            _                                    => -1
        };

    private Texture2D? GetPrefabTexture(UiRenderer cache, string controlName)
        => PrefabSet.Contains(controlName) && (PrefabSet[controlName].Images.Count > 0)
            ? cache.GetPrefabTexture(PrefabSet.Name, controlName, 0)
            : null;
    #endregion

    #region Tab Panel Management
    /// <summary>
    ///     Switches the active tab in the shared center-bottom area. Hides the current panel and shows the new one.
    /// </summary>
    public void ShowTab(HudTab tab)
    {
        //collapse inventory when switching away so it doesn't persist expanded state
        if (Inventory.IsExpanded && (tab != HudTab.Inventory))
            Inventory.SetExpanded(false);

        //hide all unique panels to prevent stale visibility
        foreach (var panel in TabPanels)
            panel?.Visible = false;

        //force hover exit on inventory when switching away — tooltip won't clear on its own
        //since the panel stops updating when hidden
        if ((ActiveTab == HudTab.Inventory) && (tab != HudTab.Inventory))
            Inventory.ForceHoverExit();

        //deselect old tab button
        var oldButtonIndex = GetTabButtonIndex(ActiveTab);

        if ((oldButtonIndex >= 0) && (oldButtonIndex < InventoryTabButtons.Length))
            if (InventoryTabButtons[oldButtonIndex] is { } oldBtn)
                oldBtn.IsSelected = false;

        ActiveTab = tab;

        var next = TabPanels[(int)tab];

        next?.Visible = true;

        //select new tab button
        var newButtonIndex = GetTabButtonIndex(tab);

        if ((newButtonIndex >= 0) && (newButtonIndex < InventoryTabButtons.Length))
            if (InventoryTabButtons[newButtonIndex] is { } newBtn)
                newBtn.IsSelected = true;
    }

    private void CreateTabPanels(Texture2D? invBgTexture, Texture2D? invBgExpandedTexture, Texture2D? livingBgTexture)
    {
        //tab panels are placed at inventorybounds (absolute screen position from inventoryrect control).
        var tabRect = InventoryBounds;

        //inventory (a)
        Inventory = new InventoryPanel(PrefabSet, invBgTexture, invBgExpandedTexture);
        RegisterTab(HudTab.Inventory, Inventory, tabRect);

        //skills (s) / skills alt (shift+s)
        SkillBook = new SkillBookPanel(PrefabSet, background: invBgTexture);

        SkillBookAlt = new SkillBookPanel(PrefabSet, true, invBgTexture);
        RegisterTab(HudTab.Skills, SkillBook, tabRect);
        RegisterTab(HudTab.SkillsAlt, SkillBookAlt, tabRect);

        //spells (d) / spells alt (shift+d)
        SpellBook = new SpellBookPanel(PrefabSet, background: invBgTexture);

        SpellBookAlt = new SpellBookPanel(PrefabSet, true, invBgTexture);
        RegisterTab(HudTab.Spells, SpellBook, tabRect);
        RegisterTab(HudTab.SpellsAlt, SpellBookAlt, tabRect);

        //chat (f)
        var chatDisplayBounds = GetRect("ChattingRect");
        ChatDisplay = new ChatPanel(chatDisplayBounds, tabRect);
        RegisterTab(HudTab.Chat, ChatDisplay, tabRect);

        //stats (g) / extended stats (shift+g) — both load from _nstatus prefab
        var statusPrefabSet = DataContext.UserControls.Get("_nstatus")!;
        StatsPanel = new StatsPanel(statusPrefabSet);
        ExtendedStatsPanel = new ExtendedStatsPanel(statusPrefabSet);
        RegisterTab(HudTab.Stats, StatsPanel, tabRect);
        RegisterTab(HudTab.ExtendedStats, ExtendedStatsPanel, tabRect);

        //tools (h)
        RegisterTab(HudTab.Tools, new ToolsPanel(PrefabSet, livingBgTexture), tabRect);

        //message history (shift+f) — displays orange bar messages in a tab panel
        var msgHistoryBounds = GetRect("ChattingRect");

        MessageHistory = new SystemMessagePanel(msgHistoryBounds, tabRect, WorldState.Chat.GetOrangeBarHistory());
        RegisterTab(HudTab.MessageHistory, MessageHistory, tabRect);

        //wire tab button clicks: btn_inv0=a, btn_inv1=s, btn_inv2=d, btn_inv3=f, btn_inv4=g, btn_inv5=h
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

                InventoryTabButtons[i]!.Clicked += () =>
                {
                    //toggle between primary and alt panes for skills/spells when clicking the same button
                    if (tab == HudTab.Skills && (ActiveTab is HudTab.Skills or HudTab.SkillsAlt))
                        ShowTab(ActiveTab == HudTab.Skills ? HudTab.SkillsAlt : HudTab.Skills);
                    else if (tab == HudTab.Spells && (ActiveTab is HudTab.Spells or HudTab.SpellsAlt))
                        ShowTab(ActiveTab == HudTab.Spells ? HudTab.SpellsAlt : HudTab.Spells);
                    else
                        ShowTab(tab);
                };
            }

        //default to inventory visible
        ShowTab(HudTab.Inventory);
    }

    private void RegisterTab(HudTab tab, UIPanel panel, Rectangle tabRect)
    {
        panel.X = tabRect.X;
        panel.Y = tabRect.Y;

        //ensure tab panels have bounds for hit-testing (background size or tab rect as fallback)
        if (panel.Width == 0)
            panel.Width = panel.Background?.Width ?? tabRect.Width;

        if (panel.Height == 0)
            panel.Height = panel.Background?.Height ?? tabRect.Height;

        panel.Visible = false;

        if (panel is PanelBase panelBase)
            panelBase.Tab = tab;

        TabPanels[(int)tab] = panel;
        AddChild(panel);
    }
    #endregion

    #region Public Methods
    public void UpdateHp(int current, int max)
    {
        HpNumLabel.Text = $"{current}";
        HpOrb.UpdateValue(current, max);
    }

    public void UpdateMp(int current, int max)
    {
        MpNumLabel.Text = $"{current}";
        MpOrb.UpdateValue(current, max);
    }

    public string PlayerName { get; private set; } = string.Empty;

    public void SetGroupOpen(bool groupOpen)
    {
        if (GroupIndicator is null)
            return;

        var cache = UiRenderer.Instance!;

        if (groupOpen)
        {
            GroupIndicator.NormalTexture = cache.GetPrefabTexture(PrefabSet.Name, "CGroup", 0);
            GroupIndicator.PressedTexture = cache.GetPrefabTexture(PrefabSet.Name, "CGroup", 1);
        } else
        {
            GroupIndicator.NormalTexture = cache.GetSpfTexture("_ni_gr0.spf");
            GroupIndicator.PressedTexture = cache.GetSpfTexture("_ni_gr0.spf", 1);
        }
    }

    public void SetPlayerName(string name)
    {
        PlayerName = name;
        PlayerNameLabel.Text = name;
    }

    public void SetZoneName(string zone) => ZoneNameLabel.Text = zone;

    public void SetWeight(int current, int max) => WeightLabel.Text = $"{current} / {max}";

    public void SetCoords(int x, int y) => CoordsLabel.Text = $"{x}, {y}";

    public void SetServerName(string name) => ServerNameLabel?.Text = name;

    /// <summary>
    ///     Shows a description text in the SZ_DESCRIPTION area (item/skill/spell name on hover). Color 0x14 = green/teal,
    ///     matching original client.
    /// </summary>
    public void SetDescription(string? text)
    {
        //don't show hover descriptions while chat input is focused — they overlap
        if (ChatInput.IsFocused && !string.IsNullOrEmpty(text))
            return;

        DescriptionLabel?.Text = text ?? string.Empty;
    }

    public bool IsOrangeBarDragging => OrangeBar.IsDragging;

    /// <summary>
    ///     Small HUD: only inventory supports expand (3 rows → 5 rows).
    /// </summary>
    public void ToggleExpand() => Inventory.SetExpanded(!Inventory.IsExpanded);

    public void ShowPersistentMessage(string text) => PersistentMessage.SetMessage(text);

    public void ShowSystemMessage(string text, Color? color = null) => SystemMessagePane.AddMessage(text, color);
    #endregion
}
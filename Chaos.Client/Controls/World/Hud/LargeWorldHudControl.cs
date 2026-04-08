#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.World.Hud.Panel;
using Chaos.Client.Data;
using Chaos.Client.ViewModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Hud;

/// <summary>
///     Large/expanded game HUD frame loaded from _nbk_l.txt. Same functionality as <see cref="WorldHudControl" /> but with
///     a different layout (larger viewport, repositioned controls). Once working, common code should be extracted into a
///     shared base class.
/// </summary>
public sealed class LargeWorldHudControl : PrefabPanel, IWorldHud
{
    private const int LARGE_NORMAL_SLOTS = 1 * 12;
    private const int LARGE_EXPANDED_SKILL_SPELL_SLOTS = 3 * 12;

    private readonly PlayerAttributes AttributesState;
    private readonly UILabel CoordsLabel; 
    private readonly UILabel? DescriptionLabel;
    private readonly Rectangle ExpandedChatBounds;
    private readonly UIImage? ExtendedTabFrame;
    private readonly UILabel HpNumLabel;
    private readonly UIProgressBar HpOrb;
    private readonly UILabel MpNumLabel;
    private readonly UIProgressBar MpOrb;
    private readonly OrangeBarControl OrangeBar;
    private readonly UILabel? PersistentMessageLabel;
    private readonly UIPanel? PersistentMessagePanel;
    private readonly UILabel PlayerNameLabel;
    private readonly UILabel? ServerNameLabel;

    private readonly UIPanel?[] TabPanels = new UIPanel?[Enum.GetValues<HudTab>()
                                                             .Length];

    private readonly UILabel WeightLabel;
    private readonly UILabel ZoneNameLabel;
    private bool Expanded;
    private Rectangle NormalChatBounds;

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
    public UITextBox ChatInput { get; }
    public EffectBarControl EffectBar { get; }
    public UIButton? EmoteButton { get; }
    public UIButton? ExpandButton { get; }
    public UIButton? GroupButton { get; }
    public UIButton? GroupIndicator { get; }
    public UIButton? HelpButton { get; }
    public Rectangle InventoryBounds { get; }
    public UIButton?[] InventoryTabButtons { get; } = new UIButton?[6];
    public UIButton? LegendButton { get; }
    public UIButton? MailButton { get; }
    public UIButton? OptionButton { get; }
    public UIButton? ScreenshotButton { get; }
    public UIButton? SettingsButton { get; }
    public UIButton? TownMapButton { get; }
    public UIButton? UsersButton { get; }
    public Rectangle ViewportBounds { get; }

    public LargeWorldHudControl()
        : base("_nbk_l", false)
    {
        Name = "GameHudLarge";
        Visible = false;
        IsPassThrough = true;

        //viewport rect
        ViewportBounds = GetRect("MAP");

        if (ViewportBounds == Rectangle.Empty)
            ViewportBounds = GetRect("EMPTY");

        //chat input
        ChatInput = CreateTextBox("SAY", 255)!;
        ChatInput.PaddingLeft = 1;
        ChatInput.PaddingRight = 1;
        ChatInput.PaddingTop = 1;
        ChatInput.PaddingBottom = 1;

        ChatInput.FocusedBackgroundColor = new Color(
            0,
            0,
            0,
            160);

        //inventory area
        InventoryBounds = GetRect("InventoryRect");

        //hp/mp orbs
        HpOrb = CreateProgressBar("ORB_HP")!;
        MpOrb = CreateProgressBar("ORB_MP")!;

        //hp/mp numeric text — centered for horizontal orbs, zindex=1 to render above orbs after re-sorts
        HpNumLabel = CreateLabel("NUM_HP", TextAlignment.Center)!;
        HpNumLabel.ZIndex = 1;
        HpNumLabel.ForegroundColor = Color.White;
        HpNumLabel.Shadowed = true;
        MpNumLabel = CreateLabel("NUM_MP", TextAlignment.Center)!;
        MpNumLabel.ZIndex = 1;
        MpNumLabel.ForegroundColor = Color.White;
        MpNumLabel.Shadowed = true;

        //info text areas
        PlayerNameLabel = CreateLabel("SZ_ID", TextAlignment.Center)!;
        ZoneNameLabel = CreateLabel("SZ_ZONE", TextAlignment.Center)!;
        ZoneNameLabel.ForegroundColor = LegendColors.White;
        WeightLabel = CreateLabel("SZ_WEIGHT", TextAlignment.Center)!;
        CoordsLabel = CreateLabel("SZ_XY", TextAlignment.Center)!;
        ServerNameLabel = CreateLabel("SZ_SERVER", TextAlignment.Center);
        DescriptionLabel = CreateLabel("SZ_DESCRIPTION");

        //effect bar
        EffectBar = new EffectBarControl
        {
            X = 618,
            Y = 2,
            ZIndex = 1
        };
        AddChild(EffectBar);

        //buttons
        OptionButton = CreateButton("BTN_OPTION");
        BulletinButton = CreateButton("BTN_BULLETIN");
        UsersButton = CreateButton("BTN_USERS");
        ExpandButton = CreateButton("BTN_EXPAND");
        ChangeLayoutButton = CreateButton("BTN_CHANGELAYOUT");
        HelpButton = CreateButton("BTN_HELP");
        LegendButton = CreateButton("BTN_LEGEND");
        TownMapButton = CreateButton("BTN_TOWNMAP");
        GroupButton = CreateButton("BTN_GROUP");
        MailButton = CreateButton("CMail");
        GroupIndicator = CreateButton("CGroup");

        ScreenshotButton = CreateButton("CShot");
        EmoteButton = CreateButton("BTN_EMOT");
        SettingsButton = CreateButton("BTN_SETTING");
        CreateButton("BTN_SCREENSHOT");

        for (var i = 0; i < 6; i++)
            InventoryTabButtons[i] = CreateButton($"BTN_INV{i}");

        ExtendedTabFrame = CreateImage("BTN_INV_EXTENDED_FRAME");

        if (ExtendedTabFrame is not null)
        {
            ExtendedTabFrame.X = InventoryBounds.X;
            ExtendedTabFrame.Y = InventoryBounds.Y - ExtendedTabFrame.Height;
            ExtendedTabFrame.ZIndex = -1;
            ExtendedTabFrame.Visible = false;
        }

        //persistent message panel
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
                PersistentMessagePanel.Background = UiRenderer.Instance!.GetPrefabTexture("lback", "EmoticonDialog", 0);

            var descRect = GetRect(lemotPrefab, "Description");

            if (descRect != Rectangle.Empty)
            {
                PersistentMessageLabel = new UILabel
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

        //resolve inventory background textures from prefab for tab panels
        var cache = UiRenderer.Instance!;
        var invBgTexture = GetPrefabTexture(cache, "InventoryBackground");
        var invBgExpandedTexture = GetPrefabTexture(cache, "InventoryBackgroundExpanded");
        var livingBgTexture = GetPrefabTexture(cache, "LivingInventoryBackground");
        var skillSpellExpandedTexture = GetPrefabTexture(cache, "SkillSpellBackgroundExpanded");
        var chatExpandedTexture = GetPrefabTexture(cache, "ChatBackgroundExpanded");
        ExpandedChatBounds = GetRect("ChattingRectExpanded");

        //orange bar
        OrangeBar = new OrangeBarControl(PrefabSet);

        //tab panels
        CreateTabPanels(
            invBgTexture,
            invBgExpandedTexture,
            livingBgTexture,
            skillSpellExpandedTexture,
            chatExpandedTexture);

        //orange bar renders above collapsed tab panels (z=0) but below expanded ones (z=10)
        OrangeBar.ZIndex = 1;
        AddChild(OrangeBar);

        //attributes subscription + initial sync if data already exists
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

    #region Tab Panel Management
    public void ShowTab(HudTab tab)
    {
        //collapse the outgoing panel before hiding it (so it returns to normal state)
        if (TabPanels[(int)ActiveTab] is ExpandablePanel { IsExpanded: true } oldExpandable)
        {
            var offset = oldExpandable.ExpandYOffset;
            oldExpandable.SetExpanded(false);
            ShiftCompanionElements(offset);

            if (ExtendedTabFrame is not null)
                ExtendedTabFrame.Visible = false;

            Expanded = false;
        }

        foreach (var panel in TabPanels)
            panel?.Visible = false;

        if ((ActiveTab == HudTab.Inventory) && (tab != HudTab.Inventory))
            Inventory.ForceHoverExit();

        var oldButtonIndex = GetTabButtonIndex(ActiveTab);

        if ((oldButtonIndex >= 0) && (oldButtonIndex < InventoryTabButtons.Length))
            if (InventoryTabButtons[oldButtonIndex] is { } oldBtn)
                oldBtn.IsSelected = false;

        ActiveTab = tab;

        TabPanels[(int)tab]?.Visible = true;

        //apply global expand state to the newly shown panel
        if (Expanded)
            ApplyExpandToActiveTab();

        var newButtonIndex = GetTabButtonIndex(tab);

        if ((newButtonIndex >= 0) && (newButtonIndex < InventoryTabButtons.Length))
            if (InventoryTabButtons[newButtonIndex] is { } newBtn)
                newBtn.IsSelected = true;
    }

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

    private void CreateTabPanels(
        Texture2D? invBgTexture,
        Texture2D? invBgExpandedTexture,
        Texture2D? livingBgTexture,
        Texture2D? skillSpellExpandedTexture,
        Texture2D? chatExpandedTexture)
    {
        var tabRect = InventoryBounds;

        //large hud: 1 row normal (12 slots), 5 rows expanded (60 slots) for inventory
        Inventory = new InventoryPanel(
            PrefabSet,
            invBgTexture,
            invBgExpandedTexture,
            LARGE_NORMAL_SLOTS);
        RegisterTab(HudTab.Inventory, Inventory, tabRect);

        //large hud: 1 row normal, 3 rows expanded for skills/spells
        SkillBook = new SkillBookPanel(PrefabSet, background: invBgTexture, normalVisibleSlots: LARGE_NORMAL_SLOTS);
        SkillBook.ConfigureExpand(skillSpellExpandedTexture, LARGE_EXPANDED_SKILL_SPELL_SLOTS);

        SkillBookAlt = new SkillBookPanel(
            PrefabSet,
            true,
            invBgTexture,
            LARGE_NORMAL_SLOTS);
        SkillBookAlt.ConfigureExpand(skillSpellExpandedTexture, LARGE_EXPANDED_SKILL_SPELL_SLOTS);

        RegisterTab(HudTab.Skills, SkillBook, tabRect);
        RegisterTab(HudTab.SkillsAlt, SkillBookAlt, tabRect);

        SpellBook = new SpellBookPanel(PrefabSet, background: invBgTexture, normalVisibleSlots: LARGE_NORMAL_SLOTS);
        SpellBook.ConfigureExpand(skillSpellExpandedTexture, LARGE_EXPANDED_SKILL_SPELL_SLOTS);

        SpellBookAlt = new SpellBookPanel(
            PrefabSet,
            true,
            invBgTexture,
            LARGE_NORMAL_SLOTS);
        SpellBookAlt.ConfigureExpand(skillSpellExpandedTexture, LARGE_EXPANDED_SKILL_SPELL_SLOTS);

        RegisterTab(HudTab.Spells, SpellBook, tabRect);
        RegisterTab(HudTab.SpellsAlt, SpellBookAlt, tabRect);

        //chat — stores both normal and expanded bounds for expand toggle
        NormalChatBounds = GetRect("ChattingRect");
        ChatDisplay = new ChatPanel(NormalChatBounds, tabRect);
        ChatDisplay.ConfigureExpand(chatExpandedTexture, ExpandedChatBounds, tabRect);
        RegisterTab(HudTab.Chat, ChatDisplay, tabRect);

        //large hud uses compact stats (_nstatur), expanding to full stats (_nstatus)
        var compactStatusPrefabSet = DataContext.UserControls.Get("_nstatur")!;
        var fullStatusPrefabSet = DataContext.UserControls.Get("_nstatus")!;
        StatsPanel = new StatsPanel(compactStatusPrefabSet);
        StatsPanel.ConfigureExpand(fullStatusPrefabSet);
        ExtendedStatsPanel = new ExtendedStatsPanel(compactStatusPrefabSet);
        ExtendedStatsPanel.ConfigureExpand(fullStatusPrefabSet);
        RegisterTab(HudTab.Stats, StatsPanel, tabRect);
        RegisterTab(HudTab.ExtendedStats, ExtendedStatsPanel, tabRect);

        //tools: 1 row normal, expanded uses the normal hud's split background
        var normalHudPrefabSet = DataContext.UserControls.Get("_nbk_s")!;
        var uiCache = UiRenderer.Instance!;
        Texture2D? toolsExpandedTexture = null;

        if (normalHudPrefabSet.Contains("LivingInventoryBackground") && (normalHudPrefabSet["LivingInventoryBackground"].Images.Count > 0))
            toolsExpandedTexture = uiCache.GetPrefabTexture(normalHudPrefabSet.Name, "LivingInventoryBackground", 0);

        var tools = new ToolsPanel(PrefabSet, livingBgTexture, LARGE_NORMAL_SLOTS);
        tools.ConfigureExpand(toolsExpandedTexture, LARGE_EXPANDED_SKILL_SPELL_SLOTS);
        RegisterTab(HudTab.Tools, tools, tabRect);

        var msgHistoryBounds = GetRect("ChattingRect");
        MessageHistory = new SystemMessagePanel(msgHistoryBounds, tabRect, WorldState.Chat.GetOrangeBarHistory());
        MessageHistory.ConfigureExpand(chatExpandedTexture, ExpandedChatBounds, tabRect);
        RegisterTab(HudTab.MessageHistory, MessageHistory, tabRect);

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

        ShowTab(HudTab.Inventory);
    }

    private void RegisterTab(HudTab tab, UIPanel panel, Rectangle tabRect)
    {
        panel.X = tabRect.X;
        panel.Y = tabRect.Y;

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
            GroupIndicator.NormalTexture = cache.GetSpfTexture("_ni_gr0b.spf");
            GroupIndicator.PressedTexture = cache.GetSpfTexture("_ni_gr0b.spf", 1);
        }
    }

    public void SetPlayerName(string name)
    {
        PlayerName = name;
        PlayerNameLabel.Text = name;
    }

    public void SetZoneName(string zone) => ZoneNameLabel.Text = zone;
    public void SetWeight(int current, int max) => WeightLabel.Text = $"{current}/{max}";
    public void SetCoords(int x, int y) => CoordsLabel.Text = $"{x}, {y}";
    public void SetServerName(string name) => ServerNameLabel?.Text = name;

    public void SetDescription(string? text)
    {
        if (ChatInput.IsFocused && !string.IsNullOrEmpty(text))
            return;

        DescriptionLabel?.Text = text ?? string.Empty;
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape && ChatInput.IsFocused)
        {
            ChatInput.IsFocused = false;
            ChatInput.Text = string.Empty;
            ChatInput.Prefix = string.Empty;
            ChatInput.ForegroundColor = Color.White;
            InputDispatcher.Instance?.ClearExplicitFocus();
            e.Handled = true;
        }
    }

    public bool IsOrangeBarDragging => OrangeBar.IsDragging;

    /// <summary>
    ///     Large HUD: expand toggles globally. Affects inventory (1→5 rows), skills/spells/tools (1→3 rows), and chat
    ///     (small→large text area). State persists across tab switches.
    /// </summary>
    public void ToggleExpand()
    {
        Expanded = !Expanded;

        ApplyExpandToActiveTab();
    }

    private void ApplyExpandToActiveTab()
    {
        if (TabPanels[(int)ActiveTab] is not ExpandablePanel expandable)
            return;

        var wasExpanded = expandable.IsExpanded;
        expandable.SetExpanded(Expanded);

        if (expandable.IsExpanded != wasExpanded)
        {
            ShiftCompanionElements(expandable.IsExpanded ? -expandable.ExpandYOffset : expandable.ExpandYOffset);

            if (ExtendedTabFrame is not null)
                ExtendedTabFrame.Visible = expandable.IsExpanded;
        }
    }

    private void ShiftCompanionElements(int yShift)
    {
        foreach (var btn in InventoryTabButtons)
            if (btn is not null)
                btn.Y += yShift;

        if (ExtendedTabFrame is not null)
            ExtendedTabFrame.Y += yShift;

        ChatInput.Y += yShift;
    }

    public void ShowPersistentMessage(string text)
    {
        if (PersistentMessagePanel is null || PersistentMessageLabel is null)
            return;

        if (string.IsNullOrEmpty(text))
            PersistentMessagePanel.Visible = false;
        else
        {
            PersistentMessageLabel.ForegroundColor = Color.White;
            PersistentMessageLabel.Text = text;
            PersistentMessagePanel.Visible = true;
        }
    }
    #endregion
}
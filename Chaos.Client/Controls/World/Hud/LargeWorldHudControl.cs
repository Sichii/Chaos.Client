#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.World.Hud.Panel;
using Chaos.Client.Data;
using Chaos.Client.Rendering;
using Chaos.Client.ViewModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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
    public PromptControl Prompt { get; }
    public UIButton? ScreenshotButton { get; }
    public UIButton? TownMapButton { get; }
    public UIButton? UsersButton { get; }
    public Rectangle ViewportBounds { get; }

    public LargeWorldHudControl()
        : base("_nbk_l", false)
    {
        Name = "GameHudLarge";
        Visible = false;

        // Viewport rect
        ViewportBounds = GetRect("MAP");

        if (ViewportBounds == Rectangle.Empty)
            ViewportBounds = GetRect("EMPTY");

        // Chat input
        ChatInput = CreateTextBox("SAY", 255)!;
        ChatInput.PaddingX = 1;
        ChatInput.PaddingY = 1;

        ChatInput.FocusedBackgroundColor = new Color(
            0,
            0,
            0,
            128);

        // Prompt
        Prompt = new PromptControl
        {
            Name = "Prompt",
            X = ChatInput.X,
            Y = ChatInput.Y,
            Width = ChatInput.Width,
            Height = ChatInput.Height,
            ZIndex = 1
        };
        AddChild(Prompt);

        // Inventory area
        InventoryBounds = GetRect("InventoryRect");

        // HP/MP orbs
        HpOrb = CreateProgressBar("ORB_HP")!;
        MpOrb = CreateProgressBar("ORB_MP")!;

        // HP/MP numeric text — centered for horizontal orbs, ZIndex=1 to render above orbs after re-sorts
        HpNumLabel = CreateLabel("NUM_HP", TextAlignment.Center)!;
        HpNumLabel.ZIndex = 1;
        MpNumLabel = CreateLabel("NUM_MP", TextAlignment.Center)!;
        MpNumLabel.ZIndex = 1;

        // Info text areas
        PlayerNameLabel = CreateLabel("SZ_ID", TextAlignment.Center)!;
        ZoneNameLabel = CreateLabel("SZ_ZONE", TextAlignment.Center)!;
        WeightLabel = CreateLabel("SZ_WEIGHT", TextAlignment.Center)!;
        CoordsLabel = CreateLabel("SZ_XY", TextAlignment.Center)!;
        ServerNameLabel = CreateLabel("SZ_SERVER", TextAlignment.Center);
        DescriptionLabel = CreateLabel("SZ_DESCRIPTION");

        // Effect bar
        EffectBar = new EffectBarControl
        {
            X = 618,
            Y = 2,
            ZIndex = 1
        };
        AddChild(EffectBar);

        // Buttons
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
        CreateButton("BTN_SETTING");
        CreateButton("BTN_SCREENSHOT");

        for (var i = 0; i < 6; i++)
            InventoryTabButtons[i] = CreateButton($"BTN_INV{i}");

        // Persistent message panel
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

        // Resolve inventory background textures from prefab for tab panels
        var cache = UiRenderer.Instance!;
        var invBgTexture = GetPrefabTexture(cache, "InventoryBackground");
        var invBgExpandedTexture = GetPrefabTexture(cache, "InventoryBackgroundExpanded");
        var livingBgTexture = GetPrefabTexture(cache, "LivingInventoryBackground");
        var skillSpellExpandedTexture = GetPrefabTexture(cache, "SkillSpellBackgroundExpanded");
        var chatExpandedTexture = GetPrefabTexture(cache, "ChatBackgroundExpanded");
        ExpandedChatBounds = GetRect("ChattingRectExpanded");

        // Orange bar
        OrangeBar = new OrangeBarControl(PrefabSet);

        // Tab panels
        CreateTabPanels(
            invBgTexture,
            invBgExpandedTexture,
            livingBgTexture,
            skillSpellExpandedTexture,
            chatExpandedTexture);

        // Orange bar drawn after tab panels
        AddChild(OrangeBar);

        // Attributes subscription + initial sync if data already exists
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
        // Collapse the outgoing panel before hiding it (so it returns to normal state)
        if (TabPanels[(int)ActiveTab] is ExpandablePanel oldExpandable)
            oldExpandable.SetExpanded(false);

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

        // Apply global expand state to the newly shown panel
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
            HudTab.Chat                          => 3,
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

        // Large HUD: 1 row normal (12 slots), 5 rows expanded (60 slots) for inventory
        Inventory = new InventoryPanel(
            PrefabSet,
            invBgTexture,
            invBgExpandedTexture,
            LARGE_NORMAL_SLOTS);
        RegisterTab(HudTab.Inventory, Inventory, tabRect);

        // Large HUD: 1 row normal, 3 rows expanded for skills/spells
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

        // Chat — stores both normal and expanded bounds for expand toggle
        NormalChatBounds = GetRect("ChattingRect");
        ChatDisplay = new ChatPanel(NormalChatBounds, tabRect);
        ChatDisplay.ConfigureExpand(chatExpandedTexture, ExpandedChatBounds, tabRect);
        RegisterTab(HudTab.Chat, ChatDisplay, tabRect);

        // Large HUD uses compact stats (_nstatur) for both normal Stats and ExtendedStats
        var compactStatusPrefabSet = DataContext.UserControls.Get("_nstatur")!;
        StatsPanel = new StatsPanel("_nstatur");
        ExtendedStatsPanel = new ExtendedStatsPanel(compactStatusPrefabSet);
        RegisterTab(HudTab.Stats, StatsPanel, tabRect);
        RegisterTab(HudTab.ExtendedStats, ExtendedStatsPanel, tabRect);

        // Tools: 1 row normal, 3 rows expanded (same as skills/spells)
        var tools = new ToolsPanel(PrefabSet, livingBgTexture, LARGE_NORMAL_SLOTS);
        tools.ConfigureExpand(skillSpellExpandedTexture, LARGE_EXPANDED_SKILL_SPELL_SLOTS);
        RegisterTab(HudTab.Tools, tools, tabRect);

        var msgHistoryBounds = GetRect("ChattingRect");
        var msgHistoryPanel = new MessageHistoryPanel(msgHistoryBounds, tabRect, WorldState.Chat.GetOrangeBarHistory());
        msgHistoryPanel.ConfigureExpand(chatExpandedTexture, ExpandedChatBounds);
        RegisterTab(HudTab.MessageHistory, msgHistoryPanel, tabRect);

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
        HpNumLabel.Text = $"{current}";
        HpOrb.UpdateValue(current, max);
    }

    public void UpdateMp(int current, int max)
    {
        MpNumLabel.Text = $"{current}";
        MpOrb.UpdateValue(current, max);
    }

    public string PlayerName { get; private set; } = string.Empty;

    public void SetPlayerName(string name)
    {
        PlayerName = name;
        PlayerNameLabel.Text = name;
    }

    public void SetZoneName(string zone) => ZoneNameLabel.Text = zone;
    public void SetWeight(int current, int max) => WeightLabel.Text = $"{current}/{max}";
    public void SetCoords(int x, int y) => CoordsLabel.Text = $"{x},{y}";
    public void SetServerName(string name) => ServerNameLabel?.Text = name;

    public void SetDescription(string? text)
    {
        if (ChatInput.IsFocused && !string.IsNullOrEmpty(text))
            return;

        DescriptionLabel?.Text = text ?? string.Empty;
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
        if (TabPanels[(int)ActiveTab] is ExpandablePanel expandable)
            expandable.SetExpanded(Expanded);
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
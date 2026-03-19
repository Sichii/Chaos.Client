#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Definitions;
using Chaos.Client.Models;
using Chaos.Client.Networking;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using EquipmentSlot = Chaos.DarkAges.Definitions.EquipmentSlot;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Status book container using _nui prefab (main page). Contains tab navigation and hosts sub-pages: Equipment,
///     Skills, Legend, Events, Family. Each tab page is a separate prefab that swaps in when the tab is selected.
/// </summary>
public sealed class SelfProfileTabControl : PrefabPanel
{
    private const int TAB_COUNT = 6;

    // Tab control names in the _nui prefab, in order matching StatusBookTab enum
    private static readonly string[] TabControlNames =
    [
        "TAB_INTRO",
        "TAB_LEGEND",
        "TAB_SKILL",
        "TAB_EVENT",
        "TAB_ALBUM",
        "TAB_FAMILY"
    ];

    private readonly Rectangle ContentRect;
    private readonly UIButton?[] TabButtons = new UIButton?[TAB_COUNT];
    private readonly Dictionary<StatusBookTab, PrefabPanel?> TabPages = new();

    public StatusBookTab ActiveTab { get; private set; } = StatusBookTab.Equipment;

    public UIButton? CloseButton { get; }

    public SelfProfileTabControl(GraphicsDevice device)
        : base(device, "_nui", false)
    {
        Name = "StatusBook";
        Visible = false;
        X = 0;
        Y = 0;

        var elements = AutoPopulate();

        // CONTENT rect defines where tab page content is positioned
        ContentRect = GetRect("CONTENT");

        // Hide prefab elements that we render manually
        if (elements.TryGetValue("CONTENT", out var contentElement))
            contentElement.Visible = false;

        // Hide the TAB_INTRO_E element — it's just a rect template, not a visible control
        if (elements.TryGetValue("TAB_INTRO_E", out var introEElement))
            introEElement.Visible = false;

        // Close button (TAB_CLOSE)
        CloseButton = elements.GetValueOrDefault("TAB_CLOSE") as UIButton;

        if (CloseButton is not null)
            CloseButton.OnClick += () =>
            {
                Hide();
                OnClose?.Invoke();
            };

        // Load normal/small tab textures from _nui_tb1.spf
        var smallFrames = TextureConverter.LoadSpfTextures(device, "_nui_tb1.spf");

        // Configure tab buttons — AutoPopulate created them as UIButtons with the big/selected
        // texture as NormalTexture (the only image in the prefab). We swap that to SelectedTexture
        // and assign the small/normal texture from _nui_tb1.spf.
        for (var i = 0; i < TAB_COUNT; i++)
        {
            var tab = (StatusBookTab)i;

            if (elements.GetValueOrDefault(TabControlNames[i]) is not UIButton tabBtn)
                continue;

            TabButtons[i] = tabBtn;
            tabBtn.CenterTexture = true;

            // The prefab's image is the big/selected state — move it to SelectedTexture
            tabBtn.SelectedTexture = tabBtn.NormalTexture;

            // Load the small/normal texture from _nui_tb1.spf using the same frame index
            var frameIndex = i;

            if (PrefabSet.Contains(TabControlNames[i]))
            {
                var prefab = PrefabSet[TabControlNames[i]];

                if (prefab.Control.Images is { Count: > 0 })
                    frameIndex = prefab.Control.Images[0].FrameIndex;
            }

            tabBtn.NormalTexture = frameIndex < smallFrames.Length ? smallFrames[frameIndex] : null;

            // Wire click → switch tab
            var capturedTab = tab;
            tabBtn.OnClick += () => SwitchTab(capturedTab);

            // Set initial selection state
            tabBtn.IsSelected = tab == ActiveTab;

            // Draw tabs on top of tab page content
            tabBtn.ZIndex = 1;
        }

        CloseButton?.ZIndex = 1;

        // Lazily load tab pages
        TabPages[StatusBookTab.Equipment] = null;
        TabPages[StatusBookTab.Skills] = null;
        TabPages[StatusBookTab.Legend] = null;
        TabPages[StatusBookTab.Events] = null;
        TabPages[StatusBookTab.Album] = null;
        TabPages[StatusBookTab.Family] = null;

        // Load the default tab so content is visible immediately
        SwitchTab(StatusBookTab.Equipment);
    }

    private PrefabPanel? CreateTabPage(StatusBookTab tab)
    {
        var prefabName = tab switch
        {
            StatusBookTab.Equipment => "_nui_eq",
            StatusBookTab.Skills    => "_nui_sk",
            StatusBookTab.Legend    => "_nui_dr",
            StatusBookTab.Events    => "_nui_ev",
            StatusBookTab.Album     => "_nui_al",
            StatusBookTab.Family    => "_nui_fm",
            _                       => null
        };

        if (prefabName is null)
            return null;

        // Tab pages may not exist in all client versions
        if (DataContext.UserControls.Get(prefabName) is null)
            return null;

        PrefabPanel page = tab switch
        {
            StatusBookTab.Equipment => new SelfProfileEquipmentTab(Device, prefabName),
            StatusBookTab.Skills    => new SelfProfileAbilityMetadataTab(Device, prefabName),
            StatusBookTab.Legend    => new ProfileLegendTab(Device, prefabName),
            StatusBookTab.Events    => new SelfProfileEventsTab(Device, prefabName),
            StatusBookTab.Album     => new StatusBookTabPage(Device, prefabName),
            StatusBookTab.Family    => new SelfProfileFamilyTab(Device, prefabName),
            _                       => new StatusBookTabPage(Device, prefabName)
        };

        page.X = ContentRect.X;
        page.Y = ContentRect.Y;

        if (page is SelfProfileEquipmentTab equipTab)
            equipTab.OnUnequip += slot => OnUnequip?.Invoke(slot);

        return page;
    }

    private T? GetOrCreatePage<T>(StatusBookTab tab) where T: PrefabPanel
    {
        if (TabPages.TryGetValue(tab, out var page) && page is T existing)
            return existing;

        if (page is null)
        {
            page = CreateTabPage(tab);
            TabPages[tab] = page;

            if (page is not null)
                AddChild(page);
        }

        return page as T;
    }

    public event Action? OnClose;
    public event Action<EquipmentSlot>? OnUnequip;

    #region Events API
    /// <summary>
    ///     Sets the event/quest entries on the Events tab page.
    /// </summary>
    public void SetEvents(List<EventEntry> events)
    {
        if (GetOrCreatePage<SelfProfileEventsTab>(StatusBookTab.Events) is { } page)
            page.SetEvents(events);
    }
    #endregion

    #region Family API
    /// <summary>
    ///     Updates the Family tab with player and spouse information.
    /// </summary>
    public void SetFamilyInfo(string selfName, string spouseName)
    {
        if (GetOrCreatePage<SelfProfileFamilyTab>(StatusBookTab.Family) is { } page)
            page.SetFamilyInfo(selfName, spouseName);
    }
    #endregion

    #region Legend API
    /// <summary>
    ///     Sets the legend marks on the Legend tab page.
    /// </summary>
    public void SetLegendMarks(List<LegendMarkEntry> marks)
    {
        if (GetOrCreatePage<ProfileLegendTab>(StatusBookTab.Legend) is { } page)
            page.SetMarks(marks);
    }
    #endregion

    public void SwitchTab(StatusBookTab tab)
    {
        // Hide current tab page
        if (TabPages.TryGetValue(ActiveTab, out var currentPage) && currentPage is not null)
            currentPage.Visible = false;

        // Deselect old tab button, select new
        var oldIndex = (int)ActiveTab;
        var newIndex = (int)tab;

        if ((oldIndex < TAB_COUNT) && TabButtons[oldIndex] is not null)
            TabButtons[oldIndex]!.IsSelected = false;

        ActiveTab = tab;

        if ((newIndex < TAB_COUNT) && TabButtons[newIndex] is not null)
            TabButtons[newIndex]!.IsSelected = true;

        // Lazy-load and show the new tab page
        if (!TabPages.TryGetValue(tab, out var page) || page is null)
        {
            page = CreateTabPage(tab);
            TabPages[tab] = page;

            if (page is not null)
                AddChild(page);
        }

        page?.Visible = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (input.WasKeyPressed(Keys.Escape))
        {
            Hide();
            OnClose?.Invoke();

            return;
        }

        base.Update(gameTime, input);
    }

    #region Equipment API
    /// <summary>
    ///     Sets the item icon for a specific equipment slot. The icon is rendered from the panel item sprite sheet using
    ///     PanelItemRepository.
    /// </summary>
    public void SetEquipmentSlot(EquipmentSlot slot, ushort sprite)
    {
        var equipPage = GetOrCreateEquipmentPage();

        equipPage?.SetSlot(slot, sprite);
    }

    /// <summary>
    ///     Clears the item icon for a specific equipment slot, restoring the placeholder.
    /// </summary>
    public void ClearEquipmentSlot(EquipmentSlot slot)
    {
        var equipPage = GetOrCreateEquipmentPage();

        equipPage?.ClearSlot(slot);
    }

    /// <summary>
    ///     Bulk-refreshes all equipment slots from the provided equipment dictionary. Typically called when the status book is
    ///     first opened or after a map change.
    /// </summary>
    public void RefreshEquipment(IReadOnlyDictionary<EquipmentSlot, EquipmentInfo> equipment)
    {
        var equipPage = GetOrCreateEquipmentPage();

        if (equipPage is null)
            return;

        equipPage.ClearAllSlots();

        foreach ((var slot, var info) in equipment)
            equipPage.SetSlot(slot, info.Sprite, info.Name);
    }

    /// <summary>
    ///     Updates the stat labels on the equipment tab page.
    /// </summary>
    public void UpdateEquipmentStats(
        int str,
        int intel,
        int wis,
        int con,
        int dex,
        int ac)
    {
        var equipPage = GetOrCreateEquipmentPage();

        equipPage?.UpdateStats(
            str,
            intel,
            wis,
            con,
            dex,
            ac);
    }

    /// <summary>
    ///     Updates the player identity labels on the equipment tab page (name, class, clan, title).
    /// </summary>
    public void SetPlayerInfo(
        string name,
        string className = "",
        string clanName = "",
        string clanTitle = "",
        string title = "")
    {
        var equipPage = GetOrCreateEquipmentPage();

        equipPage?.SetPlayerInfo(
            name,
            className,
            clanName,
            clanTitle,
            title);
    }

    /// <summary>
    ///     Renders the paperdoll from the player's current appearance (same full AislingRenderer as world, frozen at idle).
    /// </summary>
    public void SetPaperdoll(AislingRenderer renderer, in AislingAppearance appearance)
    {
        var equipPage = GetOrCreateEquipmentPage();

        equipPage?.SetPaperdoll(renderer, in appearance);
    }

    /// <summary>
    ///     Sets the emoticon/social status display on the equipment page.
    /// </summary>
    public void SetEmoticonState(byte state, string statusText)
    {
        var equipPage = GetOrCreateEquipmentPage();

        equipPage?.SetEmoticonState(state, statusText);
    }

    /// <summary>
    ///     Toggles the group button between enabled and disabled states.
    /// </summary>
    public void SetGroupOpen(bool groupOpen)
    {
        var equipPage = GetOrCreateEquipmentPage();

        equipPage?.SetGroupOpen(groupOpen);
    }

    public bool ContainsEquipmentSlotPoint(int screenX, int screenY)
    {
        var equipPage = GetOrCreateEquipmentPage();

        return equipPage?.ContainsEquipmentSlotPoint(screenX, screenY) ?? false;
    }

    private SelfProfileEquipmentTab? GetOrCreateEquipmentPage()
    {
        if (TabPages.TryGetValue(StatusBookTab.Equipment, out var page) && page is SelfProfileEquipmentTab existing)
            return existing;

        // Force lazy-create if not yet created
        if (page is null)
        {
            page = CreateTabPage(StatusBookTab.Equipment);
            TabPages[StatusBookTab.Equipment] = page;

            if (page is not null)
                AddChild(page);
        }

        return page as SelfProfileEquipmentTab;
    }
    #endregion

    #region Skills API
    /// <summary>
    ///     Adds or updates a skill entry on the Skills tab page.
    /// </summary>
    public void SetSkill(
        int index,
        ushort iconSprite,
        string name,
        string level,
        bool isSpell)
    {
        if (GetOrCreatePage<SelfProfileAbilityMetadataTab>(StatusBookTab.Skills) is { } page)
            page.SetEntry(
                index,
                iconSprite,
                name,
                level,
                isSpell);
    }

    /// <summary>
    ///     Clears all skill/spell entries on the Skills tab.
    /// </summary>
    public void ClearSkills()
    {
        if (GetOrCreatePage<SelfProfileAbilityMetadataTab>(StatusBookTab.Skills) is { } page)
            page.ClearAll();
    }
    #endregion
}
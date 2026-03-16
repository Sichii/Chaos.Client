#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
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
public class StatusBookControl : PrefabPanel
{
    /// <summary>
    ///     The available tabs in the status book.
    /// </summary>
    public enum StatusBookTab
    {
        Equipment,
        Skills,
        Legend,
        Events,
        Family
    }

    private readonly GraphicsDevice DeviceRef;
    private readonly Dictionary<StatusBookTab, PrefabPanel?> TabPages = new();
    public StatusBookTab ActiveTab { get; private set; } = StatusBookTab.Equipment;

    public UIButton? CloseButton { get; }

    // Tab buttons — names are likely Tab0..Tab4 or similar from _nui_tb1/_nui_tb2 sprites
    public UIButton? EquipmentTab { get; }
    public UIButton? EventsTab { get; }
    public UIButton? FamilyTab { get; }
    public UIButton? LegendTab { get; }
    public UIButton? SkillsTab { get; }

    public StatusBookControl(GraphicsDevice device)
        : base(device, "_nui")
    {
        DeviceRef = device;
        Name = "StatusBook";
        Visible = false;

        var elements = AutoPopulate();

        CloseButton = elements.GetValueOrDefault("Close") as UIButton;

        if (CloseButton is not null)
            CloseButton.OnClick += () =>
            {
                Hide();
                OnClose?.Invoke();
            };

        // Tab buttons — try common naming patterns
        EquipmentTab = elements.GetValueOrDefault("Tab0") as UIButton ?? elements.GetValueOrDefault("Equipment") as UIButton;
        SkillsTab = elements.GetValueOrDefault("Tab1") as UIButton ?? elements.GetValueOrDefault("Skills") as UIButton;
        LegendTab = elements.GetValueOrDefault("Tab2") as UIButton ?? elements.GetValueOrDefault("Legend") as UIButton;
        EventsTab = elements.GetValueOrDefault("Tab3") as UIButton ?? elements.GetValueOrDefault("Events") as UIButton;
        FamilyTab = elements.GetValueOrDefault("Tab4") as UIButton ?? elements.GetValueOrDefault("Family") as UIButton;

        if (EquipmentTab is not null)
            EquipmentTab.OnClick += () => SwitchTab(StatusBookTab.Equipment);

        if (SkillsTab is not null)
            SkillsTab.OnClick += () => SwitchTab(StatusBookTab.Skills);

        if (LegendTab is not null)
            LegendTab.OnClick += () => SwitchTab(StatusBookTab.Legend);

        if (EventsTab is not null)
            EventsTab.OnClick += () => SwitchTab(StatusBookTab.Events);

        if (FamilyTab is not null)
            FamilyTab.OnClick += () => SwitchTab(StatusBookTab.Family);

        // Lazily load tab pages
        TabPages[StatusBookTab.Equipment] = null;
        TabPages[StatusBookTab.Skills] = null;
        TabPages[StatusBookTab.Legend] = null;
        TabPages[StatusBookTab.Events] = null;
        TabPages[StatusBookTab.Family] = null;
    }

    private PrefabPanel? CreateTabPage(StatusBookTab tab)
    {
        var prefabName = tab switch
        {
            StatusBookTab.Equipment => "_nui_eq",
            StatusBookTab.Skills    => "_nui_sk",
            StatusBookTab.Legend    => "_nui_dr",
            StatusBookTab.Events    => "_nui_ev",
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
            StatusBookTab.Equipment => new EquipmentTabPage(DeviceRef, prefabName),
            StatusBookTab.Skills    => new SkillsTabPage(DeviceRef, prefabName),
            StatusBookTab.Legend    => new LegendTabPage(DeviceRef, prefabName),
            StatusBookTab.Events    => new EventsTabPage(DeviceRef, prefabName),
            StatusBookTab.Family    => new FamilyTabPage(DeviceRef, prefabName),
            _                       => new StatusBookTabPage(DeviceRef, prefabName)
        };

        page.X = 0;
        page.Y = 0;

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

    #region Events API
    /// <summary>
    ///     Sets the event/quest entries on the Events tab page.
    /// </summary>
    public void SetEvents(List<EventEntry> events)
    {
        if (GetOrCreatePage<EventsTabPage>(StatusBookTab.Events) is { } page)
            page.SetEvents(events);
    }
    #endregion

    #region Family API
    /// <summary>
    ///     Updates the Family tab with player and spouse information.
    /// </summary>
    public void SetFamilyInfo(string selfName, string spouseName)
    {
        if (GetOrCreatePage<FamilyTabPage>(StatusBookTab.Family) is { } page)
            page.SetFamilyInfo(selfName, spouseName);
    }
    #endregion

    #region Legend API
    /// <summary>
    ///     Sets the legend marks on the Legend tab page.
    /// </summary>
    public void SetLegendMarks(List<LegendMarkEntry> marks)
    {
        if (GetOrCreatePage<LegendTabPage>(StatusBookTab.Legend) is { } page)
            page.SetMarks(marks);
    }
    #endregion

    public void SwitchTab(StatusBookTab tab)
    {
        // Hide current tab page
        if (TabPages.TryGetValue(ActiveTab, out var currentPage) && currentPage is not null)
            currentPage.Visible = false;

        ActiveTab = tab;

        // Lazy-load and show the new tab page
        if (!TabPages.TryGetValue(tab, out var page) || page is null)
        {
            page = CreateTabPage(tab);
            TabPages[tab] = page;

            if (page is not null)
                AddChild(page);
        }

        if (page is not null)
            page.Visible = true;
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
            equipPage.SetSlot(slot, info.Sprite);
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

    private EquipmentTabPage? GetOrCreateEquipmentPage()
    {
        if (TabPages.TryGetValue(StatusBookTab.Equipment, out var page) && page is EquipmentTabPage existing)
            return existing;

        // Force lazy-create if not yet created
        if (page is null)
        {
            page = CreateTabPage(StatusBookTab.Equipment);
            TabPages[StatusBookTab.Equipment] = page;

            if (page is not null)
                AddChild(page);
        }

        return page as EquipmentTabPage;
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
        if (GetOrCreatePage<SkillsTabPage>(StatusBookTab.Skills) is { } page)
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
        if (GetOrCreatePage<SkillsTabPage>(StatusBookTab.Skills) is { } page)
            page.ClearAll();
    }
    #endregion
}

/// <summary>
///     Equipment tab page within the status book, loaded from _nui_eq prefab. Displays 18 equipment slots as a paper doll
///     layout with item icons. Each slot has a fixed position from the prefab and maps to an <see cref="EquipmentSlot" />.
///     Empty slots show a placeholder icon from _nui_eqi; occupied slots show the item's panel icon.
/// </summary>
public class EquipmentTabPage : PrefabPanel
{
    /// <summary>
    ///     Maps control names from the _nui_eq prefab to their corresponding <see cref="EquipmentSlot" /> values. The control
    ///     names match those defined in the _nui_eq.txt control file: WEAPON=1, ARMOR=2, SHIELD=3, HEAD=Helmet(4),
    ///     EAR=Earrings(5), NECK=Necklace(6), LHAND=LeftRing(7), RHAND=RightRing(8), LARM=LeftGaunt(9), RARM=RightGaunt(10),
    ///     BELT=11, LEG=Greaves(12), FOOT=Boots(13), CAPE=Accessory1(14), ARMOR2=Overcoat(15), HEAD2=OverHelm(16),
    ///     CAPE2=Accessory2(17), CAPE3=Accessory3(18).
    /// </summary>
    private static readonly (string ControlName, EquipmentSlot Slot)[] SlotMappings =
    [
        ("WEAPON", EquipmentSlot.Weapon),
        ("ARMOR", EquipmentSlot.Armor),
        ("SHIELD", EquipmentSlot.Shield),
        ("HEAD", EquipmentSlot.Helmet),
        ("EAR", EquipmentSlot.Earrings),
        ("NECK", EquipmentSlot.Necklace),
        ("LHAND", EquipmentSlot.LeftRing),
        ("RHAND", EquipmentSlot.RightRing),
        ("LARM", EquipmentSlot.LeftGaunt),
        ("RARM", EquipmentSlot.RightGaunt),
        ("BELT", EquipmentSlot.Belt),
        ("LEG", EquipmentSlot.Greaves),
        ("FOOT", EquipmentSlot.Boots),
        ("CAPE", EquipmentSlot.Accessory1),
        ("ARMOR2", EquipmentSlot.Overcoat),
        ("HEAD2", EquipmentSlot.OverHelm),
        ("CAPE2", EquipmentSlot.Accessory2),
        ("CAPE3", EquipmentSlot.Accessory3)
    ];

    private readonly UILabel? AcLabel;
    private readonly UILabel? ClanLabel;
    private readonly UILabel? ClanTitleLabel;
    private readonly UILabel? ClassLabel;
    private readonly UILabel? ConLabel;

    private readonly GraphicsDevice DeviceRef;
    private readonly UILabel? DexLabel;
    private readonly UILabel? IntLabel;

    // Player info labels
    private readonly UILabel? NameLabel;

    // Equipment slot rendering: maps EquipmentSlot to its visual state
    private readonly Dictionary<EquipmentSlot, EquipmentSlotVisual> SlotVisuals = new();

    // Stat labels from the _nui_eq prefab (N_ prefix)
    private readonly UILabel? StrLabel;
    private readonly UILabel? TitleLabel;
    private readonly UILabel? WisLabel;

    public EquipmentTabPage(GraphicsDevice device, string prefabName)
        : base(device, prefabName, false)
    {
        DeviceRef = device;
        Name = prefabName;
        Visible = false;

        // Create all non-anchor controls via AutoPopulate, then extract the slot elements
        var elements = AutoPopulate();

        // Build slot visuals from the prefab-created elements.
        // AutoPopulate creates UIImage elements for DoesNotReturnValue controls that have images.
        // Each slot image initially shows its _nui_eqi placeholder icon.
        foreach ((var controlName, var slot) in SlotMappings)
        {
            if (!elements.TryGetValue(controlName, out var element))
                continue;

            if (element is not UIImage slotImage)
                continue;

            // The placeholder texture was already set by AutoPopulate from the _nui_eqi frame
            var visual = new EquipmentSlotVisual
            {
                Image = slotImage,
                PlaceholderTexture = slotImage.Texture
            };

            SlotVisuals[slot] = visual;
        }

        // Stat labels — the _nui_eq prefab defines these with N_ prefix as DoesNotReturnValue
        // AutoPopulate creates UIImages for them (no text content). We need UILabels instead.
        StrLabel = FindOrCreateLabel(elements, "N_STR");
        IntLabel = FindOrCreateLabel(elements, "N_INT");
        WisLabel = FindOrCreateLabel(elements, "N_WIS");
        ConLabel = FindOrCreateLabel(elements, "N_CON");
        DexLabel = FindOrCreateLabel(elements, "N_DEX");
        AcLabel = FindOrCreateLabel(elements, "N_AC");

        // Player info labels
        NameLabel = FindOrCreateLabel(elements, "NAME");
        ClassLabel = FindOrCreateLabel(elements, "CLASSTEXT");
        ClanLabel = FindOrCreateLabel(elements, "CLANTEXT");
        ClanTitleLabel = FindOrCreateLabel(elements, "CLANTITLETEXT");
        TitleLabel = FindOrCreateLabel(elements, "TITLETEXT");
    }

    /// <summary>
    ///     Clears all equipment slot icons, restoring placeholders.
    /// </summary>
    public void ClearAllSlots()
    {
        foreach ((_, var visual) in SlotVisuals)
        {
            if (visual.ItemTexture is not null)
            {
                visual.ItemTexture.Dispose();
                visual.ItemTexture = null;
            }

            visual.Image.Texture = visual.PlaceholderTexture;
        }
    }

    /// <summary>
    ///     Clears the item icon for a specific equipment slot, restoring the placeholder.
    /// </summary>
    public void ClearSlot(EquipmentSlot slot)
    {
        if (!SlotVisuals.TryGetValue(slot, out var visual))
            return;

        if (visual.ItemTexture is not null)
        {
            visual.ItemTexture.Dispose();
            visual.ItemTexture = null;
        }

        visual.Image.Texture = visual.PlaceholderTexture;
    }

    public override void Dispose()
    {
        // Dispose only item textures — placeholder textures belong to the UIImage children
        // and will be disposed via the base UIPanel.Dispose() -> Children.Dispose() chain
        foreach ((_, var visual) in SlotVisuals)
            if (visual.ItemTexture is not null)
            {
                // If the UIImage is currently showing the item texture, null it so the
                // base dispose doesn't double-dispose
                if (visual.Image.Texture == visual.ItemTexture)
                    visual.Image.Texture = null;

                visual.ItemTexture.Dispose();
                visual.ItemTexture = null;
            }

        SlotVisuals.Clear();

        base.Dispose();
    }

    /// <summary>
    ///     Finds an existing UILabel from AutoPopulate results, or creates one from the prefab if it was created as a UIImage
    ///     (DoesNotReturnValue type with no images = no-op). For stat/text areas that need label behavior.
    /// </summary>
    private UILabel? FindOrCreateLabel(Dictionary<string, UIElement> elements, string name)
    {
        // If AutoPopulate already created a UILabel for this control, use it
        if (elements.TryGetValue(name, out var element) && element is UILabel existingLabel)
            return existingLabel;

        // Otherwise, create a label at this control's rect position
        return CreateLabel(name, TextAlignment.Right);
    }

    /// <summary>
    ///     Renders an item icon from the panel item sprite sheet using the same pipeline as inventory icons.
    /// </summary>
    private Texture2D? RenderItemIcon(ushort spriteId)
        => TextureConverter.RenderSprite(DeviceRef, DataContext.PanelItems.GetPanelItemSprite(spriteId));

    /// <summary>
    ///     Updates the player identity labels (name, class, clan, title).
    /// </summary>
    public void SetPlayerInfo(
        string name,
        string className,
        string clanName,
        string clanTitle,
        string title)
    {
        NameLabel?.SetText(name);
        ClassLabel?.SetText(className);
        ClanLabel?.SetText(clanName);
        ClanTitleLabel?.SetText(clanTitle);
        TitleLabel?.SetText(title);
    }

    /// <summary>
    ///     Sets the item icon for a specific equipment slot.
    /// </summary>
    public void SetSlot(EquipmentSlot slot, ushort sprite)
    {
        if (!SlotVisuals.TryGetValue(slot, out var visual))
            return;

        // Dispose previous item texture (not the placeholder — that's shared/owned by the prefab)
        if (visual.ItemTexture is not null)
        {
            visual.ItemTexture.Dispose();
            visual.ItemTexture = null;
        }

        var texture = RenderItemIcon(sprite);

        if (texture is not null)
        {
            visual.ItemTexture = texture;
            visual.Image.Texture = texture;
        } else

            // Failed to render — show placeholder
            visual.Image.Texture = visual.PlaceholderTexture;
    }

    /// <summary>
    ///     Updates the stat display labels on the equipment page.
    /// </summary>
    public void UpdateStats(
        int str,
        int intel,
        int wis,
        int con,
        int dex,
        int ac)
    {
        StrLabel?.SetText($"{str}");
        IntLabel?.SetText($"{intel}");
        WisLabel?.SetText($"{wis}");
        ConLabel?.SetText($"{con}");
        DexLabel?.SetText($"{dex}");
        AcLabel?.SetText($"{ac}");
    }

    /// <summary>
    ///     Tracks the visual state of a single equipment slot.
    /// </summary>
    private sealed class EquipmentSlotVisual
    {
        /// <summary>
        ///     The UIImage element created by AutoPopulate, positioned at the prefab-defined rect.
        /// </summary>
        public required UIImage Image { get; init; }

        /// <summary>
        ///     The currently rendered item icon texture, or null if the slot is empty. Owned by this visual and must be disposed
        ///     when replaced or cleared.
        /// </summary>
        public Texture2D? ItemTexture { get; set; }

        /// <summary>
        ///     The placeholder texture from the _nui_eqi sprite, shown when the slot is empty. This texture is owned by the
        ///     UIImage and disposed via the normal UIPanel dispose chain.
        /// </summary>
        public Texture2D? PlaceholderTexture { get; init; }
    }
}

/// <summary>
///     A generic tab page within the status book. Loaded from a tab-specific prefab (_nui_sk, _nui_dr, etc.).
/// </summary>
public class StatusBookTabPage : PrefabPanel
{
    public StatusBookTabPage(GraphicsDevice device, string prefabName)
        : base(device, prefabName, false)
    {
        Name = prefabName;
        Visible = false;

        AutoPopulate();
    }
}

/// <summary>
///     Skills tab page (_nui_sk). Two-column layout: SPELL (left) and SKILL (right). Each column holds rows of skill/spell
///     entries rendered using _nui_ski template (32x32 icon, name, level text, 43px per row).
/// </summary>
public class SkillsTabPage : PrefabPanel
{
    private const int ROW_HEIGHT = 43;
    private const int ICON_SIZE = 32;
    private const int ICON_X = 7;
    private const int ICON_Y = 7;
    private const int NAME_X = 48;
    private const int NAME_Y = 7;
    private const int LEVEL_X = 48;
    private const int LEVEL_Y = 27;
    private const int MAX_ENTRIES_PER_COLUMN = 5;

    private readonly GraphicsDevice DeviceRef;
    private readonly List<SkillSpellEntry> SkillEntries = [];
    private readonly Texture2D?[] SkillIcons;
    private readonly CachedText[] SkillLevelCaches;
    private readonly CachedText[] SkillNameCaches;
    private readonly Rectangle SkillRect;

    private readonly List<SkillSpellEntry> SpellEntries = [];
    private readonly Texture2D?[] SpellIcons;
    private readonly CachedText[] SpellLevelCaches;

    private readonly CachedText[] SpellNameCaches;
    private readonly Rectangle SpellRect;

    private int DataVersion;
    private int RenderedVersion = -1;

    public SkillsTabPage(GraphicsDevice device, string prefabName)
        : base(device, prefabName, false)
    {
        DeviceRef = device;
        Name = prefabName;
        Visible = false;

        AutoPopulate();

        SpellRect = GetRect("SPELL");
        SkillRect = GetRect("SKILL");

        // Default rects if not found
        if (SpellRect == Rectangle.Empty)
            SpellRect = new Rectangle(
                32,
                33,
                233,
                239);

        if (SkillRect == Rectangle.Empty)
            SkillRect = new Rectangle(
                331,
                33,
                233,
                239);

        SpellNameCaches = new CachedText[MAX_ENTRIES_PER_COLUMN];
        SpellLevelCaches = new CachedText[MAX_ENTRIES_PER_COLUMN];
        SkillNameCaches = new CachedText[MAX_ENTRIES_PER_COLUMN];
        SkillLevelCaches = new CachedText[MAX_ENTRIES_PER_COLUMN];
        SpellIcons = new Texture2D?[MAX_ENTRIES_PER_COLUMN];
        SkillIcons = new Texture2D?[MAX_ENTRIES_PER_COLUMN];

        for (var i = 0; i < MAX_ENTRIES_PER_COLUMN; i++)
        {
            SpellNameCaches[i] = new CachedText(device);
            SpellLevelCaches[i] = new CachedText(device);
            SkillNameCaches[i] = new CachedText(device);
            SkillLevelCaches[i] = new CachedText(device);
        }
    }

    /// <summary>
    ///     Clears all entries from both columns.
    /// </summary>
    public void ClearAll()
    {
        SpellEntries.Clear();
        SkillEntries.Clear();
        DataVersion++;
    }

    public override void Dispose()
    {
        foreach (var c in SpellNameCaches)
            c.Dispose();

        foreach (var c in SpellLevelCaches)
            c.Dispose();

        foreach (var c in SkillNameCaches)
            c.Dispose();

        foreach (var c in SkillLevelCaches)
            c.Dispose();

        foreach (var t in SpellIcons)
            t?.Dispose();

        foreach (var t in SkillIcons)
            t?.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        RefreshCaches();

        var sx = ScreenX;
        var sy = ScreenY;

        DrawColumn(
            spriteBatch,
            SpellEntries,
            SpellIcons,
            SpellNameCaches,
            SpellLevelCaches,
            sx + SpellRect.X,
            sy + SpellRect.Y);

        DrawColumn(
            spriteBatch,
            SkillEntries,
            SkillIcons,
            SkillNameCaches,
            SkillLevelCaches,
            sx + SkillRect.X,
            sy + SkillRect.Y);
    }

    private void DrawColumn(
        SpriteBatch spriteBatch,
        List<SkillSpellEntry> entries,
        Texture2D?[] icons,
        CachedText[] nameCaches,
        CachedText[] levelCaches,
        int colX,
        int colY)
    {
        for (var i = 0; (i < MAX_ENTRIES_PER_COLUMN) && (i < entries.Count); i++)
        {
            var rowY = colY + i * ROW_HEIGHT;

            if (icons[i] is { } icon)
                spriteBatch.Draw(icon, new Vector2(colX + ICON_X, rowY + ICON_Y), Color.White);

            nameCaches[i]
                .Draw(spriteBatch, new Vector2(colX + NAME_X, rowY + NAME_Y));

            levelCaches[i]
                .Draw(spriteBatch, new Vector2(colX + LEVEL_X, rowY + LEVEL_Y));
        }
    }

    private void RefreshCaches()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        RefreshColumnCaches(
            SpellEntries,
            SpellIcons,
            SpellNameCaches,
            SpellLevelCaches);

        RefreshColumnCaches(
            SkillEntries,
            SkillIcons,
            SkillNameCaches,
            SkillLevelCaches);
    }

    private void RefreshColumnCaches(
        List<SkillSpellEntry> entries,
        Texture2D?[] icons,
        CachedText[] nameCaches,
        CachedText[] levelCaches)
    {
        for (var i = 0; i < MAX_ENTRIES_PER_COLUMN; i++)
            if ((i < entries.Count) && entries[i].Name is not null)
            {
                nameCaches[i]
                    .Update(entries[i].Name!, 0, Color.White);

                levelCaches[i]
                    .Update(entries[i].Level ?? string.Empty, 0, new Color(200, 200, 200));

                if (icons[i] is null && (entries[i].IconSprite > 0))
                    icons[i] = TextureConverter.RenderSprite(DeviceRef, DataContext.PanelIcons.GetSkillIcon(entries[i].IconSprite));
            } else
            {
                nameCaches[i]
                    .Update(string.Empty, 0, Color.White);

                levelCaches[i]
                    .Update(string.Empty, 0, Color.White);
            }
    }

    /// <summary>
    ///     Adds or updates a skill/spell entry. Spells go in the left column, skills in the right.
    /// </summary>
    public void SetEntry(
        int index,
        ushort iconSprite,
        string name,
        string level,
        bool isSpell)
    {
        var list = isSpell ? SpellEntries : SkillEntries;

        while (list.Count <= index)
            list.Add(new SkillSpellEntry());

        list[index] = new SkillSpellEntry
        {
            IconSprite = iconSprite,
            Name = name,
            Level = level
        };
        DataVersion++;
    }

    private struct SkillSpellEntry
    {
        public ushort IconSprite;
        public string? Name;
        public string? Level;
    }
}

/// <summary>
///     Legend tab page (_nui_dr). Full-width LegendList area (524x237) displaying legend mark entries with icons, colored
///     text, and timestamps. Legend marks come from SelfProfile/OtherProfile packets.
/// </summary>
public class LegendTabPage : PrefabPanel
{
    private const int ROW_HEIGHT = 16;
    private const int ICON_SIZE = 16;
    private const int ICON_X = 6;
    private const int TEXT_X = 28;

    private readonly GraphicsDevice DeviceRef;
    private readonly Rectangle LegendListRect;
    private readonly int MaxVisibleRows;

    private readonly CachedText[] TextCaches;
    private int DataVersion;
    private List<LegendMarkEntry> Marks = [];
    private int RenderedVersion = -1;
    private int ScrollOffset;

    public LegendTabPage(GraphicsDevice device, string prefabName)
        : base(device, prefabName, false)
    {
        DeviceRef = device;
        Name = prefabName;
        Visible = false;

        AutoPopulate();

        LegendListRect = GetRect("LegendList");

        if (LegendListRect == Rectangle.Empty)
            LegendListRect = new Rectangle(
                38,
                33,
                524,
                237);

        MaxVisibleRows = LegendListRect.Height / ROW_HEIGHT;
        TextCaches = new CachedText[MaxVisibleRows];

        for (var i = 0; i < MaxVisibleRows; i++)
            TextCaches[i] = new CachedText(device);
    }

    public override void Dispose()
    {
        foreach (var c in TextCaches)
            c.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        RefreshCaches();

        var listX = ScreenX + LegendListRect.X;
        var listY = ScreenY + LegendListRect.Y;

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            var markIndex = ScrollOffset + i;

            if (markIndex >= Marks.Count)
                break;

            var rowY = listY + i * ROW_HEIGHT;

            // Legend text (colored)
            TextCaches[i]
                .Draw(spriteBatch, new Vector2(listX + TEXT_X, rowY + 2));
        }
    }

    private void RefreshCaches()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            var markIndex = ScrollOffset + i;

            if (markIndex < Marks.Count)
            {
                var mark = Marks[markIndex];

                TextCaches[i]
                    .Update(mark.Text, 0, mark.Color);
            } else
                TextCaches[i]
                    .Update(string.Empty, 0, Color.White);
        }
    }

    /// <summary>
    ///     Sets the legend mark entries to display.
    /// </summary>
    public void SetMarks(List<LegendMarkEntry> marks)
    {
        Marks = marks;
        ScrollOffset = 0;
        DataVersion++;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        base.Update(gameTime, input);

        // Scroll wheel
        if ((input.ScrollDelta != 0) && (Marks.Count > MaxVisibleRows))
        {
            ScrollOffset = Math.Clamp(ScrollOffset - input.ScrollDelta, 0, Marks.Count - MaxVisibleRows);
            DataVersion++;
        }
    }
}

/// <summary>
///     Events/quest tab page (_nui_ev). Two-column layout: EV1 (left) and EV2 (right). NEXT/PREV pagination buttons for
///     multi-page event lists. Each event entry shows an icon and name.
/// </summary>
public class EventsTabPage : PrefabPanel
{
    private const int ROW_HEIGHT = 16;
    private const int MAX_PER_COLUMN = 14;
    private const int MAX_PER_PAGE = 28;

    private readonly GraphicsDevice DeviceRef;

    private readonly CachedText[] Ev1Caches;
    private readonly Rectangle Ev1Rect;
    private readonly CachedText[] Ev2Caches;
    private readonly Rectangle Ev2Rect;
    private readonly UIButton? NextButton;
    private readonly UIButton? PrevButton;
    private int CurrentPage;
    private int DataVersion;

    private List<EventEntry> Events = [];
    private int RenderedVersion = -1;

    public EventsTabPage(GraphicsDevice device, string prefabName)
        : base(device, prefabName, false)
    {
        DeviceRef = device;
        Name = prefabName;
        Visible = false;

        var elements = AutoPopulate();

        Ev1Rect = GetRect("EV1");
        Ev2Rect = GetRect("EV2");

        if (Ev1Rect == Rectangle.Empty)
            Ev1Rect = new Rectangle(
                32,
                33,
                233,
                239);

        if (Ev2Rect == Rectangle.Empty)
            Ev2Rect = new Rectangle(
                331,
                33,
                233,
                239);

        NextButton = elements.GetValueOrDefault("NEXT") as UIButton;
        PrevButton = elements.GetValueOrDefault("PREV") as UIButton;

        if (NextButton is not null)
            NextButton.OnClick += () =>
            {
                if (((CurrentPage + 1) * MAX_PER_PAGE) < Events.Count)
                {
                    CurrentPage++;
                    DataVersion++;
                }
            };

        if (PrevButton is not null)
            PrevButton.OnClick += () =>
            {
                if (CurrentPage > 0)
                {
                    CurrentPage--;
                    DataVersion++;
                }
            };

        Ev1Caches = new CachedText[MAX_PER_COLUMN];
        Ev2Caches = new CachedText[MAX_PER_COLUMN];

        for (var i = 0; i < MAX_PER_COLUMN; i++)
        {
            Ev1Caches[i] = new CachedText(device);
            Ev2Caches[i] = new CachedText(device);
        }
    }

    public override void Dispose()
    {
        foreach (var c in Ev1Caches)
            c.Dispose();

        foreach (var c in Ev2Caches)
            c.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        RefreshCaches();

        var sx = ScreenX;
        var sy = ScreenY;

        DrawColumn(
            spriteBatch,
            Ev1Caches,
            sx + Ev1Rect.X,
            sy + Ev1Rect.Y,
            0);

        DrawColumn(
            spriteBatch,
            Ev2Caches,
            sx + Ev2Rect.X,
            sy + Ev2Rect.Y,
            MAX_PER_COLUMN);
    }

    private void DrawColumn(
        SpriteBatch spriteBatch,
        CachedText[] caches,
        int colX,
        int colY,
        int colOffset)
    {
        for (var i = 0; i < MAX_PER_COLUMN; i++)
            caches[i]
                .Draw(spriteBatch, new Vector2(colX + 4, colY + i * ROW_HEIGHT + 2));
    }

    private void RefreshCaches()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        var pageStart = CurrentPage * MAX_PER_PAGE;

        for (var i = 0; i < MAX_PER_COLUMN; i++)
        {
            var leftIndex = pageStart + i;
            var rightIndex = pageStart + MAX_PER_COLUMN + i;

            Ev1Caches[i]
                .Update(leftIndex < Events.Count ? Events[leftIndex].Name : string.Empty, 0, Color.White);

            Ev2Caches[i]
                .Update(rightIndex < Events.Count ? Events[rightIndex].Name : string.Empty, 0, Color.White);
        }
    }

    /// <summary>
    ///     Sets the event/quest entries.
    /// </summary>
    public void SetEvents(List<EventEntry> events)
    {
        Events = events;
        CurrentPage = 0;
        DataVersion++;
    }
}

/// <summary>
///     Family tab page (_nui_fm). Displays player and spouse name, plus 10 family stat labels.
/// </summary>
public class FamilyTabPage : PrefabPanel
{
    private readonly UILabel? FamilyLabel;
    private readonly UILabel? SelfLabel;
    private readonly UILabel?[] TextLabels = new UILabel?[10];

    public FamilyTabPage(GraphicsDevice device, string prefabName)
        : base(device, prefabName, false)
    {
        Name = prefabName;
        Visible = false;

        AutoPopulate();

        SelfLabel = CreateLabel("Self");
        FamilyLabel = CreateLabel("Family");

        for (var i = 0; i < 10; i++)
            TextLabels[i] = CreateLabel($"Text{i}");
    }

    /// <summary>
    ///     Updates the player and spouse names.
    /// </summary>
    public void SetFamilyInfo(string selfName, string spouseName)
    {
        SelfLabel?.SetText(selfName);
        FamilyLabel?.SetText(spouseName);
    }

    /// <summary>
    ///     Sets a specific family stat text field (0-9).
    /// </summary>
    public void SetTextField(int index, string text)
    {
        if ((index >= 0) && (index < 10))
            TextLabels[index]
                ?.SetText(text);
    }
}

/// <summary>
///     A single legend mark entry for the Legend tab page.
/// </summary>
public record LegendMarkEntry(
    string Text,
    Color Color,
    byte Icon = 0,
    string Key = "");

/// <summary>
///     A single event/quest entry for the Events tab page.
/// </summary>
public record EventEntry(string Name, ushort IconSprite = 0, string Description = "");
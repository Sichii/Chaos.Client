#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Models;
using Chaos.Client.Rendering;
using Chaos.Networking.Entities.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Profile;

/// <summary>
///     Tab-based profile viewer for other players, using the _nui prefab. Only enables Equipment and Legend tabs (plus
///     Close). Equipment tab uses _nui_eqa (no stats), legend tab reuses <see cref="SelfProfileLegendTab" />.
/// </summary>
public sealed class OtherProfileTabControl : PrefabPanel
{
    private static readonly (string ControlName, StatusBookTab Tab)[] EnabledTabs =
    [
        ("TAB_INTRO", StatusBookTab.Equipment),
        ("TAB_LEGEND", StatusBookTab.Legend)
    ];

    private readonly Rectangle ContentRect;
    private readonly UIButton?[] TabButtons = new UIButton?[EnabledTabs.Length];
    private readonly Dictionary<StatusBookTab, PrefabPanel?> TabPages = new();

    private StatusBookTab ActiveTab = StatusBookTab.Equipment;

    public UIButton? CloseButton { get; }

    public OtherProfileTabControl()
        : base("_nui", false)
    {
        Name = "OtherProfile";
        Visible = false;
        X = 0;
        Y = 0;

        ContentRect = GetRect("CONTENT");

        // Close button
        CloseButton = CreateButton("TAB_CLOSE");

        if (CloseButton is not null)
            CloseButton.OnClick += Hide;

        // Only create Equipment + Legend tab buttons
        var cache = UiRenderer.Instance!;

        for (var i = 0; i < EnabledTabs.Length; i++)
        {
            (var controlName, var tab) = EnabledTabs[i];

            if (CreateButton(controlName) is not { } tabBtn)
                continue;

            TabButtons[i] = tabBtn;
            tabBtn.CenterTexture = true;

            // Prefab image is big/selected — swap to SelectedTexture
            tabBtn.SelectedTexture = tabBtn.NormalTexture;

            // Load small/normal texture from _nui_tb1.spf
            var frameIndex = (int)tab;

            if (PrefabSet.Contains(controlName))
            {
                var prefab = PrefabSet[controlName];

                if (prefab.Control.Images is { Count: > 0 })
                    frameIndex = prefab.Control.Images[0].FrameIndex;
            }

            tabBtn.NormalTexture = cache.GetSpfTexture("_nui_tb1.spf", frameIndex);

            var capturedTab = tab;
            tabBtn.OnClick += () => SwitchTab(capturedTab);

            tabBtn.IsSelected = tab == ActiveTab;
            tabBtn.ZIndex = 1;
        }

        CloseButton?.ZIndex = 1;

        TabPages[StatusBookTab.Equipment] = null;
        TabPages[StatusBookTab.Legend] = null;

        SwitchTab(StatusBookTab.Equipment);
    }

    private PrefabPanel? CreateTabPage(StatusBookTab tab)
    {
        var prefabName = tab switch
        {
            StatusBookTab.Equipment => "_nui_eqa",
            StatusBookTab.Legend    => "_nui_dr",
            _                       => null
        };

        if (prefabName is null)
            return null;

        if (DataContext.UserControls.Get(prefabName) is null)
            return null;

        PrefabPanel page = tab switch
        {
            StatusBookTab.Equipment => new OtherProfileEquipmentTab(prefabName),
            StatusBookTab.Legend    => new SelfProfileLegendTab(prefabName),
            _                       => new StatusBookTabPage(prefabName)
        };

        page.X = ContentRect.X;
        page.Y = ContentRect.Y;

        return page;
    }

    private static int FindTabIndex(StatusBookTab tab)
    {
        for (var i = 0; i < EnabledTabs.Length; i++)
            if (EnabledTabs[i].Tab == tab)
                return i;

        return -1;
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

    public new void Hide() => Visible = false;

    /// <summary>
    ///     Populates and shows the other player's profile.
    /// </summary>
    public void Show(OtherProfileArgs args, List<LegendMarkEntry> legendMarks, AislingRenderer aislingRenderer)
    {
        // Equipment tab
        var equipPage = GetOrCreatePage<OtherProfileEquipmentTab>(StatusBookTab.Equipment);

        if (equipPage is not null)
        {
            equipPage.SetPlayerInfo(
                args.Name,
                args.DisplayClass,
                args.GuildName ?? string.Empty,
                args.GuildRank ?? string.Empty,
                args.Title ?? string.Empty);

            equipPage.SetEquipment(args.Equipment);
            equipPage.SetGroupOpen(args.GroupOpen);
            equipPage.SetNation((byte)args.Nation);
            equipPage.SetEmoticonState((byte)args.SocialStatus, args.SocialStatus.ToString());

            // Paperdoll from the entity's current appearance on the map
            var entity = WorldState.GetEntity(args.Id);

            if (entity?.Appearance is { } appearance)
                equipPage.SetPaperdoll(aislingRenderer, in appearance);
        }

        // Legend tab
        var legendPage = GetOrCreatePage<SelfProfileLegendTab>(StatusBookTab.Legend);

        legendPage?.SetMarks(legendMarks);

        SwitchTab(StatusBookTab.Equipment);
        Visible = true;
    }

    public void SwitchTab(StatusBookTab tab)
    {
        // Only allow Equipment or Legend
        if (tab is not (StatusBookTab.Equipment or StatusBookTab.Legend))
            return;

        // Hide current tab page
        if (TabPages.TryGetValue(ActiveTab, out var currentPage) && currentPage is not null)
            currentPage.Visible = false;

        // Deselect old tab, select new
        var oldIndex = FindTabIndex(ActiveTab);
        var newIndex = FindTabIndex(tab);

        if ((oldIndex >= 0) && TabButtons[oldIndex] is not null)
            TabButtons[oldIndex]!.IsSelected = false;

        ActiveTab = tab;

        if ((newIndex >= 0) && TabButtons[newIndex] is not null)
            TabButtons[newIndex]!.IsSelected = true;

        // Lazy-load and show
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

            return;
        }

        base.Update(gameTime, input);
    }
}
#region
using Chaos.Client.Controls.Components;
using Chaos.Networking.Entities.Server;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Tabbed group window using _ngcmain prefab. Tab 0 = group member list, Tab 1 = group recruitment config. Hosts
///     <see cref="GroupTab" /> and <see cref="GroupRecruitPanel" /> as child panels swapped by tab selection.
/// </summary>
public sealed class GroupTabControl : PrefabPanel
{
    public GroupTab MembersPanel { get; }
    public GroupRecruitPanel RecruitPanel { get; }

    private UIButton? Tab0Button { get; }
    private UIButton? Tab1Button { get; }

    public GroupTabControl()
        : base("_ngcmain", false)
    {
        Name = "GroupMain";
        Visible = false;
        UsesControlStack = true;
        X = 0;
        Y = 0;

        var cache = UiRenderer.Instance!;

        // Tab buttons — prefab image is _ngcbtnb.spf (selected/big), swap to SelectedTexture
        // and load _ngcbtns.spf (normal/small) from TAB0_S/TAB1_S prefab entries as NormalTexture
        Tab0Button = CreateButton("TAB0");

        if (Tab0Button is not null)
        {
            Tab0Button.CenterTexture = true;
            Tab0Button.SelectedTexture = Tab0Button.NormalTexture;
            Tab0Button.NormalTexture = cache.GetPrefabTexture("_ngcmain", "TAB0_S", 0);
            Tab0Button.Clicked += () => SwitchTab(0);
        }

        Tab1Button = CreateButton("TAB1");

        if (Tab1Button is not null)
        {
            Tab1Button.CenterTexture = true;
            Tab1Button.SelectedTexture = Tab1Button.NormalTexture;
            Tab1Button.NormalTexture = cache.GetPrefabTexture("_ngcmain", "TAB1_S", 0);
            Tab1Button.Clicked += () => SwitchTab(1);
        }

        // Create child panels — positioned at DLGFRAME rect (0,0 within the main container)
        MembersPanel = new GroupTab();
        MembersPanel.Visible = true;

        RecruitPanel = new GroupRecruitPanel();
        RecruitPanel.Visible = false;

        AddChild(MembersPanel);
        AddChild(RecruitPanel);

        // Wire close events from child panels
        MembersPanel.OnClose += () =>
        {
            Hide();
            OnClose?.Invoke();
        };

        RecruitPanel.OnClose += () =>
        {
            Hide();
            OnClose?.Invoke();
        };

        SwitchTab(0);
    }

    public override void Dispose()
    {
        MembersPanel.Dispose();
        RecruitPanel.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);
    }

    public event Action? OnClose;

    /// <summary>
    ///     Shows the window on the members tab.
    /// </summary>
    public void ShowMembers()
    {
        SwitchTab(0);
        Show();
    }

    /// <summary>
    ///     Shows the window on the recruit tab in owner mode.
    /// </summary>
    public void ShowRecruitOwner()
    {
        SwitchTab(1);
        RecruitPanel.ShowAsOwner();
        Show();
    }

    /// <summary>
    ///     Shows the window on the recruit tab in owner-edit mode, populated from server data.
    /// </summary>
    public void ShowRecruitOwnerEdit(DisplayGroupBoxInfo info)
    {
        SwitchTab(1);
        RecruitPanel.ShowAsOwnerEdit(info);
        Show();
    }

    /// <summary>
    ///     Shows the window on the recruit tab in viewer mode, populated from server data.
    /// </summary>
    public void ShowRecruitViewer(string sourceName, DisplayGroupBoxInfo info)
    {
        SwitchTab(1);
        RecruitPanel.ShowAsViewer(sourceName, info);
        Show();
    }

    private void SwitchTab(int tab)
    {
        MembersPanel.Visible = tab == 0;
        RecruitPanel.Visible = tab == 1;

        if (Tab0Button is not null)
        {
            Tab0Button.IsSelected = tab == 0;
            Tab0Button.ZIndex = tab == 0 ? 1 : -1;
        }

        if (Tab1Button is not null)
        {
            Tab1Button.IsSelected = tab == 1;
            Tab1Button.ZIndex = tab == 1 ? 1 : -1;
        }
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            Hide();
            OnClose?.Invoke();
            e.Handled = true;
        }
    }
}
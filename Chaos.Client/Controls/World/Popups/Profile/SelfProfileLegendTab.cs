#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Scrolling;
using Chaos.Client.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Popups.Profile;

/// <summary>
///     Legend tab page (_nui_dr). Displays legend mark entries with icons and colored text.
///     Exactly 12 rows visible, with scroll support.
/// </summary>
public sealed class SelfProfileLegendTab : PrefabPanel
{
    private const int MAX_VISIBLE_ROWS = 12;

    private readonly Texture2D[] IconFrames;
    private readonly VirtualizedRowList<LegendMarkEntry> RowList;

    public SelfProfileLegendTab(string prefabName)
        : base(prefabName, false)
    {
        Name = prefabName;
        Visible = false;

        var legendListRect = GetRect("LegendList");

        if (legendListRect == Rectangle.Empty)
            legendListRect = new Rectangle(
                38,
                33,
                524,
                237);

        var rowHeight = legendListRect.Height / MAX_VISIBLE_ROWS;

        //legend mark icons from legends.epf
        var cache = UiRenderer.Instance!;
        var frameCount = cache.GetEpfFrameCount("legends.epf");
        IconFrames = new Texture2D[frameCount];

        for (var i = 0; i < frameCount; i++)
            IconFrames[i] = cache.GetEpfTexture("legends.epf", i);

        //display-only virtualized list (no selection) hosted in a scroll viewer; rows keep the original 1px gap.
        RowList = new VirtualizedRowList<LegendMarkEntry>(
            legendListRect.Width,
            legendListRect.Height,
            rowHeight,
            static () => new LegendMarkControl(),
            BindRow,
            rowGap: 1);

        var viewer = new ScrollViewerControl(RowList)
        {
            X = legendListRect.X,
            Y = legendListRect.Y,
            Width = legendListRect.Width,
            Height = legendListRect.Height
        };

        AddChild(viewer);
    }

    private void BindRow(UIElement row, LegendMarkEntry mark, bool selected)
    {
        var legendRow = (LegendMarkControl)row;
        var icon = mark.Icon < IconFrames.Length ? IconFrames[mark.Icon] : null;
        var iconWidth = icon?.Width ?? 21;
        var iconHeight = icon?.Height ?? 20;

        legendRow.SetMark(
            icon,
            mark.Text,
            mark.Color,
            iconWidth,
            iconHeight);
    }

    public override void Dispose()
    {
        foreach (var t in IconFrames)
            t.Dispose();

        base.Dispose();
    }

    /// <summary>
    ///     Sets the legend mark entries to display.
    /// </summary>
    public void SetMarks(List<LegendMarkEntry> marks) => RowList.SetItems(marks);
}

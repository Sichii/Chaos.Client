#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data.Models;
using Chaos.Networking.Entities.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud.Panel;

/// <summary>
///     Character stats panel. Normal HUD loads from _nstatus, large HUD loads from _nstatur (compact).
///     In the large HUD, expanding switches to the full _nstatus layout.
/// </summary>
public sealed class StatsPanel : ExpandablePanel
{
    private const int IDX_STR = 0;
    private const int IDX_INT = 1;
    private const int IDX_WIS = 2;
    private const int IDX_CON = 3;
    private const int IDX_DEX = 4;
    private const int IDX_HP = 5;
    private const int IDX_HP_MAX = 6;
    private const int IDX_MP = 7;
    private const int IDX_MP_MAX = 8;
    private const int IDX_EXP = 9;
    private const int IDX_AB_EXP = 10;
    private const int IDX_GOLD = 11;
    private const int IDX_GP = 12;
    private const int IDX_LEV = 13;
    private const int IDX_NEXT_LEV = 14;
    private const int IDX_AB = 15;
    private const int IDX_NEXT_AB = 16;
    private const int LABEL_COUNT = 17;

    private static readonly string[] LABEL_NAMES =
    [
        "s_Str",
        "s_Int",
        "s_Wis",
        "s_Con",
        "s_Dex",
        "s_HP",
        "s_HPMax",
        "s_MP",
        "s_MPMax",
        "s_EXP",
        "s_AEXP",
        "s_Gold",
        "s_GP",
        "s_Lev",
        "s_nextLev",
        "s_Ab",
        "s_nextAb"
    ];

    private readonly UILabel?[] Labels = new UILabel?[LABEL_COUNT];

    // Expand repositioning — compact and expanded label layouts
    private LabelLayout[]? CompactLayouts;
    private bool[]? ExistsInCompact;
    private LabelLayout[]? ExpandedLayouts;

    public StatsPanel(ControlPrefabSet prefabSet)
    {
        Name = "Stats";
        Visible = false;

        var statusRect = PrefabPanel.GetRect(prefabSet, "Status");

        if (statusRect != Rectangle.Empty)
        {
            Width = statusRect.Width;
            Height = statusRect.Height;
        }

        if (prefabSet.Contains("Status") && (prefabSet["Status"].Images.Count > 0))
            Background = UiRenderer.Instance!.GetPrefabTexture(prefabSet.Name, "Status", 0);

        for (var i = 0; i < LABEL_COUNT; i++)
            Labels[i] = CreatePrefabLabel(prefabSet, LABEL_NAMES[i]);
    }

    /// <summary>
    ///     Configures expand support. The expanded prefab set provides the full-size Status background and label positions.
    ///     Labels that only exist in the expanded prefab are created hidden and shown on expand.
    /// </summary>
    public void ConfigureExpand(ControlPrefabSet expandedPrefabSet)
    {
        Texture2D? expandedTexture = null;

        if (expandedPrefabSet.Contains("Status") && (expandedPrefabSet["Status"].Images.Count > 0))
            expandedTexture = UiRenderer.Instance!.GetPrefabTexture(expandedPrefabSet.Name, "Status", 0);

        ConfigureExpand(expandedTexture);

        CompactLayouts = new LabelLayout[LABEL_COUNT];
        ExpandedLayouts = new LabelLayout[LABEL_COUNT];
        ExistsInCompact = new bool[LABEL_COUNT];

        for (var i = 0; i < LABEL_COUNT; i++)
        {
            ExistsInCompact[i] = Labels[i] is not null;

            // Create missing labels that only exist in the expanded prefab
            if (Labels[i] is null)
            {
                var exRect = PrefabPanel.GetRect(expandedPrefabSet, LABEL_NAMES[i]);

                if (exRect != Rectangle.Empty)
                {
                    Labels[i] = new UILabel
                    {
                        Name = LABEL_NAMES[i],
                        X = exRect.X,
                        Y = exRect.Y,
                        Width = exRect.Width,
                        Height = exRect.Height,
                        Alignment = TextAlignment.Right,
                        Visible = false
                    };

                    AddChild(Labels[i]!);
                }
            }

            if (Labels[i] is not null)
                CompactLayouts[i] = new LabelLayout(
                    Labels[i]!.X,
                    Labels[i]!.Y,
                    Labels[i]!.Width,
                    Labels[i]!.Height);

            var expandedRect = PrefabPanel.GetRect(expandedPrefabSet, LABEL_NAMES[i]);

            ExpandedLayouts[i] = new LabelLayout(
                expandedRect.X,
                expandedRect.Y,
                expandedRect.Width,
                expandedRect.Height);
        }
    }

    private UILabel? CreatePrefabLabel(ControlPrefabSet prefabSet, string name)
    {
        var rect = PrefabPanel.GetRect(prefabSet, name);

        if (rect == Rectangle.Empty)
            return null;

        var label = new UILabel
        {
            Name = name,
            X = rect.X,
            Y = rect.Y,
            Width = rect.Width,
            Height = rect.Height,
            Alignment = TextAlignment.Right
        };

        AddChild(label);

        return label;
    }

    public override void SetExpanded(bool expanded)
    {
        base.SetExpanded(expanded);

        if (ExistsInCompact is null)
            return;

        var layouts = expanded ? ExpandedLayouts : CompactLayouts;

        if (layouts is null)
            return;

        for (var i = 0; i < LABEL_COUNT; i++)
        {
            if (Labels[i] is null)
                continue;

            // Labels that only exist in the expanded prefab are hidden when collapsed
            if (!ExistsInCompact[i])
            {
                Labels[i]!.Visible = expanded;

                if (!expanded)
                    continue;
            }

            Labels[i]!.X = layouts[i].X;
            Labels[i]!.Y = layouts[i].Y;
            Labels[i]!.Width = layouts[i].Width;
            Labels[i]!.Height = layouts[i].Height;
        }
    }

    private void SetLabel(int index, string text)
    {
        if (Labels[index] is not null)
            Labels[index]!.Text = text;
    }

    public void UpdateAttributes(AttributesArgs attrs)
    {
        SetLabel(IDX_STR, $"{attrs.Str}");
        SetLabel(IDX_INT, $"{attrs.Int}");
        SetLabel(IDX_WIS, $"{attrs.Wis}");
        SetLabel(IDX_CON, $"{attrs.Con}");
        SetLabel(IDX_DEX, $"{attrs.Dex}");
        SetLabel(IDX_HP, $"{attrs.CurrentHp}");
        SetLabel(IDX_HP_MAX, $"{attrs.MaximumHp}");
        SetLabel(IDX_MP, $"{attrs.CurrentMp}");
        SetLabel(IDX_MP_MAX, $"{attrs.MaximumMp}");
        SetLabel(IDX_EXP, $"{attrs.TotalExp}");
        SetLabel(IDX_AB_EXP, $"{attrs.TotalAbility}");
        SetLabel(IDX_GOLD, $"{attrs.Gold}");
        SetLabel(IDX_GP, $"{attrs.GamePoints}");
        SetLabel(IDX_LEV, $"{attrs.Level}");
        SetLabel(IDX_NEXT_LEV, $"{attrs.ToNextLevel}");
        SetLabel(IDX_AB, $"{attrs.Ability}");
        SetLabel(IDX_NEXT_AB, $"{attrs.ToNextAbility}");
    }

    private record struct LabelLayout(
        int X,
        int Y,
        int Width,
        int Height);
}
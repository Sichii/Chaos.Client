#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data.Models;
using Chaos.DarkAges.Definitions;
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
    private const int STAT_BUTTON_COUNT = 5;
    private const int STAT_BUTTON_X = 71;
    private const int STAT_BUTTON_Y = 6;
    private const int STAT_BUTTON_W = 16;
    private const int STAT_BUTTON_H = 18;
    private const int STAT_BUTTON_SPACING = 19;
    private const float BLINK_INTERVAL_MS = 500f;
    private const string LEVELUP_EPF = "levelup.epf";

    private static readonly Stat[] STAT_BUTTON_STATS =
    [
        Stat.STR,
        Stat.INT,
        Stat.WIS,
        Stat.CON,
        Stat.DEX
    ];

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
    private readonly UIButton?[] StatButtons = new UIButton?[STAT_BUTTON_COUNT];
    private Texture2D? BlinkFrameA;
    private Texture2D? BlinkFrameB;
    private Texture2D? HoverFrame;
    private float BlinkTimer;
    private bool BlinkPhase;
    private bool HasUnspentPoints;

    //expand repositioning — compact and expanded label layouts
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

        //widen exp and gold labels to accommodate large numbers
        foreach (var idx in new[] { IDX_EXP, IDX_GOLD })
            if (Labels[idx] is { } label)
            {
                label.X -= 10;
                label.Width += 10;
            }

        //levelup stat raise buttons — load levelup.epf frames and create clickable arrows
        var cache = UiRenderer.Instance!;
        var frameCount = cache.GetEpfFrameCount(LEVELUP_EPF);

        if (frameCount >= 3)
        {
            BlinkFrameA = cache.GetEpfTexture(LEVELUP_EPF, 0);
            BlinkFrameB = cache.GetEpfTexture(LEVELUP_EPF, 1);
            HoverFrame = cache.GetEpfTexture(LEVELUP_EPF, 2);

            for (var i = 0; i < STAT_BUTTON_COUNT; i++)
            {
                var stat = STAT_BUTTON_STATS[i];

                var btn = new UIButton
                {
                    Name = $"LevelUp_{stat}",
                    X = STAT_BUTTON_X,
                    Y = STAT_BUTTON_Y + i * STAT_BUTTON_SPACING,
                    Width = STAT_BUTTON_W,
                    Height = STAT_BUTTON_H,
                    NormalTexture = BlinkFrameA,
                    PressedTexture = HoverFrame,
                    Visible = false
                };

                var capturedStat = stat;
                btn.Clicked += () => OnRaiseStat?.Invoke(capturedStat);

                AddChild(btn);
                StatButtons[i] = btn;
            }
        }
    }

    public event Action<Stat>? OnRaiseStat;

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

            //create missing labels that only exist in the expanded prefab
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
                        HorizontalAlignment = HorizontalAlignment.Right,
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

            var exX = expandedRect.X;
            var exW = expandedRect.Width;

            if (i is IDX_EXP or IDX_GOLD)
            {
                exX -= 10;
                exW += 10;
            }

            ExpandedLayouts[i] = new LabelLayout(exX, expandedRect.Y, exW, expandedRect.Height);
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
            HorizontalAlignment = HorizontalAlignment.Right
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

            //labels that only exist in the expanded prefab are hidden when collapsed
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

        //levelup buttons only show in expanded mode (positions are from the full _nstatus layout)
        for (var i = 0; i < STAT_BUTTON_COUNT; i++)
            if (StatButtons[i] is not null)
                StatButtons[i]!.Visible = expanded && HasUnspentPoints;
    }

    private void SetLabel(int index, string text)
    {
        if (Labels[index] is not null)
            Labels[index]!.Text = text;
    }

    public override void Update(GameTime gameTime)
    {
        if (!Visible)
            return;

        base.Update(gameTime);

        if (!HasUnspentPoints || BlinkFrameA is null || BlinkFrameB is null)
            return;

        BlinkTimer += (float)gameTime.ElapsedGameTime.TotalMilliseconds;

        if (BlinkTimer < BLINK_INTERVAL_MS)
            return;

        BlinkTimer -= BLINK_INTERVAL_MS;
        BlinkPhase = !BlinkPhase;
        var frame = BlinkPhase ? BlinkFrameB : BlinkFrameA;

        for (var i = 0; i < STAT_BUTTON_COUNT; i++)
            if (StatButtons[i] is not null)
                StatButtons[i]!.NormalTexture = frame;
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

        SetUnspentPoints(attrs.UnspentPoints > 0);
    }

    private void SetUnspentPoints(bool hasPoints)
    {
        if (HasUnspentPoints == hasPoints)
            return;

        HasUnspentPoints = hasPoints;
        BlinkTimer = 0;
        BlinkPhase = false;

        for (var i = 0; i < STAT_BUTTON_COUNT; i++)
            if (StatButtons[i] is not null)
            {
                StatButtons[i]!.Visible = hasPoints;
                StatButtons[i]!.NormalTexture = BlinkFrameA;
            }
    }

    private record struct LabelLayout(
        int X,
        int Y,
        int Width,
        int Height);
}
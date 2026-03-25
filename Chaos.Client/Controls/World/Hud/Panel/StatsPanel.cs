#region
using Chaos.Client.Controls.Components;
using Chaos.Networking.Entities.Server;
using Microsoft.Xna.Framework;
#endregion

namespace Chaos.Client.Controls.World.Hud.Panel;

/// <summary>
///     Character stats panel loaded from _nstatus prefab. Only renders the "Status" image (normal stats, 444x108). The
///     "ExtraStatus" image is handled by ExtendedStatsControl as a separate tab. Control text rects use s_ prefix.
/// </summary>
public sealed class StatsPanel : PrefabPanel
{
    private readonly UILabel? AbExpLabel;
    private readonly UILabel? AbilityLabel;
    private readonly UILabel? ConLabel;
    private readonly UILabel? DexLabel;
    private readonly UILabel? ExpLabel;
    private readonly UILabel? GoldLabel;
    private readonly UILabel? GpLabel;
    private readonly UILabel? HpLabel;
    private readonly UILabel? HpMaxLabel;
    private readonly UILabel? IntLabel;
    private readonly UILabel? LevelLabel;
    private readonly UILabel? MpLabel;
    private readonly UILabel? MpMaxLabel;
    private readonly UILabel? NextAbilityLabel;
    private readonly UILabel? NextLevelLabel;
    private readonly UILabel? StrLabel;
    private readonly UILabel? WisLabel;

    public StatsPanel(string prefabName = "_nstatus")
        : base(prefabName, false)
    {
        Name = "Stats";
        Visible = false;

        // The _nstatus anchor is 640x480 (fullscreen transparent). Override with Status image dimensions.
        var statusRect = GetRect("Status");

        if (statusRect != Rectangle.Empty)
        {
            Width = statusRect.Width;
            Height = statusRect.Height;
        }

        // Background is the anchor image (fullscreen transparent) — use Status image instead
        Background = null;
        CreateImage("Status");

        // Normal stat labels (s_ prefix)
        StrLabel = CreateLabel("s_Str", TextAlignment.Right);
        IntLabel = CreateLabel("s_Int", TextAlignment.Right);
        WisLabel = CreateLabel("s_Wis", TextAlignment.Right);
        ConLabel = CreateLabel("s_Con", TextAlignment.Right);
        DexLabel = CreateLabel("s_Dex", TextAlignment.Right);
        HpLabel = CreateLabel("s_HP", TextAlignment.Right);
        HpMaxLabel = CreateLabel("s_HPMax", TextAlignment.Right);
        MpLabel = CreateLabel("s_MP", TextAlignment.Right);
        MpMaxLabel = CreateLabel("s_MPMax", TextAlignment.Right);
        ExpLabel = CreateLabel("s_EXP", TextAlignment.Right);
        AbExpLabel = CreateLabel("s_AEXP", TextAlignment.Right);
        GoldLabel = CreateLabel("s_Gold", TextAlignment.Right);
        GpLabel = CreateLabel("s_GP", TextAlignment.Right);
        LevelLabel = CreateLabel("s_Lev", TextAlignment.Right);
        NextLevelLabel = CreateLabel("s_nextLev", TextAlignment.Right);
        AbilityLabel = CreateLabel("s_Ab", TextAlignment.Right);
        NextAbilityLabel = CreateLabel("s_nextAb", TextAlignment.Right);
    }

    public void UpdateAttributes(AttributesArgs attrs)
    {
        StrLabel?.Text = $"{attrs.Str}";
        IntLabel?.Text = $"{attrs.Int}";
        WisLabel?.Text = $"{attrs.Wis}";
        ConLabel?.Text = $"{attrs.Con}";
        DexLabel?.Text = $"{attrs.Dex}";
        HpLabel?.Text = $"{attrs.CurrentHp}";
        HpMaxLabel?.Text = $"{attrs.MaximumHp}";
        MpLabel?.Text = $"{attrs.CurrentMp}";
        MpMaxLabel?.Text = $"{attrs.MaximumMp}";
        ExpLabel?.Text = $"{attrs.TotalExp}";
        AbExpLabel?.Text = $"{attrs.TotalAbility}";
        GoldLabel?.Text = $"{attrs.Gold}";
        GpLabel?.Text = $"{attrs.GamePoints}";
        LevelLabel?.Text = $"{attrs.Level}";
        NextLevelLabel?.Text = $"{attrs.ToNextLevel}";
        AbilityLabel?.Text = $"{attrs.Ability}";
        NextAbilityLabel?.Text = $"{attrs.ToNextAbility}";
    }
}
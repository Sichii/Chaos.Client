#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Definitions;
using Chaos.Networking.Entities.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

    public StatsPanel(GraphicsDevice device)
        : base(device, "_nstatus", false)
    {
        Name = "Stats";
        Visible = false;

        // Do NOT call AutoPopulate — it would create "ExtraStatus" image too.
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
        StrLabel?.SetText($"{attrs.Str}");
        IntLabel?.SetText($"{attrs.Int}");
        WisLabel?.SetText($"{attrs.Wis}");
        ConLabel?.SetText($"{attrs.Con}");
        DexLabel?.SetText($"{attrs.Dex}");
        HpLabel?.SetText($"{attrs.CurrentHp}");
        HpMaxLabel?.SetText($"{attrs.MaximumHp}");
        MpLabel?.SetText($"{attrs.CurrentMp}");
        MpMaxLabel?.SetText($"{attrs.MaximumMp}");
        ExpLabel?.SetText($"{attrs.TotalExp}");
        AbExpLabel?.SetText($"{attrs.TotalAbility}");
        GoldLabel?.SetText($"{attrs.Gold}");
        GpLabel?.SetText($"{attrs.GamePoints}");
        LevelLabel?.SetText($"{attrs.Level}");
        NextLevelLabel?.SetText($"{attrs.ToNextLevel}");
        AbilityLabel?.SetText($"{attrs.Ability}");
        NextAbilityLabel?.SetText($"{attrs.ToNextAbility}");
    }
}
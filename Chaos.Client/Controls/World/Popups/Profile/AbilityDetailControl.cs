#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Profile;

/// <summary>
///     Detail popup for a skill/spell entry (_nui_ske prefab). Shows icon, name, level, stat requirements, prerequisites,
///     and description. Dismisses on any click or Escape.
/// </summary>
public sealed class AbilityDetailControl : PrefabPanel
{
    private readonly UILabel? ConLabel;
    private readonly UILabel? DescLabel;
    private readonly UILabel? DexLabel;
    private readonly UIImage? IconImage;
    private readonly UILabel? IntLabel;
    private readonly UILabel? LevelLabel;
    private readonly UILabel? NameLabel;
    private readonly UILabel? StrLabel;
    private readonly UILabel? Sub1Label;
    private readonly UILabel? Sub2Label;
    private readonly UILabel? WisLabel;

    public AbilityDetailControl()
        : base("_nui_ske")
    {
        Visible = false;
        IsModal = true;

        IconImage = CreateImage("ICON");
        LevelLabel = CreateLabel("LEV");
        StrLabel = CreateLabel("STR");
        IntLabel = CreateLabel("INT");
        WisLabel = CreateLabel("WIS");
        ConLabel = CreateLabel("CON");
        DexLabel = CreateLabel("DEX");
        NameLabel = CreateLabel("NAME");
        Sub1Label = CreateLabel("SUB1");
        Sub2Label = CreateLabel("SUB2");
        DescLabel = CreateLabel("DESC");

        if (DescLabel is not null)
            DescLabel.WordWrap = true;
    }

    private static string FormatPreReq(string? name, byte level)
    {
        if (name is null)
            return string.Empty;

        return level > 0 ? $"{name} Lv. {level}" : name;
    }

    /// <summary>
    ///     Populates and shows the detail view for the given ability entry.
    /// </summary>
    public void ShowEntry(AbilityMetadataEntry entry, Rectangle viewport)
    {
        X = viewport.X + (viewport.Width - Width) / 2;
        Y = viewport.Y + (viewport.Height - Height) / 2;

        if (NameLabel is not null)
            NameLabel.Text = entry.Name;

        if (LevelLabel is not null)
            LevelLabel.Text = entry.Level.ToString();

        if (StrLabel is not null)
            StrLabel.Text = entry.Str.ToString();

        if (IntLabel is not null)
            IntLabel.Text = entry.Int.ToString();

        if (WisLabel is not null)
            WisLabel.Text = entry.Wis.ToString();

        if (ConLabel is not null)
            ConLabel.Text = entry.Con.ToString();

        if (DexLabel is not null)
            DexLabel.Text = entry.Dex.ToString();

        if (Sub1Label is not null)
            Sub1Label.Text = FormatPreReq(entry.PreReq1Name, entry.PreReq1Level);

        if (Sub2Label is not null)
            Sub2Label.Text = FormatPreReq(entry.PreReq2Name, entry.PreReq2Level);

        if (DescLabel is not null)
            DescLabel.Text = entry.Description;

        if (IconImage is not null)
        {
            var renderer = UiRenderer.Instance!;

            IconImage.Texture = entry.IconSprite > 0
                ? entry.IsSpell ? renderer.GetSpellIcon(entry.IconSprite) : renderer.GetSkillIcon(entry.IconSprite)
                : null;
        }

        Show();
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible)
            return;

        if (input.WasLeftButtonPressed || input.WasRightButtonPressed || input.WasKeyPressed(Keys.Escape))
        {
            Hide();

            return;
        }

        base.Update(gameTime, input);
    }
}
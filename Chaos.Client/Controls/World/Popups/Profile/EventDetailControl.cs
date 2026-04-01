#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data.Models;
using Chaos.Client.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Profile;

/// <summary>
///     Detail popup for an event entry (_nui_eve prefab). Shows state icon from leicon.epf, name, circle info,
///     prerequisites, reward, and description (Result when completed, Summary otherwise). Dismisses on any click or
///     Escape.
/// </summary>
public sealed class EventDetailControl : PrefabPanel
{
    private readonly UILabel? DescLabel;
    private readonly UIImage? IconImage;
    private readonly UILabel? LevelLabel;
    private readonly UILabel? MustLabel;
    private readonly UILabel? NameLabel;
    private readonly UILabel? RewardLabel;

    public EventDetailControl()
        : base("_nui_eve")
    {
        Visible = false;
        IsModal = true;

        IconImage = CreateImage("ICON");
        NameLabel = CreateLabel("NAME");
        LevelLabel = CreateLabel("LEV");
        MustLabel = CreateLabel("MUST");
        RewardLabel = CreateLabel("REWARD");
        DescLabel = CreateLabel("DESC");

        if (DescLabel is not null)
            DescLabel.WordWrap = true;
    }

    private static string FormatPrerequisites(EventMetadataEntry entry)
        => !string.IsNullOrEmpty(entry.PreRequisiteId) ? entry.PreRequisiteId : string.Empty;

    /// <summary>
    ///     Populates and shows the detail view for the given event entry.
    /// </summary>
    public void ShowEntry(EventMetadataEntry entry, EventState state, Rectangle viewport)
    {
        this.CenterIn(viewport);

        if (NameLabel is not null)
            NameLabel.Text = entry.Title;

        if (LevelLabel is not null)
            LevelLabel.Text = entry.Page >= 6 ? "master" : $"{entry.Page} level range";

        if (MustLabel is not null)
            MustLabel.Text = FormatPrerequisites(entry);

        if (RewardLabel is not null)
            RewardLabel.Text = entry.Reward;

        if (DescLabel is not null)
            DescLabel.Text = state == EventState.Completed ? entry.Result : entry.Summary;

        if (IconImage is not null)
        {
            var iconFrame = state switch
            {
                EventState.Completed   => 0,
                EventState.Available   => 1,
                EventState.Unavailable => 2,
                _                      => 2
            };

            IconImage.Texture = UiRenderer.Instance!.GetEpfTexture("leicon.epf", iconFrame);
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
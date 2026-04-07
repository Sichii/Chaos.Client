#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data.Models;
#endregion

namespace Chaos.Client.Controls.World.Popups.Profile;

/// <summary>
///     A single event row in the events tab. Uses the _nui_ski prefab template (211x43): 32x32 icon from leicon.epf, event
///     name, and circle text. The entire row is clickable to show details.
/// </summary>
public sealed class EventEntryControl : PrefabPanel
{
    private readonly UILabel? LevelLabel;
    private readonly UILabel? NameLabel;
    private readonly UIImage? TileImage;

    public EventMetadataEntry? Entry { get; private set; }
    public EventState State { get; private set; }

    public EventEntryControl()
        : base("_nui_ski", false)
    {
        Height += 2;
        TileImage = CreateImage("TILE");
        NameLabel = CreateLabel("NAME");
        LevelLabel = CreateLabel("LEVEL");
    }

    public void Clear()
    {
        Entry = null;

        if (TileImage is not null)
            TileImage.Texture = null;

        if (NameLabel is not null)
            NameLabel.Text = string.Empty;

        if (LevelLabel is not null)
            LevelLabel.Text = string.Empty;

        Visible = false;
    }

    private static string FormatCircleText(int page) => page >= 6 ? "master" : $"{page} level range";

    /// <summary>
    ///     Fired when the row is clicked. Passes the bound entry and its state.
    /// </summary>
    public event Action<EventMetadataEntry, EventState>? OnClicked;

    public override void OnClick(ClickEvent e)
    {
        if (Entry is not null)
        {
            OnClicked?.Invoke(Entry, State);
            e.Handled = true;
        }
    }

    public void SetEntry(EventMetadataEntry entry, EventState state)
    {
        Entry = entry;
        State = state;

        if (NameLabel is not null)
            NameLabel.Text = entry.Title;

        if (LevelLabel is not null)
        {
            LevelLabel.Text = FormatCircleText(entry.Page);
            LevelLabel.ForegroundColor = LegendColors.White;
        }

        // leicon.epf: frame 0 = completed, 1 = available, 2 = unavailable
        var iconFrame = state switch
        {
            EventState.Completed   => 0,
            EventState.Available   => 1,
            EventState.Unavailable => 2,
            _                      => 2
        };

        if (TileImage is not null)
            TileImage.Texture = UiRenderer.Instance!.GetEpfTexture("leicon.epf", iconFrame);

        Visible = true;
    }

}
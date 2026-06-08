namespace Chaos.Client.Controls.Scrolling;

/// <summary>
///     Content that a <see cref="ScrollViewerControl" /> can scroll horizontally. See <see cref="IVerticalScrollable" />.
/// </summary>
internal interface IHorizontalScrollable
{
    int HorizontalExtent { get; }
    int HorizontalViewport { get; }
    int HorizontalOffset { get; set; }
}

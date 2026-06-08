namespace Chaos.Client.Controls.Scrolling;

/// <summary>
///     Content that a <see cref="ScrollViewerControl" /> can scroll vertically. Units are content-defined but consistent
///     within the axis (e.g. wrapped-text lines, list rows). The viewer is the clamping authority — the setter just stores.
/// </summary>
internal interface IVerticalScrollable
{
    int VerticalExtent { get; }      // total scrollable units at the content's current width
    int VerticalViewport { get; }    // units visible at the content's current height
    int VerticalOffset { get; set; } // current top offset, in units
}

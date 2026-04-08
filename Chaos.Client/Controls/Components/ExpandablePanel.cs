#region
using Chaos.Client.Controls.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

/// <summary>
///     Base class for panels that support an expanded mode with a taller background drawn upward from the panel's anchor
///     position. Provides shared expand state, background swapping, and the expanded draw pass. Subclasses override
///     <see cref="SetExpanded" /> to add type-specific behavior (slot visibility, text area resize).
/// </summary>
public abstract class ExpandablePanel : UIPanel
{
    private Texture2D? ExpandedBackground;

    /// <summary>
    ///     The Y offset (in pixels) between the expanded and normal backgrounds. When expanded, the panel's Y is shifted
    ///     upward by this amount and Height grows by the same amount so that hit-testing covers the expanded area.
    /// </summary>
    public int ExpandYOffset { get; private set; }

    /// <summary>
    ///     Whether the panel is in expanded mode.
    /// </summary>
    public bool IsExpanded { get; private set; }

    /// <summary>
    ///     Configures expand support. Call once during construction to enable <see cref="SetExpanded" />.
    /// </summary>
    public void ConfigureExpand(Texture2D? expandedBackground)
    {
        ExpandedBackground = expandedBackground;

        var normalHeight = Background?.Height ?? Height;
        var expandedHeight = expandedBackground?.Height ?? normalHeight;
        ExpandYOffset = expandedHeight - normalHeight;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        if (IsExpanded && ExpandedBackground is not null)
        {
            AtlasHelper.Draw(
                spriteBatch,
                ExpandedBackground,
                new Vector2(ScreenX, ScreenY),
                Color.White);

            foreach (var child in Children)
                if (child.Visible)
                {
                    child.Draw(spriteBatch);
                    DebugOverlay.DrawElement(spriteBatch, child);
                }

            return;
        }

        base.Draw(spriteBatch);
    }

    /// <summary>
    ///     Sets the expand state. Subclasses should call
    ///     <c>
    ///         base.SetExpanded
    ///     </c>
    ///     first, then apply type-specific changes (slot visibility, text bounds, etc.).
    /// </summary>
    public virtual void SetExpanded(bool expanded)
    {
        if ((expanded == IsExpanded) || ExpandedBackground is null)
            return;

        IsExpanded = expanded;
        ZIndex = expanded ? 10 : 0;

        // Adjust panel origin and height so hit-testing covers the expanded area.
        // Children keep their original Y values — the panel origin shift positions them correctly.
        var yShift = expanded ? -ExpandYOffset : ExpandYOffset;
        Y += yShift;
        Height -= yShift;
    }
}
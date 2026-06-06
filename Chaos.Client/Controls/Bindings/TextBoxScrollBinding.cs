#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
#endregion

namespace Chaos.Client.Controls.Bindings;

/// <summary>
///     Wires a <see cref="ScrollBarControl" /> to an editable multi-line <see cref="UITextBox" />: keeps the bar sized to
///     the body's live line layout and mirrors the body's scroll offset onto the thumb. Used by the board compose panels,
///     whose body line count changes as the user types — unlike the read panels, which have static content and size their
///     scrollbar once.
/// </summary>
internal static class TextBoxScrollBinding
{
    /// <summary>
    ///     Subscribes the bar's thumb-drag to the body's scroll offset. Call once after constructing both.
    /// </summary>
    public static void Bind(ScrollBarControl scrollBar, UITextBox body)
        => scrollBar.OnValueChanged += v => body.ScrollOffset = v * TextRenderer.CHAR_HEIGHT;

    /// <summary>
    ///     Sizes the bar to the body's current line layout and mirrors the body's scroll position onto the thumb. Call every
    ///     frame (after the body's layout has been recomputed in the panel's base Update) since the line count changes as the
    ///     user types.
    /// </summary>
    public static void Sync(ScrollBarControl scrollBar, UITextBox body)
    {
        var totalLines = body.LineCount;
        var visibleLines = body.VisibleLineCount;

        scrollBar.TotalItems = totalLines;
        scrollBar.VisibleItems = visibleLines;
        scrollBar.MaxValue = Math.Max(0, totalLines - visibleLines);

        //clamp a stale offset (e.g. after deleting text while scrolled down) so the body never shows a blank region past
        //the end of content; no-op when already in range.
        body.ScrollOffset = Math.Clamp(body.ScrollOffset, 0, scrollBar.MaxValue * TextRenderer.CHAR_HEIGHT);
        scrollBar.Value = Math.Clamp(body.ScrollOffset / TextRenderer.CHAR_HEIGHT, 0, scrollBar.MaxValue);
    }

    /// <summary>
    ///     Wheel-scrolls the body from a panel-level scroll event (the body handles the wheel itself while focused; this
    ///     covers scrolling it when focus is elsewhere). Returns true if it consumed the scroll. Derives the limit from the
    ///     already-synced <see cref="ScrollBarControl.MaxValue" />.
    /// </summary>
    public static bool HandleWheel(ScrollBarControl scrollBar, UITextBox body, int delta)
    {
        var maxScroll = scrollBar.MaxValue * TextRenderer.CHAR_HEIGHT;

        if (maxScroll <= 0)
            return false;

        body.ScrollOffset = Math.Clamp(body.ScrollOffset - delta * TextRenderer.CHAR_HEIGHT, 0, maxScroll);

        return true;
    }
}
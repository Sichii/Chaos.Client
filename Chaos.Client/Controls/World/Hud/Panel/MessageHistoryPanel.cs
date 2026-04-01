#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud.Panel;

/// <summary>
///     Message history panel (Shift+F). Displays server message history (same text as the orange bar) in its own tab-sized
///     panel. Reads from the shared history list.
/// </summary>
public sealed class MessageHistoryPanel : ExpandablePanel
{
    private const int GLYPH_HEIGHT = 12;
    private readonly IReadOnlyList<string> History;
    private readonly UILabel[] Lines;
    private readonly Rectangle NormalDisplayBounds;
    private readonly int PanelOriginX;
    private readonly int PanelOriginY;
    private readonly ScrollBarControl ScrollBar;

    private Rectangle DisplayBounds;
    private Rectangle ExpandedDisplayBounds;
    private int LastHistoryCount;
    private int MaxVisibleLines;
    private int RenderedHistoryCount = -1;
    private int RenderedScrollOffset = -1;
    private int ScrollOffset;

    public MessageHistoryPanel(Rectangle displayBounds, Rectangle panelBounds, IReadOnlyList<string> history)
    {
        Name = "MessageHistory";
        NormalDisplayBounds = displayBounds;
        DisplayBounds = displayBounds;
        PanelOriginX = panelBounds.X;
        PanelOriginY = panelBounds.Y;
        History = history;

        Background = UiRenderer.Instance!.GetSpfTexture("_nchatbk.spf");

        MaxVisibleLines = displayBounds.Height > 0 ? displayBounds.Height / GLYPH_HEIGHT : 0;
        Lines = new UILabel[MaxVisibleLines];

        var relX = displayBounds.X - panelBounds.X;

        for (var i = 0; i < MaxVisibleLines; i++)
        {
            Lines[i] = new UILabel
            {
                Name = $"HistoryLine{i}",
                X = relX,
                Width = displayBounds.Width,
                Height = GLYPH_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0
            };

            AddChild(Lines[i]);
        }

        RepositionLabels();

        var relY = displayBounds.Y - panelBounds.Y;

        ScrollBar = new ScrollBarControl
        {
            X = relX + displayBounds.Width - ScrollBarControl.DEFAULT_WIDTH,
            Y = relY,
            Height = displayBounds.Height
        };

        ScrollBar.OnValueChanged += v =>
        {
            ScrollOffset = v;
            RenderedScrollOffset = -1;
        };

        AddChild(ScrollBar);
    }

    /// <summary>
    ///     Configures expand support for the large HUD message history panel (larger text area).
    /// </summary>
    public void ConfigureExpand(Texture2D? expandedBackground, Rectangle expandedBounds)
    {
        ExpandedDisplayBounds = expandedBounds;

        ConfigureExpand(expandedBackground);

        // In the large HUD, the compact area is too small for a scrollbar
        ScrollBar.Visible = false;
    }

    // Labels are children — drawn automatically by base.Draw()

    private void RefreshDisplay()
    {
        if ((History.Count == RenderedHistoryCount) && (ScrollOffset == RenderedScrollOffset))
            return;

        RenderedHistoryCount = History.Count;
        RenderedScrollOffset = ScrollOffset;

        var maxLines = Math.Min(MaxVisibleLines, Lines.Length);
        var startIndex = Math.Max(0, History.Count - maxLines - ScrollOffset);
        var lineIndex = 0;

        for (var i = startIndex; (i < History.Count) && (lineIndex < maxLines); i++)
        {
            Lines[lineIndex].Text = History[i];
            Lines[lineIndex].ForegroundColor = Color.Orange;
            lineIndex++;
        }

        for (; lineIndex < maxLines; lineIndex++)
            Lines[lineIndex].Text = string.Empty;
    }

    private void RepositionLabels()
    {
        var relY = DisplayBounds.Y - PanelOriginY;
        var maxLines = Math.Min(MaxVisibleLines, Lines.Length);

        for (var i = 0; i < Lines.Length; i++)
            if (i < maxLines)
            {
                Lines[i].Y = relY + DisplayBounds.Height - (maxLines - i) * GLYPH_HEIGHT;
                Lines[i].Visible = true;
            } else
                Lines[i].Visible = false;
    }

    public override void SetExpanded(bool expanded)
    {
        base.SetExpanded(expanded);

        DisplayBounds = expanded ? ExpandedDisplayBounds : NormalDisplayBounds;
        MaxVisibleLines = DisplayBounds.Height > 0 ? DisplayBounds.Height / GLYPH_HEIGHT : 0;
        ScrollBar.Visible = expanded;
        RepositionLabels();
        RenderedScrollOffset = -1;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        base.Update(gameTime, input);

        // Auto-scroll to bottom when new messages arrive
        if (History.Count != LastHistoryCount)
        {
            ScrollOffset = 0;
            LastHistoryCount = History.Count;

            ScrollBar.TotalItems = History.Count;
            ScrollBar.VisibleItems = MaxVisibleLines;
            ScrollBar.MaxValue = Math.Max(0, History.Count - MaxVisibleLines);
            ScrollBar.Value = 0;
        }

        if ((input.ScrollDelta != 0) && (History.Count > MaxVisibleLines))
        {
            ScrollOffset = Math.Clamp(ScrollOffset - input.ScrollDelta, 0, History.Count - MaxVisibleLines);
            ScrollBar.Value = ScrollOffset;
        }

        RefreshDisplay();
    }
}
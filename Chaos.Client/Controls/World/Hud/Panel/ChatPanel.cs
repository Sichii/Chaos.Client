#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.ViewModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud.Panel;

/// <summary>
///     Chat display panel (F key). Shows chat message history with word-wrap. Background loaded from _nchatbk.spf (shown
///     in tab area). Text rendered at ChatDisplayBounds (separate area of the HUD).
/// </summary>
public sealed class ChatPanel : ExpandablePanel
{
    private const int MAX_CHAT_LINES = 200;
    private const int GLYPH_HEIGHT = 12;
    private readonly List<ChatLine> ChatLog = [];
    private readonly UILabel[] Lines;
    private readonly Rectangle NormalDisplayBounds;
    private readonly int PanelOriginX;
    private readonly int PanelOriginY;
    private readonly ScrollBarControl ScrollBar;

    private Rectangle DisplayBounds;
    private Rectangle ExpandedDisplayBounds;
    private int LogVersion;
    private int MaxVisibleLines;
    private int RenderedVersion = -1;
    private int ScrollOffset;

    public ChatPanel(Rectangle displayBounds, Rectangle panelBounds)
    {
        Name = "Chat";
        NormalDisplayBounds = displayBounds;
        DisplayBounds = displayBounds;
        PanelOriginX = panelBounds.X;
        PanelOriginY = panelBounds.Y;

        Background = UiRenderer.Instance!.GetSpfTexture("_nchatbk.spf");

        MaxVisibleLines = displayBounds.Height > 0 ? displayBounds.Height / GLYPH_HEIGHT : 0;
        Lines = new UILabel[MaxVisibleLines];

        var relX = displayBounds.X - panelBounds.X;

        for (var i = 0; i < MaxVisibleLines; i++)
        {
            Lines[i] = new UILabel
            {
                Name = $"ChatLine{i}",
                X = relX,
                Width = displayBounds.Width,
                Height = GLYPH_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0
            };

            AddChild(Lines[i]);
        }

        RepositionLabels();

        // Position relative to panel origin (panel is placed at panelBounds by RegisterTab)
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
            LogVersion++;
        };

        AddChild(ScrollBar);
        WorldState.Chat.MessageAdded += OnMessageAdded;
    }

    private void AddMessage(string text, Color color)
    {
        var maxWidth = DisplayBounds.Width;

        if (maxWidth <= 0)
            return;

        var remaining = text;

        while (remaining.Length > 0)
        {
            var lineEnd = TextRenderer.FindLineBreak(remaining, maxWidth);

            var line = remaining[..lineEnd]
                .TrimEnd();

            remaining = remaining[lineEnd..]
                .TrimStart();

            ChatLog.Add(new ChatLine(line, color));
        }

        if (ChatLog.Count > MAX_CHAT_LINES)
            ChatLog.RemoveRange(0, ChatLog.Count - MAX_CHAT_LINES);

        // Auto-scroll to bottom on new message
        ScrollOffset = 0;
        LogVersion++;

        ScrollBar.TotalItems = ChatLog.Count;
        ScrollBar.VisibleItems = MaxVisibleLines;
        ScrollBar.MaxValue = Math.Max(0, ChatLog.Count - MaxVisibleLines);
        ScrollBar.Value = 0;
    }

    /// <summary>
    ///     Configures expand support for the large HUD chat panel (larger text area).
    /// </summary>
    public void ConfigureExpand(Texture2D? expandedBackground, Rectangle expandedBounds, Rectangle panelBounds)
    {
        ExpandedDisplayBounds = expandedBounds;

        ConfigureExpand(expandedBackground);

        // In the large HUD, the compact chat area is too small for a scrollbar
        ScrollBar.Visible = false;
    }

    public override void Dispose()
    {
        WorldState.Chat.MessageAdded -= OnMessageAdded;

        base.Dispose();
    }

    // Labels are children — drawn automatically by base.Draw()

    private void OnMessageAdded(Chat.ChatMessage msg) => AddMessage(msg.Text, msg.Color);

    private void RefreshDisplay()
    {
        if (RenderedVersion == LogVersion)
            return;

        RenderedVersion = LogVersion;

        var maxLines = Math.Min(MaxVisibleLines, Lines.Length);
        var startIndex = Math.Max(0, ChatLog.Count - maxLines - ScrollOffset);
        var lineIndex = 0;

        for (var i = startIndex; (i < ChatLog.Count) && (lineIndex < maxLines); i++)
        {
            var line = ChatLog[i];
            Lines[lineIndex].Text = line.Text;
            Lines[lineIndex].ForegroundColor = line.Color;
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
                // Bottom-up: line 0 at top, line maxLines-1 at bottom
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

        // Force re-render with new line count
        LogVersion++;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        base.Update(gameTime, input);

        if ((input.ScrollDelta != 0) && (ChatLog.Count > MaxVisibleLines))
        {
            ScrollOffset = Math.Clamp(ScrollOffset - input.ScrollDelta, 0, ChatLog.Count - MaxVisibleLines);
            ScrollBar.Value = ScrollOffset;
            LogVersion++;
        }

        RefreshDisplay();
    }

    private record struct ChatLine(string Text, Color Color);
}
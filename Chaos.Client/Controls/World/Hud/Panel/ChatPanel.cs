#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Rendering;
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
    private readonly Chat ChatState;
    private readonly TextElement[] Lines;
    private readonly Rectangle NormalDisplayBounds;
    private readonly ScrollBarControl ScrollBar;

    private Rectangle DisplayBounds;
    private Rectangle ExpandedDisplayBounds;
    private int LogVersion;
    private int MaxVisibleLines;
    private int RenderedVersion = -1;
    private int ScrollOffset;

    public ChatPanel(Rectangle displayBounds, Rectangle panelBounds, Chat chat)
    {
        Name = "Chat";
        NormalDisplayBounds = displayBounds;
        DisplayBounds = displayBounds;
        ChatState = chat;

        Background = UiRenderer.Instance!.GetSpfTexture("_nchatbk.spf");

        MaxVisibleLines = displayBounds.Height > 0 ? displayBounds.Height / GLYPH_HEIGHT : 0;
        Lines = new TextElement[MaxVisibleLines];

        for (var i = 0; i < MaxVisibleLines; i++)
            Lines[i] = new TextElement();

        // Position relative to panel origin (panel is placed at panelBounds by RegisterTab)
        var relX = displayBounds.X - panelBounds.X;
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
        ChatState.MessageAdded += OnMessageAdded;
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
        ChatState.MessageAdded -= OnMessageAdded;

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        // ExpandablePanel.Draw handles expanded bg + children, or normal bg + children
        base.Draw(spriteBatch);

        var maxLines = Math.Min(MaxVisibleLines, Lines.Length);

        if ((maxLines == 0) || (ChatLog.Count == 0))
            return;

        RefreshDisplay();

        var baseY = DisplayBounds.Y + DisplayBounds.Height;

        for (var i = maxLines - 1; i >= 0; i--)
        {
            baseY -= GLYPH_HEIGHT;

            if (baseY < DisplayBounds.Y)
                break;

            Lines[i]
                .Draw(spriteBatch, new Vector2(DisplayBounds.X, baseY));
        }
    }

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

            Lines[lineIndex]
                .Update(line.Text, line.Color);
            lineIndex++;
        }

        for (; lineIndex < maxLines; lineIndex++)
            Lines[lineIndex]
                .Update(string.Empty, Color.White);
    }

    public override void SetExpanded(bool expanded)
    {
        base.SetExpanded(expanded);

        DisplayBounds = expanded ? ExpandedDisplayBounds : NormalDisplayBounds;
        MaxVisibleLines = DisplayBounds.Height > 0 ? DisplayBounds.Height / GLYPH_HEIGHT : 0;
        ScrollBar.Visible = expanded;

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
    }

    private record struct ChatLine(string Text, Color Color);
}
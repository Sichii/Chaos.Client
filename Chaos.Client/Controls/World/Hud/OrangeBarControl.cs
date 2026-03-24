#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using Chaos.Client.ViewModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud;

/// <summary>
///     Orange bar message display above inventory. Shows the latest server message; supports drag-expand to reveal
///     history. Background from SystemMessagePane (_nsmbk.spf).
/// </summary>
public sealed class OrangeBarControl : UIElement
{
    private const int MAX_EXPAND_LINES = 10;
    private const int GLYPH_HEIGHT = 12;
    private readonly Chat ChatState;
    private readonly CachedText[] HistoryTextures;
    private readonly Texture2D? PaneBg;

    private readonly Rectangle TextBounds;
    private int DragMouseStartY;
    private int ExpandedLines;
    private Rectangle WrapBounds;

    public bool IsDragging { get; private set; }

    public OrangeBarControl(ControlPrefabSet hudPrefabSet, Chat chat)
    {
        ChatState = chat;
        Name = "OrangeBar";

        TextBounds = PrefabPanel.GetRect(hudPrefabSet, "SystemMessage");
        WrapBounds = PrefabPanel.GetRect(hudPrefabSet, "SystemMessageWrap");

        if (hudPrefabSet.Contains("SystemMessagePane"))
            PaneBg = UiRenderer.Instance!.GetPrefabTexture(hudPrefabSet.Name, "SystemMessagePane", 0);

        HistoryTextures = new CachedText[MAX_EXPAND_LINES];

        for (var i = 0; i < MAX_EXPAND_LINES; i++)
            HistoryTextures[i] = new CachedText();
    }

    public override void Dispose()
    {
        PaneBg?.Dispose();

        foreach (var texture in HistoryTextures)
            texture.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || PaneBg is null)
            return;

        var sx = Parent?.ScreenX ?? 0;
        var sy = Parent?.ScreenY ?? 0;

        var paneX = sx + WrapBounds.X;
        var topY = sy + WrapBounds.Y;
        var expandY = sy + TextBounds.Y + GLYPH_HEIGHT;
        var paneWidth = WrapBounds.Width;
        var contentHeight = ExpandedLines * GLYPH_HEIGHT;
        var totalHeight = expandY - topY + contentHeight;

        // Solid opaque fill
        DrawRect(
            spriteBatch,
            new Rectangle(
                paneX,
                topY,
                paneWidth,
                totalHeight),
            Color.Black);

        // Reveal expand texture (1:1, no stretching)
        var revealHeight = Math.Min(totalHeight, PaneBg.Height - 4);

        AtlasHelper.Draw(
            spriteBatch,
            PaneBg,
            new Vector2(paneX, topY),
            new Rectangle(
                0,
                0,
                PaneBg.Width,
                revealHeight),
            Color.White);

        // Bottom 4px edge
        var srcY = PaneBg.Height - 4;

        AtlasHelper.Draw(
            spriteBatch,
            PaneBg,
            new Vector2(paneX, topY + totalHeight),
            new Rectangle(
                0,
                srcY,
                PaneBg.Width,
                4),
            Color.White);

        // History text — newest at bottom, older above
        var history = ChatState.GetOrangeBarHistory();

        if (history.Count > 0)
        {
            var textX = sx + TextBounds.X;
            var bottomY = sy + TextBounds.Y + ExpandedLines * GLYPH_HEIGHT;
            var slot = 0;

            for (var i = history.Count - 1; (i >= 0) && (slot <= ExpandedLines); i--)
            {
                var textY = bottomY - slot * GLYPH_HEIGHT;

                HistoryTextures[slot]
                    .Update(history[i], Color.Orange);

                HistoryTextures[slot]
                    .Draw(spriteBatch, new Vector2(textX, textY));
                slot++;
            }
        }
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (PaneBg is null || (WrapBounds == Rectangle.Empty))
            return;

        var sx = Parent?.ScreenX ?? 0;
        var sy = Parent?.ScreenY ?? 0;

        if (!IsDragging && input.WasLeftButtonPressed)
        {
            var mx = input.MouseX - sx;
            var my = input.MouseY - sy;

            if (WrapBounds.Contains(mx, my))
            {
                IsDragging = true;
                DragMouseStartY = input.MouseY;
                ExpandedLines = 0;
            }
        }

        if (IsDragging)
        {
            if (input.IsLeftButtonHeld)
            {
                var dragPixels = input.MouseY - DragMouseStartY;
                ExpandedLines = Math.Clamp(dragPixels / GLYPH_HEIGHT, 0, MAX_EXPAND_LINES - 1);
            } else
            {
                IsDragging = false;
                ExpandedLines = 0;
            }
        }
    }
}
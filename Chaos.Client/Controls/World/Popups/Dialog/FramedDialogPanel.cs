#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Popups.Dialog;

/// <summary>
///     Base class for floating dialog sub-panels that share the ornate 9-slice frame (DlgBack2.spf tiled background +
///     nd_f01–f08 border pieces). Subclasses add their own content controls inside the frame.
/// </summary>
public abstract class FramedDialogPanel : PrefabPanel
{
    // Frame corner dimensions
    private const int CORNER_TL_W = 31;
    private const int CORNER_TL_H = 24;
    private const int CORNER_TR_W = 31;
    private const int CORNER_BL_H = 47;
    private const int CORNER_BR_W = 31;
    private const int CORNER_BR_H = 47;
    private const int BORDER_BOTTOM = 47;

    private Texture2D? BackgroundTile;
    private Texture2D? CornerBL;
    private Texture2D? CornerBR;
    private Texture2D? CornerTL;
    private Texture2D? CornerTR;
    private Texture2D? EdgeBottomOk;
    private Texture2D? EdgeBottomRivets;
    private Texture2D? EdgeLeft;
    private Texture2D? EdgeRight;
    private Texture2D? EdgeTop;
    private bool FrameTexturesLoaded;

    /// <summary>
    ///     The OK button used to split the bottom edge into rivets (left) and plain (right). Subclasses should set this if
    ///     they create a Btn1 control.
    /// </summary>
    protected UIButton? OkButton { get; set; }

    protected FramedDialogPanel(string prefabName, bool center = true)
        : base(prefabName, center)
        => Background = null;

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        EnsureFrameTextures();

        var sx = ScreenX;
        var sy = ScreenY;
        var w = Width;
        var h = Height;

        // 1. Tile DlgBack2.spf across entire panel as background fill
        if (BackgroundTile is not null)
            TileTexture(
                spriteBatch,
                BackgroundTile,
                sx,
                sy,
                w,
                h);

        // 2. Frame edges (tiled between corners)
        if (EdgeTop is not null)
            TileTexture(
                spriteBatch,
                EdgeTop,
                sx + CORNER_TL_W,
                sy,
                w - CORNER_TL_W - CORNER_TR_W,
                EdgeTop.Height);

        if (EdgeLeft is not null)
            TileTexture(
                spriteBatch,
                EdgeLeft,
                sx,
                sy + CORNER_TL_H,
                EdgeLeft.Width,
                h - CORNER_TL_H - CORNER_BL_H);

        if (EdgeRight is not null)
            TileTexture(
                spriteBatch,
                EdgeRight,
                sx + w - EdgeRight.Width,
                sy + CORNER_TL_H,
                EdgeRight.Width,
                h - CORNER_TL_H - CORNER_BR_H);

        // Bottom edge: rivets on the left, plain background behind the OK button on the right
        var okAreaStart = (OkButton?.X ?? w - CORNER_BR_W) - 8;
        var rivetsWidth = okAreaStart - CORNER_TL_W;
        var okAreaWidth = w - CORNER_BR_W - okAreaStart;

        if (EdgeBottomRivets is not null && (rivetsWidth > 0))
            TileTexture(
                spriteBatch,
                EdgeBottomRivets,
                sx + CORNER_TL_W,
                sy + h - BORDER_BOTTOM,
                rivetsWidth,
                EdgeBottomRivets.Height);

        if (EdgeBottomOk is not null && (okAreaWidth > 0))
            TileTexture(
                spriteBatch,
                EdgeBottomOk,
                sx + okAreaStart,
                sy + h - BORDER_BOTTOM,
                okAreaWidth,
                EdgeBottomOk.Height);

        // 3. Corners (drawn last to cover edge overlap)
        if (CornerTL is not null)
            AtlasHelper.Draw(
                spriteBatch,
                CornerTL,
                new Vector2(sx, sy),
                Color.White);

        if (CornerTR is not null)
            AtlasHelper.Draw(
                spriteBatch,
                CornerTR,
                new Vector2(sx + w - CORNER_TR_W, sy),
                Color.White);

        if (CornerBL is not null)
            AtlasHelper.Draw(
                spriteBatch,
                CornerBL,
                new Vector2(sx, sy + h - CORNER_BL_H),
                Color.White);

        if (CornerBR is not null)
            AtlasHelper.Draw(
                spriteBatch,
                CornerBR,
                new Vector2(sx + w - CORNER_BR_W, sy + h - CORNER_BR_H),
                Color.White);

        // 4. Children (content controls)
        DrawChildren(spriteBatch);
    }

    private void DrawChildren(SpriteBatch spriteBatch)
    {
        foreach (var child in Children)
            if (child.Visible)
            {
                child.Draw(spriteBatch);
                DebugOverlay.DrawElement(spriteBatch, child);
            }
    }

    private void EnsureFrameTextures()
    {
        if (FrameTexturesLoaded)
            return;

        FrameTexturesLoaded = true;
        var renderer = UiRenderer.Instance;

        if (renderer is null)
            return;

        CornerTL = renderer.GetSpfTexture("nd_f01.spf");
        CornerTR = renderer.GetSpfTexture("nd_f02.spf");
        CornerBL = renderer.GetSpfTexture("nd_f03.spf");
        CornerBR = renderer.GetSpfTexture("nd_f04.spf");
        EdgeTop = renderer.GetSpfTexture("nd_f05.spf");
        EdgeLeft = renderer.GetSpfTexture("nd_f06.spf");
        EdgeRight = renderer.GetSpfTexture("nd_f07.spf");
        EdgeBottomOk = renderer.GetSpfTexture("nd_f08.spf");
        EdgeBottomRivets = renderer.GetSpfTexture("nd_f08_1.spf");
        BackgroundTile = renderer.GetSpfTexture("DlgBack2.spf");
    }

    protected static void TileTexture(
        SpriteBatch spriteBatch,
        Texture2D texture,
        int x,
        int y,
        int width,
        int height)
    {
        if ((width <= 0) || (height <= 0))
            return;

        var texW = texture.Width;
        var texH = texture.Height;

        for (var ty = 0; ty < height; ty += texH)
        {
            var drawH = Math.Min(texH, height - ty);

            for (var tx = 0; tx < width; tx += texW)
            {
                var drawW = Math.Min(texW, width - tx);

                if ((drawW == texW) && (drawH == texH))
                    AtlasHelper.Draw(
                        spriteBatch,
                        texture,
                        new Vector2(x + tx, y + ty),
                        Color.White);
                else
                    AtlasHelper.Draw(
                        spriteBatch,
                        texture,
                        new Vector2(x + tx, y + ty),
                        new Rectangle(
                            0,
                            0,
                            drawW,
                            drawH),
                        Color.White);
            }
        }
    }
}
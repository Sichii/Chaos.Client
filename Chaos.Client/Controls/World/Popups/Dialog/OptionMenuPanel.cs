#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Dialog;

/// <summary>
///     Floating option menu panel for NPC dialog/menu interactions. DlgBack2.spf tiled background with nd_f01–f08_1
///     decorative frame and nd_n00/01/02 3-slice option row stripes. Dynamic width, right-aligned and bottom-anchored
///     above the dialog bar. Used for DialogMenu, CreatureMenu, Menu, and MenuWithArgs.
/// </summary>
public sealed class OptionMenuPanel : UIPanel
{
    private const int MIN_STRIPE_WIDTH = 185;
    private const int ROW_HEIGHT = 13;
    private const int STRIPE_GAP = 5;
    private const int TEXT_PADDING_H = 10;
    private const int BTN_WIDTH = 61;
    private const int BTN_HEIGHT = 22;

    // Frame corner dimensions (for drawing corners)
    private const int CORNER_TL_W = 31;
    private const int CORNER_TL_H = 24;
    private const int CORNER_TR_W = 31;
    private const int CORNER_BL_H = 47;
    private const int CORNER_BR_W = 31;
    private const int CORNER_BR_H = 47;

    // Border thickness from edge tiles (content measured from inside of these)
    private const int BORDER_TOP = 6; // nd_f05 height
    private const int BORDER_LEFT = 13; // nd_f06 width
    private const int BORDER_RIGHT = 31; // nd_f07 width
    private const int BORDER_BOTTOM = 47; // nd_f08/nd_f08_1 height

    // Content padding from inside of border to stripes
    private const int CONTENT_PADDING_TOP = 2;
    private const int CONTENT_PADDING_BOTTOM = -16;
    private const int CONTENT_PADDING_LEFT = 7;
    private const int CONTENT_PADDING_RIGHT = -11;

    // Bottom of panel aligns with top of the dialog bottom bar
    private const int BOTTOM_ANCHOR_Y = 372;

    private readonly UIButton? OkButton;
    private readonly List<OptionLabel> OptionLabels = [];
    private Texture2D? BackgroundTile; // DlgBack2.spf
    private Texture2D? CornerBL; // nd_f03 (31x47)
    private Texture2D? CornerBR; // nd_f04 (31x47)

    // Frame textures: corners, edges, background
    private Texture2D? CornerTL; // nd_f01 (31x24)
    private Texture2D? CornerTR; // nd_f02 (31x24)
    private Texture2D? EdgeBottomOk; // nd_f08 (18x47) tile horizontally behind OK
    private Texture2D? EdgeBottomRivets; // nd_f08_1 (18x47) tile horizontally — gold rivets
    private Texture2D? EdgeLeft; // nd_f06 (13x18) tile vertically
    private Texture2D? EdgeRight; // nd_f07 (31x18) tile vertically
    private Texture2D? EdgeTop; // nd_f05 (18x6) tile horizontally

    // 3-slice stripe pieces
    private Texture2D? StripeLeft;
    private Texture2D? StripeLeftOn;
    private Texture2D? StripeMid;
    private Texture2D? StripeMidOn;
    private Texture2D? StripeRight;
    private Texture2D? StripeRightOn;
    private bool TexturesLoaded;

    public OptionMenuPanel()
    {
        Name = "OptionMenu";
        Visible = false;

        // Create OK button from lnpcd2 prefab Btn1 — shown but disabled in option menus
        var prefabSet = DataContext.UserControls.Get("lnpcd2");

        if (prefabSet?.Contains("Btn1") == true)
        {
            var btnPrefab = prefabSet["Btn1"];
            var cache = UiRenderer.Instance!;

            OkButton = new UIButton
            {
                Name = "Btn1",
                Width = BTN_WIDTH,
                Height = BTN_HEIGHT,
                NormalTexture = btnPrefab.Images.Count > 0 ? cache.GetPrefabTexture("lnpcd2", "Btn1", 0) : null,
                PressedTexture = btnPrefab.Images.Count > 1 ? cache.GetPrefabTexture("lnpcd2", "Btn1", 1) : null,
                DisabledTexture = cache.GetSpfTexture("_nbtn.spf", 5),
                Enabled = false
            };

            AddChild(OkButton);
        }
    }

    private void ClearOptionLabels()
    {
        foreach (var label in OptionLabels)
        {
            Children.Remove(label);
            label.Dispose();
        }

        OptionLabels.Clear();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        EnsureTextures();

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
        // Give 4px margin before the button so rivets don't butt up against it
        var okAreaStart = (OkButton?.X ?? w - CORNER_BR_W) - 4;
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

        // 4. Children: option labels + OK button
        base.Draw(spriteBatch);
    }

    private void EnsureTextures()
    {
        if (TexturesLoaded)
            return;

        TexturesLoaded = true;
        var renderer = UiRenderer.Instance;

        if (renderer is null)
            return;

        // Frame pieces
        CornerTL = renderer.GetSpfTexture("nd_f01.spf");
        CornerTR = renderer.GetSpfTexture("nd_f02.spf");
        CornerBL = renderer.GetSpfTexture("nd_f03.spf");
        CornerBR = renderer.GetSpfTexture("nd_f04.spf");
        EdgeTop = renderer.GetSpfTexture("nd_f05.spf");
        EdgeLeft = renderer.GetSpfTexture("nd_f06.spf");
        EdgeRight = renderer.GetSpfTexture("nd_f07.spf");
        EdgeBottomOk = renderer.GetSpfTexture("nd_f08.spf");
        EdgeBottomRivets = renderer.GetSpfTexture("nd_f08_1.spf");

        // Background fill
        BackgroundTile = renderer.GetSpfTexture("DlgBack2.spf");

        // 3-slice stripe pieces
        StripeLeft = renderer.GetSpfTexture("nd_n00.spf");
        StripeMid = renderer.GetSpfTexture("nd_n01.spf");
        StripeRight = renderer.GetSpfTexture("nd_n02.spf");
        StripeLeftOn = renderer.GetSpfTexture("nd_n00on.spf");
        StripeMidOn = renderer.GetSpfTexture("nd_n01on.spf");
        StripeRightOn = renderer.GetSpfTexture("nd_n02on.spf");
    }

    public ushort GetOptionPursuitId(int index)
    {
        if ((index < 0) || (index >= OptionLabels.Count))
            return 0;

        return OptionLabels[index].PursuitId;
    }

    public void Hide()
    {
        Visible = false;
        ClearOptionLabels();
    }

    public event Action? OnClose;

    public event Action<int>? OnOptionSelected;

    public void ShowOptions(IReadOnlyList<(string Text, ushort Pursuit)> options)
    {
        ClearOptionLabels();
        EnsureTextures();

        var capLeftW = StripeLeft?.Width ?? 11;
        var capRightW = StripeRight?.Width ?? 11;

        // Dynamic width from longest option text
        var maxTextWidth = 0;

        foreach ((var text, _) in options)
        {
            var textWidth = TextRenderer.MeasureWidth(text);

            if (textWidth > maxTextWidth)
                maxTextWidth = textWidth;
        }

        var stripeWidth = maxTextWidth + TEXT_PADDING_H * 2;
        stripeWidth = Math.Max(stripeWidth, MIN_STRIPE_WIDTH);

        var stripeLeft = BORDER_LEFT + CONTENT_PADDING_LEFT;
        var stripeRight = BORDER_RIGHT + CONTENT_PADDING_RIGHT;

        Width = stripeWidth + stripeLeft + stripeRight;

        // Dynamic height: top border + padding + stripes + padding + bottom border
        var stripesHeight = options.Count * (ROW_HEIGHT + STRIPE_GAP) - STRIPE_GAP;
        Height = BORDER_TOP + CONTENT_PADDING_TOP + stripesHeight + CONTENT_PADDING_BOTTOM + BORDER_BOTTOM;

        // Right-aligned, bottom-anchored above dialog bar
        X = ChaosGame.VIRTUAL_WIDTH - Width;
        Y = BOTTOM_ANCHOR_Y - Height;

        // Create option label children
        for (var i = 0; i < options.Count; i++)
        {
            (var text, var pursuit) = options[i];
            var index = i;

            var label = new OptionLabel(
                text,
                pursuit,
                StripeLeft,
                StripeMid,
                StripeRight,
                StripeLeftOn,
                StripeMidOn,
                StripeRightOn)
            {
                Name = $"Option_{i}",
                X = BORDER_LEFT + CONTENT_PADDING_LEFT,
                Y = BORDER_TOP + CONTENT_PADDING_TOP + i * (ROW_HEIGHT + STRIPE_GAP),
                Width = stripeWidth,
                Height = ROW_HEIGHT
            };

            label.OnClick += () => OnOptionSelected?.Invoke(index);
            OptionLabels.Add(label);
            AddChild(label);
        }

        // OK button: 20px from right outer edge, 4px from bottom outer edge, shown as disabled
        if (OkButton is not null)
        {
            OkButton.X = Width - BTN_WIDTH - 20;
            OkButton.Y = Height - BTN_HEIGHT - 3;
        }

        Visible = true;
    }

    private static void TileTexture(
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

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (input.WasKeyPressed(Keys.Escape))
        {
            Hide();
            OnClose?.Invoke();

            return;
        }

        base.Update(gameTime, input);
    }

    /// <summary>
    ///     A single clickable text option. Draws a 3-slice dark stripe background (nd_n00 left + nd_n01 tiled + nd_n02 right)
    ///     with centered text.
    /// </summary>
    private sealed class OptionLabel : UIElement
    {
        private readonly Texture2D? StripeLeft;
        private readonly Texture2D? StripeLeftOn;
        private readonly Texture2D? StripeMid;
        private readonly Texture2D? StripeMidOn;
        private readonly Texture2D? StripeRight;
        private readonly Texture2D? StripeRightOn;
        private readonly TextElement TextCache = new();
        private readonly int TextWidth;
        private bool IsHovered;
        public ushort PursuitId { get; }
        public string Text { get; }

        public OptionLabel(
            string text,
            ushort pursuitId,
            Texture2D? stripeLeft,
            Texture2D? stripeMid,
            Texture2D? stripeRight,
            Texture2D? stripeLeftOn,
            Texture2D? stripeMidOn,
            Texture2D? stripeRightOn)
        {
            Text = text;
            PursuitId = pursuitId;
            StripeLeft = stripeLeft;
            StripeMid = stripeMid;
            StripeRight = stripeRight;
            StripeLeftOn = stripeLeftOn;
            StripeMidOn = stripeMidOn;
            StripeRightOn = stripeRightOn;
            TextWidth = TextRenderer.MeasureWidth(text);
            TextCache.Update(text, Color.White);
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible)
                return;

            var sx = ScreenX;
            var sy = ScreenY;

            // Draw 3-slice stripe background
            var left = IsHovered ? StripeLeftOn ?? StripeLeft : StripeLeft;
            var mid = IsHovered ? StripeMidOn ?? StripeMid : StripeMid;
            var right = IsHovered ? StripeRightOn ?? StripeRight : StripeRight;

            var leftW = left?.Width ?? 0;
            var rightW = right?.Width ?? 0;
            var midWidth = Width - leftW - rightW;

            if (left is not null)
                AtlasHelper.Draw(
                    spriteBatch,
                    left,
                    new Vector2(sx, sy),
                    Color.White);

            if (mid is not null)
                TileTexture(
                    spriteBatch,
                    mid,
                    sx + leftW,
                    sy,
                    midWidth,
                    mid.Height);

            if (right is not null)
                AtlasHelper.Draw(
                    spriteBatch,
                    right,
                    new Vector2(sx + Width - rightW, sy),
                    Color.White);

            // Centered text on top
            TextCache.Update(Text, Color.White);

            var textX = sx + (Width - TextWidth) / 2;
            var textY = sy + (Height - TextRenderer.CHAR_HEIGHT) / 2 + 1;

            TextCache.Draw(spriteBatch, new Vector2(textX, textY));
        }

        public event Action? OnClick;

        public override void Update(GameTime gameTime, InputBuffer input)
        {
            if (!Visible || !Enabled)
                return;

            IsHovered = ContainsPoint(input.MouseX, input.MouseY);

            if (input.WasLeftButtonPressed && IsHovered)
                OnClick?.Invoke();
        }
    }
}
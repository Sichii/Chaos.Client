#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Dialog;

/// <summary>
///     Floating option menu panel for NPC dialog/menu interactions. Uses the shared ornate frame with nd_n00/01/02 3-slice
///     option row stripes. Dynamic width, right-aligned and bottom-anchored above the dialog bar. Used for DialogMenu,
///     CreatureMenu, Menu, and MenuWithArgs.
/// </summary>
public sealed class OptionMenuPanel : FramedDialogPanel
{
    private const int MIN_STRIPE_WIDTH = 185;
    private const int ROW_HEIGHT = 13;
    private const int STRIPE_GAP = 5;
    private const int TEXT_PADDING_H = 10;
    private const int BTN_WIDTH = 61;
    private const int BTN_HEIGHT = 22;

    // Border thickness from edge tiles (content measured from inside of these)
    private const int BORDER_TOP = 6;
    private const int BORDER_LEFT = 13;
    private const int BORDER_RIGHT = 31;
    private const int BORDER_BOTTOM = 47;

    // Content padding from inside of border to stripes
    private const int CONTENT_PADDING_TOP = 2;
    private const int CONTENT_PADDING_BOTTOM = -16;
    private const int CONTENT_PADDING_LEFT = 7;
    private const int CONTENT_PADDING_RIGHT = -11;

    // Bottom of panel aligns with top of the dialog bottom bar
    private const int BOTTOM_ANCHOR_Y = 372;

    private readonly List<OptionLabel> OptionLabels = [];

    // 3-slice stripe pieces
    private Texture2D? StripeLeft;
    private Texture2D? StripeLeftOn;
    private Texture2D? StripeMid;
    private Texture2D? StripeMidOn;
    private Texture2D? StripeRight;
    private Texture2D? StripeRightOn;
    private bool StripesLoaded;

    public OptionMenuPanel()
        : base("lnpcd2", false)
    {
        Name = "OptionMenu";
        Visible = false;

        OkButton = CreateButton("Btn1");

        if (OkButton is not null)
        {
            OkButton.DisabledTexture = UiRenderer.Instance!.GetSpfTexture("_nbtn.spf", 5);
            OkButton.Enabled = false;
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

        EnsureStripeTextures();

        // Frame + children drawn by base
        base.Draw(spriteBatch);
    }

    private void EnsureStripeTextures()
    {
        if (StripesLoaded)
            return;

        StripesLoaded = true;
        var renderer = UiRenderer.Instance;

        if (renderer is null)
            return;

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

    public override void Hide()
    {
        ClearOptionLabels();
        base.Hide();
    }

    public event Action? OnClose;

    public event Action<int>? OnOptionSelected;

    public void ShowOptions(IReadOnlyList<(string Text, ushort Pursuit)> options)
    {
        ClearOptionLabels();
        EnsureStripeTextures();

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
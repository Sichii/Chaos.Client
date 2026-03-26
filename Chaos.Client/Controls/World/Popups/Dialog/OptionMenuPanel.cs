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
///     Floating option menu panel for NPC dialog/menu interactions. Displays a list of clickable text options with a
///     9-slice frame background, centered on screen. Used for DialogMenu, CreatureMenu, Menu, and MenuWithArgs interaction
///     types.
/// </summary>
public sealed class OptionMenuPanel : UIPanel
{
    private const int PANEL_WIDTH = 426;
    private const int ROW_HEIGHT = 18;
    private const int CONTENT_PADDING_TOP = 6;
    private const int CONTENT_PADDING_BOTTOM = 28;
    private const int BTN_HEIGHT = 22;
    private const int BTN_GAP = 4;

    // Content area horizontal bounds from lnpcd2 template
    private const int CONTENT_LEFT = 13;
    private const int CONTENT_RIGHT = 413;
    private const int CONTENT_WIDTH = CONTENT_RIGHT - CONTENT_LEFT;

    // Clickable text area inset from lnpcd2 TextMenuButton template
    private const int TEXT_LEFT = 20;
    private const int TEXT_RIGHT = 406;
    private const int TEXT_WIDTH = TEXT_RIGHT - TEXT_LEFT;
    private const int TEXT_ROW_HEIGHT = 14;

    // OK button template rect from lnpcd2 Btn1 (relative to panel)
    private const int BTN_WIDTH = 61;

    // 9-slice piece names in Setoa.dat (with .spf extension for archive lookup)
    private static readonly string[] FRAME_SPF_NAMES =
    [
        "nd_f01.spf", // 0: top-left corner
        "nd_f02.spf", // 1: top edge
        "nd_f03.spf", // 2: top-right corner
        "nd_f04.spf", // 3: left edge
        "nd_f05.spf", // 4: center fill
        "nd_f06.spf", // 5: right edge
        "nd_f07.spf", // 6: bottom-left corner
        "nd_f08.spf", // 7: bottom edge
        "nd_f08_1.spf" // 8: bottom-right corner
    ];

    private readonly Texture2D?[] FrameTextures = new Texture2D?[9];
    private readonly UIButton? OkButton;
    private readonly List<OptionEntry> Options = [];
    private bool FrameTexturesLoaded;
    private int HoveredIndex = -1;

    public OptionMenuPanel()
    {
        Name = "OptionMenu";
        Visible = false;

        // Create OK button from lnpcd2 prefab Btn1 (_nbtn.spf frames 3-5)
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
                PressedTexture = btnPrefab.Images.Count > 1 ? cache.GetPrefabTexture("lnpcd2", "Btn1", 1) : null
            };

            OkButton.OnClick += () => OnClose?.Invoke();
            AddChild(OkButton);
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        EnsureFrameTextures();

        var sx = ScreenX;
        var sy = ScreenY;

        // Draw the 9-slice frame filling the panel bounds
        DrawNineSlice(
            spriteBatch,
            sx,
            sy,
            Width,
            Height);

        // Draw option text rows
        var optionStartY = sy + CONTENT_PADDING_TOP;

        for (var i = 0; i < Options.Count; i++)
        {
            var rowY = optionStartY + i * ROW_HEIGHT;
            var option = Options[i];

            var color = i == HoveredIndex ? Color.Yellow : Color.White;

            option.TextCache.Update(option.Text, color);

            option.TextCache.Draw(spriteBatch, new Vector2(sx + TEXT_LEFT, rowY + (ROW_HEIGHT - TextRenderer.CHAR_HEIGHT) / 2));
        }

        // Draw children (OK button)
        base.Draw(spriteBatch);
    }

    private void DrawNineSlice(
        SpriteBatch spriteBatch,
        int x,
        int y,
        int width,
        int height)
    {
        var topLeft = FrameTextures[0];
        var topEdge = FrameTextures[1];
        var topRight = FrameTextures[2];
        var leftEdge = FrameTextures[3];
        var center = FrameTextures[4];
        var rightEdge = FrameTextures[5];
        var bottomLeft = FrameTextures[6];
        var bottomEdge = FrameTextures[7];
        var bottomRight = FrameTextures[8];

        // Corner dimensions (use actual texture sizes for accurate tiling)
        var tlW = topLeft?.Width ?? 0;
        var tlH = topLeft?.Height ?? 0;
        var trW = topRight?.Width ?? 0;
        var trH = topRight?.Height ?? 0;
        var blW = bottomLeft?.Width ?? 0;
        var blH = bottomLeft?.Height ?? 0;
        var brW = bottomRight?.Width ?? 0;
        var brH = bottomRight?.Height ?? 0;

        // Edge widths/heights
        var leftW = leftEdge?.Width ?? 0;
        var rightW = rightEdge?.Width ?? 0;
        var topH = topEdge?.Height ?? 0;
        var bottomH = bottomEdge?.Height ?? 0;

        // Inner area
        var innerX = x + tlW;
        var innerY = y + tlH;
        var innerW = width - tlW - trW;
        var innerH = height - tlH - blH;

        // Center fill — tile to cover the inner area
        if (center is not null)
            TileTexture(
                spriteBatch,
                center,
                innerX,
                innerY,
                innerW,
                innerH);

        // Top edge — tile horizontally between corners
        if (topEdge is not null)
            TileTexture(
                spriteBatch,
                topEdge,
                x + tlW,
                y,
                width - tlW - trW,
                topH);

        // Bottom edge — tile horizontally between corners
        if (bottomEdge is not null)
            TileTexture(
                spriteBatch,
                bottomEdge,
                x + blW,
                y + height - bottomH,
                width - blW - brW,
                bottomH);

        // Left edge — tile vertically between corners
        if (leftEdge is not null)
            TileTexture(
                spriteBatch,
                leftEdge,
                x,
                y + tlH,
                leftW,
                height - tlH - blH);

        // Right edge — tile vertically between corners
        if (rightEdge is not null)
            TileTexture(
                spriteBatch,
                rightEdge,
                x + width - rightW,
                y + trH,
                rightW,
                height - trH - brH);

        // Corners
        if (topLeft is not null)
            AtlasHelper.Draw(
                spriteBatch,
                topLeft,
                new Vector2(x, y),
                Color.White);

        if (topRight is not null)
            AtlasHelper.Draw(
                spriteBatch,
                topRight,
                new Vector2(x + width - trW, y),
                Color.White);

        if (bottomLeft is not null)
            AtlasHelper.Draw(
                spriteBatch,
                bottomLeft,
                new Vector2(x, y + height - blH),
                Color.White);

        if (bottomRight is not null)
            AtlasHelper.Draw(
                spriteBatch,
                bottomRight,
                new Vector2(x + width - brW, y + height - brH),
                Color.White);
    }

    private void EnsureFrameTextures()
    {
        if (FrameTexturesLoaded)
            return;

        FrameTexturesLoaded = true;
        var renderer = UiRenderer.Instance;

        if (renderer is null)
            return;

        for (var i = 0; i < FRAME_SPF_NAMES.Length; i++)
            FrameTextures[i] = renderer.GetSpfTexture(FRAME_SPF_NAMES[i]);
    }

    /// <summary>
    ///     Returns the pursuit ID for the option at the given index, or 0 if out of range.
    /// </summary>
    public ushort GetOptionPursuitId(int index)
    {
        if ((index < 0) || (index >= Options.Count))
            return 0;

        return Options[index].PursuitId;
    }

    public void Hide()
    {
        Visible = false;
        Options.Clear();
        HoveredIndex = -1;
    }

    public event Action? OnClose;

    public event Action<int>? OnOptionSelected;

    /// <summary>
    ///     Shows the option menu with the given list of text/pursuit pairs. Computes panel size from option count and centers
    ///     on screen.
    /// </summary>
    public void ShowOptions(IReadOnlyList<(string Text, ushort Pursuit)> options)
    {
        Options.Clear();
        HoveredIndex = -1;

        foreach ((var text, var pursuit) in options)
            Options.Add(new OptionEntry(text, pursuit));

        // Dynamic sizing
        var totalContentHeight = Options.Count * ROW_HEIGHT;
        var panelHeight = CONTENT_PADDING_TOP + totalContentHeight + CONTENT_PADDING_BOTTOM + BTN_HEIGHT + BTN_GAP;

        Width = PANEL_WIDTH;
        Height = panelHeight;

        // Center on screen
        X = (ChaosGame.VIRTUAL_WIDTH - Width) / 2;
        Y = (ChaosGame.VIRTUAL_HEIGHT - Height) / 2;

        // Position OK button at bottom-right of content area
        if (OkButton is not null)
        {
            OkButton.X = CONTENT_RIGHT - BTN_WIDTH;
            OkButton.Y = Height - BTN_HEIGHT - (CONTENT_PADDING_BOTTOM - BTN_HEIGHT) / 2;
        }

        Visible = true;
    }

    /// <summary>
    ///     Tiles a texture to fill a rectangular area. Draws full copies where possible and clips the final row/column via
    ///     source rectangles.
    /// </summary>
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

        // Escape dismisses the menu
        if (input.WasKeyPressed(Keys.Escape))
        {
            Hide();
            OnClose?.Invoke();

            return;
        }

        // Mouse hover tracking over option rows
        HoveredIndex = -1;

        if (Options.Count > 0)
        {
            var localX = input.MouseX - ScreenX;
            var localY = input.MouseY - ScreenY;
            var optionStartY = CONTENT_PADDING_TOP;

            if ((localX >= TEXT_LEFT) && (localX < TEXT_RIGHT))
                for (var i = 0; i < Options.Count; i++)
                {
                    var rowTop = optionStartY + i * ROW_HEIGHT;
                    var rowBottom = rowTop + ROW_HEIGHT;

                    if ((localY >= rowTop) && (localY < rowBottom))
                    {
                        HoveredIndex = i;

                        break;
                    }
                }
        }

        // Click selects option
        if (input.WasLeftButtonPressed && (HoveredIndex >= 0))
            OnOptionSelected?.Invoke(HoveredIndex);

        base.Update(gameTime, input);
    }

    private sealed record OptionEntry(string Text, ushort PursuitId)
    {
        public TextElement TextCache { get; } = CreateTextElement(Text);

        private static TextElement CreateTextElement(string text)
        {
            var element = new TextElement();
            element.Update(text, Color.White);

            return element;
        }
    }
}
#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Rendering;
using Chaos.Client.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.Generic;

/// <summary>
///     Popup text window for ScrollWindow, NonScrollWindow, and WoodenBoard ServerMessageTypes.
///     Fixed position at (140,60), size 360x180 matching the original client. ScrollWindow and
///     NonScrollWindow are identical (dialog frame with scrollbar and close button). WoodenBoard
///     uses woodbk.epf wooden plank background from Legend.dat. All styles support mouse wheel scrolling.
/// </summary>
public sealed class TextPopupControl : UIPanel
{
    // Original client RECT {top,left,bottom,right}: {60, 140, 240, 500}
    // Position: X=140, Y=60. Size: W=360, H=180.
    private const int POPUP_X = 140;
    private const int POPUP_Y = 60;
    private const int POPUP_WIDTH = 360;
    private const int POPUP_HEIGHT = 180;

    // Dialog frame text insets (16px dlgframe border)
    private const int FRAME_INSET = DialogFrame.BORDER_SIZE;

    // Wooden board text insets (per RE: 16px left/right, 12px top, 32px bottom)
    private const int WOOD_INSET_X = 16;
    private const int WOOD_INSET_TOP = 12;
    private const int WOOD_INSET_BOTTOM = 32;

    // butt001.epf frame indices for Close button (2nd button: frames 3,4,5)
    private const int CLOSE_NORMAL = 3;
    private const int CLOSE_PRESSED = 4;
    private readonly UIButton CloseButton;
    private readonly ScrollBarControl Scrollbar;

    private readonly UILabel TextLabel;
    private Texture2D? DialogBackground;
    private Texture2D? WoodBackground;

    public TextPopupControl()
    {
        Name = "TextPopup";
        Visible = false;
        X = POPUP_X;
        Y = POPUP_Y;
        Width = POPUP_WIDTH;
        Height = POPUP_HEIGHT;

        TextLabel = new UILabel
        {
            WordWrap = true,
            ForegroundColor = Color.White
        };
        AddChild(TextLabel);

        // Close button — bottom-right, under the scrollbar
        var cache = UiRenderer.Instance!;
        var closeNormalTex = cache.GetEpfTexture("butt001.epf", CLOSE_NORMAL);
        var closePressedTex = cache.GetEpfTexture("butt001.epf", CLOSE_PRESSED);
        var btnW = closeNormalTex.Width;
        var btnH = closeNormalTex.Height;

        CloseButton = new UIButton
        {
            Name = "Close",
            X = POPUP_WIDTH - FRAME_INSET - btnW,
            Y = POPUP_HEIGHT - FRAME_INSET - btnH,
            Width = btnW,
            Height = btnH,
            NormalTexture = closeNormalTex,
            PressedTexture = closePressedTex
        };
        CloseButton.OnClick += Hide;
        AddChild(CloseButton);

        // Scrollbar — right side, above the close button
        var scrollHeight = CloseButton.Y - FRAME_INSET;

        Scrollbar = new ScrollBarControl
        {
            X = POPUP_WIDTH - FRAME_INSET - ScrollBarControl.DEFAULT_WIDTH,
            Y = FRAME_INSET,
            Height = scrollHeight,
            Visible = false
        };
        AddChild(Scrollbar);

        LoadBackgrounds();
    }

    public void Hide()
    {
        Visible = false;
        OnClose?.Invoke();
    }

    private void LoadBackgrounds()
    {
        // Dialog frame: DlgBack2.spf tiled background + dlgframe.epf 8-piece border
        using var bgTile = DataContext.UserControls.GetSpfImage("DlgBack2.spf");

        if (bgTile is not null)
        {
            using var composite = DialogFrame.Composite(bgTile, POPUP_WIDTH, POPUP_HEIGHT);

            if (composite is not null)
                DialogBackground = TextureConverter.ToTexture2D(composite);
        }

        // Wooden board: woodbk.epf from Legend.dat (360x180, exact match)
        using var woodImage = DataContext.UserControls.GetLegendEpfImage("woodbk.epf");

        if (woodImage is not null)
            WoodBackground = TextureConverter.ToTexture2D(woodImage);
    }

    public event Action? OnClose;

    /// <summary>
    ///     Shows a popup with the given text. ScrollWindow and NonScrollWindow are identical (dialog
    ///     frame with scrollbar and close button). WoodenBoard uses wooden plank background.
    /// </summary>
    public void Show(string text, PopupStyle style = PopupStyle.Scroll)
    {
        var contentHeight = Scrollbar.Height;

        if (style == PopupStyle.Wooden)
        {
            Background = WoodBackground;
            TextLabel.X = WOOD_INSET_X;
            TextLabel.Y = WOOD_INSET_TOP;
            TextLabel.Width = POPUP_WIDTH - WOOD_INSET_X * 2;
            contentHeight = POPUP_HEIGHT - WOOD_INSET_TOP - WOOD_INSET_BOTTOM;
            TextLabel.Height = contentHeight;
            Scrollbar.Visible = false;
            CloseButton.Visible = false;
        } else
        {
            // Scroll and NonScroll are identical per original client RE
            Background = DialogBackground;
            TextLabel.X = FRAME_INSET;
            TextLabel.Y = FRAME_INSET;
            TextLabel.Width = POPUP_WIDTH - FRAME_INSET * 2 - ScrollBarControl.DEFAULT_WIDTH;
            TextLabel.Height = contentHeight;
            Scrollbar.Visible = true;
            CloseButton.Visible = true;
        }

        TextLabel.ScrollOffset = 0;
        TextLabel.Text = text;

        var visibleLines = contentHeight / TextRenderer.CHAR_HEIGHT;
        var totalLines = TextLabel.ContentHeight / TextRenderer.CHAR_HEIGHT;
        var scrollMax = Math.Max(0, totalLines - visibleLines);
        Scrollbar.Value = 0;
        Scrollbar.MaxValue = scrollMax;
        Scrollbar.TotalItems = totalLines;
        Scrollbar.VisibleItems = visibleLines;

        Visible = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (input.WasKeyPressed(Keys.Escape))
        {
            Hide();

            return;
        }

        // Mouse wheel scrolling (all styles)
        if ((input.ScrollDelta != 0) && (TextLabel.ContentHeight > TextLabel.Height))
        {
            var visibleLines = TextLabel.Height / TextRenderer.CHAR_HEIGHT;
            var totalLines = TextLabel.ContentHeight / TextRenderer.CHAR_HEIGHT;
            var maxScroll = Math.Max(0, totalLines - visibleLines);

            Scrollbar.Value = Math.Clamp(Scrollbar.Value - input.ScrollDelta, 0, maxScroll);
        }

        // Sync label scroll from scrollbar interaction (arrow clicks, thumb drag)
        TextLabel.ScrollOffset = Scrollbar.Value * TextRenderer.CHAR_HEIGHT;

        base.Update(gameTime, input);
    }
}
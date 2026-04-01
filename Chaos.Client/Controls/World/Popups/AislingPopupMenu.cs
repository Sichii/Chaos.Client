#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     A popup menu that appears when Ctrl+clicking a non-self aisling. Uses popupbox.epf as background. Top row shows the
///     player's name, bottom 3 rows are clickable options: info, group request, whisper.
/// </summary>
public sealed class AislingPopupMenu : UIPanel
{
    private const string EPF_FILE = "popupbox.epf";
    private const int BOX_X = 4;
    private const int BOX_WIDTH = 74;
    private const int BOX_HEIGHT = 14;
    private const int BOX_START_Y = 6;
    private const int OPTIONS_OFFSET_Y = 2;

    private static readonly Color BOX_FILL = new Color(68, 68, 68) * (140 / 255f);
    private static readonly Color BOX_HOVER = new Color(187, 187, 187) * (160 / 255f);

    private static readonly string[] OPTION_LABELS =
    [
        "info",
        "group request",
        "whisper"
    ];

    private readonly UILabel NameLabel;
    private readonly Action[] OptionCallbacks = new Action[3];
    private readonly UILabel[] OptionLabels;
    private Texture2D? FrameTexture;
    private int HoveredIndex = -1;

    public AislingPopupMenu()
    {
        Visible = false;

        NameLabel = new UILabel
        {
            Name = "Name",
            X = BOX_X,
            Y = BOX_START_Y,
            Width = BOX_WIDTH,
            Height = BOX_HEIGHT,
            Alignment = TextAlignment.Center,
            PaddingLeft = 0
        };

        AddChild(NameLabel);

        OptionLabels = new UILabel[3];

        for (var i = 0; i < 3; i++)
        {
            OptionLabels[i] = new UILabel
            {
                Name = $"Option{i}",
                X = BOX_X,
                Y = BOX_START_Y + OPTIONS_OFFSET_Y + (i + 1) * BOX_HEIGHT,
                Width = BOX_WIDTH,
                Height = BOX_HEIGHT,
                Text = OPTION_LABELS[i],
                PaddingLeft = 0
            };

            AddChild(OptionLabels[i]);
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        var bg = GetFrameTexture();

        if (bg is null)
            return;

        var sx = ScreenX;
        var sy = ScreenY;

        // Semi-transparent fill + hover, then frame on top
        DrawRect(
            spriteBatch,
            new Rectangle(
                sx,
                sy,
                Width,
                Height),
            BOX_FILL);

        if (HoveredIndex >= 0)
            DrawRect(
                spriteBatch,
                new Rectangle(
                    sx + BOX_X,
                    sy + BOX_START_Y + OPTIONS_OFFSET_Y + (HoveredIndex + 1) * BOX_HEIGHT,
                    BOX_WIDTH,
                    BOX_HEIGHT),
                BOX_HOVER);

        spriteBatch.Draw(bg, new Vector2(sx, sy), Color.White);

        // Children (NameLabel + OptionLabels) drawn by base
        base.Draw(spriteBatch);
    }

    private Texture2D? GetFrameTexture()
    {
        if (FrameTexture is not null)
            return FrameTexture;

        if (UiRenderer.Instance is null)
            return null;

        var frameCount = UiRenderer.Instance.GetEpfFrameCount(EPF_FILE);

        if (frameCount <= 0)
            return null;

        FrameTexture = UiRenderer.Instance.GetEpfTexture(EPF_FILE, 0);
        Width = FrameTexture.Width;
        Height = FrameTexture.Height;

        return FrameTexture;
    }

    public void Hide()
    {
        Visible = false;
        HoveredIndex = -1;
    }

    public void Show(
        int screenX,
        int screenY,
        string name,
        Action onInfo,
        Action onGroupRequest,
        Action onWhisper)
    {
        var bg = GetFrameTexture();

        if (bg is null)
            return;

        NameLabel.Text = name;
        NameLabel.ForegroundColor = Color.White;
        OptionCallbacks[0] = onInfo;
        OptionCallbacks[1] = onGroupRequest;
        OptionCallbacks[2] = onWhisper;

        X = screenX;
        Y = screenY;

        // Clamp to screen bounds
        if ((X + Width) > ChaosGame.VIRTUAL_WIDTH)
            X = ChaosGame.VIRTUAL_WIDTH - Width;

        if ((Y + Height) > ChaosGame.VIRTUAL_HEIGHT)
            Y = ChaosGame.VIRTUAL_HEIGHT - Height;

        HoveredIndex = -1;
        Visible = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible)
            return;

        var localX = input.MouseX - ScreenX;
        var localY = input.MouseY - ScreenY;

        // Options start at row 1 (row 0 is the name)
        var optionStartY = BOX_START_Y + OPTIONS_OFFSET_Y + BOX_HEIGHT;
        var optionEndY = optionStartY + 3 * BOX_HEIGHT;

        if ((localX >= BOX_X) && (localX < (BOX_X + BOX_WIDTH)) && (localY >= optionStartY) && (localY < optionEndY))
            HoveredIndex = (localY - optionStartY) / BOX_HEIGHT;
        else
            HoveredIndex = -1;

        if (input.WasLeftButtonPressed)
        {
            if ((HoveredIndex >= 0) && (HoveredIndex < 3))
            {
                OptionCallbacks[HoveredIndex]();
                Hide();
            } else
                Hide();

            return;
        }

        if (input.WasRightButtonPressed || input.WasKeyPressed(Keys.Escape))
            Hide();
    }
}
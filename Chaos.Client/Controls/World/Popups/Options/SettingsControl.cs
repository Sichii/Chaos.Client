#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Chaos.Client.Utilities;
using Chaos.Client.ViewModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Options;

/// <summary>
///     Settings panel using _nsett prefab. Triggered by F4 key. 13 toggle settings in 2 columns (10 left, 3 right).
///     Number buttons from _nsettb.spf (2 frames per setting: normal, pressed). Layout derived from TopButton/TopText/
///     BottomText/RightText prefab rects: ROW_HEIGHT=21, COLUMN_OFFSET=211, ROWS_PER_COLUMN=10.
/// </summary>
public sealed class SettingsControl : PrefabPanel
{
    private const int ROWS_PER_COLUMN = 10;
    private const int ROW_HEIGHT = 21;
    private const int BUTTON_X = 15;
    private const int BUTTON_Y = 40;
    private const int BUTTON_SIZE = 16;
    private const int LABEL_X = 40;
    private const int LABEL_Y = 42;
    private const int COLUMN_OFFSET = 211;
    private const int LABEL_WIDTH = 170;
    private readonly UserOptions Options;

    private readonly string[] SettingBaseNames = new string[UserOptions.SETTING_COUNT];
    private readonly UILabel[] SettingLabels = new UILabel[UserOptions.SETTING_COUNT];
    private SlideAnimator Slide;
    private int SlideAnchorY;
    private bool SlideMode;

    public UIButton? CancelButton { get; }

    public UIButton? OkButton { get; }

    public SettingsControl(UserOptions options)
        : base("_nsett", false)
    {
        Options = options;
        Name = "Settings";
        Visible = false;

        OkButton = CreateButton("OK");
        CancelButton = CreateButton("Cancel");

        if (OkButton is not null)
            OkButton.OnClick += Close;

        if (CancelButton is not null)
            CancelButton.OnClick += Close;

        // Create per-setting number buttons from _nsettb.spf (2 frames per setting: normal, pressed)
        // 2-column layout: settings 0-9 in left column, 10-12 in right column
        var cache = UiRenderer.Instance!;

        for (var i = 0; i < UserOptions.SETTING_COUNT; i++)
        {
            var settingIndex = i;
            var row = i % ROWS_PER_COLUMN;
            var col = i / ROWS_PER_COLUMN;
            var normalIdx = i * 2;
            var pressedIdx = i * 2 + 1;

            var btn = new UIButton
            {
                Name = $"Setting{i}",
                X = BUTTON_X + col * COLUMN_OFFSET,
                Y = BUTTON_Y + row * ROW_HEIGHT,
                Width = BUTTON_SIZE,
                Height = BUTTON_SIZE,
                NormalTexture = cache.GetSpfTexture("_nsettb.spf", normalIdx),
                PressedTexture = cache.GetSpfTexture("_nsettb.spf", pressedIdx)
            };

            btn.OnClick += () => Options.Toggle(settingIndex);

            AddChild(btn);

            var label = new UILabel
            {
                Name = $"SettingLabel{i}",
                X = LABEL_X + col * COLUMN_OFFSET,
                Y = LABEL_Y + row * ROW_HEIGHT,
                Width = LABEL_WIDTH,
                Height = 12,
                PaddingLeft = 0,
                PaddingTop = 0,
                Alignment = TextAlignment.Left
            };

            SettingLabels[i] = label;
            AddChild(label);
        }

        // Default names for client-local settings (server settings populated via SendOptionToggle(Request))
        SetSettingName(6, "Use Group Window");
        SetSettingName(8, "Scroll Screen");
        SetSettingName(9, "the Shift key.");
        SetSettingName(10, "click character profile");
        SetSettingName(11, "NPC Record Mundane Chat");
        SetSettingName(12, "group recruiting");

        // Refresh all labels to reflect current option values
        for (var i = 0; i < UserOptions.SETTING_COUNT; i++)
            RefreshLabel(i);

        Options.SettingChanged += (index, _) => RefreshLabel(index);
    }

    private void Close()
    {
        if (SlideMode)
            Slide.SlideOut();
        else
        {
            Hide();
            OnClose?.Invoke();
        }
    }

    public override void Hide()
    {
        if (SlideMode)
            Slide.Hide(this);
        else
            Visible = false;
    }

    public event Action? OnClose;

    private void RefreshLabel(int index)
    {
        var baseName = SettingBaseNames[index];

        if (string.IsNullOrEmpty(baseName))
            return;

        var value = Options[index];

        var text = index switch
        {
            // "Scroll Screen : Rough" / "Scroll Screen : Smooth"
            8 => $"Scroll Screen : {(value ? "Smooth" : "Rough")}",

            // "the Shift key." / "not use the Shift key."
            9 => value ? "the Shift key." : "not use the Shift key.",

            // Server settings — full text from server (already includes :ON/:OFF)
            _ when UserOptions.IsServerSetting(index) => baseName,

            // Client-local settings — append :ON/:OFF
            _ => $"{baseName} :{(value ? "ON" : "OFF")}"
        };

        SettingLabels[index].ForegroundColor = Color.White;
        SettingLabels[index].Text = text;
    }

    public void SetSettingName(int index, string name)
    {
        if (index is < 0 or >= UserOptions.SETTING_COUNT)
            return;

        SettingBaseNames[index] = name;
        RefreshLabel(index);
    }

    public void SetSlideAnchor(int anchorX, int anchorY)
    {
        Slide.SetSlideAnchor(anchorX, Width);
        SlideAnchorY = anchorY;
    }

    /// <summary>
    ///     Shows immediately at top-center of screen (hotkey mode).
    /// </summary>
    public override void Show()
    {
        X = (640 - Width) / 2;
        Y = 0;
        Visible = true;
        SlideMode = false;
    }

    /// <summary>
    ///     Slides out from the left edge of MainOptionsControl (button mode).
    /// </summary>
    public void SlideIn()
    {
        if (Visible)
            return;

        Y = SlideAnchorY;
        Slide.SlideIn(this);
        SlideMode = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (Slide.Update(gameTime, this))
        {
            OnClose?.Invoke();

            return;
        }

        if (input.WasKeyPressed(Keys.Escape) || input.WasKeyPressed(Keys.F4))
        {
            Close();

            return;
        }

        base.Update(gameTime, input);
    }
}
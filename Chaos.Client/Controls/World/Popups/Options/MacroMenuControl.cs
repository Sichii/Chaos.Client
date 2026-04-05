#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Extensions;
using Chaos.Client.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Options;

/// <summary>
///     Macro editor using _nmacro prefab. Triggered by F3 key or Options → Macro button. Displays 10 editable text fields
///     for macro commands. Loads from player config on open, saves on close.
/// </summary>
public sealed class MacroMenuControl : PrefabPanel
{
    private const int MAX_MACROS = 10;
    private const int ROW_HEIGHT = 21;
    private const int LABEL_START_Y = 40;
    private const int TEXTBOX_X = 40;
    private const int TEXTBOX_WIDTH = 385;
    private const int MAX_MACRO_LENGTH = 50;

    private readonly UITextBox[] MacroTextBoxes = new UITextBox[MAX_MACROS];
    private SlideAnimator Slide;
    private int SlideAnchorY;
    private bool SlideMode;

    public UIButton? CancelButton { get; }
    public UIButton? OkButton { get; }

    public MacroMenuControl()
        : base("_nmacro", false)
    {
        Name = "MacroMenu";
        Visible = false;

        OkButton = CreateButton("OK");
        CancelButton = CreateButton("Cancel");

        if (OkButton is not null)
            OkButton.OnClick += SaveAndClose;

        if (CancelButton is not null)
            CancelButton.OnClick += Close;

        for (var i = 0; i < MAX_MACROS; i++)
        {
            MacroTextBoxes[i] = new UITextBox
            {
                Name = $"Macro{i}",
                X = TEXTBOX_X,
                Y = LABEL_START_Y + i * ROW_HEIGHT,
                Width = TEXTBOX_WIDTH,
                Height = ROW_HEIGHT,
                MaxLength = MAX_MACRO_LENGTH,
                ForegroundColor = LegendColors.White,
                FocusedBackgroundColor = new Color(
                    0,
                    0,
                    0,
                    255)
            };

            AddChild(MacroTextBoxes[i]);
        }
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

    /// <summary>
    ///     Returns the macro value for the given slot index, or empty string if out of range.
    /// </summary>
    public string GetMacroValue(int index) => index is >= 0 and < MAX_MACROS ? MacroTextBoxes[index].Text : string.Empty;

    /// <summary>
    ///     Returns the macro values for all slots.
    /// </summary>
    public string[] GetMacroValues()
    {
        var values = new string[MAX_MACROS];

        for (var i = 0; i < MAX_MACROS; i++)
            values[i] = MacroTextBoxes[i].Text;

        return values;
    }

    public override void Hide()
    {
        if (SlideMode)
            Slide.Hide(this);
        else
            Visible = false;
    }

    public event Action? OnClose;

    private void Save()
    {
        var macros = GetMacroValues();
        DataContext.LocalPlayerSettings.SaveMacros(macros);
    }

    private void SaveAndClose()
    {
        Save();
        Close();
    }

    /// <summary>
    ///     Populates all macro textboxes from the given values array.
    /// </summary>
    public void SetMacros(string[] macros)
    {
        for (var i = 0; i < MAX_MACROS; i++)
            MacroTextBoxes[i].Text = i < macros.Length ? macros[i] : string.Empty;
    }

    /// <summary>
    ///     Sets the slide anchor to the left edge of the parent panel (MainOptionsControl). The sub-panel slides left out of
    ///     the anchor and back into it on close.
    /// </summary>
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
        this.CenterHorizontallyOnScreen();
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

        if (input.WasKeyPressed(Keys.Escape) || input.WasKeyPressed(Keys.F3))
        {
            Close();

            return;
        }

        base.Update(gameTime, input);
    }
}
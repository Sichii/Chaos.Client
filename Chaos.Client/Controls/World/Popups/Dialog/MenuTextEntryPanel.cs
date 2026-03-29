#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Extensions;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Dialog;

/// <summary>
///     Text entry sub-panel for MenuType.TextEntry (2) and MenuType.TextEntryWithArgs (3). Uses lnpcd2 template rects with
///     9-slice frame background. The panel has a text input field with an optional label, and an OK button. Visually
///     similar to OptionMenuPanel but with a text input instead of option rows.
/// </summary>
public sealed class MenuTextEntryPanel : UIPanel
{
    private const int PANEL_WIDTH = 426;
    private const int PANEL_HEIGHT = 100;

    // Text input area from lnpcd2 template rects
    private const int INPUT_LABEL_X = 22;
    private const int INPUT_LABEL_Y = 10;
    private const int INPUT_LABEL_WIDTH = 40;
    private const int INPUT_FIELD_X = 72;
    private const int INPUT_FIELD_Y = 8;
    private const int INPUT_FIELD_WIDTH = 332;
    private const int INPUT_FIELD_HEIGHT = 20;

    // Button from lnpcd2 Btn1 template (relative to panel)
    private const int BTN_X = 345;
    private const int BTN_Y = 68;
    private const int BTN_WIDTH = 61;
    private const int BTN_HEIGHT = 22;

    private readonly TextElement InputLabelText = new();
    private readonly UIButton? OkButton;
    private readonly UITextBox TextInput;

    /// <summary>
    ///     Previous args string carried from a MenuWithArgs/TextEntryWithArgs interaction. Sent back to the server alongside
    ///     the user's input text.
    /// </summary>
    public string? PreviousArgs { get; private set; }

    public MenuTextEntryPanel()
    {
        Name = "MenuTextEntry";
        Visible = false;

        Width = PANEL_WIDTH;
        Height = PANEL_HEIGHT;
        this.CenterOnScreen();

        InputLabelText.Update("Input:", Color.White);

        TextInput = new UITextBox
        {
            Name = "TextInput",
            X = INPUT_FIELD_X,
            Y = INPUT_FIELD_Y,
            Width = INPUT_FIELD_WIDTH,
            Height = INPUT_FIELD_HEIGHT,
            MaxLength = 255,
            ForegroundColor = Color.White,
            FocusedBackgroundColor = new Color(
                0,
                0,
                0,
                120)
        };

        AddChild(TextInput);

        // Try to load button from lnpcd2 prefab
        try
        {
            var prefab = DataContext.UserControls.Get("lnpcd2");

            if (prefab?.Contains("Btn1") == true)
            {
                var btnPrefab = prefab["Btn1"];
                var rect = btnPrefab.Control.Rect;

                if (rect is not null)
                {
                    var cache = UiRenderer.Instance!;

                    OkButton = new UIButton
                    {
                        Name = "Btn1",
                        X = BTN_X,
                        Y = BTN_Y,
                        Width = BTN_WIDTH,
                        Height = BTN_HEIGHT,
                        NormalTexture = btnPrefab.Images.Count > 0 ? cache.GetPrefabTexture("lnpcd2", "Btn1", 0) : null,
                        PressedTexture = btnPrefab.Images.Count > 1 ? cache.GetPrefabTexture("lnpcd2", "Btn1", 1) : null
                    };
                }
            }
        } catch
        {
            // Prefab not available
        }

        OkButton ??= new UIButton
        {
            Name = "Btn1",
            X = BTN_X,
            Y = BTN_Y,
            Width = BTN_WIDTH,
            Height = BTN_HEIGHT
        };

        OkButton.OnClick += HandleSubmit;
        AddChild(OkButton);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        // Draw dark background
        DrawRect(
            spriteBatch,
            new Rectangle(
                ScreenX,
                ScreenY,
                Width,
                Height),
            new Color(
                20,
                20,
                30,
                220));

        // Draw input label
        InputLabelText.Draw(spriteBatch, new Vector2(ScreenX + INPUT_LABEL_X, ScreenY + INPUT_LABEL_Y));

        base.Draw(spriteBatch);
    }

    private void HandleSubmit()
    {
        var text = TextInput.Text.Trim();

        if (text.Length > 0)
            OnTextSubmit?.Invoke(text);
    }

    public void Hide()
    {
        TextInput.IsFocused = false;
        Visible = false;
    }

    public event Action? OnClose;

    public event Action<string>? OnTextSubmit;

    public void ShowTextEntry(string? previousArgs)
    {
        PreviousArgs = previousArgs;

        TextInput.Text = string.Empty;
        TextInput.CursorPosition = 0;
        TextInput.IsFocused = true;

        Visible = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (input.WasKeyPressed(Keys.Escape))
        {
            OnClose?.Invoke();

            return;
        }

        if (input.WasKeyPressed(Keys.Enter))
        {
            HandleSubmit();

            return;
        }

        base.Update(gameTime, input);
    }
}
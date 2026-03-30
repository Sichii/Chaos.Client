#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Dialog;

/// <summary>
///     Dual-field ID/password entry sub-panel for DialogType.Protected (9). Uses the shared ornate frame with manual layout
///     for prompt, ID field, password field (masked), and OK button. Right-aligned, bottom-anchored above the dialog bar.
/// </summary>
public sealed class ProtectedEntryPanel : FramedDialogPanel
{
    private const int BOTTOM_ANCHOR_Y = 372;
    private const int PANEL_WIDTH = 426;
    private const int PANEL_HEIGHT = 130;
    private const int PADDING_LEFT = 23;
    private const int LABEL_WIDTH = 90;
    private const int FIELD_HEIGHT = 20;
    private const int ROW_GAP = 6;

    private readonly UITextBox IdField;
    private readonly TextElement IdLabelText = new();
    private readonly UITextBox PasswordField;
    private readonly TextElement PasswordLabelText = new();
    private readonly TextElement PrologText = new();

    public ProtectedEntryPanel()
        : base("lnpcd2", false)
    {
        Name = "ProtectedEntry";
        Visible = false;

        Width = PANEL_WIDTH;
        Height = PANEL_HEIGHT;
        X = ChaosGame.VIRTUAL_WIDTH - Width;
        Y = BOTTOM_ANCHOR_Y - Height;

        IdLabelText.Update("I   D   : ", Color.White);
        PasswordLabelText.Update("Password: ", Color.White);

        // ID field
        IdField = new UITextBox
        {
            Name = "IdField",
            X = PADDING_LEFT + LABEL_WIDTH,
            Y = 36,
            Width = PANEL_WIDTH - PADDING_LEFT * 2 - LABEL_WIDTH,
            Height = FIELD_HEIGHT,
            MaxLength = 50,
            ForegroundColor = Color.Black,
            FocusedBackgroundColor = Color.White
        };

        AddChild(IdField);

        // Password field (masked)
        PasswordField = new UITextBox
        {
            Name = "PwField",
            X = PADDING_LEFT + LABEL_WIDTH,
            Y = 36 + FIELD_HEIGHT + ROW_GAP,
            Width = PANEL_WIDTH - PADDING_LEFT * 2 - LABEL_WIDTH,
            Height = FIELD_HEIGHT,
            MaxLength = 50,
            IsMasked = true,
            ForegroundColor = Color.Black,
            FocusedBackgroundColor = Color.White
        };

        AddChild(PasswordField);

        OkButton = CreateButton("Btn1");

        if (OkButton is not null)
        {
            OkButton.X = Width - 61 - 20;
            OkButton.Y = Height - 22 - 3;
            OkButton.OnClick += HandleSubmit;
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        // Frame + children drawn by base
        base.Draw(spriteBatch);

        // Labels drawn manually on top
        var sx = ScreenX;
        var sy = ScreenY;

        PrologText.Draw(spriteBatch, new Vector2(sx + PADDING_LEFT, sy + 12));
        IdLabelText.Draw(spriteBatch, new Vector2(sx + PADDING_LEFT, sy + 38));
        PasswordLabelText.Draw(spriteBatch, new Vector2(sx + PADDING_LEFT, sy + 38 + FIELD_HEIGHT + ROW_GAP));
    }

    private void HandleSubmit()
    {
        var id = IdField.Text.Trim();
        var password = PasswordField.Text.Trim();

        if ((id.Length > 0) && (password.Length > 0))
            OnProtectedSubmit?.Invoke(id, password);
    }

    public override void Hide()
    {
        IdField.IsFocused = false;
        PasswordField.IsFocused = false;
        base.Hide();
    }

    public event Action? OnClose;

    public event Action<string, string>? OnProtectedSubmit;

    public void ShowProtected(string promptText)
    {
        PrologText.Update(promptText, Color.White);

        IdField.Text = string.Empty;
        IdField.CursorPosition = 0;
        IdField.IsFocused = true;

        PasswordField.Text = string.Empty;
        PasswordField.CursorPosition = 0;

        Show();
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

        // Tab between fields
        if (input.WasKeyPressed(Keys.Tab))
        {
            if (IdField.IsFocused)
            {
                IdField.IsFocused = false;
                PasswordField.IsFocused = true;
            } else
            {
                PasswordField.IsFocused = false;
                IdField.IsFocused = true;
            }
        }

        base.Update(gameTime, input);
    }
}
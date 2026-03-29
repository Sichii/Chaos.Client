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
///     Dual-field ID/password entry sub-panel for DialogType.Protected (9). Based on lnpcnid control file layout from the
///     original client. Displays a prompt, an ID field, a password field (masked input), and OK button.
/// </summary>
public sealed class ProtectedEntryPanel : UIPanel
{
    private const int PANEL_WIDTH = 426;
    private const int PANEL_HEIGHT = 160;
    private const int PADDING = 13;
    private const int LABEL_HEIGHT = 16;
    private const int FIELD_HEIGHT = 20;
    private const int ROW_GAP = 6;
    private const int LABEL_WIDTH = 90;
    private readonly UITextBox IdField;

    private readonly TextElement IdLabelText = new();
    private readonly UIButton? OkButton;
    private readonly UITextBox PasswordField;
    private readonly TextElement PasswordLabelText = new();
    private readonly TextElement PrologText = new();

    public ProtectedEntryPanel()
    {
        Name = "ProtectedEntry";
        Visible = false;

        Width = PANEL_WIDTH;
        Height = PANEL_HEIGHT;
        this.CenterOnScreen();

        IdLabelText.Update("I   D   : ", Color.White);
        PasswordLabelText.Update("Password: ", Color.White);

        // ID field
        IdField = new UITextBox
        {
            Name = "IdField",
            X = PADDING + LABEL_WIDTH,
            Y = 36,
            Width = PANEL_WIDTH - PADDING * 2 - LABEL_WIDTH,
            Height = FIELD_HEIGHT,
            MaxLength = 50,
            ForegroundColor = Color.White,
            FocusedBackgroundColor = new Color(
                0,
                0,
                0,
                120)
        };

        AddChild(IdField);

        // Password field (masked)
        PasswordField = new UITextBox
        {
            Name = "PwField",
            X = PADDING + LABEL_WIDTH,
            Y = 36 + FIELD_HEIGHT + ROW_GAP,
            Width = PANEL_WIDTH - PADDING * 2 - LABEL_WIDTH,
            Height = FIELD_HEIGHT,
            MaxLength = 50,
            IsMasked = true,
            ForegroundColor = Color.White,
            FocusedBackgroundColor = new Color(
                0,
                0,
                0,
                120)
        };

        AddChild(PasswordField);

        // Try to load Btn1 from lnpcnid prefab if available, otherwise create manually
        try
        {
            var prefab = DataContext.UserControls.Get("lnpcnid");

            if (prefab?.Contains("Btn1") == true)
            {
                var btnPrefab = prefab["Btn1"];
                var rect = btnPrefab.Control.Rect;

                if (rect is not null)
                {
                    var r = rect.Value;
                    var cache = UiRenderer.Instance!;

                    OkButton = new UIButton
                    {
                        Name = "Btn1",
                        X = (int)r.Left,
                        Y = (int)r.Top,
                        Width = (int)r.Width,
                        Height = (int)r.Height,
                        NormalTexture = btnPrefab.Images.Count > 0 ? cache.GetPrefabTexture("lnpcnid", "Btn1", 0) : null,
                        PressedTexture = btnPrefab.Images.Count > 1 ? cache.GetPrefabTexture("lnpcnid", "Btn1", 1) : null
                    };
                }
            }
        } catch
        {
            // lnpcnid prefab not in archives — create button manually
        }

        OkButton ??= new UIButton
        {
            Name = "Btn1",
            X = PANEL_WIDTH - PADDING - 61,
            Y = PANEL_HEIGHT - 26,
            Width = 61,
            Height = 22
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

        // Prolog text
        PrologText.Draw(spriteBatch, new Vector2(ScreenX + PADDING, ScreenY + 10));

        // Labels
        IdLabelText.Draw(spriteBatch, new Vector2(ScreenX + PADDING, ScreenY + 38));
        PasswordLabelText.Draw(spriteBatch, new Vector2(ScreenX + PADDING, ScreenY + 38 + FIELD_HEIGHT + ROW_GAP));

        base.Draw(spriteBatch);
    }

    private void HandleSubmit()
    {
        var id = IdField.Text.Trim();
        var password = PasswordField.Text.Trim();

        if ((id.Length > 0) && (password.Length > 0))
            OnProtectedSubmit?.Invoke(id, password);
    }

    public void Hide()
    {
        IdField.IsFocused = false;
        PasswordField.IsFocused = false;
        Visible = false;
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
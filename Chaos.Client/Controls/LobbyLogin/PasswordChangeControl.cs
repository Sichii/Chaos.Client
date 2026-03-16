#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.LobbyLogin;

public class PasswordChangeControl : UIPanel
{
    public UIButton CancelButton { get; }
    public UITextBox ConfirmPasswordField { get; }
    public UITextBox CurrentPasswordField { get; }
    public UITextBox NameField { get; }
    public UITextBox NewPasswordField { get; }
    public UIButton OkButton { get; }

    public PasswordChangeControl(GraphicsDevice device)
    {
        Name = "PasswordChange";
        Visible = false;

        var prefabSet = DataContext.UserControls.Get("_npw");

        if (prefabSet is null)
            throw new InvalidOperationException("Failed to load _npw control prefab set");

        // Anchor — panel dimensions and background (buttons are baked into the background image)
        var anchor = prefabSet[0];
        var anchorRect = anchor.Control.Rect!.Value;

        Width = (int)anchorRect.Width;
        Height = (int)anchorRect.Height;
        X = (640 - Width) / 2;
        Y = (480 - Height) / 2;

        if (anchor.Images.Count > 0)
            Background = TextureConverter.ToTexture2D(device, anchor.Images[0]);

        // Name (username) field
        var namePrefab = prefabSet["Name"];
        var nameRect = namePrefab.Control.Rect!.Value;

        NameField = new UITextBox(device)
        {
            Name = "Name",
            X = (int)nameRect.Left,
            Y = (int)nameRect.Top,
            Width = (int)nameRect.Width,
            Height = (int)nameRect.Height,
            MaxLength = 12,
            IsMasked = false,
            IsFocused = false
        };
        NameField.OnFocused += OnTextBoxFocused;
        AddChild(NameField);

        // Current password field
        var passPrefab = prefabSet["Password"];
        var passRect = passPrefab.Control.Rect!.Value;

        CurrentPasswordField = new UITextBox(device)
        {
            Name = "Password",
            X = (int)passRect.Left,
            Y = (int)passRect.Top,
            Width = (int)passRect.Width,
            Height = (int)passRect.Height,
            MaxLength = 12,
            IsMasked = true,
            IsFocused = false
        };
        CurrentPasswordField.OnFocused += OnTextBoxFocused;
        AddChild(CurrentPasswordField);

        // New password field
        var newPassPrefab = prefabSet["NewPassword"];
        var newPassRect = newPassPrefab.Control.Rect!.Value;

        NewPasswordField = new UITextBox(device)
        {
            Name = "NewPassword",
            X = (int)newPassRect.Left,
            Y = (int)newPassRect.Top,
            Width = (int)newPassRect.Width,
            Height = (int)newPassRect.Height,
            MaxLength = 12,
            IsMasked = true,
            IsFocused = false
        };
        NewPasswordField.OnFocused += OnTextBoxFocused;
        AddChild(NewPasswordField);

        // Confirm password field
        var confirmPrefab = prefabSet["Confirm"];
        var confirmRect = confirmPrefab.Control.Rect!.Value;

        ConfirmPasswordField = new UITextBox(device)
        {
            Name = "Confirm",
            X = (int)confirmRect.Left,
            Y = (int)confirmRect.Top,
            Width = (int)confirmRect.Width,
            Height = (int)confirmRect.Height,
            MaxLength = 12,
            IsMasked = true,
            IsFocused = false
        };
        ConfirmPasswordField.OnFocused += OnTextBoxFocused;
        AddChild(ConfirmPasswordField);

        // OK / Cancel — click regions only, button visuals are baked into the background
        var okPrefab = prefabSet["OK"];
        var okRect = okPrefab.Control.Rect!.Value;

        OkButton = new UIButton
        {
            Name = "OK",
            X = (int)okRect.Left,
            Y = (int)okRect.Top,
            Width = (int)okRect.Width,
            Height = (int)okRect.Height
        };
        OkButton.OnClick += () => OnOk?.Invoke();
        AddChild(OkButton);

        var cancelPrefab = prefabSet["Cancel"];
        var cancelRect = cancelPrefab.Control.Rect!.Value;

        CancelButton = new UIButton
        {
            Name = "Cancel",
            X = (int)cancelRect.Left,
            Y = (int)cancelRect.Top,
            Width = (int)cancelRect.Width,
            Height = (int)cancelRect.Height
        };
        CancelButton.OnClick += () => OnCancel?.Invoke();
        AddChild(CancelButton);
    }

    public void Hide()
    {
        Visible = false;
        NameField.IsFocused = false;
        CurrentPasswordField.IsFocused = false;
        NewPasswordField.IsFocused = false;
        ConfirmPasswordField.IsFocused = false;
        NameField.Text = string.Empty;
        CurrentPasswordField.Text = string.Empty;
        NewPasswordField.Text = string.Empty;
        ConfirmPasswordField.Text = string.Empty;
    }

    public event Action? OnCancel;

    public event Action? OnOk;

    private void OnTextBoxFocused(UITextBox focused)
    {
        if (focused != NameField)
            NameField.IsFocused = false;

        if (focused != CurrentPasswordField)
            CurrentPasswordField.IsFocused = false;

        if (focused != NewPasswordField)
            NewPasswordField.IsFocused = false;

        if (focused != ConfirmPasswordField)
            ConfirmPasswordField.IsFocused = false;
    }

    public void Show()
    {
        NameField.Text = string.Empty;
        CurrentPasswordField.Text = string.Empty;
        NewPasswordField.Text = string.Empty;
        ConfirmPasswordField.Text = string.Empty;
        NameField.IsFocused = true;
        CurrentPasswordField.IsFocused = false;
        NewPasswordField.IsFocused = false;
        ConfirmPasswordField.IsFocused = false;
        Visible = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        // Tab cycles focus through all 4 fields
        if (input.WasKeyPressed(Keys.Tab))
        {
            if (NameField.IsFocused)
            {
                NameField.IsFocused = false;
                CurrentPasswordField.IsFocused = true;
            } else if (CurrentPasswordField.IsFocused)
            {
                CurrentPasswordField.IsFocused = false;
                NewPasswordField.IsFocused = true;
            } else if (NewPasswordField.IsFocused)
            {
                NewPasswordField.IsFocused = false;
                ConfirmPasswordField.IsFocused = true;
            } else
            {
                ConfirmPasswordField.IsFocused = false;
                NameField.IsFocused = true;
            }
        }

        if (input.WasKeyPressed(Keys.Enter))
            OkButton.PerformClick();

        if (input.WasKeyPressed(Keys.Escape))
            CancelButton.PerformClick();

        base.Update(gameTime, input);
    }
}
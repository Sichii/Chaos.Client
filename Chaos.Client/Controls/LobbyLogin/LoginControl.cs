#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.LobbyLogin;

public class LoginControl : UIPanel
{
    public UIButton CancelButton { get; }
    public UIButton OkButton { get; }
    public UITextBox PasswordField { get; }
    public UITextBox UsernameField { get; }

    public LoginControl(GraphicsDevice device)
    {
        Name = "LoginDialog";
        Visible = false;

        var prefabSet = DataContext.UserControls.Get("_nlogin");

        if (prefabSet is null)
            throw new InvalidOperationException("Failed to load _nlogin control prefab set");

        // Anchor control provides dialog dimensions
        var anchor = prefabSet[0];
        var anchorRect = anchor.Control.Rect!.Value;
        var dialogWidth = (int)anchorRect.Width;
        var dialogHeight = (int)anchorRect.Height;

        Width = dialogWidth;
        Height = dialogHeight;

        // Center on screen
        X = (640 - dialogWidth) / 2;
        Y = (480 - dialogHeight) / 2;

        // Dialog background
        if (anchor.Images.Count > 0)
            Background = TextureConverter.ToTexture2D(device, anchor.Images[0]);

        // Username text field
        var namePrefab = prefabSet["Name"];
        var nameRect = namePrefab.Control.Rect!.Value;

        UsernameField = new UITextBox(device)
        {
            Name = "Username",
            X = (int)nameRect.Left,
            Y = (int)nameRect.Top,
            Width = (int)nameRect.Width,
            Height = (int)nameRect.Height,
            MaxLength = 12,
            IsMasked = false,
            IsFocused = false
        };
        UsernameField.OnFocused += OnTextBoxFocused;
        AddChild(UsernameField);

        // Password text field
        var passPrefab = prefabSet["Password"];
        var passRect = passPrefab.Control.Rect!.Value;

        PasswordField = new UITextBox(device)
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
        PasswordField.OnFocused += OnTextBoxFocused;
        AddChild(PasswordField);

        // OK button
        var okPrefab = prefabSet["OK"];
        var okRect = okPrefab.Control.Rect!.Value;

        OkButton = new UIButton
        {
            Name = "OK",
            X = (int)okRect.Left,
            Y = (int)okRect.Top,
            Width = (int)okRect.Width,
            Height = (int)okRect.Height,
            NormalTexture = okPrefab.Images.Count > 0 ? TextureConverter.ToTexture2D(device, okPrefab.Images[0]) : null,
            PressedTexture = okPrefab.Images.Count > 1 ? TextureConverter.ToTexture2D(device, okPrefab.Images[1]) : null
        };
        AddChild(OkButton);

        // Cancel button
        var cancelPrefab = prefabSet["Cancel"];
        var cancelRect = cancelPrefab.Control.Rect!.Value;

        CancelButton = new UIButton
        {
            Name = "Cancel",
            X = (int)cancelRect.Left,
            Y = (int)cancelRect.Top,
            Width = (int)cancelRect.Width,
            Height = (int)cancelRect.Height,
            NormalTexture = cancelPrefab.Images.Count > 0 ? TextureConverter.ToTexture2D(device, cancelPrefab.Images[0]) : null,
            PressedTexture = cancelPrefab.Images.Count > 1 ? TextureConverter.ToTexture2D(device, cancelPrefab.Images[1]) : null
        };
        AddChild(CancelButton);
    }

    public void Hide()
    {
        Visible = false;
        UsernameField.IsFocused = false;
        PasswordField.IsFocused = false;
        UsernameField.Text = string.Empty;
        PasswordField.Text = string.Empty;
    }

    private void OnTextBoxFocused(UITextBox focused)
    {
        if (focused != UsernameField)
            UsernameField.IsFocused = false;

        if (focused != PasswordField)
            PasswordField.IsFocused = false;
    }

    public void Show()
    {
        Visible = true;
        UsernameField.Text = string.Empty;
        PasswordField.Text = string.Empty;
        UsernameField.IsFocused = true;
        PasswordField.IsFocused = false;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        // Tab switches focus between username and password fields
        if (input.WasKeyPressed(Keys.Tab))
        {
            if (UsernameField.IsFocused)
            {
                UsernameField.IsFocused = false;
                PasswordField.IsFocused = true;
            } else
            {
                PasswordField.IsFocused = false;
                UsernameField.IsFocused = true;
            }
        }

        // Enter in username → move to password, Enter in password → login
        if (input.WasKeyPressed(Keys.Enter))
        {
            if (UsernameField.IsFocused)
            {
                UsernameField.IsFocused = false;
                PasswordField.IsFocused = true;
            } else
                OkButton.PerformClick();
        }

        base.Update(gameTime, input);
    }
}
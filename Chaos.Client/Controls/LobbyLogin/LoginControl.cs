#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.LobbyLogin;

public sealed class LoginControl : PrefabPanel
{
    public UIButton? CancelButton { get; }
    public UIButton? OkButton { get; }
    public UITextBox? PasswordField { get; }
    public UITextBox? UsernameField { get; }

    public LoginControl()
        : base("_nlogin")
    {
        Name = "LoginDialog";
        Visible = false;

        // Buttons
        OkButton = CreateButton("OK");
        CancelButton = CreateButton("Cancel");

        // Text fields — type 7 with 0 images, must be created manually
        UsernameField = CreateTextBox("Name");
        PasswordField = CreateTextBox("Password");

        PasswordField?.IsMasked = true;

        if (UsernameField is not null)
            UsernameField.OnFocused += OnTextBoxFocused;

        if (PasswordField is not null)
            PasswordField.OnFocused += OnTextBoxFocused;
    }

    public override void Hide()
    {
        Visible = false;

        if (UsernameField is not null)
        {
            UsernameField.IsFocused = false;
            UsernameField.Text = string.Empty;
        }

        if (PasswordField is not null)
        {
            PasswordField.IsFocused = false;
            PasswordField.Text = string.Empty;
        }
    }

    private void OnTextBoxFocused(UITextBox focused)
    {
        if (UsernameField is not null && (focused != UsernameField))
            UsernameField.IsFocused = false;

        if (PasswordField is not null && (focused != PasswordField))
            PasswordField.IsFocused = false;
    }

    public override void Show()
    {
        if (UsernameField is not null)
        {
            UsernameField.Text = string.Empty;
            UsernameField.IsFocused = true;
        }

        if (PasswordField is not null)
        {
            PasswordField.Text = string.Empty;
            PasswordField.IsFocused = false;
        }

        Visible = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        // Tab switches focus between username and password fields
        if (input.WasKeyPressed(Keys.Tab))
        {
            if (UsernameField?.IsFocused == true)
            {
                UsernameField.IsFocused = false;

                PasswordField?.IsFocused = true;
            } else
            {
                PasswordField?.IsFocused = false;

                UsernameField?.IsFocused = true;
            }
        }

        // Enter in username → move to password, Enter in password → login
        if (input.WasKeyPressed(Keys.Enter))
        {
            if (UsernameField?.IsFocused == true)
            {
                UsernameField.IsFocused = false;

                PasswordField?.IsFocused = true;
            } else
                OkButton?.PerformClick();
        }

        base.Update(gameTime, input);
    }
}
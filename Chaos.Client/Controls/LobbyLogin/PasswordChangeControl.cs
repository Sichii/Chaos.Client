#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.LobbyLogin;

public sealed class PasswordChangeControl : PrefabPanel
{
    public UIButton? CancelButton { get; }
    public UITextBox? ConfirmPasswordField { get; }
    public UITextBox? CurrentPasswordField { get; }
    public UITextBox? NameField { get; }
    public UITextBox? NewPasswordField { get; }
    public UIButton? OkButton { get; }

    public PasswordChangeControl()
        : base("_npw")
    {
        Name = "PasswordChange";
        Visible = false;

        // Buttons
        OkButton = CreateButton("OK");
        CancelButton = CreateButton("Cancel");

        if (OkButton is not null)
            OkButton.OnClick += () => OnOk?.Invoke();

        if (CancelButton is not null)
            CancelButton.OnClick += () => OnCancel?.Invoke();

        // Text fields — type 7 with 0 images, manually created
        NameField = CreateTextBox("Name");
        CurrentPasswordField = CreateTextBox("Password");
        NewPasswordField = CreateTextBox("NewPassword");
        ConfirmPasswordField = CreateTextBox("Confirm");

        NameField?.ForegroundColor = LegendColors.White;
        CurrentPasswordField?.ForegroundColor = LegendColors.White;
        NewPasswordField?.ForegroundColor = LegendColors.White;
        ConfirmPasswordField?.ForegroundColor = LegendColors.White;
        CurrentPasswordField?.IsMasked = true;
        NewPasswordField?.IsMasked = true;
        ConfirmPasswordField?.IsMasked = true;

        // Focus management
        if (NameField is not null)
            NameField.OnFocused += OnTextBoxFocused;

        if (CurrentPasswordField is not null)
            CurrentPasswordField.OnFocused += OnTextBoxFocused;

        if (NewPasswordField is not null)
            NewPasswordField.OnFocused += OnTextBoxFocused;

        if (ConfirmPasswordField is not null)
            ConfirmPasswordField.OnFocused += OnTextBoxFocused;
    }

    private void ClearFields()
    {
        UITextBox?[] fields =
        [
            NameField,
            CurrentPasswordField,
            NewPasswordField,
            ConfirmPasswordField
        ];

        foreach (var field in fields)
        {
            if (field is null)
                continue;

            field.IsFocused = false;
            field.Text = string.Empty;
        }
    }

    public override void Hide()
    {
        Visible = false;
        ClearFields();
    }

    public event Action? OnCancel;

    public event Action? OnOk;

    private void OnTextBoxFocused(UITextBox focused)
    {
        UITextBox?[] fields =
        [
            NameField,
            CurrentPasswordField,
            NewPasswordField,
            ConfirmPasswordField
        ];

        foreach (var field in fields)
            if (field is not null && (field != focused))
                field.IsFocused = false;
    }

    public override void Show()
    {
        ClearFields();

        NameField?.IsFocused = true;

        Visible = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        // Tab cycles focus through all 4 fields
        if (input.WasKeyPressed(Keys.Tab))
        {
            if (NameField?.IsFocused == true)
            {
                NameField.IsFocused = false;
                CurrentPasswordField?.IsFocused = true;
            } else if (CurrentPasswordField?.IsFocused == true)
            {
                CurrentPasswordField.IsFocused = false;
                NewPasswordField?.IsFocused = true;
            } else if (NewPasswordField?.IsFocused == true)
            {
                NewPasswordField.IsFocused = false;
                ConfirmPasswordField?.IsFocused = true;
            } else
            {
                ConfirmPasswordField?.IsFocused = false;
                NameField?.IsFocused = true;
            }
        }

        if (input.WasKeyPressed(Keys.Enter))
            OkButton?.PerformClick();

        if (input.WasKeyPressed(Keys.Escape))
            CancelButton?.PerformClick();

        base.Update(gameTime, input);
    }
}
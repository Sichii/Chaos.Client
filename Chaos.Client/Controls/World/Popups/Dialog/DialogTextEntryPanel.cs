#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Dialog;

/// <summary>
///     Text entry sub-panel using the lnpcd4 prefab. Displays a prompt (Prolog) above a text input field, optional text
///     below (Epilog), and an OK button. Used for DialogType.TextEntry (4) and DialogType.Speak (5).
/// </summary>
public sealed class DialogTextEntryPanel : PrefabPanel
{
    private readonly UILabel? EpilogLabel;
    private readonly UIButton? OkButton;
    private readonly UILabel? PrologLabel;
    private readonly UITextBox? TextInput;

    public DialogTextEntryPanel()
        : base("lnpcd4")
    {
        Name = "DialogTextEntry";
        Visible = false;

        PrologLabel = CreateLabel("Prolog");
        TextInput = CreateTextBox("TextInput", 255);
        EpilogLabel = CreateLabel("Epilog");
        OkButton = CreateButton("Btn1");

        if (OkButton is not null)
            OkButton.OnClick += HandleSubmit;

        if (TextInput is not null)
        {
            TextInput.ForegroundColor = Color.White;

            TextInput.FocusedBackgroundColor = new Color(
                0,
                0,
                0,
                120);
        }
    }

    private void HandleSubmit()
    {
        if (TextInput is null)
            return;

        var text = TextInput.Text.Trim();

        if (text.Length > 0)
            OnTextSubmit?.Invoke(text);
    }

    public override void Hide()
    {
        if (TextInput is not null)
            TextInput.IsFocused = false;

        base.Hide();
    }

    public event Action? OnClose;

    public event Action<string>? OnTextSubmit;

    public void ShowTextEntry(string prolog, byte maxLength, string epilog)
    {
        if (PrologLabel is not null)
            PrologLabel.Text = prolog;

        if (EpilogLabel is not null)
            EpilogLabel.Text = epilog;

        if (TextInput is not null)
        {
            TextInput.Text = string.Empty;
            TextInput.MaxLength = maxLength > 0 ? maxLength : 255;
            TextInput.CursorPosition = 0;
            TextInput.IsFocused = true;
        }

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

        base.Update(gameTime, input);
    }
}
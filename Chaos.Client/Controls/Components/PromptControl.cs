#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.Components;

/// <summary>
///     A text input prompt that occupies the chat input area. White background with black text. Displays a prefix label
///     and accepts typed input. Enter confirms, Escape does NOT close. Used for item drop counts, spell targeting prompts,
///     etc.
/// </summary>
public sealed class PromptControl : UITextBox
{
    public PromptControl()
    {
        Visible = false;
        BackgroundColor = Color.White;
        FocusedBackgroundColor = Color.White;
        ForegroundColor = Color.Black;
        PaddingLeft = 1;
        PaddingRight = 1;
        PaddingTop = 1;
        PaddingBottom = 1;
        MaxLength = 12;
    }

    public void HidePrompt()
    {
        Visible = false;
        IsFocused = false;
        Prefix = string.Empty;
        Text = string.Empty;
    }

    /// <summary>
    ///     Fired when the user presses Enter. Parameter is the typed text.
    /// </summary>
    public event Action<string>? OnConfirm;

    public void ShowPrompt(string prefix)
    {
        OnConfirm = null;
        Prefix = prefix;
        Text = string.Empty;
        Visible = true;
        IsFocused = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (input.WasKeyPressed(Keys.Enter))
        {
            var text = Text;
            HidePrompt();
            OnConfirm?.Invoke(text);

            return;
        }

        // Escape does NOT close prompts — intentionally no handler

        base.Update(gameTime, input);
    }
}
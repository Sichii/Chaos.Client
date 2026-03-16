#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Gold exchange miniscreen using _nmoney prefab. Allows entering a gold amount for trading/dropping.
/// </summary>
public class GoldExchangeControl : PrefabPanel
{
    public UITextBox? AmountTextBox { get; }
    public UIButton? CancelButton { get; }
    public UIButton? OkButton { get; }

    public GoldExchangeControl(GraphicsDevice device)
        : base(device, "_nmoney")
    {
        Name = "GoldExchange";
        Visible = false;

        var elements = AutoPopulate();

        OkButton = elements.GetValueOrDefault("OK") as UIButton;
        CancelButton = elements.GetValueOrDefault("Cancel") as UIButton;
        AmountTextBox = elements.GetValueOrDefault("Amount") as UITextBox;

        if (AmountTextBox is not null)
            AmountTextBox.MaxLength = 10;

        if (OkButton is not null)
            OkButton.OnClick += () =>
            {
                OnOk?.Invoke(AmountTextBox?.Text ?? string.Empty);
            };

        if (CancelButton is not null)
            CancelButton.OnClick += () =>
            {
                Hide();
                OnCancel?.Invoke();
            };
    }

    public event Action? OnCancel;

    public event Action<string>? OnOk;

    public new void Show()
    {
        if (AmountTextBox is not null)
        {
            AmountTextBox.Text = string.Empty;
            AmountTextBox.IsFocused = true;
        }

        Visible = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (input.WasKeyPressed(Keys.Escape))
        {
            Hide();
            OnCancel?.Invoke();

            return;
        }

        if (input.WasKeyPressed(Keys.Enter))
        {
            OnOk?.Invoke(AmountTextBox?.Text ?? string.Empty);

            return;
        }

        base.Update(gameTime, input);
    }
}
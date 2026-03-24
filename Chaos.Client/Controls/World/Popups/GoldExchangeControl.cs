#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Gold drop/exchange popup using _nmoney prefab. Stores the pending drop target (entity or tile)
///     so the caller only needs to show it and subscribe to OnConfirm.
/// </summary>
public sealed class GoldExchangeControl : PrefabPanel
{
    /// <summary>
    ///     Entity ID to drop gold on, or null for ground drop.
    /// </summary>
    public uint? TargetEntityId { get; private set; }

    public int TargetTileX { get; private set; }
    public int TargetTileY { get; private set; }
    public UITextBox? AmountTextBox { get; }
    public UIButton? CancelButton { get; }
    public UIButton? OkButton { get; }

    public GoldExchangeControl()
        : base("_nmoney")
    {
        Name = "GoldExchange";
        Visible = false;

        OkButton = CreateButton("OK");
        CancelButton = CreateButton("Cancel");
        AmountTextBox = CreateTextBox("Text", 10);

        // Replace any existing text display with a label showing the prompt
        var titleLabel = CreateLabel("Title");

        titleLabel?.SetText("Gold amount to drop?", Color.White);

        if (OkButton is not null)
            OkButton.OnClick += Confirm;

        if (CancelButton is not null)
            CancelButton.OnClick += Cancel;
    }

    private void Cancel() => Hide();

    private void Confirm()
    {
        var text = AmountTextBox?.Text ?? string.Empty;

        Hide();

        if (!uint.TryParse(text, out var amount) || (amount == 0))
            return;

        OnConfirm?.Invoke(amount);
    }

    /// <summary>
    ///     Fired when the user confirms a gold amount. Parameter is the parsed amount.
    /// </summary>
    public event Action<uint>? OnConfirm;

    public override void Show()
    {
        if (AmountTextBox is not null)
        {
            AmountTextBox.Text = string.Empty;
            AmountTextBox.IsFocused = true;
        }

        Visible = true;
    }

    public void ShowForTarget(uint? entityId, int tileX, int tileY)
    {
        TargetEntityId = entityId;
        TargetTileX = tileX;
        TargetTileY = tileY;
        Show();
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (input.WasKeyPressed(Keys.Escape))
        {
            Hide();

            return;
        }

        if (input.WasKeyPressed(Keys.Enter))
        {
            Confirm();

            return;
        }

        base.Update(gameTime, input);
    }
}
#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Exchange/trade panel using _nexch prefab. Two-sided layout: left side (player items/gold), right side (other player
///     items/gold). Up to 4 items per side. Server drives all state via DisplayExchangeArgs.
/// </summary>
public sealed class ExchangeControl : PrefabPanel
{
    private const int MAX_ITEMS_PER_SIDE = 4;
    private const int ITEM_ROW_HEIGHT = 36;
    private readonly Rectangle MyExchangeRect;
    private readonly UILabel? MyIdLabel;

    private readonly ExchangeItemControl[] MyItems = new ExchangeItemControl[MAX_ITEMS_PER_SIDE];
    private readonly UILabel? MyMoneyLabel;

    private readonly UIImage? YourAckImage;
    private readonly Rectangle YourExchangeRect;
    private readonly UILabel? YourIdLabel;
    private readonly ExchangeItemControl[] YourItems = new ExchangeItemControl[MAX_ITEMS_PER_SIDE];
    private readonly UILabel? YourMoneyLabel;

    public uint OtherUserId { get; private set; }
    public UIButton? CancelButton { get; }
    public UIButton? OkButton { get; }

    public ExchangeControl(GraphicsDevice device)
        : base(device, "_nexch")
    {
        Name = "Exchange";
        Visible = false;

        var elements = AutoPopulate();

        OkButton = elements.GetValueOrDefault("OK") as UIButton;
        CancelButton = elements.GetValueOrDefault("Cancel") as UIButton;

        if (OkButton is not null)
            OkButton.OnClick += () => OnOk?.Invoke();

        if (CancelButton is not null)
            CancelButton.OnClick += () => OnCancel?.Invoke();

        MyIdLabel = CreateLabel("MyID");
        YourIdLabel = CreateLabel("YourID");
        MyMoneyLabel = CreateLabel("MyMoney");
        YourMoneyLabel = CreateLabel("YourMoney");

        MyExchangeRect = GetRect("MyExchange");
        YourExchangeRect = GetRect("YourExchange");

        YourAckImage = elements.GetValueOrDefault("YourACK") as UIImage;
        YourAckImage?.Visible = false;

        // Create item controls for both sides
        CreateItemControls(device, MyItems, MyExchangeRect);
        CreateItemControls(device, YourItems, YourExchangeRect);
    }

    public void AddItem(
        bool rightSide,
        byte exchangeIndex,
        ushort itemSprite,
        string? itemName)
    {
        // Server sends 1-based slot indices
        var index = exchangeIndex - 1;

        if ((index < 0) || (index >= MAX_ITEMS_PER_SIDE))
            return;

        var items = rightSide ? YourItems : MyItems;

        items[index]
            .SetItem(itemSprite, itemName ?? string.Empty);
    }

    private void ClearAllItems()
    {
        foreach (var item in MyItems)
            item.ClearItem();

        foreach (var item in YourItems)
            item.ClearItem();
    }

    public void CloseExchange()
    {
        ClearAllItems();
        Hide();
    }

    private void CreateItemControls(GraphicsDevice device, ExchangeItemControl[] items, Rectangle rect)
    {
        for (var i = 0; i < MAX_ITEMS_PER_SIDE; i++)
        {
            var control = new ExchangeItemControl(device)
            {
                Name = $"ExchangeItem{i}",
                X = rect.X,
                Y = rect.Y + i * ITEM_ROW_HEIGHT,
                Width = rect.Width
            };

            items[i] = control;
            AddChild(control);
        }
    }

    /// <summary>
    ///     Returns true if the given screen coordinates are within the MyMoney label area.
    /// </summary>
    public bool IsMyMoneyClicked(int mouseX, int mouseY) => MyMoneyLabel?.ContainsPoint(mouseX, mouseY) ?? false;

    public event Action? OnCancel;
    public event Action? OnOk;

    public void SetGold(bool rightSide, int goldAmount)
    {
        var label = rightSide ? YourMoneyLabel : MyMoneyLabel;
        label?.SetText(goldAmount.ToString("N0"));
    }

    public void ShowOtherAccepted() => YourAckImage?.Visible = true;

    public void StartExchange(uint otherUserId, string otherUserName, string myName)
    {
        OtherUserId = otherUserId;
        ClearAllItems();

        MyIdLabel?.SetText(myName);
        YourIdLabel?.SetText(otherUserName);
        MyMoneyLabel?.SetText("0");
        YourMoneyLabel?.SetText("0");

        YourAckImage?.Visible = false;

        Show();
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (input.WasKeyPressed(Keys.Escape))
        {
            OnCancel?.Invoke();

            return;
        }

        base.Update(gameTime, input);
    }
}
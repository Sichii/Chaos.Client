#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Exchange;

/// <summary>
///     Exchange/trade panel using _nexch prefab. Thin view that subscribes to
///     <see cref="Exchange" /> state events. Two-sided layout with up to 4 items per side.
/// </summary>
public sealed class ExchangeControl : PrefabPanel
{
    private const int MAX_ITEMS_PER_SIDE = 4;
    private const int ITEM_ROW_HEIGHT = 36;
    private readonly Rectangle MyExchangeRect;
    private readonly UILabel? MyIdLabel;

    private readonly ExchangeItemControl[] MyItems = new ExchangeItemControl[MAX_ITEMS_PER_SIDE];
    private readonly UILabel? MyMoneyLabel;
    private readonly Rectangle ViewportBounds;

    private readonly UIImage? YourAckImage;
    private readonly Rectangle YourExchangeRect;
    private readonly UILabel? YourIdLabel;
    private readonly ExchangeItemControl[] YourItems = new ExchangeItemControl[MAX_ITEMS_PER_SIDE];
    private readonly UILabel? YourMoneyLabel;

    public UIButton? CancelButton { get; }
    public UIButton? OkButton { get; }

    public uint OtherUserId => WorldState.Exchange.OtherUserId;

    /// <summary>
    ///     The player's own name, used for the left side label. Set from WorldScreen after DisplayAisling.
    /// </summary>
    private static string PlayerName => WorldState.PlayerName;

    public ExchangeControl(Rectangle viewportBounds)
        : base("_nexch")
    {
        Name = "Exchange";
        Visible = false;
        UsesControlStack = true;
        ViewportBounds = viewportBounds;

        OkButton = CreateButton("OK");
        CancelButton = CreateButton("Cancel");

        if (OkButton is not null)
            OkButton.Clicked += () =>
            {
                OkButton.Enabled = false;
                OnOk?.Invoke();
            };

        if (CancelButton is not null)
            CancelButton.Clicked += () => OnCancel?.Invoke();

        MyIdLabel = CreateLabel("MyID");
        YourIdLabel = CreateLabel("YourID");
        MyMoneyLabel = CreateLabel("MyMoney");
        YourMoneyLabel = CreateLabel("YourMoney");

        MyExchangeRect = GetRect("MyExchange");
        YourExchangeRect = GetRect("YourExchange");

        YourAckImage = CreateImage("YourACK");

        YourAckImage?.Visible = false;

        //create item controls for both sides
        CreateItemControls(MyItems, MyExchangeRect);
        CreateItemControls(YourItems, YourExchangeRect);

        //subscribe to state events
        WorldState.Exchange.Started += OnExchangeStarted;
        WorldState.Exchange.ItemAdded += OnExchangeItemAdded;
        WorldState.Exchange.GoldSet += OnExchangeGoldSet;
        WorldState.Exchange.OtherAccepted += OnExchangeOtherAccepted;
        WorldState.Exchange.Closed += OnExchangeClosed;
    }

    private void ClearAllItems()
    {
        foreach (var item in MyItems)
            item.ClearItem();

        foreach (var item in YourItems)
            item.ClearItem();
    }

    private void CreateItemControls(ExchangeItemControl[] items, Rectangle rect)
    {
        for (var i = 0; i < MAX_ITEMS_PER_SIDE; i++)
        {
            var control = new ExchangeItemControl
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

    public override void Dispose()
    {
        WorldState.Exchange.Started -= OnExchangeStarted;
        WorldState.Exchange.ItemAdded -= OnExchangeItemAdded;
        WorldState.Exchange.GoldSet -= OnExchangeGoldSet;
        WorldState.Exchange.OtherAccepted -= OnExchangeOtherAccepted;
        WorldState.Exchange.Closed -= OnExchangeClosed;

        base.Dispose();
    }

    /// <summary>
    ///     Returns true if the given screen coordinates are within the MyMoney label area.
    /// </summary>
    public bool IsMyMoneyClicked(int mouseX, int mouseY) => MyMoneyLabel?.ContainsPoint(mouseX, mouseY) ?? false;

    public event Action? OnCancel;

    private void OnExchangeClosed()
    {
        ClearAllItems();
        Hide();
    }

    private void OnExchangeGoldSet(bool rightSide)
    {
        var amount = rightSide ? WorldState.Exchange.OtherGold : WorldState.Exchange.MyGold;
        var label = rightSide ? YourMoneyLabel : MyMoneyLabel;
        label?.Text = amount.ToString("N0");
    }

    private void OnExchangeItemAdded(bool rightSide, byte index)
    {
        if (index >= MAX_ITEMS_PER_SIDE)
            return;

        var data = WorldState.Exchange.GetItem(rightSide, index);

        if (data.HasValue)
        {
            var items = rightSide ? YourItems : MyItems;

            items[index]
                .SetItem(data.Value.Sprite, data.Value.Name ?? string.Empty);
        }
    }

    private void OnExchangeOtherAccepted() => YourAckImage?.Visible = true;

    private void OnExchangeStarted()
    {
        ClearAllItems();

        MyIdLabel?.Text = PlayerName;
        YourIdLabel?.Text = WorldState.Exchange.OtherUserName;
        MyMoneyLabel?.Text = "0";
        YourMoneyLabel?.Text = "0";
        YourAckImage?.Visible = false;

        OkButton?.Enabled = true;

        //center vertically in viewport
        Y = ViewportBounds.Y + (ViewportBounds.Height - Height) / 2;

        Show();
    }

    public event Action? OnOk;

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            OnCancel?.Invoke();
            e.Handled = true;
        }
    }
}
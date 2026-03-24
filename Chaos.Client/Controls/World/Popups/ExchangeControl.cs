#region
using Chaos.Client.Controls.Components;
using Chaos.Client.ViewModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Exchange/trade panel using _nexch prefab. Thin view that subscribes to
///     <see cref="Exchange" /> state events. Two-sided layout with up to 4 items per side.
/// </summary>
public sealed class ExchangeControl : PrefabPanel
{
    private const int MAX_ITEMS_PER_SIDE = 4;
    private const int ITEM_ROW_HEIGHT = 36;
    private readonly Exchange ExchangeState;
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

    /// <summary>
    ///     The player's own name, used for the left side label. Set from WorldScreen after DisplayAisling.
    /// </summary>
    public string PlayerName { get; set; } = string.Empty;

    public UIButton? CancelButton { get; }
    public UIButton? OkButton { get; }

    public uint OtherUserId => ExchangeState.OtherUserId;

    public ExchangeControl(Exchange exchange, Rectangle viewportBounds)
        : base("_nexch")
    {
        Name = "Exchange";
        Visible = false;
        ExchangeState = exchange;
        ViewportBounds = viewportBounds;

        OkButton = CreateButton("OK");
        CancelButton = CreateButton("Cancel");

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

        YourAckImage = CreateImage("YourACK");

        if (YourAckImage is not null)
            YourAckImage.Visible = false;

        // Create item controls for both sides
        CreateItemControls(MyItems, MyExchangeRect);
        CreateItemControls(YourItems, YourExchangeRect);

        // Subscribe to state events
        ExchangeState.Started += OnExchangeStarted;
        ExchangeState.ItemAdded += OnExchangeItemAdded;
        ExchangeState.GoldSet += OnExchangeGoldSet;
        ExchangeState.OtherAccepted += OnExchangeOtherAccepted;
        ExchangeState.Closed += OnExchangeClosed;
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
        ExchangeState.Started -= OnExchangeStarted;
        ExchangeState.ItemAdded -= OnExchangeItemAdded;
        ExchangeState.GoldSet -= OnExchangeGoldSet;
        ExchangeState.OtherAccepted -= OnExchangeOtherAccepted;
        ExchangeState.Closed -= OnExchangeClosed;

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
        var amount = rightSide ? ExchangeState.OtherGold : ExchangeState.MyGold;
        var label = rightSide ? YourMoneyLabel : MyMoneyLabel;
        label?.SetText(amount.ToString("N0"));
    }

    private void OnExchangeItemAdded(bool rightSide, byte index)
    {
        if (index >= MAX_ITEMS_PER_SIDE)
            return;

        var data = ExchangeState.GetItem(rightSide, index);

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

        MyIdLabel?.SetText(PlayerName);
        YourIdLabel?.SetText(ExchangeState.OtherUserName);
        MyMoneyLabel?.SetText("0");
        YourMoneyLabel?.SetText("0");
        YourAckImage?.Visible = false;

        // Center vertically in viewport
        Y = ViewportBounds.Y + (ViewportBounds.Height - Height) / 2;

        Show();
    }

    public event Action? OnOk;

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
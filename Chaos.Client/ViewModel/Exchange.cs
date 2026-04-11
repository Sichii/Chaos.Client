using Chaos.DarkAges.Definitions;

namespace Chaos.Client.ViewModel;

/// <summary>
///     Authoritative exchange (trade) state. Only one exchange can be active at a time. Fires events on state transitions
///     for UI reconciliation.
/// </summary>
public sealed class Exchange
{
    private const int MAX_ITEMS = 4;

    private readonly ExchangeItemData?[] MyItems = new ExchangeItemData?[MAX_ITEMS];
    private readonly ExchangeItemData?[] OtherItems = new ExchangeItemData?[MAX_ITEMS];

    /// <summary>
    ///     Whether an exchange is currently active.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    ///     Whether the other player has accepted.
    /// </summary>
    public bool IsOtherAccepted { get; private set; }

    /// <summary>
    ///     My gold amount in the exchange.
    /// </summary>
    public int MyGold { get; private set; }

    /// <summary>
    ///     The other player's gold amount in the exchange.
    /// </summary>
    public int OtherGold { get; private set; }

    /// <summary>
    ///     The other player's entity ID.
    /// </summary>
    public uint OtherUserId { get; private set; }

    /// <summary>
    ///     The other player's name.
    /// </summary>
    public string OtherUserName { get; private set; } = string.Empty;

    public void AddItem(
        bool rightSide,
        byte exchangeIndex,
        ushort sprite,
        DisplayColor color,
        string? name)
    {
        //server sends 1-based indices
        var index = exchangeIndex - 1;

        if (index is < 0 or >= MAX_ITEMS)
            return;

        var item = new ExchangeItemData(sprite, color, name);

        if (rightSide)
            OtherItems[index] = item;
        else
            MyItems[index] = item;

        ItemAdded?.Invoke(rightSide, (byte)index);
    }

    /// <summary>
    ///     Fired when the server requests a stackable item count from a specific inventory slot.
    /// </summary>
    public event ExchangeAmountRequestedHandler? AmountRequested;

    public void Close()
    {
        IsActive = false;
        OtherUserId = 0;
        OtherUserName = string.Empty;
        MyGold = 0;
        OtherGold = 0;
        IsOtherAccepted = false;
        Array.Clear(MyItems);
        Array.Clear(OtherItems);
        Closed?.Invoke();
    }

    /// <summary>
    ///     Fired when the exchange is closed (cancelled or completed).
    /// </summary>
    public event ExchangeClosedHandler? Closed;

    /// <summary>
    ///     Returns an item on the specified side and index, or null.
    /// </summary>
    public ExchangeItemData? GetItem(bool rightSide, byte index)
    {
        if (index >= MAX_ITEMS)
            return null;

        return rightSide ? OtherItems[index] : MyItems[index];
    }

    /// <summary>
    ///     Fired when gold is set on either side. Argument is true for the other player's side.
    /// </summary>
    public event ExchangeGoldSetHandler? GoldSet;

    /// <summary>
    ///     Fired when an item is added to either side. Argument is true for the other player's side.
    /// </summary>
    public event ExchangeItemAddedHandler? ItemAdded;

    /// <summary>
    ///     Fired when the other player accepts the exchange.
    /// </summary>
    public event ExchangeOtherAcceptedHandler? OtherAccepted;

    public void RequestAmount(byte fromSlot) => AmountRequested?.Invoke(fromSlot);

    public void SetGold(bool rightSide, int amount)
    {
        if (rightSide)
            OtherGold = amount;
        else
            MyGold = amount;

        GoldSet?.Invoke(rightSide);
    }

    public void SetOtherAccepted()
    {
        IsOtherAccepted = true;
        OtherAccepted?.Invoke();
    }

    public void Start(uint otherUserId, string otherUserName)
    {
        IsActive = true;
        OtherUserId = otherUserId;
        OtherUserName = otherUserName;
        MyGold = 0;
        OtherGold = 0;
        IsOtherAccepted = false;
        Array.Clear(MyItems);
        Array.Clear(OtherItems);
        Started?.Invoke();
    }

    /// <summary>
    ///     Fired when a new exchange is started.
    /// </summary>
    public event ExchangeStartedHandler? Started;

    public readonly record struct ExchangeItemData(ushort Sprite, DisplayColor Color, string? Name);
}
#region
using Chaos.Client.ViewModel;
using Chaos.DarkAges.Definitions;
#endregion

namespace Chaos.Client.Definitions;

#region Shared Delegates
/// <summary>
///     A slot at the given 1-based index changed (added, updated, or cleared).
/// </summary>
public delegate void SlotChangedHandler(byte slot);

/// <summary>
///     Fired when all entries in the owning collection are cleared (e.g. on logout or full server re-entry).
/// </summary>
public delegate void ClearedHandler();

/// <summary>
///     Fired when the owning state object is updated by the server.
/// </summary>
public delegate void ChangedHandler();
#endregion

#region Equipment Delegates
/// <summary>
///     An equipment slot was equipped or updated.
/// </summary>
public delegate void EquipmentSlotChangedHandler(EquipmentSlot equipmentSlot);

/// <summary>
///     An equipment slot was unequipped.
/// </summary>
public delegate void EquipmentSlotClearedHandler(EquipmentSlot equipmentSlot);
#endregion

#region Inventory Delegates
/// <summary>
///     The player's gold amount changed.
/// </summary>
public delegate void GoldChangedHandler();
#endregion

#region Exchange Delegates
/// <summary>
///     A new exchange was started.
/// </summary>
public delegate void ExchangeStartedHandler();

/// <summary>
///     An item was added to one side of the exchange.
/// </summary>
public delegate void ExchangeItemAddedHandler(bool isOtherSide, byte index);

/// <summary>
///     Gold was set on one side of the exchange.
/// </summary>
public delegate void ExchangeGoldSetHandler(bool isOtherSide);

/// <summary>
///     The other player accepted the exchange.
/// </summary>
public delegate void ExchangeOtherAcceptedHandler();

/// <summary>
///     The exchange was closed (cancelled or completed).
/// </summary>
public delegate void ExchangeClosedHandler();

/// <summary>
///     The server requests a stackable item count from the given inventory slot.
/// </summary>
public delegate void ExchangeAmountRequestedHandler(byte fromSlot);
#endregion

#region Chat Delegates
/// <summary>
///     A chat message was added (public, whisper, group, guild).
/// </summary>
public delegate void ChatMessageAddedHandler(Chat.ChatMessage message);

/// <summary>
///     An orange bar message was added (system messages).
/// </summary>
public delegate void OrangeBarMessageAddedHandler(string text);
#endregion

#region NPC Interaction Delegates
/// <summary>
///     The current NPC dialog changed (shown, updated, or closed).
/// </summary>
public delegate void DialogChangedHandler();

/// <summary>
///     A new NPC menu was displayed.
/// </summary>
public delegate void MenuChangedHandler();
#endregion

#region Board Delegates
/// <summary>
///     The board post list was shown or updated (new page appended).
/// </summary>
public delegate void PostListChangedHandler();

/// <summary>
///     A single post was displayed for reading.
/// </summary>
public delegate void PostViewedHandler();

/// <summary>
///     A board list was received (multiple boards available).
/// </summary>
public delegate void BoardListReceivedHandler();

/// <summary>
///     A server response message was received (submit/delete/highlight result).
/// </summary>
public delegate void BoardResponseReceivedHandler(string message, bool success);
#endregion

#region Group Delegates
/// <summary>
///     A group-related interaction was received from the server.
/// </summary>
public delegate void GroupInviteReceivedHandler();
#endregion
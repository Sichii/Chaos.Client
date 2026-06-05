#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
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
    private const int MAX_VISIBLE_ITEMS = 4;
    private const int ITEM_ROW_HEIGHT = 32;

    private readonly ScrollBarControl MyHorizontalScroll;
    private readonly ScrollBarControl MyVerticalScroll;
    private readonly UIPanel MyItemsContainer;
    private readonly Rectangle MyExchangeRect;
    private readonly UILabel? MyIdLabel;

    private readonly ExchangeItemControl[] MyItems = new ExchangeItemControl[MAX_VISIBLE_ITEMS];
    private readonly NumericTextBox? MyMoneyTextBox;
    private readonly Rectangle ViewportBounds;

    private readonly UIImage? YourAckImage;
    private readonly ScrollBarControl YourHorizontalScroll;
    private readonly ScrollBarControl YourVerticalScroll;
    private readonly UIPanel YourItemsContainer;
    private readonly Rectangle YourExchangeRect;
    private readonly UILabel? YourIdLabel;
    private readonly ExchangeItemControl[] YourItems = new ExchangeItemControl[MAX_VISIBLE_ITEMS];
    private readonly UILabel? YourMoneyLabel;

    private int MyHorizontalOffset;
    private int MyVerticalOffset;
    private int YourHorizontalOffset;
    private int YourVerticalOffset;

    //last gold value the client asserted for our side — the value we last SENT (optimistically) or the server last
    //CONFIRMED via OnExchangeGoldSet. used as the dirty-check reference in CommitMoneyInput; reset to 0 per exchange.
    //(distinct from WorldState.Exchange.MyGold, which only tracks the server-confirmed value and lags an in-flight send.)
    private int LastAssertedGold;

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

                //accepting locks our gold in (the server rejects further SetGold once we've accepted)
                LockMoneyInput();

                OnOk?.Invoke();
            };

        if (CancelButton is not null)
            CancelButton.Clicked += () => OnCancel?.Invoke();

        MyIdLabel = CreateLabel("MyID");
        YourIdLabel = CreateLabel("YourID");
        YourMoneyLabel = CreateLabel("YourMoney");

        //our own money field is an inline editable box: click to focus, type digits; commits on blur (Enter,
        //click-away, or accept). the other player's side stays a read-only label — match its colour so the two read alike.
        MyMoneyTextBox = CreateTextBox<NumericTextBox>("MyMoney", 10);

        if (MyMoneyTextBox is not null)
        {
            MyMoneyTextBox.Text = "0";

            //match the YourMoney label's 1px padding so both sides' gold reads at the same inset (textbox defaults to 2px)
            MyMoneyTextBox.PaddingLeft = 1;
            MyMoneyTextBox.PaddingRight = 1;
            MyMoneyTextBox.PaddingTop = 1;
            MyMoneyTextBox.PaddingBottom = 1;

            if (YourMoneyLabel is not null)
                MyMoneyTextBox.ForegroundColor = YourMoneyLabel.ForegroundColor;

            //inline field in a non-modal panel: don't modally block the rest of the UI while editing, and commit +
            //release focus when the user clicks away (drags an item in, hits OK, etc.). the dispatcher drops focus on
            //an outside press; both that and Enter funnel through LostFocus → the single commit path below.
            MyMoneyTextBox.BlocksMouseWhenFocused = false;
            MyMoneyTextBox.LostFocus += _ => CommitMoneyInput();
        }

        MyExchangeRect = GetRect("MyExchange");
        YourExchangeRect = GetRect("YourExchange");

        YourAckImage = CreateImage("YourACK");

        YourAckImage?.Visible = false;

        //container panels for item clipping — sized to exclude scrollbar area
        var clipWidth = MyExchangeRect.Width - ScrollBarControl.DEFAULT_WIDTH - 4;

        MyItemsContainer = new UIPanel
        {
            Name = "MyItemsContainer",
            X = MyExchangeRect.X,
            Y = MyExchangeRect.Y,
            Width = clipWidth,
            Height = MyExchangeRect.Height,
            IsPassThrough = true
        };

        AddChild(MyItemsContainer);

        YourItemsContainer = new UIPanel
        {
            Name = "YourItemsContainer",
            X = YourExchangeRect.X,
            Y = YourExchangeRect.Y,
            Width = clipWidth,
            Height = YourExchangeRect.Height,
            IsPassThrough = true
        };

        AddChild(YourItemsContainer);

        //create item controls as children of the container panels
        CreateItemControls(MyItems, MyItemsContainer);
        CreateItemControls(YourItems, YourItemsContainer);

        //vertical scrollbars — right edge of each exchange rect
        MyVerticalScroll = new ScrollBarControl
        {
            Name = "MyVerticalScroll",
            X = MyExchangeRect.Right - ScrollBarControl.DEFAULT_WIDTH,
            Y = MyExchangeRect.Y,
            Height = MyExchangeRect.Height - ScrollBarControl.DEFAULT_WIDTH
        };

        MyVerticalScroll.OnValueChanged += v =>
        {
            MyVerticalOffset = v;
            RefreshVisibleItems(false);
        };

        AddChild(MyVerticalScroll);

        YourVerticalScroll = new ScrollBarControl
        {
            Name = "YourVerticalScroll",
            X = YourExchangeRect.Right - ScrollBarControl.DEFAULT_WIDTH,
            Y = YourExchangeRect.Y,
            Height = YourExchangeRect.Height - ScrollBarControl.DEFAULT_WIDTH
        };

        YourVerticalScroll.OnValueChanged += v =>
        {
            YourVerticalOffset = v;
            RefreshVisibleItems(true);
        };

        AddChild(YourVerticalScroll);

        //horizontal scrollbars — bottom edge of each exchange rect
        MyHorizontalScroll = new ScrollBarControl
        {
            Name = "MyHorizontalScroll",
            Orientation = ScrollOrientation.Horizontal,
            X = MyExchangeRect.X,
            Y = MyExchangeRect.Bottom - ScrollBarControl.DEFAULT_WIDTH,
            Width = MyExchangeRect.Width - ScrollBarControl.DEFAULT_WIDTH,
            Height = ScrollBarControl.DEFAULT_WIDTH
        };

        MyHorizontalScroll.OnValueChanged += v =>
        {
            MyHorizontalOffset = v;
            ApplyHorizontalOffset(MyItems, v);
        };

        AddChild(MyHorizontalScroll);

        YourHorizontalScroll = new ScrollBarControl
        {
            Name = "YourHorizontalScroll",
            Orientation = ScrollOrientation.Horizontal,
            X = YourExchangeRect.X,
            Y = YourExchangeRect.Bottom - ScrollBarControl.DEFAULT_WIDTH,
            Width = YourExchangeRect.Width - ScrollBarControl.DEFAULT_WIDTH,
            Height = ScrollBarControl.DEFAULT_WIDTH
        };

        YourHorizontalScroll.OnValueChanged += v =>
        {
            YourHorizontalOffset = v;
            ApplyHorizontalOffset(YourItems, v);
        };

        AddChild(YourHorizontalScroll);

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

    private static void CreateItemControls(ExchangeItemControl[] items, UIPanel container)
    {
        var itemWidth = container.Width;

        for (var i = 0; i < MAX_VISIBLE_ITEMS; i++)
        {
            var control = new ExchangeItemControl
            {
                Name = $"ExchangeItem{i}",
                Y = i * ITEM_ROW_HEIGHT,
                Width = itemWidth
            };

            control.SetBaseX(0);
            items[i] = control;
            container.AddChild(control);
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

    public event CancelHandler? OnCancel;

    /// <summary>
    ///     Fired when the local player commits a gold amount in the inline money field — on blur (Enter, click-away, or
    ///     accept), via the single LostFocus → CommitMoneyInput path.
    /// </summary>
    public event ExchangeGoldEnteredHandler? OnSetGold;

    /// <summary>
    ///     Single commit path for the inline money field, invoked on every blur (outside-press, Enter, lock-on-accept,
    ///     auto-unfocus when hidden). Parses the field and raises <see cref="OnSetGold" />. The server treats this as a
    ///     set (not an add) and echoes the confirmed amount back via <see cref="OnExchangeGoldSet" />.
    /// </summary>
    private void CommitMoneyInput()
    {
        //guard uniformly here so every caller is protected: skip when the panel isn't showing (teardown / reset blurs)
        //or the field is locked (we've already accepted, and the server would ignore further SetGold anyway).
        if (MyMoneyTextBox is null || !Visible || MyMoneyTextBox.IsReadOnly)
            return;

        var text = MyMoneyTextBox.Text;

        //an empty field means zero gold; digit-only filtering guarantees any non-empty text parses cleanly. clamp to
        //the int range the exchange protocol carries — the server then validates against gold actually held and echoes back.
        var amount = 0L;

        if ((text.Length > 0) && !long.TryParse(text, out amount))
            return;

        var clamped = (int)Math.Clamp(amount, 0L, int.MaxValue);

        //dirty-check against the last value we asserted (sent or confirmed), NOT just the server-confirmed MyGold.
        //MyGold lags an in-flight send, so checking it would silently drop a revert edit that happens to equal the
        //last confirmed value while a different amount is still in flight. avoids re-sending when nothing changed.
        if (clamped == LastAssertedGold)
            return;

        LastAssertedGold = clamped;
        OnSetGold?.Invoke(clamped);
    }

    /// <summary>
    ///     Locks the inline money field (read-only, unfocused) once we've accepted — the server rejects gold changes
    ///     after our acceptance. Reset on the next exchange in <see cref="OnExchangeStarted" />.
    /// </summary>
    private void LockMoneyInput()
    {
        if (MyMoneyTextBox is null)
            return;

        //commit any pending edit before locking — ReleaseMoneyFocus blurs the field while it's still editable, so the
        //LostFocus → SetGold (if changed) precedes the Accept packet. then lock so no further edits are accepted.
        ReleaseMoneyFocus();
        MyMoneyTextBox.IsReadOnly = true;
    }

    /// <summary>
    ///     Drops focus from the money field. Setting <see cref="UITextBox.IsFocused" /> false fires its
    ///     <see cref="UITextBox.LostFocus" /> (→ <see cref="CommitMoneyInput" />) but does not touch the dispatcher's
    ///     keyboard focus, so the explicit focus is cleared here too — otherwise Phase 1 keeps routing keys to the box
    ///     after it is no longer being edited.
    /// </summary>
    private void ReleaseMoneyFocus()
    {
        if (MyMoneyTextBox is null)
            return;

        MyMoneyTextBox.IsFocused = false;

        if (InputDispatcher.Instance?.ExplicitFocus == MyMoneyTextBox)
            InputDispatcher.Instance.ClearExplicitFocus();
    }

    private void OnExchangeClosed(string? message)
    {
        ClearAllItems();
        ResetAllScrollbars();
        Hide();
    }

    private void OnExchangeGoldSet(bool rightSide)
    {
        if (rightSide)
            YourMoneyLabel?.Text = WorldState.Exchange.OtherGold.ToString("N0");
        else if (MyMoneyTextBox is not null)
        {
            //plain digits (no separators) so the server-confirmed value stays directly re-editable in the field
            MyMoneyTextBox.Text = WorldState.Exchange.MyGold.ToString();

            //re-anchor the dirty-check to the server's authoritative value — this also corrects an optimistic
            //LastAssertedGold the server clamped (e.g. when more gold was requested than actually held).
            LastAssertedGold = WorldState.Exchange.MyGold;
        }
    }

    private void OnExchangeItemAdded(bool rightSide, byte index)
    {
        UpdateVerticalScrollbar(rightSide);
        UpdateHorizontalScrollbar(rightSide);
        RefreshVisibleItems(rightSide);
    }

    private void OnExchangeOtherAccepted() => YourAckImage?.Visible = true;

    private void OnExchangeStarted()
    {
        ClearAllItems();
        ResetAllScrollbars();

        MyIdLabel?.Text = PlayerName;
        YourIdLabel?.Text = WorldState.Exchange.OtherUserName;
        YourMoneyLabel?.Text = "0";
        YourAckImage?.Visible = false;

        //reset the inline money field for the new exchange — re-editable, unfocused, back to zero
        if (MyMoneyTextBox is not null)
        {
            MyMoneyTextBox.Text = "0";
            LastAssertedGold = 0;
            MyMoneyTextBox.IsReadOnly = false;
            ReleaseMoneyFocus();
        }

        OkButton?.Enabled = true;

        //center vertically in viewport
        Y = ViewportBounds.Y + (ViewportBounds.Height - Height) / 2;

        Show();
    }

    public event OkHandler? OnOk;

    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case Keys.Escape:
                OnCancel?.Invoke();
                e.Handled = true;

                break;

            //Enter commits the inline money field and releases focus. the single-line box leaves Enter unhandled, so
            //it bubbles here via the focused-child→parent keyboard path. blurring funnels through LostFocus → the
            //single commit path, so Enter and click-away end in the same state (committed + unfocused).
            case Keys.Enter when MyMoneyTextBox is { IsReadOnly: false, IsFocused: true }:
                ReleaseMoneyFocus();
                e.Handled = true;

                break;
        }
    }

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        var mouseX = e.ScreenX;
        var mouseY = e.ScreenY;

        //determine which exchange rect the mouse is over
        if (MyItemsContainer.ContainsPoint(mouseX, mouseY))
        {
            if (MyVerticalScroll.TotalItems <= MyVerticalScroll.VisibleItems)
            {
                e.Handled = true;

                return;
            }

            var newValue = Math.Clamp(MyVerticalScroll.Value - e.Delta, 0, MyVerticalScroll.MaxValue);

            if (newValue != MyVerticalScroll.Value)
            {
                MyVerticalScroll.Value = newValue;
                MyVerticalOffset = newValue;
                RefreshVisibleItems(false);
            }

            e.Handled = true;
        } else if (YourItemsContainer.ContainsPoint(mouseX, mouseY))
        {
            if (YourVerticalScroll.TotalItems <= YourVerticalScroll.VisibleItems)
            {
                e.Handled = true;

                return;
            }

            var newValue = Math.Clamp(YourVerticalScroll.Value - e.Delta, 0, YourVerticalScroll.MaxValue);

            if (newValue != YourVerticalScroll.Value)
            {
                YourVerticalScroll.Value = newValue;
                YourVerticalOffset = newValue;
                RefreshVisibleItems(true);
            }

            e.Handled = true;
        }
    }

    private static void ApplyHorizontalOffset(ExchangeItemControl[] items, int offset)
    {
        foreach (var item in items)
            item.HorizontalOffset = offset;
    }

    private void RefreshVisibleItems(bool rightSide)
    {
        var items = rightSide ? YourItems : MyItems;
        var offset = rightSide ? YourVerticalOffset : MyVerticalOffset;
        var horizontalOffset = rightSide ? YourHorizontalOffset : MyHorizontalOffset;
        var totalCount = WorldState.Exchange.GetItemCount(rightSide);

        for (var i = 0; i < MAX_VISIBLE_ITEMS; i++)
        {
            var dataIndex = (byte)(offset + i);

            if (dataIndex < totalCount)
            {
                var data = WorldState.Exchange.GetItem(rightSide, dataIndex);

                if (data.HasValue)
                {
                    items[i]
                        .SetItem(data.Value.Sprite, data.Value.Color, data.Value.Name ?? string.Empty);

                    items[i].HorizontalOffset = horizontalOffset;

                    continue;
                }
            }

            items[i]
                .ClearItem();
        }

        UpdateHorizontalScrollbar(rightSide);
    }

    private void ResetAllScrollbars()
    {
        MyVerticalOffset = 0;
        YourVerticalOffset = 0;
        MyHorizontalOffset = 0;
        YourHorizontalOffset = 0;

        MyVerticalScroll.Value = 0;
        MyVerticalScroll.TotalItems = 0;
        MyVerticalScroll.VisibleItems = MAX_VISIBLE_ITEMS;
        MyVerticalScroll.MaxValue = 0;

        YourVerticalScroll.Value = 0;
        YourVerticalScroll.TotalItems = 0;
        YourVerticalScroll.VisibleItems = MAX_VISIBLE_ITEMS;
        YourVerticalScroll.MaxValue = 0;

        MyHorizontalScroll.Value = 0;
        MyHorizontalScroll.TotalItems = 0;
        MyHorizontalScroll.VisibleItems = 0;
        MyHorizontalScroll.MaxValue = 0;

        YourHorizontalScroll.Value = 0;
        YourHorizontalScroll.TotalItems = 0;
        YourHorizontalScroll.VisibleItems = 0;
        YourHorizontalScroll.MaxValue = 0;

        ApplyHorizontalOffset(MyItems, 0);
        ApplyHorizontalOffset(YourItems, 0);
    }

    private void UpdateHorizontalScrollbar(bool rightSide)
    {
        var items = rightSide ? YourItems : MyItems;
        var scrollbar = rightSide ? YourHorizontalScroll : MyHorizontalScroll;
        var rect = rightSide ? YourExchangeRect : MyExchangeRect;
        var visibleWidth = rect.Width - ScrollBarControl.DEFAULT_WIDTH - 10;

        var maxEntryWidth = 0;

        foreach (var item in items)
            if (item.Visible && (item.EntryWidth > maxEntryWidth))
                maxEntryWidth = item.EntryWidth;

        var overflow = maxEntryWidth - visibleWidth;

        if (overflow > 0)
        {
            scrollbar.TotalItems = maxEntryWidth;
            scrollbar.VisibleItems = visibleWidth;
            scrollbar.MaxValue = overflow;
        } else
        {
            scrollbar.TotalItems = 0;
            scrollbar.VisibleItems = 0;
            scrollbar.MaxValue = 0;
            scrollbar.Value = 0;

            //reset offset when no overflow
            if (rightSide)
            {
                YourHorizontalOffset = 0;
                ApplyHorizontalOffset(YourItems, 0);
            } else
            {
                MyHorizontalOffset = 0;
                ApplyHorizontalOffset(MyItems, 0);
            }
        }
    }

    private void UpdateVerticalScrollbar(bool rightSide)
    {
        var totalCount = WorldState.Exchange.GetItemCount(rightSide);
        var scrollbar = rightSide ? YourVerticalScroll : MyVerticalScroll;

        scrollbar.TotalItems = totalCount;
        scrollbar.VisibleItems = MAX_VISIBLE_ITEMS;
        scrollbar.MaxValue = Math.Max(0, totalCount - MAX_VISIBLE_ITEMS);
    }
}
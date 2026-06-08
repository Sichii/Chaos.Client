#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Controls.Scrolling;
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
    //gap (px) between the item text's clip edge and the vertical scrollbar — the prefab has a UI element just left of
    //the bar, so the text needs to stop short of it. Applied to the viewer's content width AND the H-bar visible width
    //so the horizontal pan range stays consistent with the narrower clip.
    private const int ITEM_TEXT_RIGHT_INSET = 5;

    private readonly Rectangle MyExchangeRect;
    private readonly UILabel? MyIdLabel;
    private readonly ExchangeItemList MyItemList;
    private readonly NumericTextBox? MyMoneyTextBox;

    private readonly UIImage? YourAckImage;
    private readonly Rectangle YourExchangeRect;
    private readonly UILabel? YourIdLabel;
    private readonly ExchangeItemList YourItemList;
    private readonly UILabel? YourMoneyLabel;

    private readonly Rectangle ViewportBounds;

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

        //item list for the local player's side, hosted in a ScrollViewerControl that owns the vertical scrollbar chrome
        MyItemList = new ExchangeItemList(rightSide: false)
        {
            Name = "MyItemList",
            Width = MyExchangeRect.Width,
            Height = MyExchangeRect.Height
        };

        var myViewer = new ScrollViewerControl(MyItemList)
        {
            Name = "MyScrollViewer",
            X = MyExchangeRect.X,
            Y = MyExchangeRect.Y,
            Width = MyExchangeRect.Width,
            Height = MyExchangeRect.Height,
            ContentRightPadding = ITEM_TEXT_RIGHT_INSET
        };

        AddChild(myViewer);

        //item list for the other player's side
        YourItemList = new ExchangeItemList(rightSide: true)
        {
            Name = "YourItemList",
            Width = YourExchangeRect.Width,
            Height = YourExchangeRect.Height
        };

        var yourViewer = new ScrollViewerControl(YourItemList)
        {
            Name = "YourScrollViewer",
            X = YourExchangeRect.X,
            Y = YourExchangeRect.Y,
            Width = YourExchangeRect.Width,
            Height = YourExchangeRect.Height,
            ContentRightPadding = ITEM_TEXT_RIGHT_INSET
        };

        AddChild(yourViewer);

        //subscribe to state events
        WorldState.Exchange.Started += OnExchangeStarted;
        WorldState.Exchange.ItemAdded += OnExchangeItemAdded;
        WorldState.Exchange.GoldSet += OnExchangeGoldSet;
        WorldState.Exchange.OtherAccepted += OnExchangeOtherAccepted;
        WorldState.Exchange.Closed += OnExchangeClosed;
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
        MyItemList.Reset();
        YourItemList.Reset();
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
        var list = rightSide ? YourItemList : MyItemList;
        list.Refresh();
    }

    private void OnExchangeOtherAccepted() => YourAckImage?.Visible = true;

    private void OnExchangeStarted()
    {
        MyItemList.Reset();
        YourItemList.Reset();

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
}

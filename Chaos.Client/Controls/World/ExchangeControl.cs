#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Exchange/trade panel using _nexch prefab. Two-sided layout: left side (player items/gold), right side (other player
///     items/gold). Up to 4 items per side (169x144 grid area, ~36px per row). Server drives all state via
///     DisplayExchangeArgs.
/// </summary>
public class ExchangeControl : PrefabPanel
{
    private const int MAX_ITEMS_PER_SIDE = 4;
    private const int ITEM_ROW_HEIGHT = 36;
    private const int ICON_SIZE = 32;
    private const int ICON_PADDING = 2;
    private const int TEXT_OFFSET_X = 36;

    private readonly GraphicsDevice DeviceRef;

    // Item grid rects
    private readonly Rectangle MyExchangeRect;

    // Name labels
    private readonly UILabel? MyIdLabel;

    // Item state — icon textures and names per side
    private readonly ExchangeSlot[] MyItems = new ExchangeSlot[MAX_ITEMS_PER_SIDE];

    // Gold labels
    private readonly UILabel? MyMoneyLabel;

    // Other player's accept indicator
    private readonly UIImage? YourAckImage;
    private readonly Rectangle YourExchangeRect;
    private readonly UILabel? YourIdLabel;
    private readonly ExchangeSlot[] YourItems = new ExchangeSlot[MAX_ITEMS_PER_SIDE];
    private readonly UILabel? YourMoneyLabel;

    public uint OtherUserId { get; private set; }
    public UIButton? CancelButton { get; }

    public UIButton? OkButton { get; }

    public ExchangeControl(GraphicsDevice device)
        : base(device, "_nexch")
    {
        DeviceRef = device;
        Name = "Exchange";
        Visible = false;

        var elements = AutoPopulate();

        OkButton = elements.GetValueOrDefault("OK") as UIButton;
        CancelButton = elements.GetValueOrDefault("Cancel") as UIButton;

        if (OkButton is not null)
            OkButton.OnClick += () => OnOk?.Invoke();

        if (CancelButton is not null)
            CancelButton.OnClick += () => OnCancel?.Invoke();

        // Name labels
        MyIdLabel = CreateLabel("MyID");
        YourIdLabel = CreateLabel("YourID");

        // Gold labels
        MyMoneyLabel = CreateLabel("MyMoney", TextAlignment.Right);
        YourMoneyLabel = CreateLabel("YourMoney", TextAlignment.Right);

        // Item grid rects
        MyExchangeRect = GetRect("MyExchange");
        YourExchangeRect = GetRect("YourExchange");

        // YourACK indicator — the second image (pressed OK) shows when other player accepts
        YourAckImage = elements.GetValueOrDefault("YourACK") as UIImage;

        if (YourAckImage is not null)
            YourAckImage.Visible = false;
    }

    /// <summary>
    ///     Adds an item to the exchange display.
    /// </summary>
    public void AddItem(
        bool rightSide,
        byte exchangeIndex,
        ushort itemSprite,
        string? itemName)
    {
        if (exchangeIndex >= MAX_ITEMS_PER_SIDE)
            return;

        var slots = rightSide ? YourItems : MyItems;
        ref var slot = ref slots[exchangeIndex];

        slot.IconTexture?.Dispose();
        slot.IconTexture = TextureConverter.RenderSprite(DeviceRef, DataContext.PanelItems.GetPanelItemSprite(itemSprite));
        slot.Name = itemName ?? string.Empty;
        slot.NameCache ??= new CachedText(DeviceRef);
        slot.NameCache.Update(slot.Name, 0, Color.White);
        slot.Occupied = true;
    }

    private void ClearAllItems()
    {
        ClearSlots(MyItems);
        ClearSlots(YourItems);
    }

    private static void ClearSlots(ExchangeSlot[] slots)
    {
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i]
                .IconTexture
                ?.Dispose();
            slots[i].IconTexture = null;
            slots[i].Name = string.Empty;
            slots[i].Occupied = false;
        }
    }

    /// <summary>
    ///     Closes and resets the exchange panel.
    /// </summary>
    public void CloseExchange()
    {
        ClearAllItems();
        Hide();
    }

    public override void Dispose()
    {
        ClearAllItems();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        var sx = ScreenX;
        var sy = ScreenY;

        DrawItemGrid(
            spriteBatch,
            MyItems,
            sx + MyExchangeRect.X,
            sy + MyExchangeRect.Y);

        DrawItemGrid(
            spriteBatch,
            YourItems,
            sx + YourExchangeRect.X,
            sy + YourExchangeRect.Y);
    }

    private void DrawItemGrid(
        SpriteBatch spriteBatch,
        ExchangeSlot[] slots,
        int gridX,
        int gridY)
    {
        for (var i = 0; i < MAX_ITEMS_PER_SIDE; i++)
        {
            ref var slot = ref slots[i];

            if (!slot.Occupied)
                continue;

            var rowY = gridY + i * ITEM_ROW_HEIGHT;

            if (slot.IconTexture is not null)
                spriteBatch.Draw(slot.IconTexture, new Vector2(gridX + ICON_PADDING, rowY + ICON_PADDING), Color.White);

            slot.NameCache?.Draw(spriteBatch, new Vector2(gridX + TEXT_OFFSET_X, rowY + (ITEM_ROW_HEIGHT - 12) / 2));
        }
    }

    public event Action? OnCancel;

    public event Action? OnOk;

    /// <summary>
    ///     Updates the gold amount display for one side.
    /// </summary>
    public void SetGold(bool rightSide, int goldAmount)
    {
        var label = rightSide ? YourMoneyLabel : MyMoneyLabel;
        label?.SetText(goldAmount.ToString("N0"));
    }

    /// <summary>
    ///     Shows the other player's accept indicator.
    /// </summary>
    public void ShowOtherAccepted()
    {
        if (YourAckImage is not null)
            YourAckImage.Visible = true;
    }

    /// <summary>
    ///     Initializes a new exchange session with the other player.
    /// </summary>
    public void StartExchange(uint otherUserId, string otherUserName)
    {
        OtherUserId = otherUserId;
        ClearAllItems();

        MyIdLabel?.SetText("You");
        YourIdLabel?.SetText(otherUserName);
        MyMoneyLabel?.SetText("0");
        YourMoneyLabel?.SetText("0");

        if (YourAckImage is not null)
            YourAckImage.Visible = false;

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

    private struct ExchangeSlot
    {
        public Texture2D? IconTexture;
        public string Name;
        public CachedText? NameCache;
        public bool Occupied;
    }
}
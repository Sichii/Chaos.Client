namespace Chaos.Client.Networking;

public sealed record InventorySlotInfo(
    ushort Sprite,
    string Name,
    bool Stackable,
    uint Count);
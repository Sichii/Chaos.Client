# Opcode 0xFF — ExtendedStats

Server-to-client packet carrying combat stats that the stock DA client never supported. Sent by Hybrasyl alongside the standard 0x08 (Attributes) packet whenever the Secondary stat flag triggers.

## Why this exists

The legacy 0x08 packet sends MR/Hit/Dmg as single bytes via a lossy rating system (0-255 scale, 128 = baseline). Crit, MagicCrit, Dodge, and MagicDodge are never sent at all — they're server-internal. This packet replaces the workaround with raw float values the client can display directly.

## Wire format

All values are **IEEE 754 single-precision floats** (4 bytes each), big-endian byte order.

```
Offset  Type    Field       Semantics
------  ------  ----------  -------------------------------------------------
0x00    float   Mr          Magic resistance bonus (0.0 = none, 0.16 = +16%, -0.08 = -8%)
0x04    float   Hit         Hit bonus (same scale as Mr)
0x08    float   Dmg         Damage bonus (same scale as Mr)
0x0C    float   Crit        Physical crit chance (0.055 = 5.5%)
0x10    float   MagicCrit   Magic crit chance (same scale as Crit)
0x14    float   Dodge       Physical dodge chance (same scale as Crit)
0x18    float   MagicDodge  Magic dodge chance (same scale as Crit)
```

Total payload: **28 bytes** (7 x 4-byte floats).

## Value semantics

**Mr, Hit, Dmg** — These are the raw bonus values, not multipliers. Zero means no modifier. Positive is a buff, negative is a debuff. To display as a percentage: `value * 100` (e.g. `0.16` displays as `+16%`, `-0.08` displays as `-8%`).

The server internally adds 1.0 to these for use as multipliers (`damage *= Dmg + 1.0`), but that offset is not included in the packet.

**Crit, MagicCrit, Dodge, MagicDodge** — These are absolute probabilities (base + bonus combined). To display as a percentage: `value * 100` (e.g. `0.055` displays as `5.5%`).

## When it's sent

Sent immediately after the 0x08 Attributes packet whenever `StatUpdateFlags.Secondary` is set. This includes:
- Login / world entry
- Equipment changes affecting these stats
- Buff/debuff apply/fade
- Any status effect that modifies MR, Hit, Dmg, Crit, or Dodge stats

The 0x08 packet continues to carry the legacy rating bytes — they're vestigial but harmless.

## Relationship to stats-display-direction.md

This packet is the "extended `AttributesArgs` variant" referenced in the stats display direction doc. It provides the raw server-authoritative values for the Martial Awareness / extended stats panel. The client can display these directly without any derived-stat computation for these specific fields.

Future stats (LifeSteal, ManaSteal, ReflectPhysical, ReflectMagical, ExtraGold, ExtraXp, etc.) will be appended to this packet as additional floats. The client should read as many floats as the packet contains and ignore any it doesn't recognize.

## Implementation guide for Chaos.Client

### 1. Add to ServerOpCode enum

```csharp
ExtendedStats = 0xFF
```

> Allocated at the top of the 1-byte opcode space per the custom-opcode convention (0xFF downward for Hybrasyl modernization opcodes). Retail ServerOpCode values max out at 0x7E, so 0x80–0xFF is unused territory.

### 2. Define the args record

```csharp
public sealed record ExtendedStatsArgs
{
    public float Mr { get; init; }
    public float Hit { get; init; }
    public float Dmg { get; init; }
    public float Crit { get; init; }
    public float MagicCrit { get; init; }
    public float Dodge { get; init; }
    public float MagicDodge { get; init; }
}
```

### 3. Deserialize (manual, since this isn't in Chaos.Networking)

```csharp
private static ExtendedStatsArgs DeserializeExtendedStats(ServerPacket pkt)
{
    return new ExtendedStatsArgs
    {
        Mr = pkt.ReadSingle(),
        Hit = pkt.ReadSingle(),
        Dmg = pkt.ReadSingle(),
        Crit = pkt.ReadSingle(),
        MagicCrit = pkt.ReadSingle(),
        Dodge = pkt.ReadSingle(),
        MagicDodge = pkt.ReadSingle()
    };
}
```

Note: `ReadSingle()` reads 4 bytes big-endian and returns an IEEE 754 float. If `ServerPacket` doesn't have this method, implement as:
```csharp
var bits = pkt.ReadInt32(); // existing big-endian int32 read
var value = BitConverter.Int32BitsToSingle(bits);
```

### 4. Register handler in ConnectionManager

```csharp
// In IndexHandlers(), under the world entry or attributes section:
PacketHandlers[(byte)ServerOpCode.ExtendedStats] = HandleExtendedStats;
```

```csharp
public ExtendedStatsArgs? ExtendedStats { get; private set; }
public event Action<ExtendedStatsArgs>? OnExtendedStats;

private void HandleExtendedStats(ServerPacket pkt)
{
    ExtendedStats = DeserializeExtendedStats(pkt);
    OnExtendedStats?.Invoke(ExtendedStats);
}
```

### 5. Display formatting

```
Mr:         String.Format("{0:+0.#%;-0.#%}", args.Mr)        // "+16%" or "-8%"
Hit:        String.Format("{0:+0.#%;-0.#%}", args.Hit)
Dmg:        String.Format("{0:+0.#%;-0.#%}", args.Dmg)
Crit:       String.Format("{0:0.#%}", args.Crit)              // "5.5%"
MagicCrit:  String.Format("{0:0.#%}", args.MagicCrit)
Dodge:      String.Format("{0:0.#%}", args.Dodge)
MagicDodge: String.Format("{0:0.#%}", args.MagicDodge)
```

## Server source

- Packet definition: `hybrasyl/Networking/ServerPackets/ExtendedStats.cs`
- Opcode constant: `hybrasyl/Internals/Enums/OpCodes.cs` (`ExtendedStats = 0xFF`)
- Send site: `hybrasyl/Objects/User.cs` — `UpdateAttributes()`, after the 0x08 enqueue
- Float writer: `hybrasyl/Networking/ServerPacket.cs` — `WriteSingle(float)`

#region
using Chaos.Extensions.Common;
#endregion

namespace Chaos.Client.ViewModel;

/// <summary>
///     Authoritative skill book state. Owns skill slot data and cooldown timers. Fires change events for UI
///     reconciliation.
/// </summary>
public sealed class SkillBook
{
    public const int MAX_SLOTS = 89;
    private readonly float[] CooldownDuration = new float[MAX_SLOTS];
    private readonly float[] CooldownRemaining = new float[MAX_SLOTS];

    private readonly SkillSlotData[] Slots = new SkillSlotData[MAX_SLOTS];

    /// <summary>
    ///     Clears all slots and cooldowns. Fires <see cref="Cleared" />.
    /// </summary>
    public void Clear()
    {
        Array.Clear(Slots);
        Array.Clear(CooldownRemaining);
        Array.Clear(CooldownDuration);
        Cleared?.Invoke();
    }

    /// <summary>
    ///     Fired when all slots are cleared (e.g., on logout or full re-entry).
    /// </summary>
    public event ClearedHandler? Cleared;

    /// <summary>
    ///     Clears a skill slot and its cooldown. Fires <see cref="SlotChanged" />.
    /// </summary>
    public void ClearSlot(byte slot)
    {
        var index = slot - 1;

        if (index is < 0 or >= MAX_SLOTS)
            return;

        Slots[index] = default;
        CooldownRemaining[index] = 0;
        CooldownDuration[index] = 0;
        SlotChanged?.Invoke(slot);
    }

    /// <summary>
    ///     Returns the cooldown progress for a 1-based slot (0 = ready, 1 = just started).
    /// </summary>
    public float GetCooldownPercent(byte slot)
    {
        var index = slot - 1;

        if (index is < 0 or >= MAX_SLOTS)
            return 0;

        return CooldownDuration[index] > 0 ? CooldownRemaining[index] / CooldownDuration[index] : 0;
    }

    /// <summary>
    ///     Returns the data for a 1-based slot number. Returns default if slot is out of range.
    /// </summary>
    public ref readonly SkillSlotData GetSlot(byte slot)
    {
        var index = slot - 1;

        if (index is < 0 or >= MAX_SLOTS)
            return ref SkillSlotData.Empty;

        return ref Slots[index];
    }

    /// <summary>
    ///     Returns true if any slot contains a skill whose name matches (case-insensitive, prefix match for leveled skills).
    /// </summary>
    public bool HasSkillByName(string name)
    {
        for (var i = 0; i < MAX_SLOTS; i++)
        {
            var slotName = Slots[i].Name;

            if (!Slots[i].IsOccupied || slotName is null)
                continue;

            if (slotName.EqualsI(name))
                return true;

            // Prefix match for leveled skills (e.g., "swimming Lv.5")
            if (slotName.StartsWithI(name) && (slotName.Length > name.Length) && (slotName[name.Length] == ' '))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Returns true if the 1-based slot has an active cooldown.
    /// </summary>
    public bool IsOnCooldown(byte slot)
    {
        var index = slot - 1;

        if (index is < 0 or >= MAX_SLOTS)
            return false;

        return CooldownRemaining[index] > 0;
    }

    /// <summary>
    ///     Starts or resets a cooldown on a 1-based slot. No event — controls poll cooldown directly each frame.
    /// </summary>
    public void SetCooldown(byte slot, uint durationSecs)
    {
        var index = slot - 1;

        if (index is < 0 or >= MAX_SLOTS)
            return;

        var durationMs = durationSecs * 1000f;
        CooldownRemaining[index] = durationMs;
        CooldownDuration[index] = durationMs;
    }

    /// <summary>
    ///     Sets or updates a skill slot. Fires <see cref="SlotChanged" />.
    /// </summary>
    public void SetSlot(
        byte slot,
        ushort sprite,
        string? name,
        string? chant = null)
    {
        var index = slot - 1;

        if (index is < 0 or >= MAX_SLOTS)
            return;

        Slots[index] = new SkillSlotData(sprite, name, chant);
        SlotChanged?.Invoke(slot);
    }

    /// <summary>
    ///     Fired when a specific slot's data changes (added, updated, or cleared). Argument is the 1-based slot number.
    /// </summary>
    public event SlotChangedHandler? SlotChanged;

    /// <summary>
    ///     Ticks all active cooldown timers. Called once per frame by WorldState.Update().
    /// </summary>
    public void Update(float elapsedMs)
    {
        for (var i = 0; i < MAX_SLOTS; i++)
        {
            if (CooldownRemaining[i] <= 0)
                continue;

            CooldownRemaining[i] -= elapsedMs;

            if (CooldownRemaining[i] <= 0)
            {
                CooldownRemaining[i] = 0;
                CooldownDuration[i] = 0;
            }
        }
    }

    /// <summary>
    ///     Immutable data for a single skill slot.
    /// </summary>
    public readonly record struct SkillSlotData(ushort Sprite, string? Name, string? Chant)
    {
        internal static readonly SkillSlotData Empty;

        /// <summary>
        ///     True if this slot contains a skill (has a sprite assigned).
        /// </summary>
        public bool IsOccupied => Sprite > 0;
    }
}
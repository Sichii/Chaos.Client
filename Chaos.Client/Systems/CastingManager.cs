#region
using Chaos.Client.Controls.World.Hud.Panel.Slots;
using Chaos.Client.Networking;
#endregion

namespace Chaos.Client.Systems;

/// <summary>
///     Tracks the state of the spell casting pipeline: target selection (cast mode) and chant line sequencing.
/// </summary>
public sealed class CastingManager
{
    private const float CHANT_INTERVAL_MS = 1000f;
    private float ElapsedMs;

    private int LineIndex;

    /// <summary>
    ///     True when chant lines are being sent on a timer.
    /// </summary>
    public bool IsChanting { get; private set; }

    /// <summary>
    ///     True when waiting for the player to select a target entity.
    /// </summary>
    public bool IsTargeting { get; private set; }

    public SpellSlot? SpellSlot { get; private set; }
    public uint TargetId { get; private set; }
    public int TargetX { get; private set; }
    public int TargetY { get; private set; }

    /// <summary>
    ///     True when any casting activity is in progress (targeting or chanting).
    /// </summary>
    public bool IsActive => IsTargeting || IsChanting;

    /// <summary>
    ///     Enters target selection mode for a spell.
    /// </summary>
    public void BeginTargeting(SpellSlot spellSlot)
    {
        SpellSlot = spellSlot;
        IsTargeting = true;
        IsChanting = false;
    }

    /// <summary>
    ///     Cancels all casting activity.
    /// </summary>
    public void Reset()
    {
        IsTargeting = false;
        IsChanting = false;
        SpellSlot = null;
        LineIndex = 0;
        ElapsedMs = 0;
    }

    /// <summary>
    ///     Sets the target and begins the chant sequence (or casts immediately for 0-line spells). Returns true if chanting
    ///     was started, false if the spell was cast immediately.
    /// </summary>
    public bool SelectTarget(
        uint targetId,
        int targetX,
        int targetY,
        ConnectionManager connection)
    {
        TargetId = targetId;
        TargetX = targetX;
        TargetY = targetY;
        IsTargeting = false;

        if (SpellSlot is null)
            return false;

        if (SpellSlot.CastLines == 0)
        {
            connection.UseSpellOnTarget(
                SpellSlot.Slot,
                targetId,
                targetX,
                targetY);
            Reset();

            return false;
        }

        // Begin chant sequence
        connection.SendBeginChant(SpellSlot.CastLines);

        var firstChant = SpellSlot.Chants[0];

        if (!string.IsNullOrEmpty(firstChant))
            connection.SendChant(firstChant);

        LineIndex = 1;
        ElapsedMs = 0;
        IsChanting = true;

        return true;
    }

    /// <summary>
    ///     Ticks the chant timer. Sends the next chant line every second. On the final tick, sends the spell name and cast
    ///     packet.
    /// </summary>
    public void Update(float deltaMs, ConnectionManager connection)
    {
        if (!IsChanting || SpellSlot is null)
            return;

        ElapsedMs += deltaMs;

        if (ElapsedMs < CHANT_INTERVAL_MS)
            return;

        ElapsedMs -= CHANT_INTERVAL_MS;

        var castLines = SpellSlot.CastLines;

        if (LineIndex < castLines)
        {
            var chant = SpellSlot.Chants[LineIndex];

            if (!string.IsNullOrEmpty(chant))
                connection.SendChant(chant);

            LineIndex++;
        } else
        {
            // Final: send spell name as chant + cast packet
            var spellName = SpellSlot.AbilityName ?? string.Empty;

            if (!string.IsNullOrEmpty(spellName))
                connection.SendChant(spellName);

            connection.UseSpellOnTarget(
                SpellSlot.Slot,
                TargetId,
                TargetX,
                TargetY);
            Reset();
        }
    }
}
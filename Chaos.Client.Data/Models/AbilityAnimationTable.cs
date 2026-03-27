#region
using System.Collections.Frozen;
using Chaos.DarkAges.Definitions;
#endregion

namespace Chaos.Client.Data.Models;

/// <summary>
///     Frozen lookup that maps (BodyAnimation, armorSpriteId) to whether that armor is allowed to play the animation.
///     Parsed from Skill_e.tbl (normal armors) and Skill_i.tbl (overcoat armors) in Legend.dat.
/// </summary>
public sealed class AbilityAnimationTable
{
    private const byte CLASS_ANIM_BASE = 128;

    /// <summary>
    ///     Key: skill entry index (0-17, maps to BodyAnimation via (byte)anim - 128). Value: frozen set of allowed armor
    ///     sprite IDs. Overcoat IDs stored as id + 1000.
    /// </summary>
    private readonly FrozenDictionary<byte, FrozenSet<int>> AllowedArmorsByEntry;

    internal AbilityAnimationTable(Dictionary<byte, HashSet<int>> mutableData)
        => AllowedArmorsByEntry = mutableData.ToFrozenDictionary(kvp => kvp.Key, kvp => kvp.Value.ToFrozenSet());

    /// <summary>
    ///     Returns true if the given armor sprite is allowed to play the specified body animation. Animations below 0x80
    ///     (peasant/emote) are always allowed. Unknown entries (not in the table) are allowed defensively.
    /// </summary>
    public bool IsAbilityAnimationAllowed(BodyAnimation anim, int spriteId)
    {
        var animByte = (byte)anim;

        if (animByte < CLASS_ANIM_BASE)
            return true;

        var entryIndex = (byte)(animByte - CLASS_ANIM_BASE);

        if (!AllowedArmorsByEntry.TryGetValue(entryIndex, out var allowedIds))
            return true;

        return allowedIds.Contains(spriteId);
    }
}
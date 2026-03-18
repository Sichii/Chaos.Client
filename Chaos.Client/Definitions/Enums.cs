namespace Chaos.Client.Definitions;

public enum TextAlignment
{
    Left,
    Center,
    Right
}

public enum PopupStyle
{
    Scroll,
    NonScroll,
    Wooden
}

/// <summary>
///     Identifies which panel is active in the shared "center bottom" tab area of the HUD. Only one tab panel is visible
///     at a time.
/// </summary>
public enum HudTab
{
    Inventory,
    Skills,
    Spells,
    SkillsAlt,
    SpellsAlt,
    Chat,
    Stats,
    ExtendedStats,
    Tools,
    MessageHistory
}

/// <summary>
///     Cooldown rendering style for panel slots.
/// </summary>
public enum CooldownStyle
{
    /// <summary>
    ///     No cooldown rendering.
    /// </summary>
    None,

    /// <summary>
    ///     Spell-style: swap to CooldownTexture for the full duration.
    /// </summary>
    Swap,

    /// <summary>
    ///     Skill-style: normal icon + blue overlay at 33% opacity + blue progressively revealed top-to-bottom as cooldown
    ///     elapses.
    /// </summary>
    Progressive
}

/// <summary>
///     The type of a visible entity in the game world.
/// </summary>
/// <summary>
///     The type of a visible entity in the game world.
///     Ordered by draw priority: ground items render first (underneath), then creatures, then aislings.
/// </summary>
public enum ClientEntityType : byte
{
    GroundItem,
    Creature,
    Aisling
}

/// <summary>
///     The current animation state of a world entity.
/// </summary>
public enum EntityAnimState : byte
{
    Idle,
    Walking,
    BodyAnim
}

/// <summary>
///     The available tabs in the status book.
/// </summary>
public enum StatusBookTab
{
    Equipment,
    Legend,
    Skills,
    Events,
    Album,
    Family
}
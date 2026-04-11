namespace Chaos.Client.Definitions;

public enum HorizontalAlignment
{
    Left,
    Center,
    Right
}

public enum VerticalAlignment
{
    Center,
    Top,
    Bottom
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

/// <summary>
///     Determines which icon variant to display for an ability in the metadata tab.
/// </summary>
public enum AbilityIconState
{
    /// <summary>
    ///     Player already knows this ability — use the standard icon (001 variant).
    /// </summary>
    Known,

    /// <summary>
    ///     Player meets all requirements to learn this ability — use the learnable icon (002 variant).
    /// </summary>
    Learnable,

    /// <summary>
    ///     Player does not meet the requirements — use the locked icon (003 variant).
    /// </summary>
    Locked
}

/// <summary>
///     Determines the display state of an event entry in the events metadata tab.
/// </summary>
public enum EventState
{
    /// <summary>
    ///     Player does not meet circle, class, or prerequisite requirements — gray text.
    /// </summary>
    Unavailable,

    /// <summary>
    ///     Player meets all requirements and can complete this event — blue text.
    /// </summary>
    Available,

    /// <summary>
    ///     Player has a legend mark matching this event's ID — green text.
    /// </summary>
    Completed
}

/// <summary>
///     Scrollbar orientation.
/// </summary>
public enum ScrollOrientation
{
    Vertical,
    Horizontal
}
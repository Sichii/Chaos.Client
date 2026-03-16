#region
using Chaos.Client.Collections;
using Chaos.Client.Rendering;
using Chaos.Geometry.Abstractions.Definitions;
using Microsoft.Xna.Framework;
#endregion

namespace Chaos.Client.Models;

/// <summary>
///     The type of a visible entity in the game world.
/// </summary>
public enum EntityType : byte
{
    Aisling,
    Creature,
    GroundItem
}

/// <summary>
///     Represents a visible entity in the game world. Tracked by <see cref="WorldState" /> and rendered by GameScreen.
/// </summary>
public sealed class WorldEntity
{
    public AislingAppearance? Appearance { get; set; }

    // Direction (Up=0, Right=1, Down=2, Left=3)
    public Direction Direction { get; set; }
    public uint Id { get; init; }
    public bool IsMoving { get; set; }

    // Display
    public string Name { get; set; } = string.Empty;

    // Visual identity
    public ushort SpriteId { get; set; }

    // Position
    public int TileX { get; set; }
    public int TileY { get; set; }
    public EntityType Type { get; set; }

    // Movement interpolation (snap for now, lerp later)
    public Vector2 VisualOffset { get; set; }
    public int SortDepth => TileX + TileY;
}
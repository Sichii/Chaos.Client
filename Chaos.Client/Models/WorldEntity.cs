#region
using Chaos.Client.Collections;
using Chaos.Client.Definitions;
using Chaos.Client.Rendering;
using Chaos.DarkAges.Definitions;
using Chaos.Geometry.Abstractions.Definitions;
using Microsoft.Xna.Framework;
#endregion

namespace Chaos.Client.Models;

/// <summary>
///     Represents a visible entity in the game world. Tracked by <see cref="WorldState" /> and rendered by GameScreen.
///     Animation state is managed by <see cref="AnimationManager" />.
/// </summary>
public sealed class WorldEntity
{
    public BodyAnimation? ActiveBodyAnimation { get; set; }
    public float AnimElapsedMs { get; set; }
    public int AnimFrameCount { get; set; }
    public int AnimFrameIndex { get; set; }
    public float AnimFrameIntervalMs { get; set; }

    // Animation state — managed by AnimationManager
    public EntityAnimState AnimState { get; set; }

    // Appearance
    public AislingAppearance? Appearance { get; set; }

    // Draw ordering: monotonic counter incremented each time entity enters a tile.
    // Higher = arrived more recently = draws on top of entities in the same category at the same tile.
    public uint ArrivalOrder { get; set; }
    public Direction Direction { get; set; }
    public uint Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public ushort SpriteId { get; set; }

    // Position
    public int TileX { get; set; }
    public int TileY { get; set; }
    public ClientEntityType Type { get; set; }
    public Vector2 VisualOffset { get; set; }
    public Vector2 WalkStartOffset { get; set; }
    public int SortDepth => TileX + TileY;
}
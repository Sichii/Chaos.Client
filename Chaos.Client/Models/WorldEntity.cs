#region
using Chaos.Client.Collections;
using Chaos.Client.Rendering;
using Chaos.Client.Systems;
using Chaos.DarkAges.Definitions;
using Chaos.Geometry.Abstractions.Definitions;
using Microsoft.Xna.Framework;
#endregion

namespace Chaos.Client.Models;

/// <summary>
///     Represents a visible entity in the game world. Tracked by <see cref="WorldState" /> and rendered by GameScreen.
///     Animation state is managed by <see cref="AnimationSystem" />.
/// </summary>
public sealed class WorldEntity
{
    public BodyAnimation? ActiveBodyAnimation { get; set; }

    // Emote overlay — animated frame range in emot01.epf, -1 = no active emote
    public int ActiveEmoteFrame { get; set; } = -1;
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
    public int BodyAnimRepeatsLeft { get; set; }
    public CreatureType CreatureType { get; set; }
    public Direction Direction { get; set; }
    public float EmoteDurationMs { get; set; }
    public float EmoteElapsedMs { get; set; }
    public int EmoteFrameCount { get; set; }
    public float EmoteRemainingMs { get; set; }
    public int EmoteStartFrame { get; set; }
    public string? GroupBoxText { get; set; }
    public uint Id { get; init; }
    public float IdleAnimElapsedMs { get; set; }

    // Idle animation — frames per direction in "04" EPF (0 = no idle anim)
    // IdleAnimTick increments independently of AnimState so idle cycling survives body animations.
    public int IdleAnimFrameCount { get; set; }
    public int IdleAnimTick { get; set; }
    public bool IsDead { get; set; }
    public bool IsTransparent { get; set; }
    public byte ItemColor { get; set; }
    public LanternSize LanternSize { get; set; }
    public string Name { get; set; } = string.Empty;
    public NameTagStyle NameTagStyle { get; set; }
    public RestPosition RestPosition { get; set; }
    public ushort SpriteId { get; set; }

    // Position
    public int TileX { get; set; }
    public int TileY { get; set; }
    public ClientEntityType Type { get; set; }
    public Vector2 VisualOffset { get; set; }
    public Vector2 WalkStartOffset { get; set; }
    public int SortDepth => TileX + TileY;
    public bool UsesCreatureWalkTiming => (Type == ClientEntityType.Creature) || (Appearance is null && (SpriteId > 0));
}
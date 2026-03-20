#region
using Chaos.Client.Definitions;
using Chaos.Client.Extensions;
using Chaos.Client.Models;
using Chaos.Client.Rendering;
using Chaos.Client.Systems.Animation;
using Chaos.DarkAges.Definitions;
using Chaos.Geometry;
using Chaos.Geometry.Abstractions;
using Chaos.Geometry.Abstractions.Definitions;
using Chaos.Networking.Entities.Server;
#endregion

namespace Chaos.Client.Collections;

/// <summary>
///     Tracks all visible entities in the current map. Updated from network packets, read by GameScreen for rendering.
/// </summary>
public sealed class WorldState
{
    private readonly Dictionary<uint, WorldEntity> Entities = new();
    private readonly List<WorldEntity> SortBuffer = [];

    /// <summary>
    ///     The player's entity ID, assigned by the server.
    /// </summary>
    public uint PlayerEntityId { get; set; }

    /// <summary>
    ///     Active spell/effect animations currently playing in the world.
    /// </summary>
    public List<ActiveEffect> ActiveEffects { get; } = [];

    /// <summary>
    ///     Active creature death dissolve animations.
    /// </summary>
    public List<DyingEffect> DyingEffects { get; } = [];

    /// <summary>
    ///     Adds or updates an aisling entity from a DisplayAisling packet.
    /// </summary>
    public void AddOrUpdateAisling(DisplayAislingArgs args)
    {
        if (args.IsHidden)
        {
            Entities.Remove(args.Id);

            return;
        }

        if (!Entities.TryGetValue(args.Id, out var entity))
        {
            entity = new WorldEntity
            {
                Id = args.Id,
                Type = ClientEntityType.Aisling
            };

            Entities[args.Id] = entity;
        }

        entity.Type = ClientEntityType.Aisling;
        entity.TileX = args.X;
        entity.TileY = args.Y;
        entity.Direction = args.Direction;
        entity.Name = args.Name;

        // Check for morph mode (creature form)
        if (args.Sprite.HasValue)
        {
            entity.SpriteId = args.Sprite.Value;
            entity.Appearance = null;
        } else
        {
            entity.SpriteId = 0;

            entity.Appearance = new AislingAppearance
            {
                Gender = args.BodySprite is BodySprite.Female or BodySprite.FemaleGhost ? Gender.Female : Gender.Male,
                BodyColor = (int)args.BodyColor,
                HeadSprite = args.HeadSprite,
                HeadColor = args.HeadColor,
                FaceSprite = args.FaceSprite,
                ArmorSprite = args.ArmorSprite1,
                ArmorColor = DisplayColor.Default,
                OvercoatSprite = args.OvercoatSprite,
                OvercoatColor = args.OvercoatColor,
                BootsSprite = args.BootsSprite,
                BootsColor = args.BootsColor,
                WeaponSprite = args.WeaponSprite,
                ShieldSprite = args.ShieldSprite,
                Accessory1Sprite = args.AccessorySprite1,
                Accessory1Color = args.AccessoryColor1,
                Accessory2Sprite = args.AccessorySprite2,
                Accessory2Color = args.AccessoryColor2,
                Accessory3Sprite = args.AccessorySprite3,
                Accessory3Color = args.AccessoryColor3,
                PantsColor = args.PantsColor
            };
        }
    }

    /// <summary>
    ///     Adds or updates visible entities (creatures + ground items) from a batch packet.
    /// </summary>
    public void AddOrUpdateVisibleEntities(DisplayVisibleEntitiesArgs args)
    {
        foreach (var obj in args.VisibleObjects)
        {
            if (!Entities.TryGetValue(obj.Id, out var entity))
            {
                entity = new WorldEntity
                {
                    Id = obj.Id
                };
                Entities[obj.Id] = entity;
            }

            entity.TileX = obj.X;
            entity.TileY = obj.Y;
            entity.SpriteId = obj.Sprite;
            entity.Appearance = null;

            switch (obj)
            {
                case CreatureInfo creature:
                    entity.Type = ClientEntityType.Creature;
                    entity.CreatureType = creature.CreatureType;
                    entity.Direction = creature.Direction;
                    entity.Name = creature.Name ?? string.Empty;

                    break;

                case GroundItemInfo:
                    entity.Type = ClientEntityType.GroundItem;

                    break;
            }
        }
    }

    /// <summary>
    ///     Clears all tracked entities and active effects. Call on map change.
    /// </summary>
    public void Clear()
    {
        Entities.Clear();
        ActiveEffects.Clear();

        foreach (var dying in DyingEffects)
            dying.Dispose();

        DyingEffects.Clear();
    }

    /// <summary>
    ///     Returns tile positions of all blocking entities (creatures except WalkThrough, and aislings excluding the player).
    /// </summary>
    public List<IPoint> GetBlockedPoints()
    {
        var blocked = new List<IPoint>();

        foreach (var entity in Entities.Values)
        {
            if (entity.Id == PlayerEntityId)
                continue;

            if (!IsBlockingEntity(entity))
                continue;

            blocked.Add(new Point(entity.TileX, entity.TileY));
        }

        return blocked;
    }

    /// <summary>
    ///     Returns an entity by ID, or null if not tracked.
    /// </summary>
    public WorldEntity? GetEntity(uint id) => Entities.GetValueOrDefault(id);

    /// <summary>
    ///     Returns the first entity at the specified tile, prioritizing creatures/aislings over ground items.
    /// </summary>
    public WorldEntity? GetEntityAt(int tileX, int tileY)
    {
        WorldEntity? groundItem = null;

        foreach (var entity in Entities.Values)
        {
            if ((entity.TileX != tileX) || (entity.TileY != tileY))
                continue;

            // Prefer clickable entities over ground items
            if (entity.Type is ClientEntityType.Aisling or ClientEntityType.Creature)
                return entity;

            groundItem ??= entity;
        }

        return groundItem;
    }

    /// <summary>
    ///     Returns the display name of an entity, or null if not tracked.
    /// </summary>
    public string? GetEntityName(uint id) => Entities.TryGetValue(id, out var entity) ? entity.Name : null;

    /// <summary>
    ///     Returns the first ground item at the specified tile, or null.
    /// </summary>
    public WorldEntity? GetGroundItemAt(int tileX, int tileY)
    {
        foreach (var entity in Entities.Values)
            if ((entity.Type == ClientEntityType.GroundItem) && (entity.TileX == tileX) && (entity.TileY == tileY))
                return entity;

        return null;
    }

    /// <summary>
    ///     Returns the player entity, or null if not yet tracked.
    /// </summary>
    public WorldEntity? GetPlayerEntity() => Entities.GetValueOrDefault(PlayerEntityId);

    /// <summary>
    ///     Returns all entities sorted by depth (TileX + TileY), then by TileX ascending. Reuses an internal buffer to avoid
    ///     per-frame allocation.
    /// </summary>
    public IReadOnlyList<WorldEntity> GetSortedEntities()
    {
        SortBuffer.Clear();
        SortBuffer.AddRange(Entities.Values);

        SortBuffer.Sort(static (a, b) =>
        {
            var depthCmp = a.SortDepth.CompareTo(b.SortDepth);

            return depthCmp != 0 ? depthCmp : a.TileX.CompareTo(b.TileX);
        });

        return SortBuffer;
    }

    /// <summary>
    ///     Handles another entity changing facing direction.
    /// </summary>
    public void HandleCreatureTurn(uint id, Direction direction)
    {
        if (!Entities.TryGetValue(id, out var entity))
            return;

        entity.Direction = direction;
    }

    /// <summary>
    ///     Handles another entity walking from oldPoint in a direction.
    /// </summary>
    public void HandleCreatureWalk(
        uint id,
        int oldX,
        int oldY,
        Direction direction,
        int? walkFrameCount = null)
    {
        if (!Entities.TryGetValue(id, out var entity))
            return;

        // Compute new position from oldPoint + direction
        (var dx, var dy) = direction.ToTileOffset();
        entity.TileX = oldX + dx;
        entity.TileY = oldY + dy;
        entity.Direction = direction;

        AnimationManager.StartWalk(entity, direction, walkFrameCount);
    }

    /// <summary>
    ///     Handles the player's own walk being confirmed by the server.
    /// </summary>
    public void HandlePlayerWalk(Direction direction, int oldX, int oldY)
    {
        if (!Entities.TryGetValue(PlayerEntityId, out var entity))
            return;

        (var dx, var dy) = direction.ToTileOffset();
        entity.TileX = oldX + dx;
        entity.TileY = oldY + dy;
        entity.Direction = direction;

        AnimationManager.StartWalk(entity, direction);
    }

    /// <summary>
    ///     Returns true if any blocking entity (aisling, non-WalkThrough creature) occupies the tile,
    ///     excluding the specified entity ID (typically the player).
    /// </summary>
    public bool HasBlockingEntityAt(int tileX, int tileY, uint excludeId)
    {
        foreach (var entity in Entities.Values)
        {
            if ((entity.TileX != tileX) || (entity.TileY != tileY) || (entity.Id == excludeId))
                continue;

            if (IsBlockingEntity(entity))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Returns true if there is a ground item at the specified tile.
    /// </summary>
    public bool HasGroundItemAt(int tileX, int tileY) => GetGroundItemAt(tileX, tileY) is not null;

    private static bool IsBlockingEntity(WorldEntity entity)
        => (entity.Type == ClientEntityType.Aisling)
           || ((entity.Type == ClientEntityType.Creature) && (entity.CreatureType != CreatureType.WalkThrough));

    /// <summary>
    ///     Removes an entity from tracking.
    /// </summary>
    public void RemoveEntity(uint id) => Entities.Remove(id);

    /// <summary>
    ///     Advances all active spell/effect animations by the given elapsed time.
    /// </summary>
    public void UpdateEffects(float elapsedMs)
    {
        for (var i = ActiveEffects.Count - 1; i >= 0; i--)
        {
            var effect = ActiveEffects[i];
            effect.ElapsedMs += elapsedMs;

            while (effect.ElapsedMs >= effect.FrameIntervalMs)
            {
                effect.CurrentFrame++;
                effect.ElapsedMs -= effect.FrameIntervalMs;
            }

            if (effect.IsComplete)
                ActiveEffects.RemoveAt(i);
        }

        for (var i = DyingEffects.Count - 1; i >= 0; i--)
        {
            var dying = DyingEffects[i];
            dying.Update(elapsedMs);

            if (dying.IsComplete)
            {
                dying.Dispose();
                DyingEffects.RemoveAt(i);
            }
        }
    }
}
#region
using Chaos.Client.Definitions;
using Chaos.Client.Models;
using Chaos.Client.Rendering;
using Chaos.DarkAges.Definitions;
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
    ///     Clears all tracked entities. Call on map change.
    /// </summary>
    public void Clear() => Entities.Clear();

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
        Direction direction)
    {
        if (!Entities.TryGetValue(id, out var entity))
            return;

        // Compute new position from oldPoint + direction
        (var dx, var dy) = direction switch
        {
            Direction.Up    => (0, -1),
            Direction.Right => (1, 0),
            Direction.Down  => (0, 1),
            Direction.Left  => (-1, 0),
            _               => (0, 0)
        };

        entity.TileX = oldX + dx;
        entity.TileY = oldY + dy;
        entity.Direction = direction;
    }

    /// <summary>
    ///     Handles the player's own walk being confirmed by the server.
    /// </summary>
    public void HandlePlayerWalk(Direction direction, int oldX, int oldY)
    {
        if (!Entities.TryGetValue(PlayerEntityId, out var entity))
            return;

        (var dx, var dy) = direction switch
        {
            Direction.Up    => (0, -1),
            Direction.Right => (1, 0),
            Direction.Down  => (0, 1),
            Direction.Left  => (-1, 0),
            _               => (0, 0)
        };

        entity.TileX = oldX + dx;
        entity.TileY = oldY + dy;
        entity.Direction = direction;
    }

    /// <summary>
    ///     Removes an entity from tracking.
    /// </summary>
    public void RemoveEntity(uint id) => Entities.Remove(id);
}
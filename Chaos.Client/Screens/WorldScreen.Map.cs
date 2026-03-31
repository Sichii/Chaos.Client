#region
using Chaos.Client.Collections;
using Chaos.Client.Data;
using Chaos.Client.Rendering;
using Chaos.Client.Systems;
using Chaos.Geometry;
using Chaos.Geometry.Abstractions;
using Chaos.Networking.Entities.Server;
using Chaos.Pathfinding;
using DALib.Cryptography;
using DALib.Data;
using DALib.Extensions;
using Pathfinder = Chaos.Pathfinding.Pathfinder;
using TileFlags = DALib.Definitions.TileFlags;
#endregion

namespace Chaos.Client.Screens;

public sealed partial class WorldScreen
{
    #region Map Assembly
    private void HandleUserId(uint id) => WorldState.PlayerEntityId = id;

    private void HandleMapInfo(MapInfoArgs args)
    {
        // Same map (refresh) — skip expensive teardown, just clear transient entity state
        if ((args.MapId == CurrentMapId) && MapFile is not null)
        {
            ClearTransientState();
            UpdateHuds(h => h.SetZoneName(args.Name));

            return;
        }

        // New map — dispose old caches, load fresh MapFile from local files
        MapRenderer.Dispose();
        MapRenderer = new MapRenderer();

        MapFile = LoadMapFile(
            args.MapId,
            args.Width,
            args.Height,
            args.CheckSum);
        MapPreloaded = false;
        AwaitingMapData = false;
        CurrentMapId = args.MapId;
        MapLoading.Show();

        // Local file missing, corrupt, or checksum mismatch — request from server
        if (MapFile is null)
        {
            MapFile = new MapFile(args.Width, args.Height);
            InitializeEmptyTiles(MapFile);
            AwaitingMapData = true;
            Game.Connection.RequestMapData();
        }

        // Clear entity + renderer caches for the new map
        ClearTransientState();
        Game.CreatureRenderer.Clear();
        Game.AislingRenderer.ClearCache();
        Game.AislingRenderer.ClearLayerCache();
        Game.ItemRenderer.Clear();

        // Reset darkness state and load HEA light map for the new map
        DarknessRenderer.OnMapChanged(args.MapId);

        UpdateHuds(h => h.SetZoneName(args.Name));
    }

    private void ClearTransientState()
    {
        WorldState.Clear();
        ClearAislingCache();
        Overlays.Clear();
        DebugRenderer.Clear();
        NpcSession.HideAll();
        Pathfinding.Clear();
        PendingWalks.Clear();
        GroupHighlightedIds.Clear();
        ClearGroupTintCache();
    }

    private void HandleMapData(MapDataArgs args)
    {
        if (MapFile is null)
            return;

        var y = args.CurrentYIndex;

        if (y >= MapFile.Height)
            return;

        // Each tile is 6 bytes: bg(2 BE), lfg(2 BE), rfg(2 BE)
        var data = args.MapData;
        var tileCount = Math.Min(data.Length / 6, MapFile.Width);

        for (var x = 0; x < tileCount; x++)
        {
            var offset = x * 6;
            var background = (short)((data[offset] << 8) | data[offset + 1]);
            var leftForeground = (short)((data[offset + 2] << 8) | data[offset + 3]);
            var rightForeground = (short)((data[offset + 4] << 8) | data[offset + 5]);

            MapFile.Tiles[x, y] = new MapTile
            {
                Background = background,
                LeftForeground = leftForeground,
                RightForeground = rightForeground
            };
        }

        // Last row received — save to disk and finalize
        if (AwaitingMapData && (y >= (MapFile.Height - 1)))
        {
            AwaitingMapData = false;
            SaveMapFile(CurrentMapId);
            FinalizeMapLoad();
        }
    }

    private void HandleMapLoadComplete()
    {
        // When awaiting server map data, ignore this — FinalizeMapLoad will be called from HandleMapData
        if (AwaitingMapData)
            return;

        FinalizeMapLoad();
    }

    private void FinalizeMapLoad()
    {
        if (MapFile is null)
            return;

        if (!MapPreloaded)
        {
            MapRenderer.PreloadMapTiles(Device, MapFile, MapLoading.SetProgress);
            TabMapRenderer.Generate(Device, MapFile);
            MapPathfinder = BuildPathfinder(MapFile);
            MapPreloaded = true;
        }

        MapLoading.Hide();
        FollowPlayerCamera();
    }

    private static Pathfinder BuildPathfinder(MapFile mapFile)
    {
        var sotpData = DataContext.Tiles.SotpData;
        var gndAttrs = DataContext.Tiles.GroundAttributes;
        var walls = new List<IPoint>();

        for (var y = 0; y < mapFile.Height; y++)
            for (var x = 0; x < mapFile.Width; x++)
            {
                var tile = mapFile.Tiles[x, y];

                if (IsTileWall(tile.LeftForeground, sotpData) || IsTileWall(tile.RightForeground, sotpData))
                    walls.Add(new Point(x, y));
                else if (gndAttrs.TryGetValue(tile.Background, out var gndAttr) && gndAttr.IsWalkBlocking)
                    walls.Add(new Point(x, y));
            }

        return new Pathfinder(
            new GridDetails
            {
                Width = mapFile.Width,
                Height = mapFile.Height,
                Walls = walls,
                BlockingReactors = []
            });
    }

    private bool TileHasForeground(int tileX, int tileY)
    {
        if (MapFile is null)
            return false;

        if ((tileX < 0) || (tileY < 0) || (tileX >= MapFile.Width) || (tileY >= MapFile.Height))
            return false;

        var tile = MapFile.Tiles[tileX, tileY];

        return tile.LeftForeground.IsRenderedTileIndex() || tile.RightForeground.IsRenderedTileIndex();
    }

    private static bool IsTileWall(int fgIndex, byte[] sotpData)
    {
        if (fgIndex <= 0)
            return false;

        var sotpIndex = fgIndex - 1;

        if (sotpIndex >= sotpData.Length)
            return false;

        return ((TileFlags)sotpData[sotpIndex]).HasFlag(TileFlags.Wall);
    }

    private bool IsTilePassable(int tileX, int tileY)
    {
        if (MapFile is null)
            return true;

        // Check wall tiles (foreground SOTP data)
        var tile = MapFile.Tiles[tileX, tileY];
        var sotpData = DataContext.Tiles.SotpData;

        if (IsTileWall(tile.LeftForeground, sotpData) || IsTileWall(tile.RightForeground, sotpData))
            return false;

        // Check gndattr walk-blocking (deep water tiles)
        if (DataContext.Tiles.GroundAttributes.TryGetValue(tile.Background, out var gndAttr) && gndAttr.IsWalkBlocking)
            return false;

        // Check entities at the destination tile
        if (WorldState.HasBlockingEntityAt(tileX, tileY, WorldState.PlayerEntityId))
            return false;

        return true;
    }

    private static MapFile? LoadMapFile(
        int mapId,
        int width,
        int height,
        ushort serverCheckSum)
    {
        var path = Path.Combine(DataContext.DataPath, "maps", $"lod{mapId}.map");

        if (!File.Exists(path))
            return null;

        try
        {
            var fileBytes = File.ReadAllBytes(path);

            if (fileBytes.Length != (width * height * 6))
                return null;

            if (CRC16.Calculate(fileBytes) != serverCheckSum)
                return null;

            // Parse in-place — file format is LE int16 x3 per tile, y-major x-minor
            var mapFile = new MapFile(width, height);
            var index = 0;

            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var background = (short)(fileBytes[index] | (fileBytes[index + 1] << 8));
                    var leftForeground = (short)(fileBytes[index + 2] | (fileBytes[index + 3] << 8));
                    var rightForeground = (short)(fileBytes[index + 4] | (fileBytes[index + 5] << 8));
                    index += 6;

                    mapFile.Tiles[x, y] = new MapTile
                    {
                        Background = background,
                        LeftForeground = leftForeground,
                        RightForeground = rightForeground
                    };
                }

            return mapFile;
        } catch
        {
            return null;
        }
    }

    private void SaveMapFile(int mapId)
    {
        var path = Path.Combine(DataContext.DataPath, "maps", $"lod{mapId}.map");
        MapFile!.Save(path);
    }

    private void HandleLocationChanged(int x, int y)
    {
        UpdateHuds(h => h.SetCoords(x, y));

        var player = WorldState.GetPlayerEntity();

        if (player is null)
            return;

        // If the server position matches, nothing to reconcile
        if ((player.TileX == x) && (player.TileY == y))
            return;

        // Server-authoritative position correction — clear all pending predictions and snap back
        PendingWalks.Clear();
        QueuedWalkDirection = null;
        player.TileX = x;
        player.TileY = y;
        WorldState.MarkSortDirty();
        AnimationSystem.ResetToIdle(player);
        Pathfinding.Clear();
        FollowPlayerCamera();
    }

    /// <summary>
    ///     Updates camera position to follow the player entity's visual position, including walk interpolation offset. In
    ///     rough scroll mode, only updates at fixed intervals for a choppier look.
    /// </summary>
    private void FollowPlayerCamera()
    {
        if (MapFile is null)
            return;

        var player = WorldState.GetPlayerEntity();

        if (player is null)
            return;

        var tileWorld = Camera.TileToWorld(player.TileX, player.TileY, MapFile.Height);
        Camera.Position = tileWorld + player.VisualOffset;
    }

    private static void InitializeEmptyTiles(MapFile mapFile)
    {
        for (var y = 0; y < mapFile.Height; y++)
            for (var x = 0; x < mapFile.Width; x++)
                mapFile.Tiles[x, y] = new MapTile();
    }
    #endregion
}
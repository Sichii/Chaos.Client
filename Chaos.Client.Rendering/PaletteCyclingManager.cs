#region
using Chaos.Client.Data;
using DALib.Data;
using DALib.Definitions;
using DALib.Drawing;
using DALib.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Rendering;

public sealed class PaletteCyclingManager : IDisposable
{
    private readonly Dictionary<int, CyclingSlot> BgSlots = new();
    private readonly Dictionary<int, Color[][]> BgVariants = new();
    private readonly Dictionary<int, CyclingSlot> FgSlots = new();
    private readonly Dictionary<int, Texture2D[]> FgVariants = new();
    private TextureAtlas? BgAtlas;
    private Dictionary<int, Texture2D>? FgCache;
    private int LastTick;

    public void Dispose()
    {
        // Remove cycling fg tiles from shared cache to prevent double-dispose by MapRenderer
        if (FgCache is not null)
            foreach (var tileId in FgVariants.Keys)
                FgCache.Remove(tileId);

        foreach (var variants in FgVariants.Values)
        {
            foreach (var texture in variants)
                texture.Dispose();
        }

        FgVariants.Clear();
        BgVariants.Clear();
        BgSlots.Clear();
        FgSlots.Clear();
        BgAtlas = null;
        FgCache = null;
    }

    private static bool AdvanceSlot(CyclingSlot slot)
    {
        slot.Counter++;

        if (slot.Counter < slot.Period)
            return false;

        slot.Counter = 0;
        slot.CurrentStep = (slot.CurrentStep + 1) % slot.TotalSteps;

        return true;
    }

    private static int ComputeTotalSteps(IReadOnlyList<PaletteCyclingEntry> entries)
    {
        var result = 1;

        foreach (var entry in entries)
        {
            var rangeLength = entry.EndIndex - entry.StartIndex + 1;
            result = Lcm(result, rangeLength);
        }

        return result;
    }

    private static void ExpandAnimatedTiles(
        Func<int, TileAnimationEntry?> getAnimation,
        HashSet<int> registered,
        Dictionary<int, List<int>> tilesByPalette)
    {
        var expansions = new List<(int PaletteNumber, int TileId)>();

        foreach ((var paletteNumber, var tileIds) in tilesByPalette)
        {
            foreach (var tileId in tileIds)
            {
                var anim = getAnimation(tileId);

                if (anim is null)
                    continue;

                foreach (var frameTileId in anim.TileSequence)
                    if (registered.Add(frameTileId))
                        expansions.Add((paletteNumber, frameTileId));
            }
        }

        foreach ((var paletteNumber, var tileId) in expansions)
            tilesByPalette[paletteNumber]
                .Add(tileId);
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
            (a, b) = (b, a % b);

        return a;
    }

    public void Initialize(MapFile mapFile, TextureAtlas? bgAtlas, Dictionary<int, Texture2D> fgCache)
    {
        BgAtlas = bgAtlas;
        FgCache = fgCache;

        var bgLookup = DataContext.Tiles.BackgroundPaletteLookup;
        var fgLookup = DataContext.Tiles.ForegroundPaletteLookup;

        var registeredBg = new HashSet<int>();
        var registeredFg = new HashSet<int>();
        var bgTilesByPalette = new Dictionary<int, List<int>>();
        var fgTilesByPalette = new Dictionary<int, List<int>>();

        for (var y = 0; y < mapFile.Height; y++)
        {
            for (var x = 0; x < mapFile.Width; x++)
            {
                var tile = mapFile.Tiles[x, y];

                if (tile.Background > 0)
                    TryRegisterTile(
                        tile.Background,
                        bgLookup,
                        registeredBg,
                        bgTilesByPalette);

                if (tile.LeftForeground.IsRenderedTileIndex())
                    TryRegisterTile(
                        tile.LeftForeground,
                        fgLookup,
                        registeredFg,
                        fgTilesByPalette);

                if (tile.RightForeground.IsRenderedTileIndex())
                    TryRegisterTile(
                        tile.RightForeground,
                        fgLookup,
                        registeredFg,
                        fgTilesByPalette);
            }
        }

        ExpandAnimatedTiles(DataContext.Tiles.GetBgAnimation, registeredBg, bgTilesByPalette);
        ExpandAnimatedTiles(DataContext.Tiles.GetFgAnimation, registeredFg, fgTilesByPalette);

        PreRenderBgVariants(bgLookup, bgTilesByPalette);
        PreRenderFgVariants(fgLookup, fgTilesByPalette);
    }

    private static int Lcm(int a, int b) => a / Gcd(a, b) * b;

    private void PreRenderBgVariants(PaletteLookup lookup, Dictionary<int, List<int>> tilesByPalette)
    {
        foreach ((var paletteNumber, var tileIds) in tilesByPalette)
        {
            if (tileIds.Count == 0)
                continue;

            var cycling = lookup.Table.GetCyclingEntries(paletteNumber);

            if (cycling is null)
                continue;

            if (!lookup.Palettes.TryGetValue(paletteNumber, out var originalPalette))
                continue;

            var totalSteps = ComputeTotalSteps(cycling);
            var period = cycling[0].Period;

            var tiles = new Dictionary<int, Tile>();

            foreach (var tileId in tileIds)
            {
                var palettized = DataContext.Tiles.GetBackgroundTile(tileId);

                if (palettized is not null)
                    tiles[tileId] = palettized.Entity;
            }

            for (var step = 0; step < totalSteps; step++)
            {
                var cycledPalette = originalPalette;

                foreach (var entry in cycling)
                    cycledPalette = cycledPalette.Cycle((byte)entry.StartIndex, (byte)entry.EndIndex, step);

                foreach ((var tileId, var tile) in tiles)
                {
                    if (!BgVariants.TryGetValue(tileId, out var variants))
                    {
                        variants = new Color[totalSteps][];
                        BgVariants[tileId] = variants;
                    }

                    using var image = Graphics.RenderTile(tile, cycledPalette);
                    using var texture = TextureConverter.ToTexture2D(image);

                    var buffer = new Color[CONSTANTS.TILE_WIDTH * CONSTANTS.TILE_HEIGHT];
                    texture.GetData(buffer);
                    variants[step] = buffer;
                }
            }

            BgSlots[paletteNumber] = new CyclingSlot
            {
                TotalSteps = totalSteps,
                Period = period,
                TileIds = [.. tileIds]
            };
        }
    }

    private void PreRenderFgVariants(PaletteLookup lookup, Dictionary<int, List<int>> tilesByPalette)
    {
        foreach ((var paletteNumber, var tileIds) in tilesByPalette)
        {
            if (tileIds.Count == 0)
                continue;

            var cycling = lookup.Table.GetCyclingEntries(paletteNumber);

            if (cycling is null)
                continue;

            if (!lookup.Palettes.TryGetValue(paletteNumber, out var originalPalette))
                continue;

            var totalSteps = ComputeTotalSteps(cycling);
            var period = cycling[0].Period;

            var hpfFiles = new Dictionary<int, HpfFile>();

            foreach (var tileId in tileIds)
            {
                var palettized = DataContext.Tiles.GetForegroundTile(tileId);

                if (palettized is not null)
                    hpfFiles[tileId] = palettized.Entity;
            }

            for (var step = 0; step < totalSteps; step++)
            {
                var cycledPalette = originalPalette;

                foreach (var entry in cycling)
                    cycledPalette = cycledPalette.Cycle((byte)entry.StartIndex, (byte)entry.EndIndex, step);

                foreach ((var tileId, var hpf) in hpfFiles)
                {
                    if (!FgVariants.TryGetValue(tileId, out var variants))
                    {
                        variants = new Texture2D[totalSteps];
                        FgVariants[tileId] = variants;
                    }

                    using var image = Graphics.RenderImage(hpf, cycledPalette);
                    variants[step] = TextureConverter.ToTexture2D(image);
                }
            }

            // Replace original textures in cache with step-0 variants (visually identical, now owned by us)
            foreach (var tileId in hpfFiles.Keys)
            {
                if (FgCache!.TryGetValue(tileId, out var original))
                    original.Dispose();

                FgCache[tileId] = FgVariants[tileId][0];
            }

            FgSlots[paletteNumber] = new CyclingSlot
            {
                TotalSteps = totalSteps,
                Period = period,
                TileIds = [.. tileIds]
            };
        }
    }

    private static void TryRegisterTile(
        int tileId,
        PaletteLookup lookup,
        HashSet<int> registered,
        Dictionary<int, List<int>> tilesByPalette)
    {
        if (!registered.Add(tileId))
            return;

        var paletteNumber = lookup.Table.GetPaletteNumber(tileId + 1);
        var cycling = lookup.Table.GetCyclingEntries(paletteNumber);

        if (cycling is null)
        {
            registered.Remove(tileId);

            return;
        }

        if (!tilesByPalette.TryGetValue(paletteNumber, out var list))
        {
            list = [];
            tilesByPalette[paletteNumber] = list;
        }

        list.Add(tileId);
    }

    public void Update(int animationTick)
    {
        if (animationTick == LastTick)
            return;

        LastTick = animationTick;

        foreach ((_, var slot) in BgSlots)
        {
            if (!AdvanceSlot(slot))
                continue;

            if (BgAtlas is null)
                continue;

            foreach (var tileId in slot.TileIds)
            {
                if (!BgVariants.TryGetValue(tileId, out var variants))
                    continue;

                var region = BgAtlas.TryGetRegion(tileId);

                if (region.HasValue)
                    region.Value.Atlas.SetData(
                        0,
                        region.Value.SourceRect,
                        variants[slot.CurrentStep],
                        0,
                        CONSTANTS.TILE_WIDTH * CONSTANTS.TILE_HEIGHT);
            }
        }

        // Fg path: zero GPU work, just swap cached texture pointers
        foreach ((_, var slot) in FgSlots)
        {
            if (!AdvanceSlot(slot))
                continue;

            foreach (var tileId in slot.TileIds)
            {
                if (!FgVariants.TryGetValue(tileId, out var variants))
                    continue;

                FgCache![tileId] = variants[slot.CurrentStep];
            }
        }
    }

    private sealed class CyclingSlot
    {
        public int Counter;
        public int CurrentStep;
        public required int Period;
        public required int[] TileIds;
        public required int TotalSteps;
    }
}
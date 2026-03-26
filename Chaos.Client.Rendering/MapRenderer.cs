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

public sealed class MapRenderer : IDisposable
{
    private readonly Dictionary<int, Texture2D> BgTextureCache = new();
    private readonly Dictionary<int, Texture2D> FgTextureCache = new();

    private TextureAtlas? BgAtlas;

    /// <summary>
    ///     Extra tile margin derived from the tallest foreground tile on the current map. Used by callers to expand visible
    ///     bounds for foreground culling.
    /// </summary>
    public int ForegroundExtraMargin { get; private set; }

    public void Dispose()
    {
        BgAtlas?.Dispose();
        BgAtlas = null;

        foreach (var texture in BgTextureCache.Values)
            texture.Dispose();

        foreach (var texture in FgTextureCache.Values)
            texture.Dispose();

        BgTextureCache.Clear();
        FgTextureCache.Clear();
    }

    private void BuildBgAtlas(GraphicsDevice device)
    {
        if (BgTextureCache.Count == 0)
            return;

        var atlas = new TextureAtlas(
            device,
            PackingMode.Grid,
            CONSTANTS.TILE_WIDTH,
            CONSTANTS.TILE_HEIGHT);

        foreach ((var tileId, var texture) in BgTextureCache)
            atlas.Add(tileId, texture);

        atlas.Build();

        // Dispose individual textures — the atlas owns the pixel data now
        foreach (var texture in BgTextureCache.Values)
            texture.Dispose();

        BgTextureCache.Clear();

        BgAtlas = atlas;
    }

    /// <summary>
    ///     Convenience method that draws background + foreground without entity interleaving. Foreground uses simple y-major
    ///     order (correct for maps without entities).
    /// </summary>
    public void Draw(
        SpriteBatch spriteBatch,
        MapFile mapFile,
        Camera camera,
        int animationTick)
    {
        DrawBackground(
            spriteBatch,
            mapFile,
            camera,
            animationTick);

        (var fgMinX, var fgMinY, var fgMaxX, var fgMaxY)
            = camera.GetVisibleTileBounds(mapFile.Width, mapFile.Height, ForegroundExtraMargin);

        for (var y = fgMinY; y <= fgMaxY; y++)
        {
            for (var x = fgMinX; x <= fgMaxX; x++)
                DrawForegroundTile(
                    spriteBatch,
                    mapFile,
                    camera,
                    x,
                    y,
                    animationTick);
        }
    }

    /// <summary>
    ///     Draws background tiles in y-major order (floor tiles, no overlap concerns).
    ///     Uses the background tile atlas when available for single-draw-call batching.
    /// </summary>
    public void DrawBackground(
        SpriteBatch spriteBatch,
        MapFile mapFile,
        Camera camera,
        int animationTick)
    {
        (var bgMinX, var bgMinY, var bgMaxX, var bgMaxY) = camera.GetVisibleTileBounds(mapFile.Width, mapFile.Height);

        for (var y = bgMinY; y <= bgMaxY; y++)
        {
            for (var x = bgMinX; x <= bgMaxX; x++)
            {
                var bgIndex = mapFile.Tiles[x, y].Background;

                if (bgIndex <= 0)
                    continue;

                bgIndex = ResolveAnimatedTileId(bgIndex, DataContext.Tiles.GetBgAnimation(bgIndex), animationTick);

                var worldPos = Camera.TileToWorld(x, y, mapFile.Height);
                var screenPos = camera.WorldToScreen(worldPos);

                if (((screenPos.X + CONSTANTS.TILE_WIDTH) <= 0)
                    || (screenPos.X >= camera.ViewportWidth)
                    || ((screenPos.Y + CONSTANTS.TILE_HEIGHT) <= 0)
                    || (screenPos.Y >= camera.ViewportHeight))
                    continue;

                // Prefer atlas path — all bg tiles in a single texture enables SpriteBatch batching
                if (BgAtlas is not null)
                {
                    var region = BgAtlas.TryGetRegion(bgIndex);

                    if (region.HasValue)
                    {
                        spriteBatch.Draw(
                            region.Value.Atlas,
                            screenPos,
                            region.Value.SourceRect,
                            Color.White);

                        continue;
                    }
                }

                // Fallback to individual texture
                var bgTexture = GetOrCreateBgTexture(bgIndex);

                if (bgTexture is not null)
                    spriteBatch.Draw(bgTexture, screenPos, Color.White);
            }
        }
    }

    /// <summary>
    ///     Draws the foreground tiles (left + right) at a specific tile position. Called by the game screen during diagonal
    ///     stripe iteration for correct draw ordering.
    /// </summary>
    public void DrawForegroundTile(
        SpriteBatch spriteBatch,
        MapFile mapFile,
        Camera camera,
        int x,
        int y,
        int animationTick)
    {
        var tile = mapFile.Tiles[x, y];
        var worldPos = Camera.TileToWorld(x, y, mapFile.Height);

        // Left foreground
        if (tile.LeftForeground.IsRenderedTileIndex())
        {
            var lfgTileId = ResolveAnimatedTileId(
                tile.LeftForeground,
                DataContext.Tiles.GetFgAnimation(tile.LeftForeground),
                animationTick);
            var lfgTexture = GetOrCreateFgTexture(lfgTileId);

            if (lfgTexture is not null)
            {
                var lfgWorldY = worldPos.Y + CONSTANTS.HALF_TILE_HEIGHT * 2 - lfgTexture.Height;

                var lfgScreenPos = camera.WorldToScreen(new Vector2(worldPos.X, lfgWorldY));

                if (IsOnScreen(lfgScreenPos, lfgTexture, camera))
                    spriteBatch.Draw(lfgTexture, lfgScreenPos, Color.White);
            }
        }

        // Right foreground
        if (tile.RightForeground.IsRenderedTileIndex())
        {
            var rfgTileId = ResolveAnimatedTileId(
                tile.RightForeground,
                DataContext.Tiles.GetFgAnimation(tile.RightForeground),
                animationTick);
            var rfgTexture = GetOrCreateFgTexture(rfgTileId);

            if (rfgTexture is not null)
            {
                var rfgWorldX = worldPos.X + CONSTANTS.HALF_TILE_WIDTH;
                var rfgWorldY = worldPos.Y + CONSTANTS.HALF_TILE_HEIGHT * 2 - rfgTexture.Height;
                var rfgScreenPos = camera.WorldToScreen(new Vector2(rfgWorldX, rfgWorldY));

                if (IsOnScreen(rfgScreenPos, rfgTexture, camera))
                    spriteBatch.Draw(rfgTexture, rfgScreenPos, Color.White);
            }
        }
    }

    private Texture2D? GetOrCreateBgTexture(int tileId)
    {
        if (BgTextureCache.TryGetValue(tileId, out var cached))
            return cached;

        var palettized = DataContext.Tiles.GetBackgroundTile(tileId);

        if (palettized is null)
            return null;

        using var image = Graphics.RenderTile(palettized.Entity, palettized.Palette);
        var texture = TextureConverter.ToTexture2D(image);
        BgTextureCache[tileId] = texture;

        return texture;
    }

    private Texture2D? GetOrCreateFgTexture(int tileId)
    {
        if (FgTextureCache.TryGetValue(tileId, out var cached))
            return cached;

        var palettized = DataContext.Tiles.GetForegroundTile(tileId);

        if (palettized is null)
            return null;

        using var image = Graphics.RenderImage(palettized.Entity, palettized.Palette);
        var texture = TextureConverter.ToTexture2D(image);
        FgTextureCache[tileId] = texture;

        return texture;
    }

    private static bool IsOnScreen(Vector2 screenPos, Texture2D texture, Camera camera)
        => ((screenPos.X + texture.Width) > 0)
           && (screenPos.X < camera.ViewportWidth)
           && ((screenPos.Y + texture.Height) > 0)
           && (screenPos.Y < camera.ViewportHeight);

    /// <summary>
    ///     Preloads all unique tile textures used by the map into GPU caches. Computes the foreground extra margin from the
    ///     tallest foreground tile. Builds a background tile atlas for batched rendering.
    /// </summary>
    public void PreloadMapTiles(GraphicsDevice device, MapFile mapFile)
    {
        var maxFgHeight = 0;

        for (var y = 0; y < mapFile.Height; y++)
        {
            for (var x = 0; x < mapFile.Width; x++)
            {
                var tile = mapFile.Tiles[x, y];

                // Background
                var bgIndex = tile.Background;

                if (bgIndex > 0)
                    GetOrCreateBgTexture(bgIndex);

                // Left foreground
                if (tile.LeftForeground.IsRenderedTileIndex())
                {
                    var lfgTexture = GetOrCreateFgTexture(tile.LeftForeground);

                    if (lfgTexture is not null && (lfgTexture.Height > maxFgHeight))
                        maxFgHeight = lfgTexture.Height;
                }

                // Right foreground
                if (tile.RightForeground.IsRenderedTileIndex())
                {
                    var rfgTexture = GetOrCreateFgTexture(tile.RightForeground);

                    if (rfgTexture is not null && (rfgTexture.Height > maxFgHeight))
                        maxFgHeight = rfgTexture.Height;
                }
            }
        }

        // Expand animated BG tiles: load all frames in each animation sequence into the cache
        var bgAnimEntries = new HashSet<TileAnimationEntry>(ReferenceEqualityComparer.Instance);

        foreach (var bgId in BgTextureCache.Keys.ToArray())
        {
            var anim = DataContext.Tiles.GetBgAnimation(bgId);

            if (anim is null || !bgAnimEntries.Add(anim))
                continue;

            foreach (var frameTileId in anim.TileSequence)
                GetOrCreateBgTexture(frameTileId);
        }

        // Expand animated FG tiles: load all frames, track max height for culling margin
        var fgAnimEntries = new HashSet<TileAnimationEntry>(ReferenceEqualityComparer.Instance);

        foreach (var fgId in FgTextureCache.Keys.ToArray())
        {
            var anim = DataContext.Tiles.GetFgAnimation(fgId);

            if (anim is null || !fgAnimEntries.Add(anim))
                continue;

            foreach (var frameTileId in anim.TileSequence)
            {
                var fgTexture = GetOrCreateFgTexture(frameTileId);

                if (fgTexture is not null && (fgTexture.Height > maxFgHeight))
                    maxFgHeight = fgTexture.Height;
            }
        }

        // Convert max pixel height to tile rows: each tile row = 14px
        ForegroundExtraMargin = (int)MathF.Ceiling(maxFgHeight / (float)CONSTANTS.HALF_TILE_HEIGHT);

        // Build background tile atlas from all preloaded tiles (includes animation frames)
        BuildBgAtlas(device);
    }

    /// <summary>
    ///     Resolves an animated tile to its current frame's tile ID. Returns the original ID if not animated.
    /// </summary>
    private static int ResolveAnimatedTileId(int tileId, TileAnimationEntry? anim, int animationTick)
    {
        if (anim is null)
            return tileId;

        var frameIndex = animationTick / (anim.AnimationIntervalMs / 100) % anim.TileSequence.Count;

        return anim.TileSequence[frameIndex];
    }
}
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

    /// <summary>
    ///     Extra tile margin derived from the tallest foreground tile on the current map. Used by callers to expand visible
    ///     bounds for foreground culling.
    /// </summary>
    public int ForegroundExtraMargin { get; private set; }

    public void Dispose()
    {
        foreach (var texture in BgTextureCache.Values)
            texture.Dispose();

        foreach (var texture in FgTextureCache.Values)
            texture.Dispose();

        BgTextureCache.Clear();
        FgTextureCache.Clear();
    }

    /// <summary>
    ///     Convenience method that draws background + foreground without entity interleaving. Foreground uses simple y-major
    ///     order (correct for maps without entities).
    /// </summary>
    public void Draw(SpriteBatch spriteBatch, MapFile mapFile, Camera camera)
    {
        DrawBackground(spriteBatch, mapFile, camera);

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
                    y);
        }
    }

    /// <summary>
    ///     Draws background tiles in y-major order (floor tiles, no overlap concerns).
    /// </summary>
    public void DrawBackground(SpriteBatch spriteBatch, MapFile mapFile, Camera camera)
    {
        var device = spriteBatch.GraphicsDevice;
        (var bgMinX, var bgMinY, var bgMaxX, var bgMaxY) = camera.GetVisibleTileBounds(mapFile.Width, mapFile.Height);

        for (var y = bgMinY; y <= bgMaxY; y++)
        {
            for (var x = bgMinX; x <= bgMaxX; x++)
            {
                var bgIndex = mapFile.Tiles[x, y].Background;
                var bgTileId = bgIndex > 0 ? bgIndex : 1;

                var bgTexture = GetOrCreateBgTexture(device, bgTileId);

                if (bgTexture is null)
                    continue;

                var worldPos = Camera.TileToWorld(x, y, mapFile.Height);
                var screenPos = camera.WorldToScreen(worldPos);

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
        int y)
    {
        var device = spriteBatch.GraphicsDevice;
        var tile = mapFile.Tiles[x, y];
        var worldPos = Camera.TileToWorld(x, y, mapFile.Height);

        // Left foreground
        if (tile.LeftForeground.IsRenderedTileIndex())
        {
            var lfgTexture = GetOrCreateFgTexture(device, tile.LeftForeground);

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
            var rfgTexture = GetOrCreateFgTexture(device, tile.RightForeground);

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

    private Texture2D? GetOrCreateBgTexture(GraphicsDevice device, int tileId)
    {
        if (BgTextureCache.TryGetValue(tileId, out var cached))
            return cached;

        var palettized = DataContext.Tiles.GetBackgroundTile(tileId);

        if (palettized is null)
            return null;

        using var image = Graphics.RenderTile(palettized.Entity, palettized.Palette);
        var texture = TextureConverter.ToTexture2D(device, image);
        BgTextureCache[tileId] = texture;

        return texture;
    }

    private Texture2D? GetOrCreateFgTexture(GraphicsDevice device, int tileId)
    {
        if (FgTextureCache.TryGetValue(tileId, out var cached))
            return cached;

        var palettized = DataContext.Tiles.GetForegroundTile(tileId);

        if (palettized is null)
            return null;

        using var image = Graphics.RenderImage(palettized.Entity, palettized.Palette);
        var texture = TextureConverter.ToTexture2D(device, image);
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
    ///     tallest foreground tile.
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
                var bgTileId = bgIndex > 0 ? bgIndex : 1;
                GetOrCreateBgTexture(device, bgTileId);

                // Left foreground
                if (tile.LeftForeground.IsRenderedTileIndex())
                {
                    var lfgTexture = GetOrCreateFgTexture(device, tile.LeftForeground);

                    if (lfgTexture is not null && (lfgTexture.Height > maxFgHeight))
                        maxFgHeight = lfgTexture.Height;
                }

                // Right foreground
                if (tile.RightForeground.IsRenderedTileIndex())
                {
                    var rfgTexture = GetOrCreateFgTexture(device, tile.RightForeground);

                    if (rfgTexture is not null && (rfgTexture.Height > maxFgHeight))
                        maxFgHeight = rfgTexture.Height;
                }
            }
        }

        // Convert max pixel height to tile rows: each tile row = 14px
        ForegroundExtraMargin = (int)MathF.Ceiling(maxFgHeight / (float)CONSTANTS.HALF_TILE_HEIGHT);
    }
}
#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Models;
using Chaos.Client.Rendering.Models;
using DALib.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Renders debug overlays for the world viewport: foreground tile outlines, entity tile rects with color coding,
///     entity click-detection hitboxes, player crosshair, mouse hover tile highlight, and per-entity name/position labels.
///     All visualization is opt-in via DebugOverlay.IsActive.
/// </summary>
public sealed class WorldDebugRenderer
{
    private readonly Dictionary<uint, TextElement> LabelCache = new();
    private readonly List<(TextElement Text, Vector2 Position)> PendingLabels = new();

    /// <summary>
    ///     Clears all cached debug labels. Call on map change or unload.
    /// </summary>
    public void Clear() => LabelCache.Clear();

    /// <summary>
    ///     Draws all debug overlays. Call within a SpriteBatch Begin/End block with camera transform applied.
    /// </summary>
    public void Draw(
        SpriteBatch spriteBatch,
        Camera camera,
        MapFile mapFile,
        int foregroundExtraMargin,
        IReadOnlyList<WorldEntity> sortedEntities,
        WorldEntity? player,
        IReadOnlyList<EntityHitBox> entityHitBoxes,
        int mouseX,
        int mouseY,
        Rectangle viewportBounds)
    {
        PendingLabels.Clear();

        var pixel = UIElement.GetPixel();

        DrawForegroundTileOutlines(
            spriteBatch,
            pixel,
            camera,
            mapFile,
            foregroundExtraMargin);

        DrawEntityTileRects(
            spriteBatch,
            pixel,
            camera,
            mapFile,
            sortedEntities);

        DrawPlayerCrosshair(
            spriteBatch,
            pixel,
            camera,
            mapFile,
            player);

        DrawMouseHoverTile(
            spriteBatch,
            pixel,
            camera,
            mapFile,
            mouseX,
            mouseY,
            viewportBounds);
        DrawEntityClickHitboxes(spriteBatch, pixel, entityHitBoxes);

        //deferred entity debug labels — drawn after all pixel-texture geometry to minimize batch breaks
        foreach ((var text, var pos) in PendingLabels)
            text.Draw(spriteBatch, pos);
    }

    private static void DrawEntityClickHitboxes(SpriteBatch spriteBatch, Texture2D pixel, IReadOnlyList<EntityHitBox> entityHitBoxes)
    {
        for (var i = 0; i < entityHitBoxes.Count; i++)
            DrawRectOutline(
                spriteBatch,
                pixel,
                entityHitBoxes[i].ScreenRect,
                Color.Orange * 0.8f);
    }

    private void DrawEntityTileRects(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        Camera camera,
        MapFile mapFile,
        IReadOnlyList<WorldEntity> sortedEntities)
    {
        for (var i = 0; i < sortedEntities.Count; i++)
        {
            var entity = sortedEntities[i];
            var tileWorld = Camera.TileToWorld(entity.TileX, entity.TileY, mapFile.Height);
            var tileCenterX = tileWorld.X + DaLibConstants.HALF_TILE_WIDTH;
            var topLeft = camera.WorldToScreen(new Vector2(tileWorld.X, tileWorld.Y));

            var tileRect = new Rectangle(
                (int)topLeft.X,
                (int)topLeft.Y,
                DaLibConstants.HALF_TILE_WIDTH * 2,
                DaLibConstants.HALF_TILE_HEIGHT * 2);

            var color = entity.Type switch
            {
                ClientEntityType.Aisling    => Color.Lime,
                ClientEntityType.Creature   => Color.Red,
                ClientEntityType.GroundItem => Color.Yellow,
                _                           => Color.White
            };

            DrawRectOutline(
                spriteBatch,
                pixel,
                tileRect,
                color * 0.6f);
            spriteBatch.Draw(pixel, tileRect, color * 0.15f);

            //entity name/info label (cached, deferred to draw after all pixel-texture geometry)
            var label = $"{entity.Name} [{entity.Id}] ({entity.TileX},{entity.TileY})";

            if (!LabelCache.TryGetValue(entity.Id, out var cachedLabel))
            {
                cachedLabel = new TextElement();
                LabelCache[entity.Id] = cachedLabel;
            }

            cachedLabel.Update(label, color);

            if (cachedLabel.HasContent)
            {
                var labelPos = camera.WorldToScreen(new Vector2(tileCenterX - cachedLabel.Width / 2f, tileWorld.Y - 12));
                PendingLabels.Add((cachedLabel, labelPos));
            }
        }
    }

    private void DrawForegroundTileOutlines(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        Camera camera,
        MapFile mapFile,
        int foregroundExtraMargin)
    {
        (var fgMinX, var fgMinY, var fgMaxX, var fgMaxY) = camera.GetVisibleTileBounds(
            mapFile.Width,
            mapFile.Height,
            foregroundExtraMargin);

        for (var tileY = fgMinY; tileY <= fgMaxY; tileY++)
            for (var tileX = fgMinX; tileX <= fgMaxX; tileX++)
            {
                var tile = mapFile.Tiles[tileX, tileY];

                if (tile is { LeftForeground: 0, RightForeground: 0 })
                    continue;

                var tileWorld = Camera.TileToWorld(tileX, tileY, mapFile.Height);
                var topLeft = camera.WorldToScreen(new Vector2(tileWorld.X, tileWorld.Y));

                var tileRect = new Rectangle(
                    (int)topLeft.X,
                    (int)topLeft.Y,
                    DaLibConstants.HALF_TILE_WIDTH * 2,
                    DaLibConstants.HALF_TILE_HEIGHT * 2);

                DrawRectOutline(
                    spriteBatch,
                    pixel,
                    tileRect,
                    Color.Cyan * 0.3f);
            }
    }

    private static void DrawMouseHoverTile(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        Camera camera,
        MapFile mapFile,
        int mouseX,
        int mouseY,
        Rectangle viewportBounds)
    {
        var worldPos = camera.ScreenToWorld(new Vector2(mouseX - viewportBounds.X, mouseY - viewportBounds.Y));
        var tile = Camera.WorldToTile(worldPos.X, worldPos.Y, mapFile.Height);
        var hoverTileX = tile.X;
        var hoverTileY = tile.Y;

        if ((hoverTileX < 0) || (hoverTileX >= mapFile.Width) || (hoverTileY < 0) || (hoverTileY >= mapFile.Height))
            return;

        var hoverWorld = Camera.TileToWorld(hoverTileX, hoverTileY, mapFile.Height);
        var hoverScreen = camera.WorldToScreen(new Vector2(hoverWorld.X, hoverWorld.Y));

        var hoverRect = new Rectangle(
            (int)hoverScreen.X,
            (int)hoverScreen.Y,
            DaLibConstants.HALF_TILE_WIDTH * 2,
            DaLibConstants.HALF_TILE_HEIGHT * 2);

        spriteBatch.Draw(pixel, hoverRect, Color.Magenta * 0.3f);

        DrawRectOutline(
            spriteBatch,
            pixel,
            hoverRect,
            Color.Magenta);
    }

    private static void DrawPlayerCrosshair(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        Camera camera,
        MapFile mapFile,
        WorldEntity? player)
    {
        if (player is null)
            return;

        var playerWorld = Camera.TileToWorld(player.TileX, player.TileY, mapFile.Height);

        var playerCenter = camera.WorldToScreen(
            new Vector2(
                playerWorld.X + DaLibConstants.HALF_TILE_WIDTH + player.VisualOffset.X,
                playerWorld.Y + DaLibConstants.HALF_TILE_HEIGHT + player.VisualOffset.Y));

        spriteBatch.Draw(
            pixel,
            new Rectangle(
                (int)playerCenter.X - 5,
                (int)playerCenter.Y,
                11,
                1),
            Color.White);

        spriteBatch.Draw(
            pixel,
            new Rectangle(
                (int)playerCenter.X,
                (int)playerCenter.Y - 5,
                1,
                11),
            Color.White);
    }

    private static void DrawRectOutline(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        Rectangle rect,
        Color color)
    {
        spriteBatch.Draw(
            pixel,
            new Rectangle(
                rect.X,
                rect.Y,
                rect.Width,
                1),
            color);

        spriteBatch.Draw(
            pixel,
            new Rectangle(
                rect.X,
                rect.Bottom - 1,
                rect.Width,
                1),
            color);

        spriteBatch.Draw(
            pixel,
            new Rectangle(
                rect.X,
                rect.Y,
                1,
                rect.Height),
            color);

        spriteBatch.Draw(
            pixel,
            new Rectangle(
                rect.Right - 1,
                rect.Y,
                1,
                rect.Height),
            color);
    }

    /// <summary>
    ///     Removes a single entity's cached debug label. Call when an entity is removed from the world.
    /// </summary>
    public void RemoveEntity(uint entityId) => LabelCache.Remove(entityId);
}
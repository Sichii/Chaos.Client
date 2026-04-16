#region
using Chaos.Client.Data;
using DALib.Drawing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Renders and caches item sprites for ground display. Separate from UiRenderer's permanent icon cache — these
///     textures are evicted on map change. Stores frame offset metadata (Left/Top) for proper visual centering.
///     Cache key includes both sprite ID and dye color.
/// </summary>
public sealed class ItemRenderer : IDisposable
{
    private readonly Dictionary<(int SpriteId, byte Color), ItemSprite?> SpriteCache = [];

    public void Dispose() => Clear();

    /// <summary>
    ///     Disposes all cached textures and clears the cache. Call on map change.
    /// </summary>
    public void Clear()
    {
        foreach (var sprite in SpriteCache.Values)
            sprite?.Texture.Dispose();

        SpriteCache.Clear();
    }

    /// <summary>
    ///     Draws a ground item sprite centered on the tile, using visual content bounds to ignore transparent padding.
    /// </summary>
    public void Draw(
        SpriteBatch batch,
        Camera camera,
        int spriteId,
        byte color,
        float tileCenterX,
        float tileCenterY)
    {
        var sprite = GetSprite(spriteId, color);

        if (sprite is null)
            return;

        var texture = sprite.Value.Texture;

        var contentWidth = texture.Width - sprite.Value.FrameLeft;
        var contentHeight = texture.Height - sprite.Value.FrameTop;
        var contentCenterX = sprite.Value.FrameLeft + contentWidth / 2f;
        var contentCenterY = sprite.Value.FrameTop + contentHeight / 2f;
        var drawX = tileCenterX - contentCenterX;
        var drawY = tileCenterY - contentCenterY;
        var screenPos = camera.WorldToScreen(new Vector2(drawX, drawY));

        batch.Draw(texture, screenPos, Color.White);
    }

    /// <summary>
    ///     Returns the ground item sprite for the given sprite ID and dye color. Applies palette dye if color is non-zero.
    /// </summary>
    public ItemSprite? GetSprite(int spriteId, byte color = 0)
    {
        var key = (spriteId, color);

        if (SpriteCache.TryGetValue(key, out var cached))
            return cached;

        var sprite = LoadSprite(spriteId, color);
        SpriteCache[key] = sprite;

        return sprite;
    }

    private static ItemSprite? LoadSprite(int spriteId, byte color)
    {
        var palettized = DataContext.PanelSprites.GetItemSprite(spriteId);

        if (palettized is null)
            return null;

        var palette = palettized.Palette;

        //apply dye color if specified
        if ((color > 0) && DataContext.AislingDrawData.DyeColorTable.Contains(color))
            palette = palette.Dye(DataContext.AislingDrawData.DyeColorTable[color]);

        using var image = Graphics.RenderImage(palettized.Entity, palette);

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (image is null)
            return null;

        var texture = TextureConverter.ToTexture2D(image);

        return new ItemSprite(texture, palettized.Entity.Left, palettized.Entity.Top);
    }

    /// <summary>
    ///     A ground item sprite texture with its EPF frame's Left/Top transparent padding offsets, used to compute visual
    ///     content bounds for tile-centered drawing.
    /// </summary>
    public readonly record struct ItemSprite(Texture2D Texture, int FrameLeft, int FrameTop);
}
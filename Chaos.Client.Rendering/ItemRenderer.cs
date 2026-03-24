#region
using Chaos.Client.Data;
using DALib.Drawing;
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
    private readonly Dictionary<(int SpriteId, byte Color), ItemSprite?> SpriteCache = new();

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
    ///     Returns the cached item sprite for the given sprite ID and color, loading and caching on first access.
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
        var palettized = DataContext.PanelItems.GetPanelItemSprite(spriteId);

        if (palettized is null)
            return null;

        var palette = palettized.Palette;

        // Apply dye color if specified
        if ((color > 0) && DataContext.AislingData.DyeColorTable.Contains(color))
            palette = palette.Dye(DataContext.AislingData.DyeColorTable[color]);

        using var image = Graphics.RenderImage(palettized.Entity, palette);

        if (image is null)
            return null;

        var texture = TextureConverter.ToTexture2D(image);

        return new ItemSprite(texture, palettized.Entity.Left, palettized.Entity.Top);
    }

    /// <summary>
    ///     An item sprite with frame offset metadata for centering.
    /// </summary>
    public readonly record struct ItemSprite(Texture2D Texture, int FrameLeft, int FrameTop);
}
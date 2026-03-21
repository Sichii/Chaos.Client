#region
using Chaos.Client.Data;
using DALib.Drawing;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Renders and caches item sprites for ground display. Separate from UiRenderer's permanent icon cache — these
///     textures are evicted on map change. Stores frame offset metadata (Left/Top) for proper visual centering.
/// </summary>
public sealed class ItemRenderer : IDisposable
{
    private readonly Dictionary<int, ItemSprite?> SpriteCache = new();

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
    ///     Returns the cached item sprite for the given sprite ID, loading and caching on first access.
    /// </summary>
    public ItemSprite? GetSprite(GraphicsDevice device, int spriteId)
    {
        if (SpriteCache.TryGetValue(spriteId, out var cached))
            return cached;

        var sprite = LoadSprite(device, spriteId);
        SpriteCache[spriteId] = sprite;

        return sprite;
    }

    private static ItemSprite? LoadSprite(GraphicsDevice device, int spriteId)
    {
        var palettized = DataContext.PanelItems.GetPanelItemSprite(spriteId);

        if (palettized is null)
            return null;

        using var image = Graphics.RenderImage(palettized.Entity, palettized.Palette);

        if (image is null)
            return null;

        var texture = TextureConverter.ToTexture2D(device, image);

        return new ItemSprite(texture, palettized.Entity.Left, palettized.Entity.Top);
    }

    /// <summary>
    ///     An item sprite with frame offset metadata for centering.
    /// </summary>
    /// <param name="Texture">
    ///     The rendered item texture (includes Left/Top transparent padding from SimpleRender).
    /// </param>
    /// <param name="FrameLeft">
    ///     The X offset of the visual content within the texture.
    /// </param>
    /// <param name="FrameTop">
    ///     The Y offset of the visual content within the texture.
    /// </param>
    public readonly record struct ItemSprite(Texture2D Texture, int FrameLeft, int FrameTop);
}
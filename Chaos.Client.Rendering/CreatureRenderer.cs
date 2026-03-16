#region
using Chaos.Client.Data;
using DALib.Drawing;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Renders creature/NPC sprites from MpfFile data with a per-frame Texture2D cache. Cached frames are keyed by
///     (spriteId, frameIndex). Call <see cref="Clear" /> on map change to evict all cached textures.
/// </summary>
public sealed class CreatureRenderer : IDisposable
{
    private readonly Dictionary<(int SpriteId, int FrameIndex), SpriteFrame> FrameCache = new();

    /// <inheritdoc />
    public void Dispose() => Clear();

    /// <summary>
    ///     Disposes all cached textures and clears the cache.
    /// </summary>
    public void Clear()
    {
        foreach (var frame in FrameCache.Values)
            frame.Dispose();

        FrameCache.Clear();
    }

    /// <summary>
    ///     Gets the MpfFile animation metadata for a creature sprite (frame counts, indices). Returns null if the sprite
    ///     cannot be loaded.
    /// </summary>
    public CreatureAnimInfo? GetAnimInfo(int spriteId)
    {
        var palettized = DataContext.CreatureSprites.GetCreatureSprite(spriteId);

        if (palettized is null)
            return null;

        var mpf = palettized.Entity;

        return new CreatureAnimInfo(
            mpf.WalkFrameIndex,
            mpf.WalkFrameCount,
            mpf.AttackFrameIndex,
            mpf.AttackFrameCount,
            mpf.StandingFrameIndex,
            mpf.StandingFrameCount,
            mpf.Count);
    }

    /// <summary>
    ///     Gets or creates a cached SpriteFrame for the given creature sprite and frame index. Returns null if the sprite or
    ///     frame cannot be loaded.
    /// </summary>
    public SpriteFrame? GetFrame(GraphicsDevice device, int spriteId, int frameIndex)
    {
        var key = (spriteId, frameIndex);

        if (FrameCache.TryGetValue(key, out var cached))
            return cached;

        var palettized = DataContext.CreatureSprites.GetCreatureSprite(spriteId);

        if (palettized is null)
            return null;

        var mpf = palettized.Entity;

        if ((frameIndex < 0) || (frameIndex >= mpf.Count))
            return null;

        var mpfFrame = mpf[frameIndex];

        using var image = Graphics.RenderImage(mpfFrame, palettized.Palette);

        if (image is null)
            return null;

        var texture = TextureConverter.ToTexture2D(device, image);

        var spriteFrame = new SpriteFrame(
            texture,
            mpfFrame.CenterX,
            mpfFrame.CenterY,
            mpfFrame.Left,
            mpfFrame.Top);

        FrameCache[key] = spriteFrame;

        return spriteFrame;
    }
}

/// <summary>
///     Animation metadata from an MpfFile header. Used to determine which frames correspond to which animation states.
/// </summary>
public readonly record struct CreatureAnimInfo(
    byte WalkFrameIndex,
    byte WalkFrameCount,
    byte AttackFrameIndex,
    byte AttackFrameCount,
    byte StandingFrameIndex,
    byte StandingFrameCount,
    int TotalFrameCount);
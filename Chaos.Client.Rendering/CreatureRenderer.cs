#region
using Chaos.Client.Data;
using Chaos.Client.Rendering.Models;
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
            mpf.Attack2StartIndex,
            mpf.Attack2FrameCount,
            mpf.Attack3StartIndex,
            mpf.Attack3FrameCount,
            mpf.StandingFrameIndex,
            mpf.StandingFrameCount,
            mpf.OptionalAnimationFrameCount,
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

    /// <summary>
    ///     Returns the walk frame count for a creature sprite, or null if the sprite cannot be loaded.
    /// </summary>
    public int? GetWalkFrameCount(int spriteId)
    {
        var info = GetAnimInfo(spriteId);

        return info?.WalkFrameCount;
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
    byte Attack2StartIndex,
    byte Attack2FrameCount,
    byte Attack3StartIndex,
    byte Attack3FrameCount,
    byte StandingFrameIndex,
    byte StandingFrameCount,
    byte OptionalAnimationFrameCount,
    int TotalFrameCount);
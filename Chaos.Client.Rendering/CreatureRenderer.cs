#region
using Chaos.Client.Data;
using Chaos.Client.Rendering.Models;
using DALib.Drawing;
using Microsoft.Xna.Framework;
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
    private readonly Dictionary<Texture2D, Texture2D> GroupTintCache = new();
    private readonly Dictionary<Texture2D, Texture2D> HighlightTintCache = new();

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
        ClearTintCaches();
    }

    /// <summary>
    ///     Disposes and clears all cached tint textures.
    /// </summary>
    public void ClearTintCaches()
    {
        foreach (var texture in HighlightTintCache.Values)
            texture.Dispose();

        HighlightTintCache.Clear();

        foreach (var texture in GroupTintCache.Values)
            texture.Dispose();

        GroupTintCache.Clear();
    }

    /// <summary>
    ///     Draws a creature sprite at the given tile center, applying the requested tint. Returns the screen-space Y of the
    ///     texture bottom edge, or 0 if the sprite could not be drawn.
    /// </summary>
    public int Draw(
        SpriteBatch batch,
        Camera camera,
        int spriteId,
        int frameIndex,
        bool flip,
        float tileCenterX,
        float tileCenterY,
        Vector2 visualOffset,
        EntityTintType tint)
    {
        var spriteFrame = GetFrame(spriteId, frameIndex);

        if (spriteFrame is null)
            return 0;

        var frame = spriteFrame.Value;

        var texCenterX = frame.CenterX - Math.Min(0, (int)frame.Left);
        var texCenterY = frame.CenterY - Math.Min(0, (int)frame.Top);
        var anchorX = flip ? frame.Texture.Width - texCenterX : texCenterX;

        var drawX = tileCenterX + visualOffset.X - anchorX;
        var drawY = tileCenterY + visualOffset.Y - texCenterY;
        var screenPos = camera.WorldToScreen(new Vector2(drawX, drawY));

        var effects = flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

        var drawTexture = tint switch
        {
            EntityTintType.Highlight => GetOrCreateHighlightTint(frame.Texture),
            EntityTintType.Group     => GetOrCreateGroupTint(frame.Texture),
            _                        => frame.Texture
        };

        batch.Draw(
            drawTexture,
            screenPos,
            null,
            Color.White,
            0f,
            Vector2.Zero,
            1f,
            effects,
            0f);

        return (int)screenPos.Y + frame.Texture.Height;
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
            mpf.OptionalAnimationRatio,
            mpf.Count);
    }

    /// <summary>
    ///     Gets or creates a cached SpriteFrame for the given creature sprite and frame index. Returns null if the sprite or
    ///     frame cannot be loaded.
    /// </summary>
    public SpriteFrame? GetFrame(int spriteId, int frameIndex)
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

        var texture = TextureConverter.ToTexture2D(image);

        var spriteFrame = new SpriteFrame(
            texture,
            mpfFrame.CenterX,
            mpfFrame.CenterY,
            mpfFrame.Left,
            mpfFrame.Top);

        FrameCache[key] = spriteFrame;

        return spriteFrame;
    }

    private Texture2D GetOrCreateGroupTint(Texture2D source)
    {
        if (GroupTintCache.TryGetValue(source, out var cached))
            return cached;

        cached = TextureConverter.CreateGroupTintedTexture(source);
        GroupTintCache[source] = cached;

        return cached;
    }

    private Texture2D GetOrCreateHighlightTint(Texture2D source)
    {
        if (HighlightTintCache.TryGetValue(source, out var cached))
            return cached;

        cached = TextureConverter.CreateTintedTexture(source);
        HighlightTintCache[source] = cached;

        return cached;
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
    byte OptionalAnimationRatio,
    int TotalFrameCount);
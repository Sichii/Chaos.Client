#region
using Chaos.Client.Data;
using Chaos.Client.Rendering.Models;
using Chaos.Client.Rendering.Utility;
using DALib.Definitions;
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
    private readonly Dictionary<(int SpriteId, int FrameIndex), SpriteFrame> FrameCache = [];
    private readonly Dictionary<Texture2D, Texture2D> GroupTintCache = [];
    private readonly Dictionary<Texture2D, Texture2D> HighlightTintCache = [];
    private readonly Dictionary<Texture2D, Texture2D> HitTintCache = [];

    //keyed by (source frame texture, paint height, packed argb) — one entry per unique gndattr/tile combo per frame. Cleared on map change.
    private readonly Dictionary<(Texture2D Source, int PaintHeight, uint PackedColor), Texture2D> GroundTintCache = [];

    //average of (CenterY - Top) across all frames, keyed by spriteId.
    //used by overlay positioning to derive a stable "sprite top" for each creature sprite.
    //uses the frame's visible top row, which differs from the bitmap top row when Top > 0 (transparent padding).
    private readonly Dictionary<int, int> AverageTopOffsetCache = [];

    /// <inheritdoc />
    public void Dispose() => Clear();

    /// <summary>
    ///     Disposes all cached frame and tint textures. Call on map change.
    /// </summary>
    public void Clear()
    {
        FrameCache.DisposeAndClear();
        AverageTopOffsetCache.Clear();
        GroundTintCache.DisposeAndClear();
        ClearTintCaches();
    }

    /// <summary>
    ///     Disposes all cached highlight and group tint textures. Call when tint state changes (e.g., hovered entity changes).
    /// </summary>
    public void ClearTintCaches()
    {
        HighlightTintCache.DisposeAndClear();
        GroupTintCache.DisposeAndClear();
        HitTintCache.DisposeAndClear();
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
        EntityTintType tint,
        int groundPaintHeight,
        Color groundTintColor,
        float alpha = 1f)
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

        if (alpha > 0)
        {
            var effects = flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            //ground tint baked into a per-(source, paintHeight, color) cache first; entity tint chains on top of the result.
            var sourceTexture = frame.Texture;

            if (groundPaintHeight > 0)
            {
                var key = (Source: frame.Texture, PaintHeight: groundPaintHeight, PackedColor: groundTintColor.PackedValue);

                if (!GroundTintCache.TryGetValue(key, out var groundTinted))
                {
                    groundTinted = ImageUtil.BuildGroundTinted(
                        TextureConverter.Device,
                        frame.Texture,
                        groundPaintHeight,
                        frame.CenterY,
                        groundTintColor);
                    GroundTintCache[key] = groundTinted;
                }

                sourceTexture = groundTinted;
            }

            var drawTexture = tint switch
            {
                EntityTintType.Highlight => HighlightTintCache.GetOrAdd(sourceTexture, static src => ImageUtil.BuildHoverTinted(TextureConverter.Device, src)),
                EntityTintType.Group     => GroupTintCache.GetOrAdd(sourceTexture, static src => ImageUtil.BuildGroupTinted(TextureConverter.Device, src)),
                EntityTintType.HitTint   => HitTintCache.GetOrAdd(sourceTexture, static src => ImageUtil.BuildHitTinted(TextureConverter.Device, src)),
                _                        => sourceTexture
            };

            batch.Draw(
                drawTexture,
                screenPos,
                null,
                Color.White * alpha,
                0f,
                Vector2.Zero,
                1f,
                effects,
                0f);
        }

        return (int)screenPos.Y + frame.Texture.Height;
    }

    /// <summary>
    ///     Returns the average height above the entity anchor reached by this sprite's frames, in pixels. Used by overlay
    ///     positioning to derive a stable "sprite top" that accounts for frame-to-frame variance without per-frame jitter.
    ///     Cached per spriteId after the first call. Returns 0 if the sprite cannot be loaded.
    /// </summary>
    public int GetAverageTopOffset(int spriteId)
    {
        if (AverageTopOffsetCache.TryGetValue(spriteId, out var cached))
            return cached;

        var palettized = DataContext.CreatureSprites.GetCreatureSprite(spriteId);

        if (palettized is null)
            return 0;

        var mpf = palettized.Entity;

        if (mpf.Count == 0)
            return 0;

        var sum = 0;

        for (var i = 0; i < mpf.Count; i++)
        {
            var frame = mpf[i];
            sum += frame.CenterY - frame.Top;
        }

        var average = sum / mpf.Count;
        AverageTopOffsetCache[spriteId] = average;

        return average;
    }

    /// <summary>
    ///     Returns animation metadata for a creature sprite — walk/attack/standing frame ranges and total frame count. Returns
    ///     null if the sprite cannot be loaded.
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
            mpf.OptionalAnimationProbability,
            mpf.AnimationIntervalMs,
            mpf.Count);
    }

    /// <summary>
    ///     Returns the rendered sprite frame for a creature at the given frame index. Returns null if the sprite or frame
    ///     cannot be loaded.
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

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
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
///     Creature animation metadata describing frame index ranges, counts, and idle timing for every animation state
///     (walk, attack, standing).
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
    byte OptionalAnimationProbability,
    int AnimationIntervalMs,
    int TotalFrameCount)
{
    /// <summary>
    ///     The <see cref="MpfIdleType" /> that governs this creature's idle animation behavior.
    /// </summary>
    public MpfIdleType IdleType => MpfFile.DetectIdleType(StandingFrameCount, OptionalAnimationFrameCount);
}
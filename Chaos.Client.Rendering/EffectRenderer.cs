#region
using Chaos.Client.Data;
using Chaos.Client.Rendering.Models;
using DALib.Definitions;
using DALib.Drawing;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Caches spell/effect animations as <see cref="SpriteAnimation" />. Effects use the EffectTable (effect.tbl) from Roh
///     to determine the frame sequence. An EffectTable entry of [0] indicates an EFA file; otherwise the entry lists EPF
///     frame indices to play in order. Source files are cached by <see cref="Data.Repositories.EffectsRepository" />.
/// </summary>
public sealed class EffectRenderer : IDisposable
{
    private const int DEFAULT_FRAME_INTERVAL_MS = 100;

    // null value = effect doesn't exist (negative cache)
    private readonly Dictionary<int, SpriteAnimation?> AnimationCache = new();

    /// <inheritdoc />
    public void Dispose() => Clear();

    /// <summary>
    ///     Disposes all cached animations. Call on map change.
    /// </summary>
    public void Clear()
    {
        foreach (var animation in AnimationCache.Values)
            animation?.Dispose();

        AnimationCache.Clear();
    }

    /// <summary>
    ///     Returns frame count, interval, and blend mode for an effect, or null if the effect doesn't exist.
    /// </summary>
    public (int FrameCount, int FrameIntervalMs, bool IsAdditive)? GetEffectInfo(GraphicsDevice device, int effectId)
    {
        var animation = GetOrLoadAnimation(device, effectId);

        if (animation is null)
            return null;

        return (animation.FrameCount, animation.FrameIntervalMs, animation.IsAdditive);
    }

    /// <summary>
    ///     Returns the sprite frame for a specific frame of an effect.
    /// </summary>
    public SpriteFrame? GetFrame(GraphicsDevice device, int effectId, int frameIndex)
    {
        var animation = GetOrLoadAnimation(device, effectId);

        return animation?.GetFrame(frameIndex);
    }

    private SpriteAnimation? GetOrLoadAnimation(GraphicsDevice device, int effectId)
    {
        if (AnimationCache.TryGetValue(effectId, out var existing))
            return existing;

        var animation = LoadAnimation(device, effectId);

        AnimationCache[effectId] = animation;

        return animation;
    }

    private SpriteAnimation? LoadAnimation(GraphicsDevice device, int effectId)
    {
        // Check the EffectTable to determine the frame sequence and format
        var tableEntry = DataContext.Effects.GetEffectTableEntry(effectId);

        if (tableEntry is null || (tableEntry.FrameSequence.Count == 0))
            return null;

        // A single-element [0] entry means this is an EFA effect
        if (tableEntry.FrameSequence is [0])
            return LoadEfaAnimation(device, effectId);

        // Otherwise, it's an EPF effect with a specific frame sequence from the table
        return LoadEpfAnimation(device, effectId, tableEntry);
    }

    private SpriteAnimation? LoadEfaAnimation(GraphicsDevice device, int effectId)
    {
        var efaFile = DataContext.Effects.GetEfaEffect(effectId);

        if (efaFile is null)
            return null;

        var frames = new SpriteFrame[efaFile.Count];

        for (var i = 0; i < efaFile.Count; i++)
        {
            var efaFrame = efaFile[i];
            using var skImage = Graphics.RenderImage(efaFrame, efaFile.BlendingType);
            var texture = TextureConverter.ToTexture2D(device, skImage);

            frames[i] = new SpriteFrame(
                texture,
                efaFrame.CenterX,
                efaFrame.CenterY,
                efaFrame.Left,
                efaFrame.Top);
        }

        var intervalMs = efaFile.FrameIntervalMs > 0 ? efaFile.FrameIntervalMs : DEFAULT_FRAME_INTERVAL_MS;
        var isAdditive = efaFile.BlendingType == EfaBlendingType.Additive;

        return new SpriteAnimation(frames, intervalMs, isAdditive);
    }

    private SpriteAnimation? LoadEpfAnimation(GraphicsDevice device, int effectId, EffectTableEntry tableEntry)
    {
        var epfEffect = DataContext.Effects.GetEpfEffect(effectId);

        if (epfEffect is null)
            return null;

        var epfFile = epfEffect.Entity;
        var palette = epfEffect.Palette;
        var frameSequence = tableEntry.FrameSequence;

        // Load per-frame center points from the .tbl file (e.g. eff246.tbl)
        var centerPoints = DataContext.Effects.GetEffectCenterPoints(effectId, epfFile.Count);
        var frames = new SpriteFrame[frameSequence.Count];

        for (var i = 0; i < frameSequence.Count; i++)
        {
            var epfFrameIndex = frameSequence[i];

            if (epfFrameIndex >= epfFile.Count)
                continue;

            var epfFrame = epfFile[epfFrameIndex];
            using var skImage = Graphics.RenderImage(epfFrame, palette);
            var texture = TextureConverter.ToTexture2D(device, skImage);

            // Use center point from .tbl if available, otherwise fall back to half-size
            var centerX = centerPoints is not null ? centerPoints[epfFrameIndex].X : (short)(epfFrame.PixelWidth / 2);
            var centerY = centerPoints is not null ? centerPoints[epfFrameIndex].Y : (short)(epfFrame.PixelHeight / 2);

            frames[i] = new SpriteFrame(
                texture,
                centerX,
                centerY,
                epfFrame.Left,
                epfFrame.Top);
        }

        return new SpriteAnimation(frames);
    }
}
#region
using Chaos.Client.Data;
using Chaos.Client.Data.Definitions;
using Chaos.Client.Rendering.Models;
using DALib.Definitions;
using DALib.Drawing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Renders and caches spell/effect animations. Supports both EFA (self-contained animation) and EPF (frame-sequence
///     driven) effect formats. Call <see cref="Clear" /> on map change to evict cached textures.
/// </summary>
/// <remarks>
///     The EffectTable (effect.tbl) determines the format: an entry of [0] indicates EFA; otherwise the entry lists EPF
///     frame indices to play in order. Source files are cached by <see cref="Data.Repositories.EffectsRepository" />.
/// </remarks>
public sealed class EffectRenderer : IDisposable
{
    private const int DEFAULT_FRAME_INTERVAL_MS = 100;

    //null value = effect doesn't exist (negative cache)
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
    ///     Draws a single effect frame at the given tile center. Switches blend state for non-normal blend modes and restores
    ///     AlphaBlend afterward. Requires the SpriteBatch to be in Immediate mode.
    /// </summary>
    public void Draw(
        SpriteBatch batch,
        GraphicsDevice device,
        Camera camera,
        int effectId,
        int currentFrame,
        EffectBlendMode blendMode,
        float tileCenterX,
        float tileCenterY,
        Vector2 visualOffset)
    {
        var spriteFrame = GetFrame(effectId, currentFrame);

        if (spriteFrame is null)
            return;

        var frame = spriteFrame.Value;
        var drawX = tileCenterX + visualOffset.X - frame.CenterX + Math.Min(0, (int)frame.Left);
        var drawY = tileCenterY + visualOffset.Y - frame.CenterY + Math.Min(0, (int)frame.Top);
        var screenPos = camera.WorldToScreen(new Vector2(drawX, drawY));

        if (blendMode != EffectBlendMode.Normal)
            device.BlendState = blendMode switch
            {
                EffectBlendMode.Additive  => BlendState.Additive,
                EffectBlendMode.SelfAlpha => BlendStates.Screen,
                _                         => BlendState.AlphaBlend
            };

        batch.Draw(frame.Texture, screenPos, Color.White);

        if (blendMode != EffectBlendMode.Normal)
            device.BlendState = BlendState.AlphaBlend;
    }

    /// <summary>
    ///     Returns playback metadata for an effect (frame count, timing, format, blend mode). Returns null if the effect ID is
    ///     invalid.
    /// </summary>
    public (int FrameCount, int FrameIntervalMs, bool IsEfa, EffectBlendMode BlendMode)? GetEffectInfo(int effectId)
    {
        var animation = GetOrLoadAnimation(effectId);

        if (animation is null)
            return null;

        return (animation.FrameCount, animation.FrameIntervalMs, animation.IsEfa, animation.BlendMode);
    }

    /// <summary>
    ///     Returns the rendered texture and positioning data for a single animation frame of an effect.
    /// </summary>
    public SpriteFrame? GetFrame(int effectId, int frameIndex)
    {
        var animation = GetOrLoadAnimation(effectId);

        return animation?.GetFrame(frameIndex);
    }

    private SpriteAnimation? GetOrLoadAnimation(int effectId)
    {
        if (AnimationCache.TryGetValue(effectId, out var existing))
            return existing;

        var animation = LoadAnimation(effectId);

        AnimationCache[effectId] = animation;

        return animation;
    }

    private SpriteAnimation? LoadAnimation(int effectId)
    {
        //check the effecttable to determine the frame sequence and format
        var tableEntry = DataContext.Effects.GetEffectTableEntry(effectId);

        if (tableEntry is null || (tableEntry.FrameSequence.Count == 0))
            return null;

        //a single-element [0] entry means this is an efa effect
        if (tableEntry.FrameSequence is [0])
            return LoadEfaAnimation(effectId);

        //otherwise, it's an epf effect with a specific frame sequence from the table
        return LoadEpfAnimation(effectId, tableEntry);
    }

    private SpriteAnimation? LoadEfaAnimation(int effectId)
    {
        var efaFile = DataContext.Effects.GetEfaEffect(effectId);

        if (efaFile is null)
            return null;

        var frames = new SpriteFrame[efaFile.Count];

        for (var i = 0; i < efaFile.Count; i++)
        {
            var efaFrame = efaFile[i];
            using var skImage = Graphics.RenderImage(efaFrame, efaFile.BlendingType);
            var texture = TextureConverter.ToTexture2D(skImage);

            frames[i] = new SpriteFrame(
                texture,
                efaFrame.CenterX,
                efaFrame.CenterY,
                efaFrame.Left,
                efaFrame.Top);
        }

        var intervalMs = efaFile.FrameIntervalMs > 0 ? efaFile.FrameIntervalMs : DEFAULT_FRAME_INTERVAL_MS;

        var blendMode = efaFile.BlendingType switch
        {
            EfaBlendingType.Additive  => EffectBlendMode.Additive,
            EfaBlendingType.SelfAlpha => EffectBlendMode.SelfAlpha,
            _                         => EffectBlendMode.Normal
        };

        return new SpriteAnimation(
            frames,
            intervalMs,
            blendMode,
            true);
    }

    private SpriteAnimation? LoadEpfAnimation(int effectId, EffectTableEntry tableEntry)
    {
        var epfEffect = DataContext.Effects.GetEpfEffect(effectId);

        if (epfEffect is null)
            return null;

        var epfFile = epfEffect.Entity;
        var palette = epfEffect.Palette;
        var frameSequence = tableEntry.FrameSequence;
        var alphaType = DataContext.Effects.UsesLuminanceBlending(effectId) ? SKAlphaType.Unpremul : SKAlphaType.Premul;

        //load per-frame center points from the .tbl file (e.g. eff246.tbl)
        var centerPoints = DataContext.Effects.GetEffectCenterPoints(effectId, epfFile.Count);
        var frames = new SpriteFrame[frameSequence.Count];

        for (var i = 0; i < frameSequence.Count; i++)
        {
            var epfFrameIndex = frameSequence[i];

            if (epfFrameIndex >= epfFile.Count)
                continue;

            var epfFrame = epfFile[epfFrameIndex];
            using var skImage = Graphics.RenderImage(epfFrame, palette, alphaType);
            var texture = TextureConverter.ToTexture2D(skImage);

            //use center point from .tbl if available, otherwise fall back to half-size
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
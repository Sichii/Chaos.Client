#region
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Rendering.Definitions;

/// <summary>
///     Custom blend states used by the rendering pipeline.
/// </summary>
public static class BlendStates
{
    /// <summary>
    ///     Screen blend: output = src + dst * (1 - srcColor) per channel. Each color channel's value acts as its own alpha —
    ///     black pixels are fully transparent, white pixels fully opaque. Matches the original client's blend mode 0x6D. Used
    ///     for foreground tiles with <see cref="DALib.Definitions.TileFlags.Transparent" /> and SelfAlpha EFA effects.
    /// </summary>
    public static readonly BlendState Screen = new()
    {
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.InverseSourceColor,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.InverseSourceAlpha
    };

    /// <summary>
    ///     Additive ghost blend: result = dst + src * alpha. Approximates the original client's double-additive blend mode
    ///     0x210001 (result = clamp(src + 2*dst)) by drawing the source additively at very low alpha. Used for IsDead ghosts
    ///     and IsTransparent aislings.
    /// </summary>
    public static readonly BlendState Ghost = new()
    {
        ColorSourceBlend = Blend.SourceAlpha,
        ColorDestinationBlend = Blend.One,
        ColorBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.SourceAlpha,
        AlphaDestinationBlend = Blend.One
    };

    /// <summary>
    ///     Disables all color channel writes. Used for stencil-only passes (e.g. tab map entity masking).
    /// </summary>
    public static readonly BlendState NoColorWrite = new()
    {
        ColorWriteChannels = ColorWriteChannels.None
    };
}
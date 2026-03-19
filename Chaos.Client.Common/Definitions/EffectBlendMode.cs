namespace Chaos.Client.Common.Definitions;

/// <summary>
///     How an EFA spell effect blends with the scene.
/// </summary>
public enum EffectBlendMode
{
    /// <summary>
    ///     Standard alpha blending (premultiplied).
    /// </summary>
    Normal,

    /// <summary>
    ///     Additive blending: output = src + dst.
    /// </summary>
    Additive,

    /// <summary>
    ///     Per-channel self-alpha: output = src + dst * (1 - src) per channel. Uses Screen blend state: Blend.One +
    ///     Blend.InverseSourceColor.
    /// </summary>
    SelfAlpha
}
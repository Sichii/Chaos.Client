using Microsoft.Xna.Framework;

namespace Chaos.Client.Rendering.Models;

/// <summary>
///     Per-particle state for the snow weather effect. Mutable struct used with
///     <c>ref</c> indexing in <see cref="WeatherRenderer"/> for zero-allocation updates.
/// </summary>
public struct SnowParticle
{
    public Vector2 Position;
    public float VelocityY;
    public float VelocityX;
    public int FrameIndex;
}

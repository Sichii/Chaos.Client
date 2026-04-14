#region
using Chaos.Client.Data.Models;
using Chaos.Geometry.Abstractions.Definitions;
using Microsoft.Xna.Framework;
#endregion

namespace Chaos.Client.Rendering;

public readonly record struct LightSource(
    Vector2 ScreenPosition,
    int TileX,
    int TileY,
    Direction Direction,
    LightMask PixelMask,
    (int Dx, int Dy)[] TileOffsets);

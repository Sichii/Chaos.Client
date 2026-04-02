#region
using Chaos.Client.Data.Models;
using Microsoft.Xna.Framework;
#endregion

namespace Chaos.Client.Rendering;

public readonly record struct LightSource(Vector2 ScreenPosition, LightMask Mask);
#region
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     A Texture2D subclass whose Dispose is a no-op. Only the owning TextureCache can release GPU memory via
///     ForceDispose(). This allows cached textures to be freely assigned to UI controls without worrying about
///     double-dispose or premature release.
/// </summary>
public sealed class CachedTexture2D : Texture2D
{
    public CachedTexture2D(GraphicsDevice device, int width, int height)
        : base(
            device,
            width,
            height,
            false,
            SurfaceFormat.Color) { }

    protected override void Dispose(bool disposing) { }

    internal void ForceDispose() => base.Dispose(true);
}
#region
using System.Buffers;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Chaos.Client.Rendering;

public static class TextureConverter
{
    public static GraphicsDevice Device { get; set; } = null!;

    internal static T ConvertImage<T>(SKImage image, Func<GraphicsDevice, int, int, T> factory) where T: Texture2D
    {
        var width = image.Width;
        var height = image.Height;

        var info = new SKImageInfo(
            width,
            height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        var byteCount = info.BytesSize;
        var pixels = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);

            try
            {
                image.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes);
            } finally
            {
                handle.Free();
            }

            var texture = factory(Device, width, height);
            texture.SetData(pixels, 0, byteCount);

            return texture;
        } finally
        {
            ArrayPool<byte>.Shared.Return(pixels);
        }
    }

    public static Texture2D ToTexture2D(SKImage image)
        => ConvertImage(
            image,
            static (d, w, h) => new Texture2D(
                d,
                w,
                h,
                false,
                SurfaceFormat.Color));
}

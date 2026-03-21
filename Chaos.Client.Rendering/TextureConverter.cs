#region
using System.Buffers;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Chaos.Client.Rendering;

public static class TextureConverter
{
    internal static T ConvertImage<T>(GraphicsDevice device, SKImage image, Func<GraphicsDevice, int, int, T> factory) where T: Texture2D
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

            var texture = factory(device, width, height);
            texture.SetData(pixels, 0, byteCount);

            return texture;
        } finally
        {
            ArrayPool<byte>.Shared.Return(pixels);
        }
    }

    /// <summary>
    ///     Creates a tinted (blue-shifted) copy of a texture. Used for entity hover highlights.
    /// </summary>
    public static Texture2D CreateTintedTexture(GraphicsDevice device, Texture2D source)
    {
        var count = source.Width * source.Height;
        var pixels = ArrayPool<Color>.Shared.Rent(count);

        try
        {
            source.GetData(pixels, 0, count);
            TintPixels(pixels, count);

            var tinted = new Texture2D(device, source.Width, source.Height);
            tinted.SetData(pixels, 0, count);

            return tinted;
        } finally
        {
            ArrayPool<Color>.Shared.Return(pixels);
        }
    }

    /// <summary>
    ///     Applies a blue-shift tint to a pixel array in-place. Shared by both TextureConverter (regular Texture2D) and
    ///     UiRenderer (CachedTexture2D).
    /// </summary>
    internal static void TintPixels(Color[] pixels, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var p = pixels[i];

            if (p.A == 0)
                continue;

            var r = Math.Clamp((128 * p.R + 2 * p.B) / 256 + 59, 0, 255);
            var g = Math.Clamp((131 * p.G - 2 * p.B) / 256 + 82, 0, 255);
            var b = Math.Clamp((133 * p.B - 2 * p.G) / 256 + 120, 0, 255);

            pixels[i] = new Color(
                (byte)r,
                (byte)g,
                (byte)b,
                p.A);
        }
    }

    public static Texture2D ToTexture2D(GraphicsDevice device, SKImage image)
        => ConvertImage(
            device,
            image,
            static (d, w, h) => new Texture2D(
                d,
                w,
                h,
                false,
                SurfaceFormat.Color));
}
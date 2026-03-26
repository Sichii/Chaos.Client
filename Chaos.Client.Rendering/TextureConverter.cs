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

    /// <summary>
    ///     Creates a group-tinted copy of a texture using saturated additive blend with (255, 231, 59) — warm yellow/gold.
    ///     Matches the original DA client's group member highlight (palette index 0x45 from legend.pal, effect slot 1).
    /// </summary>
    public static Texture2D CreateGroupTintedTexture(Texture2D source)
    {
        var count = source.Width * source.Height;
        var pixels = ArrayPool<Color>.Shared.Rent(count);

        try
        {
            source.GetData(pixels, 0, count);
            GroupTintPixels(pixels, count);

            var tinted = new Texture2D(Device, source.Width, source.Height);
            tinted.SetData(pixels, 0, count);

            return tinted;
        } finally
        {
            ArrayPool<Color>.Shared.Return(pixels);
        }
    }

    /// <summary>
    ///     Creates a tinted (blue-shifted) copy of a texture. Used for entity hover highlights.
    /// </summary>
    public static Texture2D CreateTintedTexture(Texture2D source)
    {
        var count = source.Width * source.Height;
        var pixels = ArrayPool<Color>.Shared.Rent(count);

        try
        {
            source.GetData(pixels, 0, count);
            TintPixels(pixels, count);

            var tinted = new Texture2D(Device, source.Width, source.Height);
            tinted.SetData(pixels, 0, count);

            return tinted;
        } finally
        {
            ArrayPool<Color>.Shared.Return(pixels);
        }
    }

    /// <summary>
    ///     Applies saturated additive blend with (255, 231, 59) to a pixel array in-place. The original DA client uses this
    ///     palette-based additive tint for group member highlighting — the entity appears washed toward bright yellow-white.
    /// </summary>
    internal static void GroupTintPixels(Color[] pixels, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var p = pixels[i];

            if (p.A == 0)
                continue;

            pixels[i] = new Color(
                (byte)Math.Min(p.R + 255, 255),
                (byte)Math.Min(p.G + 231, 255),
                (byte)Math.Min(p.B + 59, 255),
                p.A);
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
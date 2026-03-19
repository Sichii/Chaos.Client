#region
using System.Buffers;
using System.Runtime.InteropServices;
using Chaos.Client.Data;
using DALib.Drawing;
using DALib.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Chaos.Client.Rendering;

public static class TextureConverter
{
    /// <summary>
    ///     Creates a tinted (blue-shifted) copy of a texture. Used for cooldown overlays and entity hover highlights.
    /// </summary>
    public static Texture2D CreateTintedTexture(GraphicsDevice device, Texture2D source)
    {
        var pixels = new Color[source.Width * source.Height];
        source.GetData(pixels);

        for (var i = 0; i < pixels.Length; i++)
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

        var tinted = new Texture2D(device, source.Width, source.Height);
        tinted.SetData(pixels);

        return tinted;
    }

    /// <summary>
    ///     Loads all frames from an EPF file in setoa.dat as Texture2D[] using the GUI palette.
    /// </summary>
    public static Texture2D[] LoadEpfTextures(GraphicsDevice device, string fileName)
    {
        var images = DataContext.UserControls.GetEpfImages(fileName);

        if (images.Length == 0)
            return [];

        var textures = new Texture2D[images.Length];

        for (var i = 0; i < images.Length; i++)
        {
            textures[i] = ToTexture2D(device, images[i]);

            images[i]
                .Dispose();
        }

        return textures;
    }

    /// <summary>
    ///     Loads a single frame from an SPF file in national.dat and returns it as a Texture2D.
    /// </summary>
    public static Texture2D? LoadNationalSpfTexture(GraphicsDevice device, string fileName, int frameIndex = 0)
    {
        using var image = DataContext.UserControls.GetNationalSpfImage(fileName, frameIndex);

        return image is not null ? ToTexture2D(device, image) : null;
    }

    /// <summary>
    ///     Loads a single frame from an SPF file in setoa.dat and returns it as a Texture2D.
    /// </summary>
    public static Texture2D? LoadSpfTexture(GraphicsDevice device, string fileName, int frameIndex = 0)
    {
        using var image = DataContext.UserControls.GetSpfImage(fileName, frameIndex);

        return image is not null ? ToTexture2D(device, image) : null;
    }

    /// <summary>
    ///     Loads all frames from an SPF file in setoa.dat as Texture2D[].
    ///     Null/empty frames in the SPF are returned as a 1x1 transparent texture to preserve index alignment.
    /// </summary>
    public static Texture2D[] LoadSpfTextures(GraphicsDevice device, string fileName)
    {
        var images = DataContext.UserControls.GetSpfImages(fileName);

        if (images.Length == 0)
            return [];

        var textures = new Texture2D[images.Length];

        for (var i = 0; i < images.Length; i++)
            if (images[i] is { } image)
            {
                textures[i] = ToTexture2D(device, image);
                image.Dispose();
            } else
            {
                textures[i] = new Texture2D(device, 1, 1);

                textures[i]
                    .SetData([Color.Transparent]);
            }

        return textures;
    }

    /// <summary>
    ///     Renders a palettized sprite frame to a Texture2D.
    /// </summary>
    public static Texture2D? RenderSprite(GraphicsDevice device, Palettized<EpfFrame>? palettized)
    {
        if (palettized is null)
            return null;

        using var image = Graphics.RenderImage(palettized.Entity, palettized.Palette);

        return image is not null ? ToTexture2D(device, image) : null;
    }

    public static Texture2D ToTexture2D(GraphicsDevice device, SKImage image)
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

            var texture = new Texture2D(
                device,
                width,
                height,
                false,
                SurfaceFormat.Color);
            texture.SetData(pixels, 0, byteCount);

            return texture;
        } finally
        {
            ArrayPool<byte>.Shared.Return(pixels);
        }
    }
}
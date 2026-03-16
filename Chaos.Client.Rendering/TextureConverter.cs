#region
using System.Buffers;
using System.Runtime.InteropServices;
using Chaos.Client.Data;
using DALib.Drawing;
using DALib.Utility;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Chaos.Client.Rendering;

public static class TextureConverter
{
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
    ///     Loads a single frame from an SPF file in setoa.dat and returns it as a Texture2D.
    /// </summary>
    public static Texture2D? LoadSpfTexture(GraphicsDevice device, string fileName, int frameIndex = 0)
    {
        using var image = DataContext.UserControls.GetSpfImage(fileName, frameIndex);

        return image is not null ? ToTexture2D(device, image) : null;
    }

    /// <summary>
    ///     Loads all frames from an SPF file in setoa.dat as Texture2D[].
    /// </summary>
    public static Texture2D[] LoadSpfTextures(GraphicsDevice device, string fileName)
    {
        var images = DataContext.UserControls.GetSpfImages(fileName);

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
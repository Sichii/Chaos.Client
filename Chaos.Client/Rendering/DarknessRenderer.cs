#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Chaos.Networking.Entities.Server;
using DALib.Drawing;
using DALib.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Manages the darkness overlay system: light metadata parsing, HEA file loading, per-pixel texture generation, and
///     overlay drawing. Supports both flat color overlays and per-pixel HEA light maps.
/// </summary>
public sealed class DarknessRenderer : IDisposable
{
    private readonly GraphicsDevice Device;

    private float Alpha;
    private string CurrentLightType = "default";
    private Color DarknessColor;
    private HeaFile? HeaFile;
    private int LastOffsetX = int.MinValue;
    private int LastOffsetY = int.MinValue;
    private LightMetadata? LightData;
    private Color[]? Pixels;
    private byte[]? ScanlineBuffer;
    private Texture2D? Texture;

    /// <summary>
    ///     Whether a per-pixel HEA light map is loaded for the current map.
    /// </summary>
    public bool HasHeaFile => HeaFile is not null;

    /// <summary>
    ///     Whether darkness is currently active (alpha > 0).
    /// </summary>
    public bool IsActive => Alpha > 0f;

    public DarknessRenderer(GraphicsDevice device) => Device = device;

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeTexture();
        Pixels = null;
        ScanlineBuffer = null;
    }

    private void DisposeTexture()
    {
        Texture?.Dispose();
        Texture = null;
    }

    /// <summary>
    ///     Draws the darkness overlay. Uses per-pixel HEA texture if available, otherwise flat color.
    /// </summary>
    public void Draw(SpriteBatch spriteBatch, Rectangle viewport)
    {
        if (Alpha <= 0f)
            return;

        if (Texture is not null && !Texture.IsDisposed)
            spriteBatch.Draw(Texture, new Vector2(viewport.X, viewport.Y), Color.White);
        else
            UIElement.DrawRect(spriteBatch, viewport, DarknessColor * Alpha);
    }

    /// <summary>
    ///     Called when the server sends a LightLevel packet. Updates the darkness alpha and color based on the current map's
    ///     light type metadata.
    /// </summary>
    public void OnLightLevel(LightLevelArgs args)
    {
        var enumHex = ((byte)args.LightLevel).ToString("X");
        var key = $"{CurrentLightType}_{enumHex}".ToLowerInvariant();

        if (LightData?.LightProperties.TryGetValue(key, out var props) is true && (props.Alpha < 32))
        {
            Alpha = (32 - props.Alpha) / 32f;
            DarknessColor = new Color(props.R, props.G, props.B);
        } else
        {
            Alpha = 0f;
            DarknessColor = Color.Transparent;
        }

        // Invalidate dirty tracking so the texture is rebuilt with updated alpha/color
        LastOffsetX = int.MinValue;
        LastOffsetY = int.MinValue;

        if (HeaFile is not null && (Alpha > 0f))
            RebuildTexture(null);
        else
            DisposeTexture();
    }

    /// <summary>
    ///     Called on map change. Looks up the map's light type and loads the HEA file if one exists.
    /// </summary>
    public void OnMapChanged(short mapId)
    {
        CurrentLightType = LightData?.MapLightTypes.TryGetValue(mapId, out var lightType) is true ? lightType : "default";

        Alpha = 0f;
        DarknessColor = Color.Transparent;
        LastOffsetX = int.MinValue;
        LastOffsetY = int.MinValue;
        DisposeTexture();

        HeaFile = TryLoadHeaFile(mapId);
    }

    private void RebuildTexture(Camera? camera, Rectangle viewport = default)
    {
        if (HeaFile is null)
            return;

        int vpWidth,
            vpHeight;

        if (viewport != default)
        {
            vpWidth = viewport.Width;
            vpHeight = viewport.Height;
        } else

            // Called from OnLightLevel before viewport is available — skip if no camera
            return;

        var ambientAlpha32 = (byte)(32 * (1f - Alpha));

        var viewportTopLeft = camera!.ScreenToWorld(Vector2.Zero);
        var worldOffsetX = (int)viewportTopLeft.X;
        var worldOffsetY = (int)viewportTopLeft.Y;

        if ((worldOffsetX == LastOffsetX) && (worldOffsetY == LastOffsetY))
            return;

        var heaOffsetX = worldOffsetX + HeaFile.ScreenWidth;
        var heaOffsetY = worldOffsetY + HeaFile.ScreenHeight;

        if (Texture is null || Texture.IsDisposed || (Texture.Width != vpWidth) || (Texture.Height != vpHeight))
        {
            Texture?.Dispose();
            Texture = new Texture2D(Device, vpWidth, vpHeight);
        }

        var pixelCount = vpWidth * vpHeight;

        if (Pixels is null || (Pixels.Length < pixelCount))
            Pixels = new Color[pixelCount];

        if (ScanlineBuffer is null || (ScanlineBuffer.Length < HeaFile.ScanlineWidth))
            ScanlineBuffer = new byte[HeaFile.ScanlineWidth];

        var pixels = Pixels;
        var darkR = DarknessColor.R;
        var darkG = DarknessColor.G;
        var darkB = DarknessColor.B;
        var scanlineBuffer = ScanlineBuffer;

        for (var vy = 0; vy < vpHeight; vy++)
        {
            var heaY = heaOffsetY + vy;

            if ((heaY < 0) || (heaY >= HeaFile.ScanlineCount))
            {
                var ambientOpacity = (byte)(255 - ambientAlpha32 * 255 / 32);

                for (var vx = 0; vx < vpWidth; vx++)
                    pixels[vy * vpWidth + vx] = new Color(
                        darkR,
                        darkG,
                        darkB,
                        ambientOpacity);

                continue;
            }

            Array.Clear(scanlineBuffer);

            for (var layer = 0; layer < HeaFile.LayerCount; layer++)
            {
                var layerWidth = HeaFile.GetLayerWidth(layer);
                var layerStart = HeaFile.Thresholds[layer];

                HeaFile.DecodeScanline(layer, heaY, scanlineBuffer.AsSpan(layerStart, layerWidth));
            }

            for (var vx = 0; vx < vpWidth; vx++)
            {
                var heaX = heaOffsetX + vx;
                byte lightValue;

                if ((heaX < 0) || (heaX >= HeaFile.ScanlineWidth))
                    lightValue = 0;
                else
                    lightValue = scanlineBuffer[heaX];

                var effective = Math.Max(ambientAlpha32, lightValue);
                var alpha = (byte)(255 - effective * 255 / 32);

                pixels[vy * vpWidth + vx] = new Color(
                    darkR,
                    darkG,
                    darkB,
                    alpha);
            }
        }

        Texture.SetData(pixels, 0, pixelCount);
        LastOffsetX = worldOffsetX;
        LastOffsetY = worldOffsetY;
    }

    /// <summary>
    ///     Reloads light metadata from disk. Call after metadata sync completes.
    /// </summary>
    public void ReloadMetadata() => LightData = DataContext.MetaFiles.GetLightMetadata();

    private static HeaFile? TryLoadHeaFile(short mapId)
    {
        var heaName = $"{mapId:D6}";

        if (!DatArchives.Seo.TryGetValue(heaName.WithExtension(".hea"), out var entry))
            return null;

        try
        {
            return HeaFile.FromEntry(entry);
        } catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Rebuilds the per-pixel darkness texture from HEA data for the visible viewport. Call each frame before drawing when
    ///     HEA data exists.
    /// </summary>
    public void Update(Camera camera, Rectangle viewport)
    {
        if (HeaFile is null || (Alpha <= 0f))
            return;

        RebuildTexture(camera, viewport);
    }
}
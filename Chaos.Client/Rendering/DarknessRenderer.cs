#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
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
    private readonly Dictionary<string, (byte Alpha, byte R, byte G, byte B)> LightProperties = [];
    private readonly Dictionary<short, string> MapLightTypes = [];

    private float Alpha;
    private string CurrentLightType = "default";
    private Color DarknessColor;
    private HeaFile? HeaFile;
    private int LastOffsetX = int.MinValue;
    private int LastOffsetY = int.MinValue;
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

    public DarknessRenderer(GraphicsDevice device)
    {
        Device = device;
        LoadLightMetadata();
    }

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
            UIElement.DrawRect(
                spriteBatch,
                Device,
                viewport,
                DarknessColor * Alpha);
    }

    /// <summary>
    ///     Parses the "Light" metadata file into light property and map-to-light-type lookups.
    /// </summary>
    private void LoadLightMetadata()
    {
        var lightFile = DataContext.MetaFiles.Get("Light");

        if (lightFile is null)
            return;

        foreach (var entry in lightFile)
            if (entry.Key.Contains('_'))
            {
                // LightPropertyMetaNode: "{TypeName}_{HexEnumValue}"
                // Properties: [StartHour, EndHour, Alpha, Red, Green, Blue]
                if (entry.Properties.Count < 6)
                    continue;

                if (!byte.TryParse(entry.Properties[2], out var alpha)
                    || !byte.TryParse(entry.Properties[3], out var r)
                    || !byte.TryParse(entry.Properties[4], out var g)
                    || !byte.TryParse(entry.Properties[5], out var b))
                    continue;

                LightProperties[entry.Key.ToLowerInvariant()] = (alpha, r, g, b);
            } else if (short.TryParse(entry.Key, out var mapId) && (entry.Properties.Count > 0))

                // MapLightMetaNode: "{MapId}" -> [LightTypeName]
                MapLightTypes[mapId] = entry.Properties[0]
                                            .ToLowerInvariant();
    }

    /// <summary>
    ///     Called when the server sends a LightLevel packet. Updates the darkness alpha and color based on the current map's
    ///     light type metadata.
    /// </summary>
    public void OnLightLevel(LightLevelArgs args)
    {
        var enumHex = ((byte)args.LightLevel).ToString("X");
        var key = $"{CurrentLightType}_{enumHex}".ToLowerInvariant();

        if (LightProperties.TryGetValue(key, out var props) && (props.Alpha < 32))
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
        CurrentLightType = MapLightTypes.TryGetValue(mapId, out var lightType) ? lightType : "default";

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
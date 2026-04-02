#region
using System.Runtime.InteropServices;
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Chaos.DarkAges.Definitions;
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
    private int CacheBaseY;
    private byte[,]? CachedLayer0;
    private byte[,]? CachedLayer1;
    private int CachedLayerIndex0 = -1;
    private int CachedLayerIndex1 = -1;
    private bool CacheValid;
    private string CurrentLightType = "default";
    private Color DarknessColor;
    private HeaFile? HeaFile;
    private bool IsDarkMap;
    private LightLevel LastLightLevel;
    private int LastLightSourceHash;
    private int LastOffsetX = int.MinValue;
    private int LastOffsetY = int.MinValue;
    private LightMetadata? LightData;
    private int LightSourceCount;
    private LightSource[] LightSources = [];
    private Color[]? Pixels;
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
        CachedLayer0 = null;
        CachedLayer1 = null;
        CachedLayerIndex0 = -1;
        CachedLayerIndex1 = -1;
        CacheValid = false;
    }

    private void ComputePixelsFromCache(
        int heaOffsetX,
        int vpWidth,
        int vpHeight,
        byte ambientAlpha32)
    {
        var pixels = Pixels!;
        var darkR = DarknessColor.R;
        var darkG = DarknessColor.G;
        var darkB = DarknessColor.B;

        var l0Start = 0;
        var l0End = 0;
        var l1Start = 0;
        var l1End = 0;

        if (CachedLayerIndex0 >= 0)
        {
            l0Start = HeaFile!.Thresholds[CachedLayerIndex0];
            l0End = l0Start + HeaFile.GetLayerWidth(CachedLayerIndex0);
        }

        if (CachedLayerIndex1 >= 0)
        {
            l1Start = HeaFile!.Thresholds[CachedLayerIndex1];
            l1End = l1Start + HeaFile.GetLayerWidth(CachedLayerIndex1);
        }

        for (var vy = 0; vy < vpHeight; vy++)
        {
            for (var vx = 0; vx < vpWidth; vx++)
            {
                var heaX = heaOffsetX + vx;
                byte lightValue;

                if ((heaX < 0) || (heaX >= HeaFile!.ScanlineWidth))
                    lightValue = 0;
                else if ((CachedLayerIndex0 >= 0) && (heaX >= l0Start) && (heaX < l0End))
                    lightValue = CachedLayer0![vy, heaX - l0Start];
                else if ((CachedLayerIndex1 >= 0) && (heaX >= l1Start) && (heaX < l1End))
                    lightValue = CachedLayer1![vy, heaX - l1Start];
                else
                    lightValue = 0;

                var effective = Math.Max(ambientAlpha32, lightValue);
                var alpha = (byte)(255 - effective * 255 / 32);

                pixels[vy * vpWidth + vx] = new Color(
                    darkR,
                    darkG,
                    darkB,
                    alpha);
            }
        }
    }

    private void DecodeLayerRows(
        int layerIndex,
        int heaStartY,
        int startRow,
        int rowCount,
        byte[,] target)
    {
        var layerWidth = HeaFile!.GetLayerWidth(layerIndex);

        for (var row = startRow; row < (startRow + rowCount); row++)
        {
            var heaY = heaStartY + row;
            var rowSpan = MemoryMarshal.CreateSpan(ref target[row, 0], layerWidth);

            if ((heaY < 0) || (heaY >= HeaFile.ScanlineCount))
            {
                rowSpan.Clear();

                continue;
            }

            HeaFile.DecodeScanline(layerIndex, heaY, rowSpan);
        }
    }

    private (int LeftLayer, int RightLayer) DetermineViewportLayers(int heaOffsetX, int vpWidth)
    {
        var leftHeaX = Math.Max(0, heaOffsetX);
        var rightHeaX = Math.Min(HeaFile!.ScanlineWidth - 1, heaOffsetX + vpWidth - 1);

        if ((rightHeaX < 0) || (leftHeaX >= HeaFile.ScanlineWidth))
            return (-1, -1);

        var leftLayer = -1;
        var rightLayer = -1;

        for (var i = 0; i < HeaFile.LayerCount; i++)
        {
            var start = HeaFile.Thresholds[i];
            var end = start + HeaFile.GetLayerWidth(i);

            if ((leftHeaX >= start) && (leftHeaX < end))
                leftLayer = i;

            if ((rightHeaX >= start) && (rightHeaX < end))
                rightLayer = i;
        }

        return (leftLayer, rightLayer);
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
            RenderHelper.DrawRect(spriteBatch, viewport, DarknessColor * Alpha);
    }

    private void EnsureLayerCache(
        ref byte[,]? cache,
        ref int cachedIndex,
        int neededIndex,
        int vpHeight)
    {
        if (neededIndex < 0)
        {
            cachedIndex = -1;

            return;
        }

        var layerWidth = HeaFile!.GetLayerWidth(neededIndex);

        if (cache is null || (cache.GetLength(0) != vpHeight) || (cache.GetLength(1) != layerWidth))
            cache = new byte[vpHeight, layerWidth];

        cachedIndex = neededIndex;
    }

    /// <summary>
    ///     Called when the server sends a LightLevel packet. Updates the darkness alpha and color based on the current map's
    ///     light type metadata.
    /// </summary>
    public void OnLightLevel(LightLevel lightLevel)
    {
        LastLightLevel = lightLevel;
        var enumHex = ((byte)lightLevel).ToString("X");
        var key = $"{CurrentLightType}_{enumHex}".ToLowerInvariant();

        if (LightData?.LightProperties.TryGetValue(key, out var props) is true && (props.Alpha < 32))
        {
            Alpha = (32 - props.Alpha) / 32f;
            DarknessColor = new Color(props.R, props.G, props.B);
        } else if (IsDarkMap)
        {
            // Dark map with no light metadata — pure black darkness
            Alpha = 1f;
            DarknessColor = Color.Black;
        } else
        {
            Alpha = 0f;
            DarknessColor = Color.Transparent;
        }

        // Invalidate dirty tracking so pixels are recomputed with updated alpha/color
        LastOffsetX = int.MinValue;
        LastOffsetY = int.MinValue;

        if (HeaFile is null || (Alpha <= 0f))
        {
            DisposeTexture();
            CacheValid = false;
            CachedLayerIndex0 = -1;
            CachedLayerIndex1 = -1;
            CachedLayer0 = null;
            CachedLayer1 = null;
        }
    }

    /// <summary>
    ///     Called on map change. Looks up the map's light type and loads the HEA file if one exists.
    /// </summary>
    public void OnMapChanged(short mapId, bool isDarkMap)
    {
        IsDarkMap = isDarkMap;
        CurrentLightType = LightData?.MapLightTypes.TryGetValue(mapId, out var lightType) is true ? lightType : "default";

        Alpha = 0f;
        DarknessColor = Color.Transparent;
        LastOffsetX = int.MinValue;
        LastOffsetY = int.MinValue;
        DisposeTexture();

        CacheValid = false;
        CachedLayerIndex0 = -1;
        CachedLayerIndex1 = -1;
        CachedLayer0 = null;
        CachedLayer1 = null;

        HeaFile = TryLoadHeaFile(mapId);
    }

    /// <summary>
    ///     Reapplies the last received light level. Called from FinalizeMapLoad to handle the case where LightLevel arrives
    ///     before MapInfo (e.g. initial login).
    /// </summary>
    public void ReapplyLightLevel() => OnLightLevel(LastLightLevel);

    private void RebuildFlatWithLights(Rectangle viewport)
    {
        var vpWidth = viewport.Width;
        var vpHeight = viewport.Height;

        if ((vpWidth <= 0) || (vpHeight <= 0))
            return;

        // Dirty check — skip rebuild if light sources haven't changed
        if (LastOffsetX != int.MinValue)
            return;

        var pixelCount = vpWidth * vpHeight;

        if (Texture is null || Texture.IsDisposed || (Texture.Width != vpWidth) || (Texture.Height != vpHeight))
        {
            Texture?.Dispose();
            Texture = new Texture2D(Device, vpWidth, vpHeight);
        }

        if (Pixels is null || (Pixels.Length < pixelCount))
            Pixels = new Color[pixelCount];

        // Fill with flat darkness — use same alpha encoding as ComputePixelsFromCache
        var ambientAlpha32 = (byte)(32 * (1f - Alpha));
        var flatAlpha = (byte)(255 - ambientAlpha32 * 255 / 32);

        var darkColor = new Color(
            DarknessColor.R,
            DarknessColor.G,
            DarknessColor.B,
            flatAlpha);

        for (var i = 0; i < pixelCount; i++)
            Pixels[i] = darkColor;

        // Stamp light sources
        StampLightSources(vpWidth, vpHeight);

        Texture.SetData(Pixels, 0, pixelCount);
        LastOffsetX = 0;
        LastOffsetY = 0;
    }

    private void RebuildTexture(Camera camera, Rectangle viewport = default)
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
            return;

        var ambientAlpha32 = (byte)(32 * (1f - Alpha));

        var viewportTopLeft = camera.ScreenToWorld(Vector2.Zero);
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
            CacheValid = false;
        }

        var pixelCount = vpWidth * vpHeight;

        if (Pixels is null || (Pixels.Length < pixelCount))
            Pixels = new Color[pixelCount];

        // Determine which layers the viewport overlaps
        (var leftLayer, var rightLayer) = DetermineViewportLayers(heaOffsetX, vpWidth);

        // Capture old indices before EnsureLayerCache mutates them — needed for layersMatch check
        var prevLayerIndex0 = CachedLayerIndex0;
        var prevLayerIndex1 = CachedLayerIndex1;

        // Allocate/reuse layer caches
        EnsureLayerCache(
            ref CachedLayer0,
            ref CachedLayerIndex0,
            leftLayer,
            vpHeight);

        if (rightLayer != leftLayer)
            EnsureLayerCache(
                ref CachedLayer1,
                ref CachedLayerIndex1,
                rightLayer,
                vpHeight);
        else
        {
            CachedLayerIndex1 = -1;
            CachedLayer1 = null;
        }

        // Check if incremental update is possible
        var dy = heaOffsetY - CacheBaseY;
        var expectedIndex1 = leftLayer != rightLayer ? rightLayer : -1;
        var layersMatch = (prevLayerIndex0 == leftLayer) && (prevLayerIndex1 == expectedIndex1);
        var canIncrement = CacheValid && layersMatch && (Math.Abs(dy) < vpHeight);

        if (canIncrement && (dy != 0))
        {
            // Shift cached rows and decode only new rows
            if (CachedLayerIndex0 >= 0)
                ShiftAndDecodeRows(
                    CachedLayer0!,
                    CachedLayerIndex0,
                    heaOffsetY,
                    dy,
                    vpHeight);

            if (CachedLayerIndex1 >= 0)
                ShiftAndDecodeRows(
                    CachedLayer1!,
                    CachedLayerIndex1,
                    heaOffsetY,
                    dy,
                    vpHeight);
        } else if (!canIncrement)
        {
            // Full decode — layers changed, large shift, or first frame
            if (CachedLayerIndex0 >= 0)
                DecodeLayerRows(
                    CachedLayerIndex0,
                    heaOffsetY,
                    0,
                    vpHeight,
                    CachedLayer0!);

            if (CachedLayerIndex1 >= 0)
                DecodeLayerRows(
                    CachedLayerIndex1,
                    heaOffsetY,
                    0,
                    vpHeight,
                    CachedLayer1!);
        }

        // else: canIncrement && dy == 0 → only X shifted within same layers, cache is valid as-is

        CacheBaseY = heaOffsetY;
        CacheValid = true;

        // Compute pixels from cache
        ComputePixelsFromCache(
            heaOffsetX,
            vpWidth,
            vpHeight,
            ambientAlpha32);

        // Stamp lantern/dynamic light sources via max-blend
        StampLightSources(vpWidth, vpHeight);

        Texture.SetData(Pixels, 0, pixelCount);
        LastOffsetX = worldOffsetX;
        LastOffsetY = worldOffsetY;
    }

    /// <summary>
    ///     Reloads light metadata from disk. Call after metadata sync completes.
    /// </summary>
    public void ReloadMetadata() => LightData = DataContext.MetaFiles.GetLightMetadata();

    /// <summary>
    ///     Sets the light sources for the current frame. Call before Update() each frame. Light sources are screen-space
    ///     positioned masks that brighten the darkness overlay via max-blend.
    /// </summary>
    public void SetLightSources(ReadOnlySpan<LightSource> sources)
    {
        if (sources.Length > LightSources.Length)
            LightSources = new LightSource[sources.Length];

        sources.CopyTo(LightSources);
        LightSourceCount = sources.Length;

        // Compute a cheap hash to detect changes — position + count
        var hash = LightSourceCount;

        for (var i = 0; i < LightSourceCount; i++)
        {
            var src = LightSources[i];

            hash = HashCode.Combine(
                hash,
                (int)src.ScreenPosition.X,
                (int)src.ScreenPosition.Y,
                src.Mask.Width);
        }

        if (hash != LastLightSourceHash)
        {
            LastLightSourceHash = hash;
            LastOffsetX = int.MinValue;
            LastOffsetY = int.MinValue;
        }

        // Clean up flat-dark texture when no longer needed
        if ((LightSourceCount == 0) && HeaFile is null)
        {
            DisposeTexture();
            Pixels = null;
        }
    }

    private void ShiftAndDecodeRows(
        byte[,] cache,
        int layerIndex,
        int newHeaOffsetY,
        int dy,
        int vpHeight)
    {
        var layerWidth = HeaFile!.GetLayerWidth(layerIndex);
        var absDy = Math.Abs(dy);

        if (dy > 0)
        {
            // Camera moved down — shift rows up, decode new rows at bottom
            Array.Copy(
                cache,
                dy * layerWidth,
                cache,
                0,
                (vpHeight - absDy) * layerWidth);

            DecodeLayerRows(
                layerIndex,
                newHeaOffsetY,
                vpHeight - absDy,
                absDy,
                cache);
        } else
        {
            // Camera moved up — shift rows down, decode new rows at top
            Array.Copy(
                cache,
                0,
                cache,
                absDy * layerWidth,
                (vpHeight - absDy) * layerWidth);

            DecodeLayerRows(
                layerIndex,
                newHeaOffsetY,
                0,
                absDy,
                cache);
        }
    }

    private void StampLightSources(int vpWidth, int vpHeight)
    {
        if (LightSourceCount == 0)
            return;

        var pixels = Pixels!;
        var darkR = DarknessColor.R;
        var darkG = DarknessColor.G;
        var darkB = DarknessColor.B;

        for (var i = 0; i < LightSourceCount; i++)
        {
            var source = LightSources[i];
            var mask = source.Mask;

            // Mask rect centered on screen position
            var maskLeft = (int)source.ScreenPosition.X - mask.Width / 2;
            var maskTop = (int)source.ScreenPosition.Y - mask.Height / 2;

            // Clip to viewport
            var startX = Math.Max(0, -maskLeft);
            var startY = Math.Max(0, -maskTop);
            var endX = Math.Min(mask.Width, vpWidth - maskLeft);
            var endY = Math.Min(mask.Height, vpHeight - maskTop);

            if ((startX >= endX) || (startY >= endY))
                continue;

            for (var my = startY; my < endY; my++)
            {
                var vpY = maskTop + my;
                var maskRowOffset = my * mask.Width;
                var pixelRowOffset = vpY * vpWidth;

                for (var mx = startX; mx < endX; mx++)
                {
                    var maskValue = mask.Pixels[maskRowOffset + mx];

                    if (maskValue == 0)
                        continue;

                    var vpX = maskLeft + mx;
                    var pixelIndex = pixelRowOffset + vpX;

                    // Reverse the existing alpha to get the current effective 0-32 value
                    var currentAlpha = pixels[pixelIndex].A;
                    var currentEffective = (byte)(32 - currentAlpha * 32 / 255);

                    if (maskValue <= currentEffective)
                        continue;

                    // Recompute the pixel with the brighter value
                    var newAlpha = (byte)(255 - maskValue * 255 / 32);

                    pixels[pixelIndex] = new Color(
                        darkR,
                        darkG,
                        darkB,
                        newAlpha);
                }
            }
        }
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
        if (Alpha <= 0f)
            return;

        if (HeaFile is not null)
            RebuildTexture(camera, viewport);
        else if (LightSourceCount > 0)
            RebuildFlatWithLights(viewport);
    }
}
#region
using System.Buffers;
using Chaos.Client.Data;
using Chaos.DarkAges.Definitions;
using DALib.Drawing;
using DALib.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Cached UI texture renderer. Deduplicates GPU textures across all UI consumers via a dictionary-backed cache. All
///     returned textures are <see cref="CachedTexture2D" /> whose Dispose is a no-op — only this renderer can release GPU
///     memory. Single instance owned by ChaosGame, exposed via <see cref="Instance" />.
/// </summary>
public sealed class UiRenderer : IDisposable
{
    private const int CHECKER_SIZE = 32;
    private const int CELL_SIZE = 4;
    private static readonly Color CheckerA = new(255, 0, 255); // neon purple
    private static readonly Color CheckerB = new(0, 255, 0); // neon green

    private const int MAX_ATLAS_ENTRY_SIZE = 512;

    private readonly Dictionary<string, CachedTexture2D> Cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly GraphicsDevice Device;
    private readonly Dictionary<string, int> EpfFrameCounts = new(StringComparer.OrdinalIgnoreCase);
    private CachedTexture2D? MissingTextureField;
    private TextureAtlas? UiAtlas;

    public static UiRenderer? Instance { get; set; }

    /// <summary>
    ///     A neon green/purple checkerboard texture returned when an asset fails to load. 32x32, 4px cells.
    /// </summary>
    public Texture2D MissingTexture => MissingTextureField ??= CreateMissingTexture();

    public UiRenderer(GraphicsDevice device) => Device = device;

    public void Dispose() => Clear();

    /// <summary>
    ///     Builds a texture atlas from all currently cached UI textures. Textures larger than 512px in either dimension are
    ///     skipped. After building, CachedTexture2D entries have their AtlasRegion set so AtlasHelper.Draw() can route draws
    ///     through the atlas. Safe to call multiple times — rebuilds from scratch each time.
    /// </summary>
    public void BuildAtlas()
    {
        // Dispose previous atlas and clear regions
        UiAtlas?.Dispose();
        UiAtlas = null;

        foreach (var texture in Cache.Values)
            texture.AtlasRegion = null;

        var atlas = new TextureAtlas(Device, PackingMode.Shelf);

        foreach ((var key, var texture) in Cache)
        {
            if ((texture.Width > MAX_ATLAS_ENTRY_SIZE) || (texture.Height > MAX_ATLAS_ENTRY_SIZE))
                continue;

            if (texture.IsDisposed)
                continue;

            atlas.Add(key, texture);
        }

        atlas.Build();

        // Set AtlasRegion on each cached texture that was packed
        foreach ((var key, var texture) in Cache)
        {
            var region = atlas.TryGetRegion(key);

            if (region.HasValue)
                texture.AtlasRegion = region.Value;
        }

        UiAtlas = atlas;
    }

    public void Clear()
    {
        UiAtlas?.Dispose();
        UiAtlas = null;

        foreach (var texture in Cache.Values)
        {
            texture.AtlasRegion = null;
            texture.ForceDispose();
        }

        Cache.Clear();
        EpfFrameCounts.Clear();

        MissingTextureField?.ForceDispose();
        MissingTextureField = null;
    }

    private CachedTexture2D Convert(SKImage image)
        => TextureConverter.ConvertImage(image, static (d, w, h) => new CachedTexture2D(d, w, h));

    private CachedTexture2D CreateMissingTexture()
    {
        var pixels = new Color[CHECKER_SIZE * CHECKER_SIZE];

        for (var y = 0; y < CHECKER_SIZE; y++)
            for (var x = 0; x < CHECKER_SIZE; x++)
            {
                var cellX = x / CELL_SIZE;
                var cellY = y / CELL_SIZE;
                pixels[y * CHECKER_SIZE + x] = ((cellX + cellY) % 2) == 0 ? CheckerA : CheckerB;
            }

        var texture = new CachedTexture2D(Device, CHECKER_SIZE, CHECKER_SIZE);
        texture.SetData(pixels);

        return texture;
    }

    private CachedTexture2D CreateTintedTexture(Texture2D source)
    {
        var count = source.Width * source.Height;
        var pixels = ArrayPool<Color>.Shared.Rent(count);

        try
        {
            source.GetData(pixels, 0, count);
            TextureConverter.TintPixels(pixels, count);

            var tinted = new CachedTexture2D(Device, source.Width, source.Height);
            tinted.SetData(pixels, 0, count);

            return tinted;
        } finally
        {
            ArrayPool<Color>.Shared.Return(pixels);
        }
    }

    /// <summary>
    ///     Returns the number of frames in an EPF file. Triggers a bulk-cache load if not yet loaded.
    /// </summary>
    public int GetEpfFrameCount(string fileName)
    {
        if (EpfFrameCounts.TryGetValue(fileName, out var count))
            return count;

        GetEpfTexture(fileName, 0);

        return EpfFrameCounts.GetValueOrDefault(fileName);
    }

    /// <summary>
    ///     Loads a single EPF frame from setoa.dat (GUI palette) and caches the resulting texture. The first call for a given
    ///     file bulk-caches all frames.
    /// </summary>
    public Texture2D GetEpfTexture(string fileName, int frameIndex)
    {
        var key = $"epf:{fileName}:{frameIndex}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var images = DataContext.UserControls.GetEpfImages(fileName);

        // Bulk-cache all frames, then dispose all SKImages
        for (var i = 0; i < images.Length; i++)
        {
            var frameKey = $"epf:{fileName}:{i}";

            if (!Cache.ContainsKey(frameKey))
                Cache[frameKey] = Convert(images[i]);

            images[i]
                .Dispose();
        }

        EpfFrameCounts[fileName] = images.Length;

        return Cache.GetValueOrDefault(key) ?? MissingTexture;
    }

    /// <summary>
    ///     Loads and caches a field image (EPF + matching PAL from setoa.dat). Used for world map backgrounds.
    /// </summary>
    public Texture2D GetFieldImage(string fieldName)
    {
        var key = $"field:{fieldName}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        using var image = DataContext.UserControls.GetFieldImage(fieldName);

        if (image is null)
            return MissingTexture;

        var texture = Convert(image);
        Cache[key] = texture;

        return texture;
    }

    /// <summary>
    ///     Renders and caches a half-size (15x15) spell icon for the effect bar.
    /// </summary>
    public Texture2D GetHalfSizeSpellIcon(byte iconId)
    {
        var key = $"spell_half:{iconId}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var palettized = DataContext.PanelIcons.GetSpellIcon(iconId);

        if (palettized is null)
            return MissingTexture;

        using var fullImage = Graphics.RenderImage(palettized.Entity, palettized.Palette);

        if (fullImage is null)
            return MissingTexture;

        const int HALF_ICON_SIZE = 15;

        var info = new SKImageInfo(
            HALF_ICON_SIZE,
            HALF_ICON_SIZE,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);

        surface.Canvas.DrawImage(
            fullImage,
            new SKRect(
                0,
                0,
                fullImage.Width,
                fullImage.Height),
            new SKRect(
                0,
                0,
                HALF_ICON_SIZE,
                HALF_ICON_SIZE));

        using var halfImage = surface.Snapshot();
        var texture = Convert(halfImage);
        Cache[key] = texture;

        return texture;
    }

    /// <summary>
    ///     Renders and caches an inventory/equipment item icon.
    /// </summary>
    public Texture2D GetItemIcon(ushort spriteId, DisplayColor color = DisplayColor.Default)
    {
        var key = color == DisplayColor.Default ? $"item:{spriteId}" : $"item:{spriteId}:{(int)color}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var palettized = DataContext.PanelItems.GetPanelItemSprite(spriteId);

        if (palettized is not null && (color != DisplayColor.Default))
        {
            var dyedPalette = DataContext.AislingData.ApplyDye(palettized.Palette, color);

            if (dyedPalette != palettized.Palette)
                palettized = new Palettized<EpfFrame>
                {
                    Entity = palettized.Entity,
                    Palette = dyedPalette
                };
        }

        var texture = RenderSprite(palettized);

        if (texture is null)
            return MissingTexture;

        Cache[key] = texture;

        return texture;
    }

    /// <summary>
    ///     Loads a single SPF frame from national.dat and caches the resulting texture.
    /// </summary>
    public Texture2D GetNationalSpfTexture(string fileName, int frameIndex = 0)
    {
        var key = $"nspf:{fileName}:{frameIndex}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        using var image = DataContext.UserControls.GetNationalSpfImage(fileName, frameIndex);

        if (image is null)
            return MissingTexture;

        var texture = Convert(image);
        Cache[key] = texture;

        return texture;
    }

    /// <summary>
    ///     Returns a cached texture for a specific image within a ControlPrefabSet control.
    /// </summary>
    public Texture2D GetPrefabTexture(string prefabSetName, string controlName, int imageIndex)
    {
        var key = $"prefab:{prefabSetName}:{controlName}:{imageIndex}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var prefabSet = DataContext.UserControls.Get(prefabSetName);

        if (prefabSet is null || !prefabSet.Contains(controlName))
            return MissingTexture;

        var prefab = prefabSet[controlName];

        if ((imageIndex < 0) || (imageIndex >= prefab.Images.Count))
            return MissingTexture;

        var texture = Convert(prefab.Images[imageIndex]);
        Cache[key] = texture;

        return texture;
    }

    /// <summary>
    ///     Renders and caches a grey skill icon (used as cooldown base).
    /// </summary>
    public Texture2D GetSkillGreyIcon(ushort spriteId)
    {
        var key = $"skill_grey:{spriteId}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var texture = RenderSprite(DataContext.PanelIcons.GetSkillLockedIcon(spriteId));

        if (texture is null)
            return MissingTexture;

        Cache[key] = texture;

        return texture;
    }

    /// <summary>
    ///     Renders and caches a skill icon.
    /// </summary>
    public Texture2D GetSkillIcon(ushort spriteId)
    {
        var key = $"skill:{spriteId}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var texture = RenderSprite(DataContext.PanelIcons.GetSkillIcon(spriteId));

        if (texture is null)
            return MissingTexture;

        Cache[key] = texture;

        return texture;
    }

    /// <summary>
    ///     Renders and caches a skill icon (learnable/002 variant).
    /// </summary>
    public Texture2D GetSkillLearnableIcon(ushort spriteId)
    {
        var key = $"skill_learnable:{spriteId}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var texture = RenderSprite(DataContext.PanelIcons.GetSkillLearnableIcon(spriteId));

        if (texture is null)
            return MissingTexture;

        Cache[key] = texture;

        return texture;
    }

    /// <summary>
    ///     Renders and caches a skill icon (locked/003 variant).
    /// </summary>
    public Texture2D GetSkillLockedIcon(ushort spriteId)
    {
        var key = $"skill_locked:{spriteId}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var texture = RenderSprite(DataContext.PanelIcons.GetSkillLockedIcon(spriteId));

        if (texture is null)
            return MissingTexture;

        Cache[key] = texture;

        return texture;
    }

    /// <summary>
    ///     Renders and caches a spell icon.
    /// </summary>
    public Texture2D GetSpellIcon(ushort spriteId)
    {
        var key = $"spell:{spriteId}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var texture = RenderSprite(DataContext.PanelIcons.GetSpellIcon(spriteId));

        if (texture is null)
            return MissingTexture;

        Cache[key] = texture;

        return texture;
    }

    /// <summary>
    ///     Renders and caches a spell icon (learnable/002 variant).
    /// </summary>
    public Texture2D GetSpellLearnableIcon(ushort spriteId)
    {
        var key = $"spell_learnable:{spriteId}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var texture = RenderSprite(DataContext.PanelIcons.GetSpellLearnableIcon(spriteId));

        if (texture is null)
            return MissingTexture;

        Cache[key] = texture;

        return texture;
    }

    /// <summary>
    ///     Renders and caches a spell icon (locked/003 variant).
    /// </summary>
    public Texture2D GetSpellLockedIcon(ushort spriteId)
    {
        var key = $"spell_locked:{spriteId}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var texture = RenderSprite(DataContext.PanelIcons.GetSpellLockedIcon(spriteId));

        if (texture is null)
            return MissingTexture;

        Cache[key] = texture;

        return texture;
    }

    /// <summary>
    ///     Loads a single SPF frame from setoa.dat and caches the resulting texture.
    /// </summary>
    public Texture2D GetSpfTexture(string fileName, int frameIndex = 0)
    {
        var key = $"spf:{fileName}:{frameIndex}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        using var image = DataContext.UserControls.GetSpfImage(fileName, frameIndex);

        if (image is null)
            return MissingTexture;

        var texture = Convert(image);
        Cache[key] = texture;

        return texture;
    }

    /// <summary>
    ///     Returns a cached blue-shifted copy of the texture identified by sourceKey.
    /// </summary>
    public Texture2D GetTintedTexture(string sourceKey, Texture2D source)
    {
        var key = $"tinted:{sourceKey}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var texture = CreateTintedTexture(source);
        Cache[key] = texture;

        return texture;
    }

    private CachedTexture2D? RenderSprite(Palettized<EpfFrame>? palettized)
    {
        if (palettized is null)
            return null;

        using var image = Graphics.RenderImage(palettized.Entity, palettized.Palette);

        return image is not null ? Convert(image) : null;
    }
}
#region
using Chaos.Client.Data;
using Chaos.Client.Data.Repositories;
using Chaos.DarkAges.Definitions;
using DALib.Definitions;
using DALib.Drawing;
using DALib.Drawing.Virtualized;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Visual appearance data for rendering an aisling. Set sprite IDs to 0 to skip that layer.
/// </summary>
public record struct AislingAppearance
{
    public DisplayColor Accessory1Color { get; init; }
    public int Accessory1Sprite { get; init; }
    public DisplayColor Accessory2Color { get; init; }
    public int Accessory2Sprite { get; init; }
    public DisplayColor Accessory3Color { get; init; }
    public int Accessory3Sprite { get; init; }
    public DisplayColor ArmorColor { get; init; }
    public int ArmorSprite { get; init; }
    public int BodyColor { get; init; }
    public DisplayColor BootsColor { get; init; }
    public int BootsSprite { get; init; }
    public int FaceSprite { get; init; }
    public required Gender Gender { get; init; }
    public DisplayColor HeadColor { get; init; }
    public int HeadSprite { get; init; }
    public DisplayColor OvercoatColor { get; init; }
    public int OvercoatSprite { get; init; }
    public DisplayColor? PantsColor { get; init; }
    public int ShieldSprite { get; init; }
    public int WeaponSprite { get; init; }
    internal char GenderPrefix => IsMale ? 'm' : 'w';

    internal bool IsMale => Gender == Gender.Male;
    internal KhanPalOverrideType OverrideType => IsMale ? KhanPalOverrideType.Male : KhanPalOverrideType.Female;
}

/// <summary>
///     A cached per-layer texture with the positioning metadata needed at draw time.
/// </summary>
public readonly record struct AislingLayerTexture(Texture2D Texture, char TypeLetter);

/// <summary>
///     Cache key that uniquely identifies a single rendered layer texture.
/// </summary>
public readonly record struct LayerCacheKey(
    char TypeLetter,
    int SpriteId,
    int FrameIndex,
    bool IsMale,
    int PaletteKey,
    string AnimSuffix);

/// <summary>
///     All the draw data needed to render an aisling as individual layer SpriteBatch draws.
/// </summary>
public sealed class AislingDrawData
{
    public LayerSlot[] DrawOrder = [];
    public bool FlipHorizontal;
    public AislingLayerTexture?[] Layers = new AislingLayerTexture?[(int)LayerSlot.Count];
}

/// <summary>
///     Layer slots for aisling composite ordering. Each slot is one visual layer.
/// </summary>
public enum LayerSlot
{
    BodyB,
    Body,
    Pants,
    Face,
    Boots,
    HeadH,
    HeadE,
    HeadF,
    Armor,
    Arms,
    WeaponW,
    WeaponP,
    Shield,
    Acc1C,
    Acc1G,
    Acc2C,
    Acc2G,
    Acc3C,
    Acc3G,
    Emotion,
    Count
}

/// <summary>
///     Renders aisling sprites by compositing body, equipment, and hair layers. All layers are positioned relative to body
///     center (BODY_CENTER_X, BODY_CENTER_Y). Horizontal flipping (for Down/Left directions) mirrors around the body
///     center.
///     <br />
///     Front-facing layer order (walk frames 5-9): f → acc1G → acc2G → body → pants → face → boots → headH → armor+arms →
///     headE → weaponW+P → shield → acc1C → acc2C
///     <br />
///     Back-facing layer order (walk frames 0-4): acc1G → acc2G → shield → body → face → pants → boots → armor+arms →
///     headE → headH → headF → weaponW+P → acc1C → acc2C
/// </summary>
public sealed class AislingRenderer : IDisposable
{
    private const int BODY_ID = 1;
    private const int PANTS_ID = 1;
    private const int MAX_MALE_HAIR_STYLE = 18;
    private const int MAX_FEMALE_HAIR_STYLE = 17;
    private const int MAX_HAIR_COLOR = 13;
    private const string WALK_ANIM = "01";
    private const string IDLE_ANIM = "04";
    public const string PEASANT_ANIM_SUFFIX = "03";

    public const int BODY_WIDTH = 57;
    public const int BODY_HEIGHT = 85;
    public const int LAYER_OFFSET_PADDING = 27;
    public const int COMPOSITE_WIDTH = BODY_WIDTH + LAYER_OFFSET_PADDING * 2;
    public const int COMPOSITE_HEIGHT = BODY_HEIGHT;
    public const int BODY_CENTER_X = BODY_WIDTH / 2;
    public const int BODY_CENTER_Y = BODY_HEIGHT / 2;

    // EPFs contain frames for 2 base directions only:
    //   Frames 0-4: Up (away-facing)
    //   Frames 5-9: Right (front-facing)
    // Down = Right frames + horizontal flip, Left = Up frames + horizontal flip
    private const int UP_IDLE_FRAME = 0;
    private const int RIGHT_IDLE_FRAME = 5;

    private static readonly HashSet<int> WALK_FRONT_FRAMES =
    [
        5,
        6,
        7,
        8,
        9
    ];

    // Front-facing composite order (first drawn = back-most)
    public static readonly LayerSlot[] FRONT_ORDER =
    [
        LayerSlot.BodyB,
        LayerSlot.HeadF,
        LayerSlot.Acc1G,
        LayerSlot.Acc2G,
        LayerSlot.Acc3G,
        LayerSlot.Body,
        LayerSlot.Pants,
        LayerSlot.Face,
        LayerSlot.Emotion,
        LayerSlot.Boots,
        LayerSlot.HeadH,
        LayerSlot.Armor,
        LayerSlot.Arms,
        LayerSlot.HeadE,
        LayerSlot.WeaponW,
        LayerSlot.WeaponP,
        LayerSlot.Shield,
        LayerSlot.Acc1C,
        LayerSlot.Acc2C,
        LayerSlot.Acc3C
    ];

    // Back-facing composite order
    public static readonly LayerSlot[] BACK_ORDER =
    [
        LayerSlot.BodyB,
        LayerSlot.Acc1G,
        LayerSlot.Acc2G,
        LayerSlot.Acc3G,
        LayerSlot.Shield,
        LayerSlot.Body,
        LayerSlot.Face,
        LayerSlot.Pants,
        LayerSlot.Boots,
        LayerSlot.Armor,
        LayerSlot.Arms,
        LayerSlot.HeadE,
        LayerSlot.HeadH,
        LayerSlot.HeadF,
        LayerSlot.WeaponW,
        LayerSlot.WeaponP,
        LayerSlot.Acc1C,
        LayerSlot.Acc2C,
        LayerSlot.Acc3C
    ];

    private readonly AislingDataRepository Data = DataContext.AislingData;
    private readonly EpfView? EmotionsEpf = LoadEmotionsEpf();
    private readonly Dictionary<LayerCacheKey, AislingLayerTexture> LayerTextureCache = new();
    private readonly LayerInfo?[] RenderLayers = new LayerInfo?[(int)LayerSlot.Count];
    private readonly Dictionary<Texture2D, Texture2D> TintedTextureCache = new();

    /// <inheritdoc />
    public void Dispose()
    {
        ClearCache();
        ClearLayerCache();
    }

    /// <summary>
    ///     Clears the cached EPF files. Call on map change to free memory.
    /// </summary>
    public void ClearCache() => Data.ClearEpfCache();

    /// <summary>
    ///     Clears the cached per-layer textures. Call on map change to free GPU memory.
    /// </summary>
    public void ClearLayerCache()
    {
        foreach (var entry in LayerTextureCache.Values)
            entry.Texture.Dispose();

        LayerTextureCache.Clear();
        ClearTintedCache();
    }

    /// <summary>
    ///     Clears all cached tinted textures. Call when the highlighted entity changes.
    /// </summary>
    public void ClearTintedCache()
    {
        foreach (var texture in TintedTextureCache.Values)
            texture.Dispose();

        TintedTextureCache.Clear();
    }

    /// <summary>
    ///     Returns the X draw offset for a layer type. Weapons (w/p) and accessories (c/g) are shifted left by 27px relative
    ///     to the body center to align correctly.
    /// </summary>
    public static int GetLayerOffsetX(char typeLetter) => typeLetter is 'w' or 'p' or 'c' or 'g' ? -27 : 0;

    /// <summary>
    ///     Returns a tinted (blue-shifted) copy of a layer texture, caching it for reuse.
    /// </summary>
    public Texture2D GetOrCreateTintedTexture(Texture2D source)
    {
        if (TintedTextureCache.TryGetValue(source, out var tinted))
            return tinted;

        tinted = TextureConverter.CreateTintedTexture(source);
        TintedTextureCache[source] = tinted;

        return tinted;
    }

    /// <summary>
    ///     Returns true if a frame index represents a front-facing direction for the given animation suffix.
    /// </summary>
    public static bool IsFrontFacing(int frameIndex, string animSuffix)
        => animSuffix switch
        {
            "01" => WALK_FRONT_FRAMES.Contains(frameIndex),
            "02" => frameIndex is 2 or 3,
            "03" => frameIndex is 1 or 4 or 5 or 8 or 9,
            _    => frameIndex >= 5
        };

    private static EpfView? LoadEmotionsEpf()
    {
        if (!DatArchives.Legend.TryGetValue("emot01.epf", out var entry))
            return null;

        return EpfView.FromEntry(entry);
    }

    #region Palette Resolution
    private Palette? ResolvePalette(
        PaletteLookup lookup,
        int spriteId,
        DisplayColor dyeColor,
        KhanPalOverrideType overrideType)
    {
        var paletteNumber = lookup.Table.GetPaletteNumber(spriteId, overrideType);

        if (paletteNumber >= 1000)
            paletteNumber -= 1000;

        if (!lookup.Palettes.TryGetValue(paletteNumber, out var basePalette))
            return null;

        return Data.ApplyDye(basePalette, dyeColor);
    }
    #endregion

    /// <summary>
    ///     A rendered layer with its EpfFrame positioning metadata preserved (composited rendering path only).
    /// </summary>
    private readonly record struct LayerInfo(
        SKImage Image,
        short FrameLeft,
        short FrameTop,
        char TypeLetter) : IDisposable
    {
        public void Dispose() => Image.Dispose();
    }

    #region Per-Layer Rendering (GPU draw path)
    /// <summary>
    ///     Resolves all layers for an aisling into individually cached textures. Returns draw data that can be rendered via
    ///     multiple SpriteBatch.Draw() calls — no SkiaSharp compositing needed.
    /// </summary>
    public AislingDrawData GetLayerFrames(
        in AislingAppearance appearance,
        int frameIndex,
        string animSuffix = WALK_ANIM,
        bool flipHorizontal = false,
        bool? isFrontFacing = null,
        int emotionFrame = -1)
    {
        var drawData = new AislingDrawData
        {
            FlipHorizontal = flipHorizontal
        };
        var isFront = isFrontFacing ?? IsFrontFacing(frameIndex, animSuffix);
        drawData.DrawOrder = isFront ? FRONT_ORDER : BACK_ORDER;

        var idleFallbackFrame = animSuffix == IDLE_ANIM ? isFront ? RIGHT_IDLE_FRAME : UP_IDLE_FRAME : -1;

        ResolveAllLayers(
            drawData.Layers,
            in appearance,
            frameIndex,
            animSuffix,
            idleFallbackFrame);

        // Emotion overlay — only on front-facing frames
        if (isFront && (emotionFrame >= 0) && EmotionsEpf is not null && (emotionFrame < EmotionsEpf.Count))
        {
            var frame = EmotionsEpf[emotionFrame];

            if (Data.BodyPalettes.TryGetValue(appearance.BodyColor, out var palette))
            {
                var paletteKey = appearance.BodyColor;

                var cacheKey = new LayerCacheKey(
                    'o',
                    0,
                    emotionFrame,
                    appearance.IsMale,
                    paletteKey,
                    "em");

                drawData.Layers[(int)LayerSlot.Emotion] = GetOrCreateLayerTexture(
                    cacheKey,
                    frame,
                    palette,
                    'o');
            }
        }

        return drawData;
    }

    private void ResolveAllLayers(
        AislingLayerTexture?[] layers,
        in AislingAppearance appearance,
        int frameIndex,
        string anim,
        int idleFallbackFrame = -1)
    {
        // Beast body (type b, always id 1) — behind all other layers
        layers[(int)LayerSlot.BodyB] = ResolveEquipLayerTexture(
            'b',
            BODY_ID,
            DisplayColor.Default,
            in appearance,
            frameIndex,
            anim,
            idleFallbackFrame);

        // Body (type m, always id 1)
        layers[(int)LayerSlot.Body] = ResolveBodyPaletteLayerTexture(
            'm',
            BODY_ID,
            in appearance,
            frameIndex,
            anim,
            idleFallbackFrame);

        // Pants (type n, always id 1) — only if server sent a pants color
        if (appearance.PantsColor.HasValue)
            layers[(int)LayerSlot.Pants] = ResolveEquipLayerTexture(
                'n',
                PANTS_ID,
                appearance.PantsColor.Value,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

        // Face (type o, uses body palette)
        if (appearance.FaceSprite > 0)
            layers[(int)LayerSlot.Face] = ResolveBodyPaletteLayerTexture(
                'o',
                appearance.FaceSprite,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

        // Boots
        if (appearance.BootsSprite > 0)
            layers[(int)LayerSlot.Boots] = ResolveEquipLayerTexture(
                'l',
                appearance.BootsSprite,
                appearance.BootsColor,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

        // Head layers (h, e, f)
        if (appearance.HeadSprite > 0)
        {
            layers[(int)LayerSlot.HeadH] = ResolveEquipLayerTexture(
                'h',
                appearance.HeadSprite,
                appearance.HeadColor,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

            layers[(int)LayerSlot.HeadE] = ResolveEquipLayerTexture(
                'e',
                appearance.HeadSprite,
                appearance.HeadColor,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

            layers[(int)LayerSlot.HeadF] = ResolveEquipLayerTexture(
                'f',
                appearance.HeadSprite,
                appearance.HeadColor,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);
        }

        // Armor/Overcoat
        if (appearance.OvercoatSprite > 0)
            ResolveArmorLayers(
                layers,
                appearance.OvercoatSprite,
                appearance.OvercoatColor,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);
        else if (appearance.ArmorSprite > 0)
            ResolveArmorLayers(
                layers,
                appearance.ArmorSprite,
                appearance.ArmorColor,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

        // Weapon (w + p sub-layers)
        if (appearance.WeaponSprite > 0)
        {
            layers[(int)LayerSlot.WeaponW] = ResolveEquipLayerTexture(
                'w',
                appearance.WeaponSprite,
                DisplayColor.Default,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

            layers[(int)LayerSlot.WeaponP] = ResolveEquipLayerTexture(
                'p',
                appearance.WeaponSprite,
                DisplayColor.Default,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);
        }

        // Shield
        if (appearance.ShieldSprite > 0)
            layers[(int)LayerSlot.Shield] = ResolveEquipLayerTexture(
                's',
                appearance.ShieldSprite,
                DisplayColor.Default,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

        // Accessories (c + g sub-layers each)
        if (appearance.Accessory1Sprite > 0)
        {
            layers[(int)LayerSlot.Acc1C] = ResolveEquipLayerTexture(
                'c',
                appearance.Accessory1Sprite,
                appearance.Accessory1Color,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

            layers[(int)LayerSlot.Acc1G] = ResolveEquipLayerTexture(
                'g',
                appearance.Accessory1Sprite,
                appearance.Accessory1Color,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);
        }

        if (appearance.Accessory2Sprite > 0)
        {
            layers[(int)LayerSlot.Acc2C] = ResolveEquipLayerTexture(
                'c',
                appearance.Accessory2Sprite,
                appearance.Accessory2Color,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

            layers[(int)LayerSlot.Acc2G] = ResolveEquipLayerTexture(
                'g',
                appearance.Accessory2Sprite,
                appearance.Accessory2Color,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);
        }

        if (appearance.Accessory3Sprite > 0)
        {
            layers[(int)LayerSlot.Acc3C] = ResolveEquipLayerTexture(
                'c',
                appearance.Accessory3Sprite,
                appearance.Accessory3Color,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

            layers[(int)LayerSlot.Acc3G] = ResolveEquipLayerTexture(
                'g',
                appearance.Accessory3Sprite,
                appearance.Accessory3Color,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);
        }
    }

    private void ResolveArmorLayers(
        AislingLayerTexture?[] layers,
        int spriteId,
        DisplayColor color,
        in AislingAppearance appearance,
        int frameIndex,
        string anim,
        int idleFallbackFrame = -1)
    {
        var isOverType = spriteId >= 1000;
        var adjustedId = isOverType ? spriteId - 1000 : spriteId;
        var bodyLetter = isOverType ? 'i' : 'u';
        var armsLetter = isOverType ? 'j' : 'a';

        layers[(int)LayerSlot.Armor] = ResolveEquipLayerTexture(
            bodyLetter,
            adjustedId,
            color,
            in appearance,
            frameIndex,
            anim,
            idleFallbackFrame);

        layers[(int)LayerSlot.Arms] = ResolveEquipLayerTexture(
            armsLetter,
            adjustedId,
            color,
            in appearance,
            frameIndex,
            anim,
            idleFallbackFrame);
    }

    private AislingLayerTexture? ResolveBodyPaletteLayerTexture(
        char typeLetter,
        int spriteId,
        in AislingAppearance appearance,
        int frameIndex,
        string anim,
        int idleFallbackFrame = -1)
    {
        if (spriteId <= 0)
            return null;

        (var epf, var resolvedFrame) = ResolveLayerEpf(
            typeLetter,
            spriteId,
            in appearance,
            frameIndex,
            anim,
            idleFallbackFrame);

        if (epf is null || (resolvedFrame < 0))
            return null;

        var frame = epf[resolvedFrame];

        if (!Data.BodyPalettes.TryGetValue(appearance.BodyColor, out var palette))
            return null;

        var paletteKey = appearance.BodyColor;

        var cacheKey = new LayerCacheKey(
            typeLetter,
            spriteId,
            resolvedFrame,
            appearance.IsMale,
            paletteKey,
            anim);

        return GetOrCreateLayerTexture(
            cacheKey,
            frame,
            palette,
            typeLetter);
    }

    private AislingLayerTexture? ResolveEquipLayerTexture(
        char typeLetter,
        int spriteId,
        DisplayColor dyeColor,
        in AislingAppearance appearance,
        int frameIndex,
        string anim,
        int idleFallbackFrame = -1)
    {
        if (spriteId <= 0)
            return null;

        (var epf, var resolvedFrame) = ResolveLayerEpf(
            typeLetter,
            spriteId,
            in appearance,
            frameIndex,
            anim,
            idleFallbackFrame);

        if (epf is null || (resolvedFrame < 0))
            return null;

        var frame = epf[resolvedFrame];
        var lookup = Data.GetPaletteLookup(typeLetter);

        var palette = ResolvePalette(
            lookup,
            spriteId,
            dyeColor,
            appearance.OverrideType);

        if (palette is null)
            return null;

        var paletteNumber = lookup.Table.GetPaletteNumber(spriteId, appearance.OverrideType);

        if (paletteNumber >= 1000)
            paletteNumber -= 1000;

        var paletteKey = paletteNumber * 256 + (int)dyeColor;

        var cacheKey = new LayerCacheKey(
            typeLetter,
            spriteId,
            resolvedFrame,
            appearance.IsMale,
            paletteKey,
            anim);

        return GetOrCreateLayerTexture(
            cacheKey,
            frame,
            palette,
            typeLetter);
    }

    private AislingLayerTexture? GetOrCreateLayerTexture(
        LayerCacheKey cacheKey,
        EpfFrame frame,
        Palette palette,
        char typeLetter)
    {
        if (LayerTextureCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var image = Graphics.RenderImage(frame, palette);

        if (image is null)
            return null;

        try
        {
            var texture = TextureConverter.ToTexture2D(image);

            var entry = new AislingLayerTexture(texture, typeLetter);
            LayerTextureCache[cacheKey] = entry;

            return entry;
        } finally
        {
            image.Dispose();
        }
    }
    #endregion

    #region Composited Rendering (paperdoll/preview path)
    /// <summary>
    ///     Composites all layers into a single image. Used for paperdoll and character creation preview.
    /// </summary>
    private static SKImage? Composite(LayerInfo?[] layers, LayerSlot[] order, bool flipHorizontal)
    {
        var width = COMPOSITE_WIDTH;
        var height = COMPOSITE_HEIGHT;

        using var bitmap = new SKBitmap(width, height);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);

            if (flipHorizontal)
                canvas.Scale(
                    -1,
                    1,
                    BODY_CENTER_X + LAYER_OFFSET_PADDING,
                    0);

            foreach (var slot in order)
            {
                if (layers[(int)slot] is not { } info)
                    continue;

                var offsetX = GetLayerOffsetX(info.TypeLetter) + LAYER_OFFSET_PADDING;
                canvas.DrawImage(info.Image, offsetX, 0);
            }
        }

        return SKImage.FromBitmap(bitmap);
    }

    /// <summary>
    ///     Renders a full aisling with all visible equipment layers into a single composited texture.
    ///     Used for paperdoll and other non-world rendering. For world rendering, use GetLayerFrames() instead.
    /// </summary>
    public Texture2D? Render(
        in AislingAppearance appearance,
        int frameIndex,
        string animSuffix = WALK_ANIM,
        bool flipHorizontal = false,
        bool? isFrontFacing = null,
        int emotionFrame = -1)
    {
        var layers = RenderLayers;
        Array.Clear(layers, 0, layers.Length);

        try
        {
            var isFront = isFrontFacing ?? IsFrontFacing(frameIndex, animSuffix);

            var idleFallbackFrame = animSuffix == IDLE_ANIM ? isFront ? RIGHT_IDLE_FRAME : UP_IDLE_FRAME : -1;

            RenderAllLayers(
                layers,
                in appearance,
                frameIndex,
                animSuffix,
                idleFallbackFrame);

            // Emotion overlay — only on front-facing frames
            if (isFront && (emotionFrame >= 0) && EmotionsEpf is not null && (emotionFrame < EmotionsEpf.Count))
            {
                var frame = EmotionsEpf[emotionFrame];

                if (Data.BodyPalettes.TryGetValue(appearance.BodyColor, out var palette))
                {
                    var image = Graphics.RenderImage(frame, palette);

                    if (image is not null)
                        layers[(int)LayerSlot.Emotion] = new LayerInfo(
                            image,
                            frame.Left,
                            frame.Top,
                            'o');
                }
            }

            if (!layers[(int)LayerSlot.Body].HasValue)
                return null;

            var order = isFront ? FRONT_ORDER : BACK_ORDER;

            using var composite = Composite(layers, order, flipHorizontal);

            return composite is not null ? TextureConverter.ToTexture2D(composite) : null;
        } finally
        {
            for (var i = 0; i < layers.Length; i++)
            {
                layers[i]
                    ?.Dispose();
                layers[i] = null;
            }
        }
    }

    /// <summary>
    ///     Renders a character creation preview (body + hair layers only).
    /// </summary>
    public Texture2D? RenderPreview(
        Gender gender,
        byte hairStyle,
        DisplayColor hairColor,
        int directionIndex = 3,
        int walkFrame = 0)
    {
        var isMale = gender == Gender.Male;
        var maxHairStyle = isMale ? MAX_MALE_HAIR_STYLE : MAX_FEMALE_HAIR_STYLE;

        if (hairStyle > maxHairStyle)
            return null;

        if ((int)hairColor > MAX_HAIR_COLOR)
            return null;

        var appearance = new AislingAppearance
        {
            Gender = gender,
            HeadSprite = hairStyle,
            HeadColor = hairColor
        };

        var baseFrame = directionIndex is 0 or 3 ? UP_IDLE_FRAME : RIGHT_IDLE_FRAME;
        var frameIndex = baseFrame + Math.Clamp(walkFrame, 0, 4);
        var flip = directionIndex is 2 or 3;

        return Render(
            in appearance,
            frameIndex,
            WALK_ANIM,
            flip);
    }
    #endregion

    #region Layer Rendering (composited path)
    private void RenderAllLayers(
        LayerInfo?[] layers,
        in AislingAppearance appearance,
        int frameIndex,
        string anim,
        int idleFallbackFrame = -1)
    {
        layers[(int)LayerSlot.BodyB] = RenderEquipLayer(
            'b',
            BODY_ID,
            DisplayColor.Default,
            in appearance,
            frameIndex,
            anim,
            idleFallbackFrame);

        layers[(int)LayerSlot.Body] = RenderBodyPaletteLayer(
            'm',
            BODY_ID,
            in appearance,
            frameIndex,
            anim,
            idleFallbackFrame);

        if (appearance.PantsColor.HasValue)
            layers[(int)LayerSlot.Pants] = RenderEquipLayer(
                'n',
                PANTS_ID,
                appearance.PantsColor.Value,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

        if (appearance.FaceSprite > 0)
            layers[(int)LayerSlot.Face] = RenderBodyPaletteLayer(
                'o',
                appearance.FaceSprite,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

        if (appearance.BootsSprite > 0)
            layers[(int)LayerSlot.Boots] = RenderEquipLayer(
                'l',
                appearance.BootsSprite,
                appearance.BootsColor,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

        if (appearance.HeadSprite > 0)
        {
            layers[(int)LayerSlot.HeadH] = RenderEquipLayer(
                'h',
                appearance.HeadSprite,
                appearance.HeadColor,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

            layers[(int)LayerSlot.HeadE] = RenderEquipLayer(
                'e',
                appearance.HeadSprite,
                appearance.HeadColor,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

            layers[(int)LayerSlot.HeadF] = RenderEquipLayer(
                'f',
                appearance.HeadSprite,
                appearance.HeadColor,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);
        }

        if (appearance.OvercoatSprite > 0)
            RenderArmorLayers(
                layers,
                appearance.OvercoatSprite,
                appearance.OvercoatColor,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);
        else if (appearance.ArmorSprite > 0)
            RenderArmorLayers(
                layers,
                appearance.ArmorSprite,
                appearance.ArmorColor,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

        if (appearance.WeaponSprite > 0)
        {
            layers[(int)LayerSlot.WeaponW] = RenderEquipLayer(
                'w',
                appearance.WeaponSprite,
                DisplayColor.Default,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

            layers[(int)LayerSlot.WeaponP] = RenderEquipLayer(
                'p',
                appearance.WeaponSprite,
                DisplayColor.Default,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);
        }

        if (appearance.ShieldSprite > 0)
            layers[(int)LayerSlot.Shield] = RenderEquipLayer(
                's',
                appearance.ShieldSprite,
                DisplayColor.Default,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

        if (appearance.Accessory1Sprite > 0)
        {
            layers[(int)LayerSlot.Acc1C] = RenderEquipLayer(
                'c',
                appearance.Accessory1Sprite,
                appearance.Accessory1Color,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

            layers[(int)LayerSlot.Acc1G] = RenderEquipLayer(
                'g',
                appearance.Accessory1Sprite,
                appearance.Accessory1Color,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);
        }

        if (appearance.Accessory2Sprite > 0)
        {
            layers[(int)LayerSlot.Acc2C] = RenderEquipLayer(
                'c',
                appearance.Accessory2Sprite,
                appearance.Accessory2Color,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

            layers[(int)LayerSlot.Acc2G] = RenderEquipLayer(
                'g',
                appearance.Accessory2Sprite,
                appearance.Accessory2Color,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);
        }

        if (appearance.Accessory3Sprite > 0)
        {
            layers[(int)LayerSlot.Acc3C] = RenderEquipLayer(
                'c',
                appearance.Accessory3Sprite,
                appearance.Accessory3Color,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

            layers[(int)LayerSlot.Acc3G] = RenderEquipLayer(
                'g',
                appearance.Accessory3Sprite,
                appearance.Accessory3Color,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);
        }
    }

    private void RenderArmorLayers(
        LayerInfo?[] layers,
        int spriteId,
        DisplayColor color,
        in AislingAppearance appearance,
        int frameIndex,
        string anim,
        int idleFallbackFrame = -1)
    {
        var isOverType = spriteId >= 1000;
        var adjustedId = isOverType ? spriteId - 1000 : spriteId;
        var bodyLetter = isOverType ? 'i' : 'u';
        var armsLetter = isOverType ? 'j' : 'a';

        layers[(int)LayerSlot.Armor] = RenderEquipLayer(
            bodyLetter,
            adjustedId,
            color,
            in appearance,
            frameIndex,
            anim,
            idleFallbackFrame);

        layers[(int)LayerSlot.Arms] = RenderEquipLayer(
            armsLetter,
            adjustedId,
            color,
            in appearance,
            frameIndex,
            anim,
            idleFallbackFrame);
    }

    private LayerInfo? RenderBodyPaletteLayer(
        char typeLetter,
        int spriteId,
        in AislingAppearance appearance,
        int frameIndex,
        string anim,
        int idleFallbackFrame = -1)
    {
        if (spriteId <= 0)
            return null;

        (var epf, var resolvedFrame) = ResolveLayerEpf(
            typeLetter,
            spriteId,
            in appearance,
            frameIndex,
            anim,
            idleFallbackFrame);

        if (epf is null || (resolvedFrame < 0))
            return null;

        var frame = epf[resolvedFrame];

        if (!Data.BodyPalettes.TryGetValue(appearance.BodyColor, out var palette))
            return null;

        var image = Graphics.RenderImage(frame, palette);

        if (image is null)
            return null;

        return new LayerInfo(
            image,
            frame.Left,
            frame.Top,
            typeLetter);
    }

    private LayerInfo? RenderEquipLayer(
        char typeLetter,
        int spriteId,
        DisplayColor dyeColor,
        in AislingAppearance appearance,
        int frameIndex,
        string anim,
        int idleFallbackFrame = -1)
    {
        if (spriteId <= 0)
            return null;

        (var epf, var resolvedFrame) = ResolveLayerEpf(
            typeLetter,
            spriteId,
            in appearance,
            frameIndex,
            anim,
            idleFallbackFrame);

        if (epf is null || (resolvedFrame < 0))
            return null;

        var frame = epf[resolvedFrame];
        var lookup = Data.GetPaletteLookup(typeLetter);

        var palette = ResolvePalette(
            lookup,
            spriteId,
            dyeColor,
            appearance.OverrideType);

        if (palette is null)
            return null;

        var image = Graphics.RenderImage(frame, palette);

        if (image is null)
            return null;

        return new LayerInfo(
            image,
            frame.Left,
            frame.Top,
            typeLetter);
    }
    #endregion

    #region Helpers
    /// <summary>
    ///     Loads the EPF for a layer, handling "04" idle wrapping and "01" fallback.
    /// </summary>
    private (EpfView? Epf, int FrameIndex) ResolveLayerEpf(
        char typeLetter,
        int spriteId,
        in AislingAppearance appearance,
        int frameIndex,
        string anim,
        int idleFallbackFrame)
    {
        var fileName = $"{appearance.GenderPrefix}{typeLetter}{spriteId:D3}{anim}";
        var epf = TryLoadEpf(typeLetter, appearance.IsMale, fileName);

        if ((anim == IDLE_ANIM) && epf is not null && (epf.Count >= 2) && (idleFallbackFrame >= 0))
        {
            var framesPerDir = epf.Count / 2;
            var dirBase = idleFallbackFrame == RIGHT_IDLE_FRAME ? framesPerDir : 0;
            frameIndex = dirBase + frameIndex % framesPerDir;
        } else if ((epf is null || (frameIndex >= epf.Count)) && (idleFallbackFrame >= 0))
        {
            fileName = $"{appearance.GenderPrefix}{typeLetter}{spriteId:D3}{WALK_ANIM}";
            epf = TryLoadEpf(typeLetter, appearance.IsMale, fileName);
            frameIndex = idleFallbackFrame;
        }

        if (epf is null || (frameIndex >= epf.Count))
            return (null, -1);

        return (epf, frameIndex);
    }

    private EpfView? TryLoadEpf(char typeLetter, bool isMale, string fileName) => Data.GetEquipmentEpf(typeLetter, isMale, fileName);

    /// <summary>
    ///     Scans all equipped layers for "04" idle animation EPFs and returns the max frames per direction found.
    /// </summary>
    public int GetIdleAnimFrameCount(in AislingAppearance appearance)
    {
        var maxFrames = 0;

        CheckIdleEpf(
            ref maxFrames,
            'c',
            appearance.Accessory1Sprite,
            in appearance);

        CheckIdleEpf(
            ref maxFrames,
            'g',
            appearance.Accessory1Sprite,
            in appearance);

        CheckIdleEpf(
            ref maxFrames,
            'c',
            appearance.Accessory2Sprite,
            in appearance);

        CheckIdleEpf(
            ref maxFrames,
            'g',
            appearance.Accessory2Sprite,
            in appearance);

        CheckIdleEpf(
            ref maxFrames,
            'c',
            appearance.Accessory3Sprite,
            in appearance);

        CheckIdleEpf(
            ref maxFrames,
            'g',
            appearance.Accessory3Sprite,
            in appearance);

        CheckIdleEpf(
            ref maxFrames,
            'h',
            appearance.HeadSprite,
            in appearance);

        CheckIdleEpf(
            ref maxFrames,
            'e',
            appearance.HeadSprite,
            in appearance);

        CheckIdleEpf(
            ref maxFrames,
            'f',
            appearance.HeadSprite,
            in appearance);

        CheckIdleEpf(
            ref maxFrames,
            'w',
            appearance.WeaponSprite,
            in appearance);

        CheckIdleEpf(
            ref maxFrames,
            'p',
            appearance.WeaponSprite,
            in appearance);

        return maxFrames;
    }

    private void CheckIdleEpf(
        ref int maxFrames,
        char typeLetter,
        int spriteId,
        in AislingAppearance appearance)
    {
        if (spriteId <= 0)
            return;

        var fileName = $"{appearance.GenderPrefix}{typeLetter}{spriteId:D3}{IDLE_ANIM}";
        var epf = TryLoadEpf(typeLetter, appearance.IsMale, fileName);

        if (epf is not null && (epf.Count >= 2))
            maxFrames = Math.Max(maxFrames, epf.Count / 2);
    }

    /// <summary>
    ///     Returns true if the displayed armor/overcoat has an EPF file for the given animation suffix.
    /// </summary>
    public bool HasArmorAnimation(in AislingAppearance appearance, string animSuffix)
    {
        var spriteId = appearance.OvercoatSprite > 0
            ? appearance.OvercoatSprite
            : appearance.ArmorSprite > 0
                ? appearance.ArmorSprite
                : 0;

        if (spriteId <= 0)
            return true;

        var isOverType = spriteId >= 1000;
        var adjustedId = isOverType ? spriteId - 1000 : spriteId;
        var bodyLetter = isOverType ? 'i' : 'u';
        var fileName = $"{appearance.GenderPrefix}{bodyLetter}{adjustedId:D3}{animSuffix}";
        var epf = TryLoadEpf(bodyLetter, appearance.IsMale, fileName);

        return epf is not null;
    }
    #endregion
}
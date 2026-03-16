#region
using Chaos.Client.Data;
using Chaos.DarkAges.Definitions;
using DALib.Data;
using DALib.Definitions;
using DALib.Drawing;
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

    private const int BODY_WIDTH = 57;
    private const int BODY_HEIGHT = 85;
    private const int BODY_CENTER_X = BODY_WIDTH / 2;
    private const int BODY_CENTER_Y = BODY_HEIGHT / 2;

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
    private static readonly LayerSlot[] FRONT_ORDER =
    [
        LayerSlot.HeadF,
        LayerSlot.Acc1G,
        LayerSlot.Acc2G,
        LayerSlot.Body,
        LayerSlot.Pants,
        LayerSlot.Face,
        LayerSlot.Boots,
        LayerSlot.HeadH,
        LayerSlot.Armor,
        LayerSlot.Arms,
        LayerSlot.HeadE,
        LayerSlot.WeaponW,
        LayerSlot.WeaponP,
        LayerSlot.Shield,
        LayerSlot.Acc1C,
        LayerSlot.Acc2C
    ];

    // Back-facing composite order
    private static readonly LayerSlot[] BACK_ORDER =
    [
        LayerSlot.Acc1G,
        LayerSlot.Acc2G,
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
        LayerSlot.Acc2C
    ];

    // Body palettes (palm) — indexed directly by body color
    private readonly Dictionary<int, Palette> BodyPalettes = Palette.FromArchive("palm", DatArchives.Khanpal);

    private readonly ColorTable DyeColorTable;
    private readonly Dictionary<string, EpfFile?> EpfCache = new();

    // Per-type palette lookups from Khanpal
    private readonly PaletteLookup PalB = PaletteLookup.FromArchive("palb", DatArchives.Khanpal)
                                                       .Freeze();

    private readonly PaletteLookup PalC = PaletteLookup.FromArchive("palc", DatArchives.Khanpal)
                                                       .Freeze();

    private readonly PaletteLookup PalE = PaletteLookup.FromArchive("pale", DatArchives.Khanpal)
                                                       .Freeze();

    private readonly PaletteLookup PalF = PaletteLookup.FromArchive("palf", DatArchives.Khanpal)
                                                       .Freeze();

    private readonly PaletteLookup PalH = PaletteLookup.FromArchive("palh", DatArchives.Khanpal)
                                                       .Freeze();

    private readonly PaletteLookup PalI = PaletteLookup.FromArchive("pali", DatArchives.Khanpal)
                                                       .Freeze();

    private readonly PaletteLookup PalL = PaletteLookup.FromArchive("pall", DatArchives.Khanpal)
                                                       .Freeze();

    private readonly PaletteLookup PalP = PaletteLookup.FromArchive("palp", DatArchives.Khanpal)
                                                       .Freeze();

    private readonly PaletteLookup PalU = PaletteLookup.FromArchive("palu", DatArchives.Khanpal)
                                                       .Freeze();

    private readonly PaletteLookup PalW = PaletteLookup.FromArchive("palw", DatArchives.Khanpal)
                                                       .Freeze();

    public AislingRenderer()
    {
        if (DatArchives.Legend.TryGetValue("color0.tbl", out var entry))
            DyeColorTable = ColorTable.FromEntry(entry);
        else
            DyeColorTable = new ColorTable();
    }

    /// <inheritdoc />
    public void Dispose() => ClearCache();

    /// <summary>
    ///     Clears the cached EPF files. Call on map change to free memory.
    /// </summary>
    public void ClearCache() => EpfCache.Clear();

    #region Compositing
    /// <summary>
    ///     Composites all layers into a single image. Layers align via RenderImage's Left/Top padding (baked into each
    ///     SKImage). Flipping mirrors around BODY_CENTER_X for Down/Left directions.
    /// </summary>
    private static SKImage? Composite(LayerInfo?[] layers, LayerSlot[] order, bool flipHorizontal)
    {
        var width = 0;
        var height = 0;

        foreach (var slot in order)
        {
            if (layers[(int)slot] is not { } info)
                continue;

            width = Math.Max(width, info.Image.Width);
            height = Math.Max(height, info.Image.Height);
        }

        if ((width <= 0) || (height <= 0))
            return null;

        using var bitmap = new SKBitmap(width, height);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);

            if (flipHorizontal)
                canvas.Scale(
                    -1,
                    1,
                    BODY_CENTER_X,
                    0);

            foreach (var slot in order)
            {
                if (layers[(int)slot] is not { } info)
                    continue;

                canvas.DrawImage(info.Image, 0, 0);
            }
        }

        return SKImage.FromBitmap(bitmap);
    }
    #endregion

    /// <summary>
    ///     Renders a full aisling with all visible equipment layers.
    /// </summary>
    public Texture2D? Render(
        GraphicsDevice device,
        in AislingAppearance appearance,
        int frameIndex,
        string animSuffix = WALK_ANIM,
        bool flipHorizontal = false)
    {
        var layers = new LayerInfo?[(int)LayerSlot.Count];

        try
        {
            RenderAllLayers(
                layers,
                in appearance,
                frameIndex,
                animSuffix);

            if (!layers[(int)LayerSlot.Body].HasValue)
                return null;

            var order = IsFrontFacing(frameIndex, animSuffix) ? FRONT_ORDER : BACK_ORDER;

            using var composite = Composite(layers, order, flipHorizontal);

            return composite is not null ? TextureConverter.ToTexture2D(device, composite) : null;
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
    /// <param name="walkFrame">
    ///     Walk cycle position (0–4). 0 = idle, 1–4 = walk steps.
    /// </param>
    public Texture2D? RenderPreview(
        GraphicsDevice device,
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

        // Up/Left use Up frames (0-4), Right/Down use Right frames (5-9)
        var baseFrame = directionIndex is 0 or 3 ? UP_IDLE_FRAME : RIGHT_IDLE_FRAME;
        var frameIndex = baseFrame + Math.Clamp(walkFrame, 0, 4);
        var flip = directionIndex is 2 or 3;

        return Render(
            device,
            in appearance,
            frameIndex,
            WALK_ANIM,
            flip);
    }

    /// <summary>
    ///     A rendered layer with its EpfFrame positioning metadata preserved. Left/Top from the EpfFrame are needed at
    ///     composite time to correctly position layers (especially those with negative offsets where Graphics.RenderImage
    ///     strips the padding).
    /// </summary>
    private readonly record struct LayerInfo(
        SKImage Image,
        short FrameLeft,
        short FrameTop,
        char TypeLetter) : IDisposable
    {
        public void Dispose() => Image.Dispose();
    }

    private enum LayerSlot
    {
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
        Count
    }

    #region Layer Rendering
    private void RenderAllLayers(
        LayerInfo?[] layers,
        in AislingAppearance appearance,
        int frameIndex,
        string anim)
    {
        // Body (type m, always id 1)
        layers[(int)LayerSlot.Body] = RenderBodyPaletteLayer(
            'm',
            BODY_ID,
            in appearance,
            frameIndex,
            anim);

        // Pants (type n, always id 1) — only if server sent a pants color
        if (appearance.PantsColor.HasValue)
            layers[(int)LayerSlot.Pants] = RenderEquipLayer(
                'n',
                PANTS_ID,
                appearance.PantsColor.Value,
                in appearance,
                frameIndex,
                anim);

        // Face (type o, uses body palette)
        if (appearance.FaceSprite > 0)
            layers[(int)LayerSlot.Face] = RenderBodyPaletteLayer(
                'o',
                appearance.FaceSprite,
                in appearance,
                frameIndex,
                anim);

        // Boots
        if (appearance.BootsSprite > 0)
            layers[(int)LayerSlot.Boots] = RenderEquipLayer(
                'l',
                appearance.BootsSprite,
                appearance.BootsColor,
                in appearance,
                frameIndex,
                anim);

        // Head layers (h, e, f) — all share the same HeadSprite ID
        if (appearance.HeadSprite > 0)
        {
            layers[(int)LayerSlot.HeadH] = RenderEquipLayer(
                'h',
                appearance.HeadSprite,
                appearance.HeadColor,
                in appearance,
                frameIndex,
                anim);

            layers[(int)LayerSlot.HeadE] = RenderEquipLayer(
                'e',
                appearance.HeadSprite,
                appearance.HeadColor,
                in appearance,
                frameIndex,
                anim);

            layers[(int)LayerSlot.HeadF] = RenderEquipLayer(
                'f',
                appearance.HeadSprite,
                appearance.HeadColor,
                in appearance,
                frameIndex,
                anim);
        }

        // Armor — overcoat (type i/j) overrides regular armor (type u/a)
        if (appearance.OvercoatSprite > 0)
        {
            layers[(int)LayerSlot.Armor] = RenderEquipLayer(
                'i',
                appearance.OvercoatSprite,
                appearance.OvercoatColor,
                in appearance,
                frameIndex,
                anim);

            layers[(int)LayerSlot.Arms] = RenderEquipLayer(
                'j',
                appearance.OvercoatSprite,
                appearance.OvercoatColor,
                in appearance,
                frameIndex,
                anim);
        } else if (appearance.ArmorSprite > 0)
        {
            layers[(int)LayerSlot.Armor] = RenderEquipLayer(
                'u',
                appearance.ArmorSprite,
                appearance.ArmorColor,
                in appearance,
                frameIndex,
                anim);

            layers[(int)LayerSlot.Arms] = RenderEquipLayer(
                'a',
                appearance.ArmorSprite,
                appearance.ArmorColor,
                in appearance,
                frameIndex,
                anim);
        }

        // Weapon (w + p sub-layers)
        if (appearance.WeaponSprite > 0)
        {
            layers[(int)LayerSlot.WeaponW] = RenderEquipLayer(
                'w',
                appearance.WeaponSprite,
                DisplayColor.Default,
                in appearance,
                frameIndex,
                anim);

            layers[(int)LayerSlot.WeaponP] = RenderEquipLayer(
                'p',
                appearance.WeaponSprite,
                DisplayColor.Default,
                in appearance,
                frameIndex,
                anim);
        }

        // Shield (no dye)
        if (appearance.ShieldSprite > 0)
            layers[(int)LayerSlot.Shield] = RenderEquipLayer(
                's',
                appearance.ShieldSprite,
                DisplayColor.Default,
                in appearance,
                frameIndex,
                anim);

        // Accessories (c + g sub-layers each)
        if (appearance.Accessory1Sprite > 0)
        {
            layers[(int)LayerSlot.Acc1C] = RenderEquipLayer(
                'c',
                appearance.Accessory1Sprite,
                appearance.Accessory1Color,
                in appearance,
                frameIndex,
                anim);

            layers[(int)LayerSlot.Acc1G] = RenderEquipLayer(
                'g',
                appearance.Accessory1Sprite,
                appearance.Accessory1Color,
                in appearance,
                frameIndex,
                anim);
        }

        if (appearance.Accessory2Sprite > 0)
        {
            layers[(int)LayerSlot.Acc2C] = RenderEquipLayer(
                'c',
                appearance.Accessory2Sprite,
                appearance.Accessory2Color,
                in appearance,
                frameIndex,
                anim);

            layers[(int)LayerSlot.Acc2G] = RenderEquipLayer(
                'g',
                appearance.Accessory2Sprite,
                appearance.Accessory2Color,
                in appearance,
                frameIndex,
                anim);
        }
    }

    /// <summary>
    ///     Renders a layer that uses the body palette (body, face).
    /// </summary>
    private LayerInfo? RenderBodyPaletteLayer(
        char typeLetter,
        int spriteId,
        in AislingAppearance appearance,
        int frameIndex,
        string anim)
    {
        if (spriteId <= 0)
            return null;

        var archive = GetArchive(typeLetter, appearance.IsMale);
        var fileName = $"{appearance.GenderPrefix}{typeLetter}{spriteId:D3}{anim}";
        var epf = TryLoadEpf(archive, fileName);

        if (epf is null || (frameIndex >= epf.Count))
            return null;

        var frame = epf[frameIndex];

        if (!BodyPalettes.TryGetValue(appearance.BodyColor, out var palette))
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

    /// <summary>
    ///     Renders an equipment layer using PaletteLookup with optional dye.
    /// </summary>
    private LayerInfo? RenderEquipLayer(
        char typeLetter,
        int spriteId,
        DisplayColor dyeColor,
        in AislingAppearance appearance,
        int frameIndex,
        string anim)
    {
        if (spriteId <= 0)
            return null;

        var archive = GetArchive(typeLetter, appearance.IsMale);
        var fileName = $"{appearance.GenderPrefix}{typeLetter}{spriteId:D3}{anim}";
        var epf = TryLoadEpf(archive, fileName);

        if (epf is null || (frameIndex >= epf.Count))
            return null;

        var frame = epf[frameIndex];
        var lookup = GetPaletteLookup(typeLetter);

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

    #region Palette Resolution
    private PaletteLookup GetPaletteLookup(char typeLetter)
        => typeLetter switch
        {
            'b' or 'n'        => PalB,
            'c' or 'g'        => PalC,
            'e'               => PalE,
            'f'               => PalF,
            'h'               => PalH,
            'i' or 'j'        => PalI,
            'l'               => PalL,
            'p'               => PalP,
            's' or 'u' or 'a' => PalU,
            'w'               => PalW,
            _                 => PalB
        };

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

        if (dyeColor == DisplayColor.Default)
            return basePalette;

        var colorIndex = (int)dyeColor;

        if (!DyeColorTable.Contains(colorIndex))
            return basePalette;

        return basePalette.Dye(DyeColorTable[colorIndex]);
    }
    #endregion

    #region Helpers
    private static bool IsFrontFacing(int frameIndex, string animSuffix)
        => animSuffix switch
        {
            "01" => WALK_FRONT_FRAMES.Contains(frameIndex),
            "02" => frameIndex is 2 or 3,
            "03" => frameIndex is 1 or 4 or 5 or 8 or 9,
            _    => frameIndex >= 5
        };

    private static DataArchive GetArchive(char typeLetter, bool isMale)
        => typeLetter switch
        {
            >= 'a' and <= 'd' => isMale ? DatArchives.Khanmad : DatArchives.Khanwad,
            >= 'e' and <= 'h' => isMale ? DatArchives.Khanmeh : DatArchives.Khanweh,
            >= 'i' and <= 'm' => isMale ? DatArchives.Khanmim : DatArchives.Khanwim,
            >= 'n' and <= 's' => isMale ? DatArchives.Khanmns : DatArchives.Khanwns,
            >= 't' and <= 'z' => isMale ? DatArchives.Khanmtz : DatArchives.Khanwtz,
            _                 => isMale ? DatArchives.Khanmad : DatArchives.Khanwad
        };

    private EpfFile? TryLoadEpf(DataArchive archive, string fileName)
    {
        if (EpfCache.TryGetValue(fileName, out var cached))
            return cached;

        if (!archive.TryGetValue($"{fileName}.epf", out var entry))
        {
            EpfCache[fileName] = null;

            return null;
        }

        var epf = EpfFile.FromEntry(entry);
        EpfCache[fileName] = epf;

        return epf;
    }
    #endregion
}
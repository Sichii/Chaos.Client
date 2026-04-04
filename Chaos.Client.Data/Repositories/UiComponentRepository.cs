#region
using System.Collections.Frozen;
using System.Text;
using Chaos.Client.Data.Abstractions;
using Chaos.Client.Data.Models;
using Chaos.Extensions.Common;
using DALib.Data;
using DALib.Definitions;
using DALib.Drawing;
using DALib.Drawing.Virtualized;
using DALib.Extensions;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;
#endregion

namespace Chaos.Client.Data.Repositories;

public sealed class UiComponentRepository : RepositoryBase
{
    private readonly IDictionary<int, Palette> GuiPalettes = LoadGuiPalettes();

    public UiComponentRepository()
    {
        PreloadFromArchive(DatArchives.Setoa);
        PreloadFromArchive(DatArchives.Cious);
    }

    /// <inheritdoc />
    protected override void ConfigureEntry(ICacheEntry entry) => entry.SetPriority(CacheItemPriority.NeverRemove);

    public ControlPrefabSet? Get(string key)
    {
        try
        {
            return GetOrCreate(key, () => LoadPrefabSet(key));
        } catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Returns the raw EpfFrame metadata for a specific frame in an EPF file. Used to read Left/Top offset values.
    /// </summary>
    public EpfFrame? GetEpfFrame(string fileName, int frameIndex)
    {
        if (!DatArchives.Setoa.TryGetValue(fileName, out var entry))
            return null;

        var epf = GetOrCreate($"EPF_{fileName}", () => EpfView.FromEntry(entry));

        if ((frameIndex < 0) || (frameIndex >= epf.Count))
            return null;

        return epf[frameIndex];
    }

    /// <summary>
    ///     Loads all frames from an EPF file in setoa.dat rendered with the appropriate GUI palette. Used for UI elements like
    ///     scroll.epf that aren't part of a control file.
    /// </summary>
    public SKImage[] GetEpfImages(string fileName)
    {
        if (!DatArchives.Setoa.TryGetValue(fileName, out var entry))
            return [];

        var epf = GetOrCreate($"EPF_{fileName}", () => EpfView.FromEntry(entry));
        var paletteNum = GetGuiPaletteNumber(fileName);

        if (!GuiPalettes.TryGetValue(paletteNum, out var palette))
            return [];

        var images = new SKImage[epf.Count];

        for (var i = 0; i < epf.Count; i++)
            images[i] = Graphics.RenderImage(epf[i], palette);

        return images;
    }

    /// <summary>
    ///     Loads a field image (EPF + matching PAL) from setoa.dat. Used for world map backgrounds where the palette is stored
    ///     alongside the EPF rather than using the GUI palette.
    /// </summary>
    public SKImage? GetFieldImage(string fieldName)
    {
        if (!DatArchives.Setoa.TryGetValue($"{fieldName}.epf", out var epfEntry))
            return null;

        if (!DatArchives.Setoa.TryGetValue($"{fieldName}.pal", out var palEntry))
            return null;

        var epf = EpfView.FromEntry(epfEntry);
        var palette = Palette.FromEntry(palEntry);

        if (epf.Count == 0)
            return null;

        return Graphics.RenderImage(epf[0], palette);
    }

    /// <summary>
    ///     Determines the gui palette number for an EPF image name based on filename prefix. Derived from ChaosAssetManager's
    ///     RenderUtil setoa dispatch table.
    /// </summary>

    // ReSharper disable once CognitiveComplexity
    private static int GetGuiPaletteNumber(string imageName)
    {
        //exceptions — must be checked before their broader prefix matches below
        if (imageName.StartsWithI("dlgcre01"))
            return 8;

        if (imageName.StartsWithI("gbicon02") || imageName.StartsWithI("mernum"))
            return 0;

        if (imageName.StartsWithI("emot00") || imageName.StartsWithI("emotdlg"))
            return 0;

        if (imageName.StartsWithI("lsbackm"))
            return 0;

        if (imageName.StartsWithI("setup12") || imageName.StartsWithI("setup13") || imageName.StartsWithI("setup14"))
            return 0;

        //1
        if (imageName.StartsWithI("gbicon12") || imageName.StartsWithI("orb"))
            return 1;

        //2
        if (imageName.StartsWithI("gbicon01") || imageName.StartsWithI("gbicon03"))
            return 2;

        //3
        if (imageName.StartsWithI("emot")
            || imageName.StartsWithI("equip02")
            || imageName.StartsWithI("mouse")
            || imageName.StartsWithI("legends"))
            return 3;

        if (imageName.StartsWithI("lback")
            || imageName.StartsWithI("dlgcre")
            || imageName.StartsWithI("lod0")
            || imageName.StartsWithI("setup"))
            return 4;

        if (imageName.StartsWithI("nation"))
            return 5;

        if (imageName.StartsWithI("skill0") || imageName.StartsWithI("spell0"))
            return 6;

        if (imageName.StartsWithI("lodbk"))
            return 7;

        if (imageName.StartsWithI("staff"))
            return 9;

        if (imageName.StartsWithI("lsback") || imageName.StartsWithI("lss") || imageName.StartsWithI("leicon"))
            return 10;

        if (imageName.StartsWithI("ldi"))
            return 11;

        if (imageName.StartsWithI("lwmap") || imageName.StartsWithI("tmapv"))
            return 12;

        if (imageName.StartsWithI("bw_back") || imageName.StartsWithI("bw_check"))
            return 13;

        if (imageName.StartsWithI("kdesc") || imageName.StartsWithI("key") || imageName.StartsWithI("khotkey"))
            return 14;

        if (imageName.StartsWithI("lg_"))
            return 15;

        if (imageName.StartsWithI("bw_flag"))
            return 16;

        if (imageName.StartsWithI("album_b") || imageName.EqualsI("album"))
            return 17;

        return 0;
    }

    /// <summary>
    ///     Loads a single EPF frame from Legend.dat rendered with the appropriate GUI palette.
    /// </summary>
    public SKImage? GetLegendEpfImage(string fileName, int frameIndex = 0)
    {
        if (!DatArchives.Legend.TryGetValue(fileName, out var entry))
            return null;

        return RenderEpfFrame(entry, fileName, frameIndex);
    }

    /// <summary>
    ///     Returns the GUI palette that would be used for the given legend.dat EPF filename.
    /// </summary>
    public Palette? GetLegendPalette(string fileName)
    {
        var palNum = GetGuiPaletteNumber(fileName);

        return GuiPalettes.TryGetValue(palNum, out var palette) ? palette : null;
    }

    /// <summary>
    ///     Reads the msg.tbl text file from setoa.dat and returns its lines. Used for localized UI strings (e.g. social status
    ///     names). Returns null if msg.tbl is not found.
    /// </summary>
    public string[]? GetMessageTableLines()
    {
        if (!DatArchives.Setoa.TryGetValue("msg.tbl", out var entry))
            return null;

        using var ms = new MemoryStream();

        using (var s = entry.ToStreamSegment())
            s.CopyTo(ms);

        var text = Encoding.GetEncoding(949)
                           .GetString(ms.ToArray());

        return text.Split('\n');
    }

    /// <summary>
    ///     Loads a single frame from an EPF file in national.dat rendered with legend.pal.
    /// </summary>
    public SKImage? GetNationalEpfImage(string fileName, int frameIndex = 0)
    {
        if (!DatArchives.National.TryGetValue(fileName, out var entry))
            return null;

        var epf = EpfView.FromEntry(entry);

        if ((frameIndex < 0) || (frameIndex >= epf.Count))
            return null;

        return Graphics.RenderImage(epf[frameIndex], LegendPalette.GetPalette());
    }

    /// <summary>
    ///     Returns the total frame count of an EPF file in national.dat, or 0 if not found.
    /// </summary>
    public int GetNationalEpfFrameCount(string fileName)
    {
        if (!DatArchives.National.TryGetValue(fileName, out var entry))
            return 0;

        var epf = EpfView.FromEntry(entry);

        return epf.Count;
    }

    /// <summary>
    ///     Loads a single frame from an SPF file in national.dat as an SKImage.
    /// </summary>
    public SKImage? GetNationalSpfImage(string fileName, int frameIndex = 0)
    {
        var spf = GetOrCreate($"NSPF_{fileName}", () => LoadNationalSpfFile(fileName));

        if (spf is null || (frameIndex >= spf.Count))
            return null;

        return spf.Format == SpfFormatType.Colorized
            ? Graphics.RenderImage(spf[frameIndex])
            : Graphics.RenderImage(spf[frameIndex], spf.PrimaryColors!);
    }

    /// <summary>
    ///     Loads a single frame from an SPF file in setoa.dat as an SKImage.
    /// </summary>
    public SKImage? GetSpfImage(string fileName, int frameIndex = 0)
    {
        var spf = GetOrCreate($"SPF_{fileName}", () => LoadSpfFile(fileName));

        if (spf is null || (frameIndex >= spf.Count))
            return null;

        return spf.Format == SpfFormatType.Colorized
            ? Graphics.RenderImage(spf[frameIndex])
            : Graphics.RenderImage(spf[frameIndex], spf.PrimaryColors!);
    }

    /// <summary>
    ///     Loads all frames from an SPF file in setoa.dat as SKImage[]. Caller is responsible for disposing the returned
    ///     images.
    /// </summary>
    public SKImage[] GetSpfImages(string fileName)
    {
        var spf = GetOrCreate($"SPF_{fileName}", () => LoadSpfFile(fileName));

        if (spf is null || (spf.Count == 0))
            return [];

        var images = new SKImage[spf.Count];

        for (var i = 0; i < spf.Count; i++)
            images[i] = spf.Format == SpfFormatType.Colorized
                ? Graphics.RenderImage(spf[i])
                : Graphics.RenderImage(spf[i], spf.PrimaryColors!);

        return images;
    }

    private static FrozenDictionary<int, Palette> LoadGuiPalettes()
    {
        var palettes = Palette.FromArchive("gui", DatArchives.Setoa);

        // Match original client behavior: copy entry[0] to entry[255], then entry[0] becomes transparent at render time
        foreach (var palette in palettes.Values)
            palette[255] = palette[0];

        return palettes.ToFrozenDictionary();
    }

    private static SpfView? LoadNationalSpfFile(string fileName)
    {
        if (!DatArchives.National.TryGetValue(fileName, out var entry))
            return null;

        return SpfView.FromEntry(entry);
    }

    private ControlPrefabSet? LoadPrefabSet(string key)
    {
        var txtKey = key.WithExtension(".txt");

        if (!DatArchives.Cious.TryGetValue(txtKey, out var entry) && !DatArchives.Setoa.TryGetValue(txtKey, out entry))
            return null;

        var controlFile = ControlFile.FromEntry(entry);

        return ResolvePrefabSet(key, controlFile);
    }

    private static SpfView? LoadSpfFile(string fileName)
    {
        if (!DatArchives.Setoa.TryGetValue(fileName, out var entry))
            return null;

        return SpfView.FromEntry(entry);
    }

    private void PreloadFromArchive(DataArchive archive)
    {
        var controlFiles = ControlFile.FromArchive(archive);

        foreach ((var name, var controlFile) in controlFiles)
            GetOrCreate(name, () => ResolvePrefabSet(name, controlFile));
    }

    private SKImage? RenderEpfFrame(DataArchiveEntry entry, string imageName, int frameIndex)
    {
        var palNum = GetGuiPaletteNumber(imageName);

        if (!GuiPalettes.TryGetValue(palNum, out var palette))
            return null;

        var epf = EpfView.FromEntry(entry);

        if ((frameIndex < 0) || (frameIndex >= epf.Count))
            return null;

        return Graphics.RenderImage(epf[frameIndex], palette);
    }

    private SKImage? RenderFrame(string imageName, int frameIndex)
    {
        // Try EPF first, then SPF
        var epfName = imageName.WithExtension(".epf");

        if (DatArchives.Setoa.TryGetValue(epfName, out var entry) || DatArchives.Cious.TryGetValue(epfName, out entry))
            return RenderEpfFrame(entry, imageName, frameIndex);

        var spfName = imageName.WithExtension(".spf");

        if (DatArchives.Setoa.TryGetValue(spfName, out entry) || DatArchives.Cious.TryGetValue(spfName, out entry))
            return RenderSpfFrame(entry, frameIndex);

        return null;
    }

    private static SKImage? RenderSpfFrame(DataArchiveEntry entry, int frameIndex)
    {
        var spf = SpfView.FromEntry(entry);

        if ((frameIndex < 0) || (frameIndex >= spf.Count))
            return null;

        return spf.Format == SpfFormatType.Colorized
            ? Graphics.RenderImage(spf[frameIndex])
            : Graphics.RenderImage(spf[frameIndex], spf.PrimaryColors!);
    }

    private ControlPrefabSet ResolvePrefabSet(string name, ControlFile controlFile)
    {
        var set = new ControlPrefabSet(name);

        foreach (var control in controlFile)
        {
            var images = new List<SKImage>();

            if (control.Images is not null)
                foreach ((var imageName, var frameIndex) in control.Images)
                {
                    var image = RenderFrame(imageName, frameIndex);

                    if (image is not null)
                        images.Add(image);
                }

            set.Add(
                new ControlPrefab
                {
                    Control = control,
                    Images = images
                });
        }

        return set;
    }
}
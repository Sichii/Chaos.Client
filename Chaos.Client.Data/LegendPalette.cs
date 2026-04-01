#region
using Chaos.Client.Data.Definitions;
using DALib.Drawing;
using SkiaSharp;
#endregion

namespace Chaos.Client.Data;

/// <summary>
///     Provides access to legend.pal colors from Legend.dat. Loaded once during initialization. Colors are accessed by
///     <see cref="LegendColor" /> enum or raw palette index.
/// </summary>
public static class LegendPalette
{
    private static Palette Palette = null!;

    /// <summary>
    ///     Text color code char-to-LegendColor mapping. Valid codes are 'a' through 'x'.
    /// </summary>
    private static readonly LegendColor?[] TextColorMap = BuildTextColorMap();

    private static LegendColor?[] BuildTextColorMap()
    {
        // 24 slots for 'a' (0) through 'x' (23)
        var map = new LegendColor?[24];

        map['a' - 'a'] = LegendColor.Silver;
        map['b' - 'a'] = LegendColor.Scarlet;
        map['c' - 'a'] = LegendColor.Gold;
        map['d' - 'a'] = LegendColor.DarkGreen;
        map['e' - 'a'] = LegendColor.CornflowerBlue;
        map['f' - 'a'] = LegendColor.Indigo;
        map['g' - 'a'] = LegendColor.LightGray;
        map['h' - 'a'] = LegendColor.Silver;
        map['i' - 'a'] = LegendColor.Gray;
        map['j' - 'a'] = LegendColor.DimGray;
        map['k' - 'a'] = LegendColor.DarkGray;
        map['l' - 'a'] = LegendColor.Charcoal;
        map['m' - 'a'] = LegendColor.Jet;
        map['n' - 'a'] = LegendColor.AlmostBlack;
        map['o' - 'a'] = LegendColor.HotPink;
        map['p' - 'a'] = LegendColor.DarkPurple;
        map['q' - 'a'] = LegendColor.Lime;
        map['r' - 'a'] = LegendColor.DarkGreen;
        map['s' - 'a'] = LegendColor.Orange;
        map['t' - 'a'] = LegendColor.Brown;
        map['u' - 'a'] = LegendColor.White;
        map['v' - 'a'] = LegendColor.CornflowerBlue;
        map['w' - 'a'] = LegendColor.HotPink;
        map['x' - 'a'] = LegendColor.Black;

        return map;
    }

    /// <summary>
    ///     Returns the SKColor for a named legend palette color.
    /// </summary>
    public static SKColor GetColor(LegendColor color) => Palette[(byte)color];

    /// <summary>
    ///     Returns the SKColor for a raw palette index (0-255).
    /// </summary>
    public static SKColor GetColor(int index) => Palette[index];

    /// <summary>
    ///     Maps a text color code character (the letter after {=) to its LegendColor, or null if invalid.
    /// </summary>
    public static LegendColor? GetTextColor(char code)
    {
        var index = code - 'a';

        if (index is < 0 or > 23)
            return null;

        return TextColorMap[index];
    }

    public static void Initialize() => Palette = Palette.FromEntry(DatArchives.Legend["legend.pal"]);
}
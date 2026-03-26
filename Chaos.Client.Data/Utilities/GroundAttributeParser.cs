#region
using System.Text;
using System.Text.RegularExpressions;
using Chaos.Client.Data.Models;
using DALib.Data;
#endregion

namespace Chaos.Client.Data.Utilities;

/// <summary>
///     Parses gndattr.tbl from seo.dat into a tile ID → <see cref="GroundAttribute" /> lookup. The file uses a custom
///     config format with C-style comments, set_attr blocks containing ATTR_gnd_paint (RGBA + height), and apply_to
///     sections listing tile IDs as individual values or (start, end) inclusive ranges.
/// </summary>
public static partial class GroundAttributeParser
{
    // Matches "apply_to :" followed by everything until the block's closing bracket
    [GeneratedRegex(@"apply_to\s*:\s*([\s\S]+)", RegexOptions.Compiled)]
    private static partial Regex ApplyToRegex();

    // Matches ATTR_gnd_paint : ( R, G, B, A ), H
    [GeneratedRegex(@"ATTR_gnd_paint\s*:\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)\s*,\s*(\d+)", RegexOptions.Compiled)]
    private static partial Regex AttrPaintRegex();

    /// <summary>
    ///     Parses the gndattr.tbl file from a DataArchiveEntry. Returns a dictionary mapping background tile IDs to their
    ///     ground attributes. When multiple set_attr blocks target the same tile ID, the last block's RGBA/Height wins
    ///     (matching the original client's vector overwrite behavior).
    /// </summary>
    public static Dictionary<int, GroundAttribute> Parse(DataArchiveEntry entry)
    {
        var text = Encoding.ASCII.GetString(entry.ToSpan());

        return Parse(text);
    }

    /// <summary>
    ///     Parses the gndattr.tbl text content.
    /// </summary>
    public static Dictionary<int, GroundAttribute> Parse(string text)
    {
        var result = new Dictionary<int, GroundAttribute>();

        // Strip C-style comments /* ... */
        text = StripComments(text);

        // Find each [ set_attr : ... ] block
        foreach (Match blockMatch in SetAttrBlockRegex()
                     .Matches(text))
        {
            var blockText = blockMatch.Value;

            // Parse ATTR_gnd_paint : ( R, G, B, A ), H
            var paintMatch = AttrPaintRegex()
                .Match(blockText);

            if (!paintMatch.Success)
                continue;

            var r = byte.Parse(paintMatch.Groups[1].Value);
            var g = byte.Parse(paintMatch.Groups[2].Value);
            var b = byte.Parse(paintMatch.Groups[3].Value);
            var a = byte.Parse(paintMatch.Groups[4].Value);
            var h = int.Parse(paintMatch.Groups[5].Value);

            // Parse apply_to section — collect all tile IDs
            var applyMatch = ApplyToRegex()
                .Match(blockText);

            if (!applyMatch.Success)
                continue;

            var tileIds = ParseTileIds(applyMatch.Groups[1].Value);

            // Apply attribute to each tile ID — flags are ORed together across blocks
            foreach (var tileId in tileIds)
            {
                if (!result.TryGetValue(tileId, out var attr))
                {
                    attr = new GroundAttribute();
                    result[tileId] = attr;
                }

                if (h == 1)
                    attr.IsWalkBlocking = true;
                else if (h == 2)
                    attr.IsAdjacentWater = true;
                else if (h > 2)
                {
                    attr.R = r;
                    attr.G = g;
                    attr.B = b;
                    attr.A = a;
                    attr.PaintHeight = h;
                }
            }
        }

        return result;
    }

    private static List<int> ParseTileIds(string applyToText)
    {
        var ids = new List<int>();

        // First extract and process all (start, end) ranges, replacing them with empty strings
        var cleaned = RangeRegex()
            .Replace(
                applyToText,
                match =>
                {
                    var start = int.Parse(match.Groups[1].Value);
                    var end = int.Parse(match.Groups[2].Value);

                    for (var id = start; id <= end; id++)
                        ids.Add(id);

                    return " ";
                });

        // Remaining tokens are individual tile IDs
        foreach (var token in cleaned.Split(
                     (char[])
                     [
                         ' ',
                         '\t',
                         '\r',
                         '\n',
                         ','
                     ],
                     StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(token, out var id))
                ids.Add(id);

        return ids;
    }

    // Matches a (start, end) inclusive range
    [GeneratedRegex(@"\(\s*(\d+)\s*,\s*(\d+)\s*\)", RegexOptions.Compiled)]
    private static partial Regex RangeRegex();

    // Matches a complete set_attr block (non-greedy — stops at the first closing bracket)
    [GeneratedRegex(@"\[\s*set_attr\s*:.*?\](?=\s*\[|\s*$)", RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex SetAttrBlockRegex();

    private static string StripComments(string text)
    {
        var sb = new StringBuilder(text.Length);
        var i = 0;

        while (i < text.Length)
            if (((i + 1) < text.Length) && (text[i] == '/') && (text[i + 1] == '*'))
            {
                // Skip until */
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end >= 0 ? end + 2 : text.Length;
            } else
            {
                sb.Append(text[i]);
                i++;
            }

        return sb.ToString();
    }
}
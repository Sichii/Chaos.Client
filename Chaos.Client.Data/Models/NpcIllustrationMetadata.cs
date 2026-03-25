#region
using DALib.Data;
#endregion

namespace Chaos.Client.Data.Models;

/// <summary>
///     Parsed "NPCIllust" metadata file. Maps NPC names to their illustration SPF filenames.
/// </summary>
public sealed class NpcIllustrationMetadata
{
    /// <summary>
    ///     NPC name (case-insensitive) to illustration SPF filename (e.g. "bank.spf").
    /// </summary>
    public IReadOnlyDictionary<string, string> Illustrations { get; }

    private NpcIllustrationMetadata(Dictionary<string, string> illustrations) => Illustrations = illustrations;

    public static NpcIllustrationMetadata Parse(MetaFile metaFile)
    {
        var illustrations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in metaFile)
            if (entry.Properties.Count > 0)
                illustrations[entry.Key] = entry.Properties[0];

        return new NpcIllustrationMetadata(illustrations);
    }
}
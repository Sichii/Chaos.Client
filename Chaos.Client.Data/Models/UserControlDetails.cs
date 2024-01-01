using System.Collections.ObjectModel;
using Chaos.Client.Data.Utilities;
using DALib.Data;

namespace Chaos.Client.Data.Models;

public sealed class UserControlDetails(string name) : KeyedCollection<string, ControlDetails>(StringComparer.OrdinalIgnoreCase)
{
    public string Name { get; set; } = name;

    public static Dictionary<string, UserControlDetails> FromArchive(DataArchive setoaArchive)
    {
        var controlLookup = new Dictionary<string, UserControlDetails>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in setoaArchive.GetEntries(".txt"))
        {
            var controlCollection = FromEntry(entry);
            controlLookup.Add(controlCollection.Name, controlCollection);
        }

        return controlLookup;
    }

    public static UserControlDetails FromEntry(DataArchiveEntry entry)
    {
        using var segment = entry.ToStreamSegment();

        var name = Path.GetFileNameWithoutExtension(entry.EntryName);
        var controlParser = new ControlInfoParser();

        return controlParser.Parse(name, segment);
    }

    /// <inheritdoc />
    protected override string GetKeyForItem(ControlDetails item) => item.Name;
}
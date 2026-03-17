#region
using Microsoft.Xna.Framework;
#endregion

namespace Chaos.Client.Models;

/// <summary>
///     A single legend mark entry for the Legend tab page.
/// </summary>
public record LegendMarkEntry(
    string Text,
    Color Color,
    byte Icon = 0,
    string Key = "");
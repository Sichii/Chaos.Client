#region
using Chaos.DarkAges.Definitions;
#endregion

namespace Chaos.Client.Rendering.Models;

public readonly record struct TabMapEntity(
    int TileX,
    int TileY,
    ClientEntityType Type,
    uint Id,
    CreatureType CreatureType);
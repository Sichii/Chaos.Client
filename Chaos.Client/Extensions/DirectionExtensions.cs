#region
using Chaos.Geometry.Abstractions.Definitions;
#endregion

namespace Chaos.Client.Extensions;

public static class DirectionExtensions
{
    public static (int DX, int DY) ToTileOffset(this Direction direction)
        => direction switch
        {
            Direction.Up    => (0, -1),
            Direction.Right => (1, 0),
            Direction.Down  => (0, 1),
            Direction.Left  => (-1, 0),
            _               => (0, 0)
        };
}
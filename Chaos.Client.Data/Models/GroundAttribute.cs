namespace Chaos.Client.Data.Models;

/// <summary>
///     Ground tile attribute parsed from gndattr.tbl. Applied to background tile IDs. Multiple set_attr blocks targeting
///     the same tile ID have their flags ORed together (a tile can be both walk-blocking AND have an adjacent-water flag).
/// </summary>
public sealed class GroundAttribute
{
    public byte A { get; set; }
    public byte B { get; set; }

    public byte G { get; set; }

    /// <summary>
    ///     Adjacent-water flag (from H == 2 blocks). Tile is near water edges.
    /// </summary>
    public bool IsAdjacentWater { get; set; }

    /// <summary>
    ///     Walk-blocking flag (from H == 1 blocks). Tile blocks movement (deep water).
    /// </summary>
    public bool IsWalkBlocking { get; set; }

    /// <summary>
    ///     Paint height value (from H > 2 blocks). Controls foreground height override — objects appear submerged.
    /// </summary>
    public int PaintHeight { get; set; }

    /// <summary>
    ///     Color overlay tint (RGBA 0-255). Applied as a color wash over the background tile.
    /// </summary>
    public byte R { get; set; }

    /// <summary>
    ///     True when the attribute has a visible color tint to apply (A > 0 and PaintHeight > 0).
    /// </summary>
    public bool HasColorTint => (A > 0) && (PaintHeight > 0);

    /// <summary>
    ///     True when the tile is a water tile — either walk-blocking (H == 1) or painted water (H > 2).
    /// </summary>
    public bool IsWater => IsWalkBlocking || (PaintHeight > 0);
}
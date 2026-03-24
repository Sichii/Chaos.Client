namespace Chaos.Client.Models;

/// <summary>
///     Tracks double-click detection for a tile position. Call <see cref="Tick" /> each frame, then <see cref="Click" />
///     when a click occurs to get whether it's a double-click.
/// </summary>
public struct TileClickTracker
{
    private const float DOUBLE_CLICK_MS = 400f;

    private int LastTileX;
    private int LastTileY;
    private float Timer = DOUBLE_CLICK_MS;

    public TileClickTracker() { }

    public void Tick(float elapsedMs) => Timer += elapsedMs;

    public bool Click(int tileX, int tileY)
    {
        var isDouble = (Timer < DOUBLE_CLICK_MS) && (tileX == LastTileX) && (tileY == LastTileY);

        Timer = 0;
        LastTileX = tileX;
        LastTileY = tileY;

        return isDouble;
    }
}
#region
using Chaos.Client.Controls.Components;
using Chaos.Geometry.Abstractions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Screens;

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

/// <summary>
///     Manages A* pathfinding state: the current path, optional entity target, and walk queue.
/// </summary>
public class PathfindingState
{
    public Stack<IPoint>? Path;
    public float RetargetTimer;
    public uint? TargetEntityId;

    public bool HasPath => Path is { Count: > 0 };
    public bool HasTarget => TargetEntityId.HasValue;

    public void Clear()
    {
        Path = null;
        TargetEntityId = null;
        RetargetTimer = 0;
    }

    public void SetEntityTarget(uint entityId)
    {
        TargetEntityId = entityId;
        RetargetTimer = 0;
    }

    public void SetPath(Stack<IPoint> path) => Path = path.Count > 0 ? path : null;
}

/// <summary>
///     Caches a tinted (highlight) version of an entity texture. Reuses the cached texture if the source and entity
///     haven't changed.
/// </summary>
public class EntityHighlightState : IDisposable
{
    public uint? HoveredEntityId;
    public bool ShowTintHighlight;
    private uint? TintedEntityId;
    private Texture2D? TintedSource;
    private Texture2D? TintedTexture;

    public void Dispose()
    {
        ClearTint();
        GC.SuppressFinalize(this);
    }

    public void ClearTint()
    {
        TintedTexture?.Dispose();
        TintedTexture = null;
        TintedEntityId = null;
        TintedSource = null;
    }

    public Texture2D? GetOrCreateTinted(Texture2D source, uint entityId, Func<Texture2D, Texture2D> createTint)
    {
        if (TintedTexture is not null && (TintedEntityId == entityId) && (TintedSource == source))
            return TintedTexture;

        TintedTexture?.Dispose();
        TintedTexture = createTint(source);
        TintedEntityId = entityId;
        TintedSource = source;

        return TintedTexture;
    }
}

/// <summary>
///     Simple FPS counter that updates once per second.
/// </summary>
public class FpsCounter
{
    private int Display;
    private float Elapsed;
    private int FrameCount;
    private CachedText? Text;

    public void Draw(SpriteBatch spriteBatch, Vector2 position) => Text?.Draw(spriteBatch, position);

    public void Update(GraphicsDevice device, float elapsedMs)
    {
        FrameCount++;
        Elapsed += elapsedMs;

        if (Elapsed < 1000f)
            return;

        var prev = Display;
        Display = FrameCount;
        FrameCount = 0;
        Elapsed -= 1000f;

        if (prev != Display)
        {
            Text ??= new CachedText(device);
            Text.Update($"FPS: {Display}", Color.White);
        }
    }
}
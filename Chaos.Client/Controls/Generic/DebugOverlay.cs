#region
using System.Diagnostics;
using System.Runtime;
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Generic;

/// <summary>
///     Debug overlay that draws colored outlines and centered names for all visible UI elements. Toggle with F12.
///     Also displays performance stats: frame time, GC collection counts, heap size, and Gen2 hitch detection.
///     Uses deferred batching and text caching to minimize draw calls and texture allocations.
/// </summary>
public static class DebugOverlay
{
    private const int FRAME_TIME_HISTORY = 180;
    private const float GRAPH_WIDTH = 180;
    private const float GRAPH_HEIGHT = 36;
    private const float GEN2_FLASH_DURATION_MS = 1000;
    private const int STATS_LINE_COUNT = 6;

    private static readonly float[] FrameTimeHistory = new float[FRAME_TIME_HISTORY];
    private static readonly Stopwatch FrameStopwatch = Stopwatch.StartNew();
    private static readonly Dictionary<UIElement, CachedText> ElementLabelCache = new();
    private static readonly List<(CachedText Text, Vector2 Position)> PendingLabels = [];
    private static CachedText[]? StatsTextCache;
    private static int FrameTimeIndex;
    private static int LastGen0Count;
    private static int LastGen1Count;
    private static int LastGen2Count;
    private static int Gen2Delta;
    private static float Gen2FlashTimer;
    private static float LastFrameTimeMs;
    private static long SnappedDrawCount;
    private static long DebugDrawCount;

    public static bool IsActive { get; set; }

    /// <summary>
    ///     Screen X position for the debug overlay.
    /// </summary>
    public static float X { get; set; } = 4;

    /// <summary>
    ///     Screen Y position for the debug overlay.
    /// </summary>
    public static float Y { get; set; } = 4;

    private static float GraphX => X;
    private static float GraphY => Y;
    private static float StatsX => X;
    private static float StatsY => Y + GRAPH_HEIGHT + 4;

    private static void ClearCaches()
    {
        foreach (var cached in ElementLabelCache.Values)
            cached.Dispose();

        ElementLabelCache.Clear();
        PendingLabels.Clear();

        if (StatsTextCache is not null)
        {
            foreach (var cached in StatsTextCache)

                // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
                cached?.Dispose();

            StatsTextCache = null;
        }
    }

    public static void Draw(SpriteBatch spriteBatch, UIPanel root)
    {
        if (!IsActive)
            return;

        PendingLabels.Clear();

        spriteBatch.Begin(samplerState: GlobalSettings.Sampler);

        // Phase 1: all pixel-texture draws (borders, rects, graph bars) — batches into minimal GPU calls
        foreach (var child in root.Children)
            DrawElementGeometry(spriteBatch, child);

        DrawPerformanceStatsGeometry(spriteBatch);

        // Phase 2: all text draws — each unique texture breaks the batch, but no pixel interruptions
        foreach ((var text, var pos) in PendingLabels)
            text.Draw(spriteBatch, pos);

        DrawPerformanceStatsText(spriteBatch);

        spriteBatch.End();

        // Capture how many draws the debug overlay itself added
        DebugDrawCount = ChaosGame.Device.Metrics.DrawCount - SnappedDrawCount;
    }

    private static void DrawElementGeometry(SpriteBatch spriteBatch, UIElement element)
    {
        if (!element.Visible)
            return;

        var sx = element.ScreenX;
        var sy = element.ScreenY;
        var w = element.Width;
        var h = element.Height;

        // Use texture dimensions as fallback when Width/Height are 0
        if ((w == 0) && (h == 0) && element is UIPanel { Background: not null } bgPanel)
        {
            w = bgPanel.Background.Width;
            h = bgPanel.Background.Height;
        }

        if ((w > 0) && (h > 0))
        {
            var color = element switch
            {
                UIButton  => Color.Lime,
                UITextBox => Color.Cyan,
                UILabel   => Color.Yellow,
                UIImage   => Color.Magenta,
                UIPanel   => Color.Red,
                _         => Color.White
            };

            var borderColor = color * 0.8f;

            UIElement.DrawBorder(
                spriteBatch,
                new Rectangle(
                    sx,
                    sy,
                    w,
                    h),
                borderColor);

            // Prepare cached label and accumulate for deferred text draw
            var name = element.Name.Length > 0
                ? element.Name
                : element.GetType()
                         .Name;

            if (!ElementLabelCache.TryGetValue(element, out var cachedLabel))
            {
                cachedLabel = new CachedText();
                ElementLabelCache[element] = cachedLabel;
            }

            cachedLabel.Update(name, color);

            if (cachedLabel.Texture is not null)
            {
                var tw = cachedLabel.Texture.Width;
                var th = cachedLabel.Texture.Height;
                var tx = sx + (w - tw) / 2;
                var ty = sy + (h - th) / 2;

                UIElement.DrawRect(
                    spriteBatch,
                    new Rectangle(
                        tx - 1,
                        ty - 1,
                        tw + 2,
                        th + 2),
                    Color.Black * 0.66f);

                PendingLabels.Add((cachedLabel, new Vector2(tx, ty)));
            }
        }

        if (element is UIPanel panel)
            foreach (var child in panel.Children)
                DrawElementGeometry(spriteBatch, child);
    }

    private static void DrawFrameTimeGraph(SpriteBatch spriteBatch)
    {
        var sampleCount = Math.Min(FrameTimeIndex, FRAME_TIME_HISTORY);

        if (sampleCount < 2)
            return;

        var barWidth = GRAPH_WIDTH / FRAME_TIME_HISTORY;

        // 33ms target line (30fps — anything above this is a serious hitch)
        var targetY33 = GraphY + GRAPH_HEIGHT - GRAPH_HEIGHT * (33f / 50f);

        // 16.67ms target line (60fps)
        var targetY16 = GraphY + GRAPH_HEIGHT - GRAPH_HEIGHT * (16.67f / 50f);

        // 60fps line
        UIElement.DrawRect(
            spriteBatch,
            new Rectangle(
                (int)GraphX,
                (int)targetY16,
                (int)GRAPH_WIDTH,
                1),
            Color.Lime * 0.4f);

        // 30fps line
        UIElement.DrawRect(
            spriteBatch,
            new Rectangle(
                (int)GraphX,
                (int)targetY33,
                (int)GRAPH_WIDTH,
                1),
            Color.Red * 0.4f);

        // Draw bars
        for (var i = 0; i < FRAME_TIME_HISTORY; i++)
        {
            var idx = (FrameTimeIndex - FRAME_TIME_HISTORY + i + FRAME_TIME_HISTORY) % FRAME_TIME_HISTORY;
            var ms = FrameTimeHistory[idx];

            if (ms <= 0)
                continue;

            // Clamp to graph range (0-50ms)
            var normalized = Math.Min(ms / 50f, 1f);
            var barHeight = (int)(GRAPH_HEIGHT * normalized);
            var barX = (int)(GraphX + i * barWidth);
            var barY = (int)(GraphY + GRAPH_HEIGHT - barHeight);

            var barColor = ms > 33
                ? Color.Red
                : ms > 17
                    ? Color.Yellow
                    : Color.Lime;

            UIElement.DrawRect(
                spriteBatch,
                new Rectangle(
                    barX,
                    barY,
                    Math.Max((int)barWidth, 1),
                    barHeight),
                barColor * 0.8f);
        }
    }

    private static void DrawPerformanceStatsGeometry(SpriteBatch spriteBatch)
    {
        // Background for stats area
        var statsHeight = 82;

        UIElement.DrawRect(
            spriteBatch,
            new Rectangle(
                (int)GraphX - 2,
                (int)GraphY - 2,
                (int)GRAPH_WIDTH + 4,
                (int)GRAPH_HEIGHT + statsHeight + 8),
            Color.Black * 0.33f);

        // Frame time graph
        DrawFrameTimeGraph(spriteBatch);

        // Flash the whole stats area red-tinted when a Gen2 collection just happened
        if (Gen2FlashTimer > 0)
        {
            var flashAlpha = Gen2FlashTimer / GEN2_FLASH_DURATION_MS * 0.3f;

            UIElement.DrawRect(
                spriteBatch,
                new Rectangle(
                    (int)GraphX - 2,
                    (int)GraphY - 2,
                    (int)GRAPH_WIDTH + 4,
                    (int)GRAPH_HEIGHT + statsHeight + 8),
                Color.Red * flashAlpha);
        }
    }

    private static void DrawPerformanceStatsText(SpriteBatch spriteBatch)
    {
        // Text stats
        var heapMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
        var fps = LastFrameTimeMs > 0 ? 1000f / LastFrameTimeMs : 0;
        var gcMode = GCSettings.IsServerGC ? "Server" : "Workstation";
        var latencyMode = GCSettings.LatencyMode;

        var drawCount = SnappedDrawCount;

        var lines = new (string Text, Color Color)[]
        {
            ($"{fps:F0} FPS  {LastFrameTimeMs:F1}ms", LastFrameTimeMs > 20
                ? Color.Red
                : LastFrameTimeMs > 17
                    ? Color.Yellow
                    : Color.Lime),
            ($"Draws: {drawCount} (excl. debug)", drawCount > 3000 ? Color.Yellow : Color.White),
            ($"Debug draws: {DebugDrawCount}", Color.Gray),
            ($"Heap: {heapMb:F1} MB  ({gcMode})", Color.White),
            ($"GC: G0={LastGen0Count} G1={LastGen1Count} G2={LastGen2Count}", Gen2Delta > 0 ? Color.Red : Color.White),
            ($"Latency: {latencyMode}", Color.Gray)
        };

        StatsTextCache ??= new CachedText[STATS_LINE_COUNT];

        var y = StatsY;

        for (var i = 0; i < lines.Length; i++)
        {
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            StatsTextCache[i] ??= new CachedText();

            StatsTextCache[i]
                .Update(lines[i].Text, lines[i].Color);

            if (StatsTextCache[i].Texture is not null)
            {
                StatsTextCache[i]
                    .Draw(spriteBatch, new Vector2(StatsX, y));
                y += StatsTextCache[i].Texture!.Height + 1;
            }
        }
    }

    /// <summary>
    ///     Captures the GPU draw count before the debug overlay renders its own draws. Call immediately before Draw() so the
    ///     reported count excludes debug overlay draws.
    /// </summary>
    public static void SnapshotDrawCount() => SnappedDrawCount = ChaosGame.Device.Metrics.DrawCount;

    public static void Toggle()
    {
        IsActive = !IsActive;

        if (!IsActive)
            ClearCaches();
    }

    /// <summary>
    ///     Call once per frame from Update() to sample frame time and GC counters.
    /// </summary>
    public static void Update(GameTime gameTime)
    {
        if (!IsActive)
            return;

        // Frame time from stopwatch (measures real wall time between Update calls)
        LastFrameTimeMs = (float)FrameStopwatch.Elapsed.TotalMilliseconds;
        FrameStopwatch.Restart();

        FrameTimeHistory[FrameTimeIndex % FRAME_TIME_HISTORY] = LastFrameTimeMs;
        FrameTimeIndex++;

        // GC collection tracking
        var g0 = GC.CollectionCount(0);
        var g1 = GC.CollectionCount(1);
        var g2 = GC.CollectionCount(2);

        Gen2Delta = g2 - LastGen2Count;

        if (Gen2Delta > 0)
            Gen2FlashTimer = GEN2_FLASH_DURATION_MS;

        LastGen0Count = g0;
        LastGen1Count = g1;
        LastGen2Count = g2;

        if (Gen2FlashTimer > 0)
            Gen2FlashTimer -= (float)gameTime.ElapsedGameTime.TotalMilliseconds;
    }
}
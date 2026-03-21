#region
using System.Diagnostics;
using System.Runtime;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

/// <summary>
///     Debug overlay that draws colored outlines and centered names for all visible UI elements. Toggle with F12.
///     Also displays performance stats: frame time, GC collection counts, heap size, and Gen2 hitch detection.
/// </summary>
public static class DebugOverlay
{
    private const int FRAME_TIME_HISTORY = 120;
    private const float GRAPH_WIDTH = 160;
    private const float GRAPH_HEIGHT = 50;
    private const float GRAPH_X = 4;
    private const float GRAPH_Y = 4;
    private const float STATS_X = 4;
    private const float STATS_Y = GRAPH_Y + GRAPH_HEIGHT + 4;
    private const float GEN2_FLASH_DURATION_MS = 1000;

    private static readonly float[] FrameTimeHistory = new float[FRAME_TIME_HISTORY];
    private static readonly Stopwatch FrameStopwatch = Stopwatch.StartNew();
    private static int FrameTimeIndex;
    private static int LastGen0Count;
    private static int LastGen1Count;
    private static int LastGen2Count;
    private static int Gen0Delta;
    private static int Gen1Delta;
    private static int Gen2Delta;
    private static float Gen2FlashTimer;
    private static float LastFrameTimeMs;

    public static bool IsActive { get; set; }

    public static void Draw(SpriteBatch spriteBatch, GraphicsDevice device, UIPanel root)
    {
        if (!IsActive)
            return;

        spriteBatch.Begin(SpriteSortMode.Immediate, samplerState: GlobalSettings.Sampler);

        foreach (var child in root.Children)
            DrawElement(spriteBatch, device, child);

        DrawPerformanceStats(spriteBatch, device);

        spriteBatch.End();
    }

    private static void DrawElement(SpriteBatch spriteBatch, GraphicsDevice device, UIElement element)
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
                device,
                new Rectangle(
                    sx,
                    sy,
                    w,
                    h),
                borderColor);

            // Name label centered in bounds with dark background
            var name = element.Name.Length > 0
                ? element.Name
                : element.GetType()
                         .Name;

            var nameTexture = TextRenderer.RenderText(device, name, color);

            var tw = nameTexture.Width;
            var th = nameTexture.Height;
            var tx = sx + (w - tw) / 2;
            var ty = sy + (h - th) / 2;

            UIElement.DrawRect(
                spriteBatch,
                device,
                new Rectangle(
                    tx - 1,
                    ty - 1,
                    tw + 2,
                    th + 2),
                Color.Black * 0.7f);
            spriteBatch.Draw(nameTexture, new Vector2(tx, ty), Color.White);
            nameTexture.Dispose();
        }

        if (element is UIPanel panel)
            foreach (var child in panel.Children)
                DrawElement(spriteBatch, device, child);
    }

    private static void DrawFrameTimeGraph(SpriteBatch spriteBatch, GraphicsDevice device)
    {
        var sampleCount = Math.Min(FrameTimeIndex, FRAME_TIME_HISTORY);

        if (sampleCount < 2)
            return;

        var barWidth = GRAPH_WIDTH / FRAME_TIME_HISTORY;

        // 33ms target line (30fps — anything above this is a serious hitch)
        var targetY33 = GRAPH_Y + GRAPH_HEIGHT - GRAPH_HEIGHT * (33f / 50f);

        // 16.67ms target line (60fps)
        var targetY16 = GRAPH_Y + GRAPH_HEIGHT - GRAPH_HEIGHT * (16.67f / 50f);

        // 60fps line
        UIElement.DrawRect(
            spriteBatch,
            device,
            new Rectangle(
                (int)GRAPH_X,
                (int)targetY16,
                (int)GRAPH_WIDTH,
                1),
            Color.Lime * 0.4f);

        // 30fps line
        UIElement.DrawRect(
            spriteBatch,
            device,
            new Rectangle(
                (int)GRAPH_X,
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
            var barX = (int)(GRAPH_X + i * barWidth);
            var barY = (int)(GRAPH_Y + GRAPH_HEIGHT - barHeight);

            var barColor = ms > 33
                ? Color.Red
                : ms > 17
                    ? Color.Yellow
                    : Color.Lime;

            UIElement.DrawRect(
                spriteBatch,
                device,
                new Rectangle(
                    barX,
                    barY,
                    Math.Max((int)barWidth, 1),
                    barHeight),
                barColor * 0.8f);
        }
    }

    private static void DrawPerformanceStats(SpriteBatch spriteBatch, GraphicsDevice device)
    {
        // Background for stats area
        var statsHeight = 80;

        UIElement.DrawRect(
            spriteBatch,
            device,
            new Rectangle(
                (int)GRAPH_X - 2,
                (int)GRAPH_Y - 2,
                (int)GRAPH_WIDTH + 4,
                (int)GRAPH_HEIGHT + statsHeight + 8),
            Color.Black * 0.75f);

        // Frame time graph
        DrawFrameTimeGraph(spriteBatch, device);

        // Text stats
        var heapMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
        var fps = LastFrameTimeMs > 0 ? 1000f / LastFrameTimeMs : 0;
        var gcMode = GCSettings.IsServerGC ? "Server" : "Workstation";
        var latencyMode = GCSettings.LatencyMode;

        var lines = new (string Text, Color Color)[]
        {
            ($"{fps:F0} FPS  {LastFrameTimeMs:F1}ms", LastFrameTimeMs > 20
                ? Color.Red
                : LastFrameTimeMs > 17
                    ? Color.Yellow
                    : Color.Lime),
            ($"Heap: {heapMb:F1} MB  ({gcMode})", Color.White),
            ($"GC: G0={LastGen0Count} G1={LastGen1Count} G2={LastGen2Count}", Color.White),
            ($"  \u0394 G0={Gen0Delta} G1={Gen1Delta} G2={Gen2Delta}", Gen2Delta > 0 ? Color.Red : Color.Gray),
            ($"Latency: {latencyMode}", Color.Gray)
        };

        // Flash the whole stats area red-tinted when a Gen2 collection just happened
        if (Gen2FlashTimer > 0)
        {
            var flashAlpha = Gen2FlashTimer / GEN2_FLASH_DURATION_MS * 0.3f;

            UIElement.DrawRect(
                spriteBatch,
                device,
                new Rectangle(
                    (int)GRAPH_X - 2,
                    (int)GRAPH_Y - 2,
                    (int)GRAPH_WIDTH + 4,
                    (int)GRAPH_HEIGHT + statsHeight + 8),
                Color.Red * flashAlpha);
        }

        var y = STATS_Y;

        foreach ((var text, var color) in lines)
        {
            var texture = TextRenderer.RenderText(device, text, color);
            spriteBatch.Draw(texture, new Vector2(STATS_X, y), Color.White);
            y += texture.Height + 1;
            texture.Dispose();
        }
    }

    public static void Toggle() => IsActive = !IsActive;

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

        Gen0Delta = g0 - LastGen0Count;
        Gen1Delta = g1 - LastGen1Count;
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
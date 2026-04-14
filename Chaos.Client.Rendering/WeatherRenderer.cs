#region
using Chaos.Client.Data;
using Chaos.Client.Rendering.Models;
using Chaos.DarkAges.Definitions;
using DALib.Drawing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Graphics = DALib.Drawing.Graphics;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Weather overlay for snow and rain. Driven by the low nibble of the <see cref="MapFlags"/> byte:
///     0 = none, 1 = Snow, 2 = Rain (client-only — retail treats case 2 as a no-op), 3 = Darkness
///     (handled by <see cref="DarknessRenderer"/>, so this renderer stays inactive on value 3). The flag
///     byte's high nibble is unrelated — NoTabMap and SnowTileset are separate concerns.
///     See docs/re_notes/map_flags.md for the full encoding.
/// </summary>
public sealed class WeatherRenderer : IDisposable
{
    // ============================================================
    // Weather tunables — adjust for visual feel. Change these,
    // rebuild, and the effect reflects the new values. No runtime
    // config surface.
    // ============================================================

    // Snow
    private const int SNOW_PARTICLE_COUNT = 50;    // retail uses ~304 across 4 types
    private const float SNOW_MIN_VELOCITY_Y = 30f; // px/sec
    private const float SNOW_MAX_VELOCITY_Y = 70f;
    private const float SNOW_DRIFT_X = 10f;        // horizontal drift range (+/-)

    // Rain
    private const float RAIN_FALL_SPEED = 400f;    // px/sec vertical fall speed
    private const int RAIN_COLUMN_COUNT = 5;       // each rain row is drawn as N vertical strips in a random order

    // ============================================================

    private readonly Random SnowRandom = new();

    //cached legend01.pal from legend.dat — used for both snow and rain per ChaosAssetManager routing
    private Palette? Legend01Palette;

    private MapFlags CurrentMapFlags;

    //low nibble of the map-flags byte — 1=Snow, 2=Rain, 3=Darkness (dark handled elsewhere)
    private byte WeatherNibble => (byte)((byte)CurrentMapFlags & 0x0F);

    // Snow state
    private Texture2D[]? SnowFrames;
    private SnowParticle[]? SnowParticles;
    private int LastViewportWidth;
    private int LastViewportHeight;

    // Rain state — queue of falling rain rows, each a full texture copy with its own shuffled columns
    private Texture2D? RainTexture;
    private readonly List<RainRow> RainRows = new();

    //each rain row is one instance of the rain texture falling down the screen with an independent
    //column ordering. new rows are added above as old ones slide off the top position, and rows are
    //removed once they fall fully below the viewport.
    private struct RainRow
    {
        public float Y;
        public int[] Permutation;
    }

    /// <summary>
    ///     Whether a weather effect is currently active (snow or rain mode with assets loaded).
    /// </summary>
    public bool IsActive => WeatherNibble is 0x01 or 0x02;

    public WeatherRenderer() { }

    /// <summary>
    ///     Call on map change. Compares nibbles; if the weather mode changed, releases old textures
    ///     and loads new ones.
    /// </summary>
    public void OnMapChanged(MapFlags flags)
    {
        var oldNibble = WeatherNibble;
        CurrentMapFlags = flags;
        var newNibble = WeatherNibble;

        Console.WriteLine($"[Weather] OnMapChanged flags=0x{(byte)flags:X2} oldNibble={oldNibble} newNibble={newNibble}");

        if (oldNibble == newNibble)
            return;

        ReleaseTextures();

        switch (newNibble)
        {
            case 0x01:
                LoadSnowAssets();

                break;

            case 0x02:
                LoadRainAsset();

                break;
        }
    }

    /// <summary>
    ///     Call each frame before <see cref="Draw"/>. Advances particle positions or rain scroll offset.
    /// </summary>
    public void Update(GameTime gameTime, Rectangle viewport)
    {
        var nibble = WeatherNibble;

        if (nibble is not (0x01 or 0x02))
            return;

        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        switch (nibble)
        {
            case 0x01:
                UpdateSnow(dt, viewport);

                break;

            case 0x02:
                UpdateRain(dt, viewport);

                break;
        }
    }

    /// <summary>
    ///     Draws the current weather overlay inside <paramref name="viewport"/>. Returns early when the
    ///     mode is none (nibble 0 or 3).
    /// </summary>
    public void Draw(SpriteBatch spriteBatch, Rectangle viewport)
    {
        var nibble = WeatherNibble;

        if (nibble is not (0x01 or 0x02))
            return;

        switch (nibble)
        {
            case 0x01:
                DrawSnow(spriteBatch);

                break;

            case 0x02:
                DrawRain(spriteBatch, viewport);

                break;
        }
    }

    /// <inheritdoc />
    public void Dispose() => ReleaseTextures();

    private void ReleaseTextures()
    {
        if (SnowFrames is not null)
        {
            for (var i = 0; i < SnowFrames.Length; i++)
                SnowFrames[i]?.Dispose();

            SnowFrames = null;
        }

        RainTexture?.Dispose();
        RainTexture = null;
        RainRows.Clear();
        SnowParticles = null;
        LastViewportWidth = 0;
        LastViewportHeight = 0;
    }

    // ============================================================
    // Snow
    // ============================================================

    private Palette? GetLegend01Palette()
    {
        if (Legend01Palette is not null)
            return Legend01Palette;

        if (!DatArchives.Legend.TryGetValue("legend01.pal", out var entry))
            return null;

        Legend01Palette = Palette.FromEntry(entry);

        return Legend01Palette;
    }

    private void LoadSnowAssets()
    {
        var palette = GetLegend01Palette();

        if (palette is null)
        {
            Console.WriteLine("[Weather] LoadSnowAssets: legend01.pal missing, dropping to None mode");
            CurrentMapFlags &= ~(MapFlags)0x0F;

            return;
        }

        var frames = new List<Texture2D>(4);

        for (var i = 0; i < 4; i++)
        {
            var name = $"snowa{i:D2}.epf";

            if (!DatArchives.Legend.TryGetValue(name, out var entry))
            {
                Console.WriteLine($"[Weather] LoadSnowAssets: {name} not in legend.dat");
                continue;
            }

            var epf = EpfFile.FromEntry(entry);

            if (epf.Count == 0)
                continue;

            using var image = Graphics.RenderImage(epf[0], palette);
            var texture = TextureConverter.ToTexture2D(image);
            Console.WriteLine($"[Weather] LoadSnowAssets: {name} loaded {texture.Width}x{texture.Height}");
            frames.Add(texture);
        }

        if (frames.Count == 0)
        {
            Console.WriteLine("[Weather] LoadSnowAssets: zero frames loaded, dropping to None mode");
            CurrentMapFlags &= ~(MapFlags)0x0F;

            return;
        }

        SnowFrames = frames.ToArray();
        SnowParticles = new SnowParticle[SNOW_PARTICLE_COUNT];
        Console.WriteLine($"[Weather] LoadSnowAssets: SUCCESS {SnowFrames.Length} frames, {SNOW_PARTICLE_COUNT} particles");
    }

    private void UpdateSnow(float dt, Rectangle viewport)
    {
        if (SnowFrames is null || SnowParticles is null || (viewport.Width <= 0) || (viewport.Height <= 0))
            return;

        //respawn all particles when viewport changes (hud swap, map change)
        if ((viewport.Width != LastViewportWidth) || (viewport.Height != LastViewportHeight))
        {
            for (var i = 0; i < SnowParticles.Length; i++)
                SnowParticles[i] = RandomSnowParticle(viewport, spawnAbove: false);

            LastViewportWidth = viewport.Width;
            LastViewportHeight = viewport.Height;
        }

        //particles keep their FrameIndex for life — the 4 snowa files are distinct shapes,
        //not animation frames of a single shape
        for (var i = 0; i < SnowParticles.Length; i++)
        {
            ref var p = ref SnowParticles[i];

            p.Position.Y += p.VelocityY * dt;
            p.Position.X += p.VelocityX * dt;

            if (p.Position.Y > viewport.Bottom)
                p = RandomSnowParticle(viewport, spawnAbove: true);
        }
    }

    private SnowParticle RandomSnowParticle(Rectangle viewport, bool spawnAbove)
    {
        var x = (float)SnowRandom.Next(viewport.Left, viewport.Right);

        var y = spawnAbove
            ? viewport.Top - SnowRandom.Next(0, 30)
            : SnowRandom.Next(viewport.Top, viewport.Bottom);

        var vy = SNOW_MIN_VELOCITY_Y + ((float)SnowRandom.NextDouble() * (SNOW_MAX_VELOCITY_Y - SNOW_MIN_VELOCITY_Y));
        var vx = (((float)SnowRandom.NextDouble() * 2f) - 1f) * SNOW_DRIFT_X;

        return new SnowParticle
        {
            Position = new Vector2(x, y),
            VelocityY = vy,
            VelocityX = vx,
            FrameIndex = SnowRandom.Next(0, SnowFrames!.Length)
        };
    }

    private bool _drawSnowLoggedOnce;
    private void DrawSnow(SpriteBatch spriteBatch)
    {
        if (SnowFrames is null || SnowParticles is null)
        {
            if (!_drawSnowLoggedOnce)
            {
                Console.WriteLine($"[Weather] DrawSnow: null guard hit — SnowFrames={(SnowFrames is null ? "null" : SnowFrames.Length.ToString())} SnowParticles={(SnowParticles is null ? "null" : SnowParticles.Length.ToString())}");
                _drawSnowLoggedOnce = true;
            }
            return;
        }

        if (!_drawSnowLoggedOnce)
        {
            var first = SnowParticles[0];
            Console.WriteLine($"[Weather] DrawSnow FIRST CALL: {SnowParticles.Length} particles, first pos=({first.Position.X:F1},{first.Position.Y:F1}) frame0 size={SnowFrames[0].Width}x{SnowFrames[0].Height}");
            _drawSnowLoggedOnce = true;
        }

        var frameCount = SnowFrames.Length;

        for (var i = 0; i < SnowParticles.Length; i++)
        {
            var p = SnowParticles[i];
            var frame = SnowFrames[p.FrameIndex % frameCount];

            spriteBatch.Draw(frame, p.Position, Color.White);
        }
    }

    // ============================================================
    // Rain
    // ============================================================

    private void LoadRainAsset()
    {
        var palette = GetLegend01Palette();

        if (palette is null)
        {
            Console.WriteLine("[Weather] LoadRainAsset: legend01.pal missing, dropping to None mode");
            CurrentMapFlags &= ~(MapFlags)0x0F;

            return;
        }

        if (!DatArchives.Legend.TryGetValue("rain01.epf", out var entry))
        {
            Console.WriteLine("[Weather] LoadRainAsset: rain01.epf not in legend.dat, dropping to None mode");
            CurrentMapFlags &= ~(MapFlags)0x0F;

            return;
        }

        var epf = EpfFile.FromEntry(entry);

        if (epf.Count == 0)
        {
            Console.WriteLine("[Weather] LoadRainAsset: rain01.epf has no frames");
            CurrentMapFlags &= ~(MapFlags)0x0F;

            return;
        }

        using var image = Graphics.RenderImage(epf[0], palette);
        RainTexture = TextureConverter.ToTexture2D(image);
        Console.WriteLine($"[Weather] LoadRainAsset: rain01.epf loaded {RainTexture.Width}x{RainTexture.Height}");
        RainRows.Clear();
    }

    private int[] GenerateColumnPermutation()
    {
        var arr = new int[RAIN_COLUMN_COUNT];

        for (var i = 0; i < RAIN_COLUMN_COUNT; i++)
            arr[i] = i;

        //fisher-yates shuffle — a single permutation applied consistently across every tile and wrap
        //so there's no mid-screen seam. a fresh shuffle is generated each time rain re-activates.
        for (var i = RAIN_COLUMN_COUNT - 1; i > 0; i--)
        {
            var j = SnowRandom.Next(i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }

        return arr;
    }

    private void UpdateRain(float dt, Rectangle viewport)
    {
        if (RainTexture is null)
            return;

        var texH = RainTexture.Height;

        //first call after rain activated — seed the queue with two rows at the top of the viewport
        if (RainRows.Count == 0)
        {
            RainRows.Add(new RainRow { Y = viewport.Y, Permutation = GenerateColumnPermutation() });
            RainRows.Add(new RainRow { Y = viewport.Y + texH, Permutation = GenerateColumnPermutation() });
        }

        //advance every row
        var dy = RAIN_FALL_SPEED * dt;

        for (var i = 0; i < RainRows.Count; i++)
        {
            var row = RainRows[i];
            row.Y += dy;
            RainRows[i] = row;
        }

        //as the topmost row's top edge falls below the top of the viewport, insert a fresh randomized
        //row above it so its bottom sits flush with the old topmost's top. while-loop handles large dt.
        while ((RainRows.Count > 0) && (RainRows[0].Y > viewport.Y))
        {
            RainRows.Insert(
                0,
                new RainRow
                {
                    Y = RainRows[0].Y - texH,
                    Permutation = GenerateColumnPermutation()
                });
        }

        //drop rows whose top has passed the bottom of the viewport
        var viewportBottom = viewport.Y + viewport.Height;

        for (var i = RainRows.Count - 1; i >= 0; i--)
            if (RainRows[i].Y > viewportBottom)
                RainRows.RemoveAt(i);
    }

    private void DrawRain(SpriteBatch spriteBatch, Rectangle viewport)
    {
        if (RainTexture is null || (viewport.Width <= 0) || (viewport.Height <= 0) || (RainRows.Count == 0))
            return;

        var texW = RainTexture.Width;
        var texH = RainTexture.Height;
        var colW = texW / RAIN_COLUMN_COUNT;
        var tilesX = ((viewport.Width + texW) - 1) / texW;

        //each row is a full texture copy falling at its own Y with its own column permutation
        for (var r = 0; r < RainRows.Count; r++)
        {
            var row = RainRows[r];
            var y = (int)row.Y;
            var perm = row.Permutation;

            for (var tx = 0; tx < tilesX; tx++)
            {
                var baseX = viewport.X + (tx * texW);

                for (var c = 0; c < RAIN_COLUMN_COUNT; c++)
                {
                    var srcX = perm[c] * colW;
                    var destX = baseX + (c * colW);
                    var srcRect = new Rectangle(srcX, 0, colW, texH);
                    var destRect = new Rectangle(destX, y, colW, texH);

                    spriteBatch.Draw(RainTexture, destRect, srcRect, Color.White);
                }
            }
        }
    }
}

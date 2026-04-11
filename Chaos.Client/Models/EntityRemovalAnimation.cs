#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Models;

/// <summary>
///     Procedural horizontal dissolve effect for creature/merchant death. Captures the entity's last sprite, then over 9
///     frames (540ms) odd scanlines shift rightward while the whole image fades out. Matches the original DA client
///     behavior (WorldObject_Dying).
/// </summary>
public sealed class EntityRemovalAnimation : IDisposable
{
    private const int FRAME_COUNT = 9;
    private const float FRAME_INTERVAL_MS = 60f;
    private const float INITIAL_ALPHA = 1.0f;
    private const float ALPHA_DECAY_PER_FRAME = 3f / 32f;

    private readonly Color[] Pixels;
    private readonly int MaxWidth;
    private readonly int Step;
    private readonly int TextureWidth;
    public float Alpha { get; private set; } = INITIAL_ALPHA;
    public int CenterXOffset { get; private set; }
    public int CurrentFrame { get; private set; }
    public float ElapsedMs { get; set; }
    public short CenterX { get; }
    public short CenterY { get; }
    public bool Flip { get; }
    public short Left { get; }
    public int SourceWidth { get; private set; }
    public int TextureHeight { get; }
    public Texture2D Texture { get; }
    public int TileX { get; }
    public int TileY { get; }
    public short Top { get; }
    public bool IsComplete => CurrentFrame >= FRAME_COUNT;

    public EntityRemovalAnimation(
        GraphicsDevice device,
        Texture2D sourceTexture,
        int tileX,
        int tileY,
        short centerX,
        short centerY,
        short left,
        short top,
        bool flip)
    {
        TileX = tileX;
        TileY = tileY;
        CenterX = centerX;
        CenterY = centerY;
        Left = left;
        Top = top;
        Flip = flip;

        TextureWidth = sourceTexture.Width;
        TextureHeight = sourceTexture.Height;

        //compute step size based on texture height
        int adjusted;

        if (TextureHeight < 121)
            adjusted = TextureHeight / 2;
        else if (TextureHeight < 301)
            adjusted = TextureHeight / 3;
        else
            adjusted = TextureHeight / 5;

        var rawStep = adjusted / FRAME_COUNT;
        Step = rawStep < 3 ? 2 : (rawStep / 2) * 2;

        //allocate wider buffer to accommodate rightward shift of odd rows
        MaxWidth = TextureWidth + FRAME_COUNT * Step;
        SourceWidth = TextureWidth;

        //copy source pixels into wider buffer (left-aligned, MaxWidth stride)
        var srcPixels = new Color[TextureWidth * TextureHeight];
        sourceTexture.GetData(srcPixels);

        Pixels = new Color[MaxWidth * TextureHeight];

        for (var row = 0; row < TextureHeight; row++)
            Array.Copy(srcPixels, row * TextureWidth, Pixels, row * MaxWidth, TextureWidth);

        Texture = new Texture2D(device, MaxWidth, TextureHeight);
        Texture.SetData(Pixels);
    }

    public void Dispose() => Texture.Dispose();

    /// <summary>
    ///     Shifts every odd row right by Step pixels and clears the leftmost Step pixels. Operates on the Pixels array
    ///     in-place, then uploads to the Texture. Creates horizontal comb dissolve matching the original DA client.
    /// </summary>
    private void ApplyRowDissolve()
    {
        for (var row = 1; row < TextureHeight; row += 2)
        {
            var rowStart = row * MaxWidth;

            //shift row content right by Step pixels (right to left to avoid overwrite)
            for (var col = SourceWidth - 1; col >= 0; col--)
                Pixels[rowStart + col + Step] = Pixels[rowStart + col];

            //clear leftmost Step pixels to transparent
            for (var col = 0; col < Step; col++)
                Pixels[rowStart + col] = Color.Transparent;
        }

        SourceWidth += Step;
        Texture.SetData(Pixels);
    }

    /// <summary>
    ///     Advances the dissolve animation. Returns true if a new frame was applied.
    /// </summary>
    public bool Update(float elapsedMs)
    {
        if (IsComplete)
            return false;

        ElapsedMs += elapsedMs;
        var advanced = false;

        while ((ElapsedMs >= FRAME_INTERVAL_MS) && !IsComplete)
        {
            ElapsedMs -= FRAME_INTERVAL_MS;
            CurrentFrame++;
            Alpha = Math.Max(0, Alpha - ALPHA_DECAY_PER_FRAME);
            CenterXOffset += Step / 2;
            ApplyRowDissolve();
            advanced = true;
        }

        return advanced;
    }
}
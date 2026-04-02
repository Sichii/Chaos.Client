#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Models;

/// <summary>
///     Procedural scanline dissolve effect for creature/merchant death. Captures the entity's last sprite, then over 9
///     frames (540ms) alternating scanlines shift downward while the whole image fades out. Matches the original DA client
///     behavior.
/// </summary>
public sealed class DyingEffect : IDisposable
{
    private const int FRAME_COUNT = 9;
    private const float FRAME_INTERVAL_MS = 60f;
    private const float INITIAL_ALPHA = 1.0f;
    private const float ALPHA_DECAY_PER_FRAME = 3f / 32f;

    private readonly Color[] Pixels;
    private readonly int Step;
    private readonly int TextureHeight;
    private readonly int TextureWidth;
    public float Alpha { get; private set; } = INITIAL_ALPHA;
    public int CenterYOffset { get; private set; }
    public int CurrentFrame { get; private set; }
    public float ElapsedMs { get; set; }
    public short CenterX { get; }
    public short CenterY { get; }
    public bool Flip { get; }
    public short Left { get; }

    public Texture2D Texture { get; }
    public int TileX { get; }
    public int TileY { get; }
    public short Top { get; }
    public bool IsComplete => CurrentFrame >= FRAME_COUNT;

    public DyingEffect(
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

        // Copy source pixels
        Pixels = new Color[TextureWidth * TextureHeight];
        sourceTexture.GetData(Pixels);

        // Create our own texture for modification
        Texture = new Texture2D(device, TextureWidth, TextureHeight);
        Texture.SetData(Pixels);

        // Compute vertical step size based on texture height
        int adjusted;

        if (TextureHeight < 121)
            adjusted = TextureHeight / 2;
        else if (TextureHeight < 301)
            adjusted = TextureHeight / 3;
        else
            adjusted = TextureHeight / 5;

        Step = Math.Max(2, adjusted / FRAME_COUNT / 2 * 2);
    }

    public void Dispose() => Texture.Dispose();

    /// <summary>
    ///     Shifts every odd column down by Step pixels and clears the top Step rows. Operates on the Pixels array
    ///     in-place, then uploads to the Texture. Creates interlaced vertical band dissolve.
    /// </summary>
    private void ApplyScanlineDissolve()
    {
        for (var col = 0; col < TextureWidth; col++)
        {
            if ((col % 2) == 0)
                continue;

            // Copy column content down by Step pixels (bottom to top to avoid overwrite)
            for (var row = TextureHeight - 1 - Step; row >= 0; row--)
            {
                var srcIndex = row * TextureWidth + col;
                var dstIndex = (row + Step) * TextureWidth + col;
                Pixels[dstIndex] = Pixels[srcIndex];
            }

            // Clear top Step pixels to transparent
            for (var row = 0; (row < Step) && (row < TextureHeight); row++)
                Pixels[row * TextureWidth + col] = Color.Transparent;
        }

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
            CenterYOffset += Step / 2;
            ApplyScanlineDissolve();
            advanced = true;
        }

        return advanced;
    }
}
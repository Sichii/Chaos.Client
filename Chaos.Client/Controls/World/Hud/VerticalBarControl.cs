#region
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud;

/// <summary>
///     Animated orb display (HP or MP). Selects a frame based on current/max percentage. Frames loaded from a named
///     control in the HUD prefab set.
/// </summary>
public sealed class VerticalBarControl : IDisposable
{
    private readonly Texture2D[] Frames;
    private readonly Rectangle Rect;
    private int CurrentFrame;

    public VerticalBarControl(GraphicsDevice device, ControlPrefabSet prefabSet, string name)
    {
        if (!prefabSet.Contains(name))
        {
            Frames = [];

            return;
        }

        var prefab = prefabSet[name];
        var rect = prefab.Control.Rect;

        if (rect is not null)
        {
            var r = rect.Value;

            Rect = new Rectangle(
                (int)r.Left,
                (int)r.Top,
                (int)r.Width,
                (int)r.Height);
        }

        if (prefab.Images.Count == 0)
        {
            Frames = [];

            return;
        }

        Frames = new Texture2D[prefab.Images.Count];

        for (var i = 0; i < prefab.Images.Count; i++)
            Frames[i] = TextureConverter.ToTexture2D(device, prefab.Images[i]);
    }

    public void Dispose()
    {
        foreach (var frame in Frames)
            frame.Dispose();

        // ReSharper disable once GCSuppressFinalizeForTypeWithoutDestructor
        GC.SuppressFinalize(this);
    }

    public void Draw(SpriteBatch spriteBatch, int parentX, int parentY)
    {
        if ((Frames.Length == 0) || (CurrentFrame >= Frames.Length))
            return;

        spriteBatch.Draw(Frames[CurrentFrame], new Vector2(parentX + Rect.X, parentY + Rect.Y), Color.White);
    }

    public void UpdateValue(int current, int max)
    {
        if ((Frames.Length == 0) || (max <= 0))
            return;

        var pct = Math.Clamp((float)current / max, 0f, 1f);
        CurrentFrame = (int)(pct * (Frames.Length - 1));
    }
}
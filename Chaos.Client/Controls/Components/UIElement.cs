#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

public abstract class UIElement : IDisposable
{
    public bool Enabled { get; set; } = true;
    public int Height { get; set; }
    public string Name { get; init; } = string.Empty;
    public UIPanel? Parent { get; internal set; }
    public bool Visible { get; set; } = true;
    public int Width { get; set; }
    public int X { get; set; }
    public int Y { get; set; }

    public int ScreenX => (Parent?.ScreenX ?? 0) + X;
    public int ScreenY => (Parent?.ScreenY ?? 0) + Y;

    public virtual void Dispose() => GC.SuppressFinalize(this);

    public bool ContainsPoint(int screenX, int screenY)
    {
        var sx = ScreenX;
        var sy = ScreenY;

        return (screenX >= sx) && (screenX < (sx + Width)) && (screenY >= sy) && (screenY < (sy + Height));
    }

    public abstract void Draw(SpriteBatch spriteBatch);

    public abstract void Update(GameTime gameTime, InputBuffer input);
}
#region
using Chaos.Client.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

public abstract class UIElement : IDisposable
{
    private static Texture2D? SharedPixel;

    /// <summary>
    ///     Solid color fill drawn behind this element. Null = no fill.
    /// </summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>
    ///     1px border drawn around this element's bounds. Null = no border.
    /// </summary>
    public Color? BorderColor { get; set; }

    public bool Enabled { get; set; } = true;
    public int Height { get; set; }

    /// <summary>
    ///     When false, the element is skipped during hit-testing. Mouse events pass through
    ///     to whatever is behind it. Default true. Set false for decorative overlays.
    /// </summary>
    public bool IsHitTestVisible { get; set; } = true;

    /// <summary>
    ///     When true on a UIPanel, children are still hit-tested but the panel itself is never
    ///     returned as a hit target. Clicks that miss all children pass through to siblings
    ///     behind this panel. Used for full-screen HUD panels with large transparent areas.
    /// </summary>
    public bool IsPassThrough { get; set; }

    public string Name { get; init; } = string.Empty;
    public int PaddingBottom { get; set; }
    public int PaddingLeft { get; set; }
    public int PaddingRight { get; set; }
    public int PaddingTop { get; set; }
    public UIPanel? Parent { get; internal set; }

    public bool Visible
    {
        get => field;

        set
        {
            if (field == value)
                return;

            field = value;
            VisibilityChanged?.Invoke(value);
        }
    } = true;

    public int Width { get; set; }
    public int X { get; set; }
    public int Y { get; set; }

    /// <summary>
    ///     Draw order within the parent panel. Higher values draw on top. Default 0. Elements with the same ZIndex draw in the
    ///     order they were added.
    /// </summary>
    public int ZIndex
    {
        get => field;

        set
        {
            if (field == value)
                return;

            field = value;

            Parent?.ChildOrderDirty = true;
        }
    }

    public Rectangle Bounds
        => new(
            X,
            Y,
            Width,
            Height);

    public Rectangle ScreenBounds
        => new(
            ScreenX,
            ScreenY,
            Width,
            Height);

    // ReSharper disable once FunctionRecursiveOnAllPaths
    public int ScreenX => (Parent?.ScreenX ?? 0) + X;

    // ReSharper disable once FunctionRecursiveOnAllPaths
    public int ScreenY => (Parent?.ScreenY ?? 0) + Y;

    public virtual void Dispose() => GC.SuppressFinalize(this);

    public bool ContainsPoint(int screenX, int screenY)
    {
        var sx = ScreenX;
        var sy = ScreenY;

        return (screenX >= sx) && (screenX < (sx + Width)) && (screenY >= sy) && (screenY < (sy + Height));
    }

    /// <summary>
    ///     Draws the element's background fill and border if set. Subclasses should call base.Draw() before drawing their own
    ///     content so the background appears behind everything.
    /// </summary>
    public virtual void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        if (BackgroundColor.HasValue || BorderColor.HasValue)
        {
            var pixel = GetPixel();

            var bounds = new Rectangle(
                ScreenX,
                ScreenY,
                Width,
                Height);

            if (BackgroundColor.HasValue)
                spriteBatch.Draw(pixel, bounds, BackgroundColor.Value);

            if (BorderColor.HasValue)
            {
                spriteBatch.Draw(
                    pixel,
                    new Rectangle(
                        bounds.X,
                        bounds.Y,
                        bounds.Width,
                        1),
                    BorderColor.Value);

                spriteBatch.Draw(
                    pixel,
                    new Rectangle(
                        bounds.X,
                        bounds.Y + bounds.Height - 1,
                        bounds.Width,
                        1),
                    BorderColor.Value);

                spriteBatch.Draw(
                    pixel,
                    new Rectangle(
                        bounds.X,
                        bounds.Y,
                        1,
                        bounds.Height),
                    BorderColor.Value);

                spriteBatch.Draw(
                    pixel,
                    new Rectangle(
                        bounds.X + bounds.Width - 1,
                        bounds.Y,
                        1,
                        bounds.Height),
                    BorderColor.Value);
            }
        }
    }

    /// <summary>
    ///     Draws a 1px border rectangle (no fill). Utility for ad-hoc drawing outside the element tree.
    /// </summary>
    public static void DrawBorder(SpriteBatch spriteBatch, Rectangle bounds, Color color)
    {
        var pixel = GetPixel();

        spriteBatch.Draw(
            pixel,
            new Rectangle(
                bounds.X,
                bounds.Y,
                bounds.Width,
                1),
            color);

        spriteBatch.Draw(
            pixel,
            new Rectangle(
                bounds.X,
                bounds.Y + bounds.Height - 1,
                bounds.Width,
                1),
            color);

        spriteBatch.Draw(
            pixel,
            new Rectangle(
                bounds.X,
                bounds.Y,
                1,
                bounds.Height),
            color);

        spriteBatch.Draw(
            pixel,
            new Rectangle(
                bounds.X + bounds.Width - 1,
                bounds.Y,
                1,
                bounds.Height),
            color);
    }

    /// <summary>
    ///     Draws a filled rectangle with a 1px border. Utility for ad-hoc drawing outside the element tree.
    /// </summary>
    public static void DrawBorderedRect(
        SpriteBatch spriteBatch,
        Rectangle bounds,
        Color fillColor,
        Color borderColor)
    {
        DrawRect(spriteBatch, bounds, fillColor);

        DrawBorder(spriteBatch, bounds, borderColor);
    }

    /// <summary>
    ///     Draws a filled rectangle with the given color. Utility for ad-hoc drawing outside the element tree.
    /// </summary>
    public static void DrawRect(SpriteBatch spriteBatch, Rectangle bounds, Color color) => spriteBatch.Draw(GetPixel(), bounds, color);

    /// <summary>
    ///     Returns the shared 1x1 white pixel texture, creating it on first use. Shared across all UI elements — do not
    ///     dispose individually.
    /// </summary>
    public static Texture2D GetPixel()
    {
        if (SharedPixel is null || SharedPixel.IsDisposed)
        {
            SharedPixel = new Texture2D(ChaosGame.Device, 1, 1);
            SharedPixel.SetData([Color.White]);
        }

        return SharedPixel;
    }

    /// <summary>
    ///     Resets transient interaction state (hover, press, drag) so the element appears idle. Called recursively when a
    ///     parent panel is hidden.
    /// </summary>
    public virtual void ResetInteractionState() { }

    /// <summary>
    ///     Advances time-based state: animations, timers, slide positions.
    ///     Called every frame for all visible elements regardless of input focus.
    /// </summary>
    public virtual void Update(GameTime gameTime) { }

    //── event handlers (dispatched by inputdispatcher) ──

    public virtual void OnMouseDown(MouseDownEvent e) { }
    public virtual void OnMouseUp(MouseUpEvent e) { }
    public virtual void OnClick(ClickEvent e) { }
    public virtual void OnDoubleClick(DoubleClickEvent e) { }
    public virtual void OnMouseMove(MouseMoveEvent e) { }
    public virtual void OnMouseScroll(MouseScrollEvent e) { }
    public virtual void OnMouseEnter() { }
    public virtual void OnMouseLeave() { }
    public virtual void OnKeyDown(KeyDownEvent e) { }
    public virtual void OnKeyUp(KeyUpEvent e) { }
    public virtual void OnTextInput(TextInputEvent e) { }
    public virtual void OnDragStart(DragStartEvent e) { }
    public virtual void OnDragMove(DragMoveEvent e) { }
    public virtual void OnDragDrop(DragDropEvent e) { }

    public event VisibilityChangedHandler? VisibilityChanged;
}
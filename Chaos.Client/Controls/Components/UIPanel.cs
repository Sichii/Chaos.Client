#region
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

public class UIPanel : UIElement
{
    private static readonly Comparison<UIElement> ZIndexComparison = (a, b) => a.ZIndex.CompareTo(b.ZIndex);
    internal bool ChildOrderDirty;

    public Texture2D? Background { get; set; }

    /// <summary>
    ///     When true, this panel captures all input while visible — other controls receive suppressed input (no keys, no mouse
    ///     events) so their animations/timers still tick.
    /// </summary>
    public bool IsModal { get; set; }

    public List<UIElement> Children { get; } = [];

    public void AddChild(UIElement child)
    {
        child.Parent = this;
        Children.Add(child);
        ChildOrderDirty = true;
    }

    public override void Dispose()
    {
        Background?.Dispose();
        Background = null;

        foreach (var child in Children)
            child.Dispose();

        Children.Clear();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        if (ChildOrderDirty)
        {
            Children.Sort(ZIndexComparison);
            ChildOrderDirty = false;
        }

        if (Background is not null)
            AtlasHelper.Draw(
                spriteBatch,
                Background,
                new Vector2(ScreenX, ScreenY),
                Color.White);

        foreach (var child in Children)
            if (child.Visible)
                child.Draw(spriteBatch);
    }

    public T? FindChild<T>(string name) where T: UIElement
    {
        foreach (var child in Children)
        {
            if (child is T typed && (typed.Name == name))
                return typed;

            if (child is UIPanel panel)
            {
                var found = panel.FindChild<T>(name);

                if (found is not null)
                    return found;
            }
        }

        return null;
    }

    public void RemoveChild(string name)
    {
        for (var i = Children.Count - 1; i >= 0; i--)
            if (string.Equals(Children[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                var child = Children[i];
                Children.RemoveAt(i);
                child.Dispose();
            }
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        // Snapshot to avoid collection-modified during enumeration
        // (e.g. button click handlers that add/remove children)
        var count = Children.Count;

        for (var i = 0; i < count; i++)
        {
            var child = Children[i];

            if (child is { Visible: true, Enabled: true })
                child.Update(gameTime, input);
        }
    }
}
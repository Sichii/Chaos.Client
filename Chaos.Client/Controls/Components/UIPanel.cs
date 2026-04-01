#region
using Chaos.Client.Controls.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

public class UIPanel : UIElement
{
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

        if (Background is not null)
            AtlasHelper.Draw(
                spriteBatch,
                Background,
                new Vector2(ScreenX, ScreenY),
                Color.White);

        foreach (var child in Children)
            if (child.Visible)
            {
                child.Draw(spriteBatch);
                DebugOverlay.DrawElement(spriteBatch, child);
            }
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

    /// <summary>
    ///     Stable in-place insertion sort by ZIndex. O(n) when already sorted (common case), stable (preserves add-order for
    ///     equal ZIndex), zero allocations.
    /// </summary>
    private static void StableSortByZIndex(List<UIElement> list)
    {
        for (var i = 1; i < list.Count; i++)
        {
            var item = list[i];
            var key = item.ZIndex;
            var j = i - 1;

            while ((j >= 0) && (list[j].ZIndex > key))
            {
                list[j + 1] = list[j];
                j--;
            }

            list[j + 1] = item;
        }
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (ChildOrderDirty)
        {
            StableSortByZIndex(Children);
            ChildOrderDirty = false;
        }

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
#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

public class UIPanel : UIElement
{
    public Texture2D? Background { get; set; }
    public List<UIElement> Children { get; } = [];

    public void AddChild(UIElement child)
    {
        child.Parent = this;
        Children.Add(child);
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

        if (Background is not null)
            spriteBatch.Draw(Background, new Vector2(ScreenX, ScreenY), Color.White);

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

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        foreach (var child in Children)
            if (child.Visible && child.Enabled)
                child.Update(gameTime, input);
    }
}
#region
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

/// <summary>
///     Base class for UI panels built from a ControlPrefabSet. Provides helpers to create buttons, images, text boxes, and
///     layout rects from prefab data. Subclasses selectively create only the controls they need.
/// </summary>
public abstract class PrefabPanel : UIPanel
{
    protected ControlPrefabSet PrefabSet { get; }

    protected PrefabPanel(string prefabName, bool center = true)
    {
        var prefabSet = DataContext.UserControls.Get(prefabName);

        PrefabSet = prefabSet ?? throw new InvalidOperationException($"Failed to load {prefabName} control prefab set");

        // Anchor — panel dimensions and background
        var anchor = prefabSet[0];
        var anchorRect = anchor.Control.Rect!.Value;

        Width = (int)anchorRect.Width;
        Height = (int)anchorRect.Height;

        if (center)
        {
            // Use anchor position if non-zero, otherwise center on screen
            var anchorLeft = (int)anchorRect.Left;
            var anchorTop = (int)anchorRect.Top;

            X = (anchorLeft != 0) || (anchorTop != 0) ? anchorLeft : (ChaosGame.VIRTUAL_WIDTH - Width) / 2;

            Y = (anchorLeft != 0) || (anchorTop != 0) ? anchorTop : (ChaosGame.VIRTUAL_HEIGHT - Height) / 2;
        }

        if (anchor.Images.Count > 0)
            Background = UiRenderer.Instance!.GetPrefabTexture(prefabName, anchor.Control.Name, 0);
    }

    protected UIButton? CreateButton(string name)
    {
        if (!PrefabSet.Contains(name))
            return null;

        if (ReuseOrRemoveExistingChild<UIButton>(name) is { } existing)
            return existing;

        var button = CreateButtonFromPrefab(PrefabSet[name]);

        if (button is not null)
            AddChild(button);

        return button;
    }

    private UIButton? CreateButtonFromPrefab(ControlPrefab prefab)
    {
        var rect = prefab.Control.Rect;

        if (rect is null)
            return null;

        var r = rect.Value;

        var cache = UiRenderer.Instance!;
        var pressedTexture = prefab.Images.Count > 1 ? cache.GetPrefabTexture(PrefabSet.Name, prefab.Control.Name, 1) : null;
        var disabledTexture = prefab.Images.Count > 2 ? cache.GetPrefabTexture(PrefabSet.Name, prefab.Control.Name, 2) : null;

        return new UIButton
        {
            Name = prefab.Control.Name,
            X = (int)r.Left,
            Y = (int)r.Top,
            Width = (int)r.Width,
            Height = (int)r.Height,
            NormalTexture = prefab.Images.Count > 0 ? cache.GetPrefabTexture(PrefabSet.Name, prefab.Control.Name, 0) : null,
            PressedTexture = pressedTexture,
            SelectedTexture = pressedTexture,
            DisabledTexture = disabledTexture
        };
    }

    protected UIImage? CreateImage(string name)
    {
        if (!PrefabSet.Contains(name))
            return null;

        if (ReuseOrRemoveExistingChild<UIImage>(name) is { } existing)
            return existing;

        var image = CreateImageFromPrefab(PrefabSet[name]);

        if (image is not null)
            AddChild(image);

        return image;
    }

    private UIImage? CreateImageFromPrefab(ControlPrefab prefab)
    {
        var rect = prefab.Control.Rect;

        if (rect is null)
            return null;

        var r = rect.Value;

        return new UIImage
        {
            Name = prefab.Control.Name,
            X = (int)r.Left,
            Y = (int)r.Top,
            Width = (int)r.Width,
            Height = (int)r.Height,
            Texture = prefab.Images.Count > 0 ? UiRenderer.Instance!.GetPrefabTexture(PrefabSet.Name, prefab.Control.Name, 0) : null
        };
    }

    protected UILabel? CreateLabel(string name, TextAlignment alignment = TextAlignment.Left)
    {
        if (!PrefabSet.Contains(name))
            return null;

        if (ReuseOrRemoveExistingChild<UILabel>(name) is { } existing)
        {
            existing.Alignment = alignment;

            return existing;
        }

        var rect = PrefabSet[name].Control.Rect;

        if (rect is null)
            return null;

        var r = rect.Value;

        var label = new UILabel
        {
            Name = name,
            X = (int)r.Left,
            Y = (int)r.Top,
            Width = (int)r.Width,
            Height = (int)r.Height,
            Alignment = alignment
        };

        AddChild(label);

        return label;
    }

    protected UIProgressBar? CreateProgressBar(string name)
    {
        if (!PrefabSet.Contains(name))
            return null;

        if (ReuseOrRemoveExistingChild<UIProgressBar>(name) is { } existing)
            return existing;

        var prefab = PrefabSet[name];
        var rect = prefab.Control.Rect;

        if (rect is null)
            return null;

        var r = rect.Value;
        var cache = UiRenderer.Instance!;
        var frames = new Texture2D[prefab.Images.Count];

        for (var i = 0; i < prefab.Images.Count; i++)
            frames[i] = cache.GetPrefabTexture(PrefabSet.Name, name, i);

        var bar = new UIProgressBar(frames)
        {
            Name = name,
            X = (int)r.Left,
            Y = (int)r.Top,
            Width = (int)r.Width,
            Height = (int)r.Height
        };

        AddChild(bar);

        return bar;
    }

    protected UITextBox? CreateTextBox(string name, int maxLength = 12)
    {
        if (!PrefabSet.Contains(name))
            return null;

        if (ReuseOrRemoveExistingChild<UITextBox>(name) is { } existing)
        {
            existing.MaxLength = maxLength;

            return existing;
        }

        var textBox = CreateTextBoxFromPrefab(PrefabSet[name]);

        if (textBox is not null)
        {
            textBox.MaxLength = maxLength;
            AddChild(textBox);
        }

        return textBox;
    }

    private UITextBox? CreateTextBoxFromPrefab(ControlPrefab prefab)
    {
        var rect = prefab.Control.Rect;

        if (rect is null)
            return null;

        var r = rect.Value;

        return new UITextBox
        {
            Name = prefab.Control.Name,
            X = (int)r.Left,
            Y = (int)r.Top,
            Width = (int)r.Width,
            Height = (int)r.Height
        };
    }

    protected Rectangle GetRect(string name) => GetRect(PrefabSet, name);

    internal static Rectangle GetRect(ControlPrefabSet prefabSet, string name)
    {
        if (!prefabSet.Contains(name))
            return Rectangle.Empty;

        var rect = prefabSet[name].Control.Rect;

        if (rect is null)
            return Rectangle.Empty;

        var r = rect.Value;

        return new Rectangle(
            (int)r.Left,
            (int)r.Top,
            (int)r.Width,
            (int)r.Height);
    }

    public virtual void Hide() => Visible = false;

    /// <summary>
    ///     If a child with the given name already exists and is of type T, returns it. Otherwise removes and disposes all
    ///     children with that name, and returns null.
    /// </summary>
    private T? ReuseOrRemoveExistingChild<T>(string name) where T: UIElement
    {
        T? reusable = null;

        for (var i = Children.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(Children[i].Name, name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (reusable is null && Children[i] is T match)
            {
                reusable = match;

                continue;
            }

            var child = Children[i];
            Children.RemoveAt(i);
            child.Dispose();
        }

        return reusable;
    }

    public virtual void Show() => Visible = true;
}
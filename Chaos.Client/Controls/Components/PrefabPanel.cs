#region
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using DALib.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

/// <summary>
///     Base class for UI panels built from a ControlPrefabSet. Provides helpers to create buttons, images, text boxes, and
///     layout rects from prefab data. Also supports auto-populating all controls from the prefab set.
/// </summary>
public abstract class PrefabPanel : UIPanel
{
    protected GraphicsDevice Device { get; }
    protected ControlPrefabSet PrefabSet { get; }

    protected PrefabPanel(GraphicsDevice device, string prefabName, bool center = true)
    {
        Device = device;

        var prefabSet = DataContext.UserControls.Get(prefabName);

        if (prefabSet is null)
            throw new InvalidOperationException($"Failed to load {prefabName} control prefab set");

        PrefabSet = prefabSet;

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
            Background = TextureConverter.ToTexture2D(device, anchor.Images[0]);
    }

    /// <summary>
    ///     Creates all non-anchor controls as UI children based on their ControlType. Returns a dictionary of created elements
    ///     keyed by control name for easy lookup.
    /// </summary>
    protected Dictionary<string, UIElement> AutoPopulate()
    {
        var elements = new Dictionary<string, UIElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var prefab in PrefabSet)
        {
            if (prefab.Control.Type == ControlType.Anchor)
                continue;

            if (prefab.Control.Rect is null)
                continue;

            UIElement? element = prefab.Control.Type switch
            {
                ControlType.ReturnsValue or ControlType.Returns0 => CreateButtonFromPrefab(prefab),
                ControlType.EditableText                         => CreateTextBoxFromPrefab(prefab),
                _                                                => CreateImageFromPrefab(prefab)
            };

            if (element is not null)
            {
                AddChild(element);
                elements[prefab.Control.Name] = element;
            }
        }

        return elements;
    }

    protected UIButton? CreateButton(string name)
    {
        if (!PrefabSet.Contains(name))
            return null;

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

        return new UIButton
        {
            Name = prefab.Control.Name,
            X = (int)r.Left,
            Y = (int)r.Top,
            Width = (int)r.Width,
            Height = (int)r.Height,
            NormalTexture = prefab.Images.Count > 0 ? TextureConverter.ToTexture2D(Device, prefab.Images[0]) : null,
            PressedTexture = prefab.Images.Count > 1 ? TextureConverter.ToTexture2D(Device, prefab.Images[1]) : null,
            SelectedTexture = prefab.Images.Count > 1 ? TextureConverter.ToTexture2D(Device, prefab.Images[1]) : null
        };
    }

    protected UIImage? CreateImage(string name)
    {
        if (!PrefabSet.Contains(name))
            return null;

        var image = CreateImageFromPrefab(PrefabSet[name]);

        if (image is not null)
            AddChild(image);

        return image;
    }

    private UIImage? CreateImageFromPrefab(ControlPrefab prefab)
    {
        var rect = prefab.Control.Rect;

        if (rect is null || (prefab.Images.Count == 0))
            return null;

        var r = rect.Value;

        return new UIImage
        {
            Name = prefab.Control.Name,
            X = (int)r.Left,
            Y = (int)r.Top,
            Width = (int)r.Width,
            Height = (int)r.Height,
            Texture = TextureConverter.ToTexture2D(Device, prefab.Images[0])
        };
    }

    protected UILabel? CreateLabel(string name, TextAlignment alignment = TextAlignment.Left)
    {
        if (!PrefabSet.Contains(name))
            return null;

        var rect = PrefabSet[name].Control.Rect;

        if (rect is null)
            return null;

        var r = rect.Value;

        var label = new UILabel(Device)
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

    protected UITextBox? CreateTextBox(string name, int maxLength = 12)
    {
        if (!PrefabSet.Contains(name))
            return null;

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

        return new UITextBox(Device)
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

    public void Hide() => Visible = false;

    public void Show() => Visible = true;
}
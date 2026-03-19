#region
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Chaos.Client.Definitions;
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
    // The DAT control files store almost everything as type 7 (DoesNotReturnValue).
    // The original client identifies buttons by name in code. This set contains all
    // known button control names across all prefabs.
    private static readonly HashSet<string> ButtonNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Common buttons (_nbtn.spf) — HashSet is case-insensitive, no need for duplicate casings
        "OK",
        "Cancel",
        "Close",
        "Quit",
        "Delete",
        "Send",
        "Reply",
        "View",
        "New",
        "Up",
        "Down",
        "Prev",
        "Next",

        // Options dialog (_noptdlg)
        "Friends",
        "Macro",
        "Setting",
        "ExitGame",

        // Start screen (_nstart)
        "Create",
        "Continue",
        "Password",
        "Credit",
        "Homepage",
        "Exit",

        // Character creation (_ncreate)
        "AngleLeft",
        "AngleRight",
        "HairLeft",
        "HairRight",
        "Help",

        // HUD buttons (_nbk_s)
        "BTN_GROUP",
        "BTN_HELP",
        "BTN_TOWNMAP",
        "BTN_OPTION",
        "BTN_BULLETIN",
        "BTN_USERS",
        "BTN_LEGEND",
        "BTN_EXPAND",
        "BTN_CHANGELAYOUT",
        "BTN_SETTING",
        "BTN_SCREENSHOT",
        "BTN_EMOT",
        "BTN_INV0",
        "BTN_INV1",
        "BTN_INV2",
        "BTN_INV3",
        "BTN_INV4",
        "BTN_INV5",
        "CMail",
        "CGroup",
        "CGroup0",
        "CShot",

        // Group dialog (_ngcdlg0, _ngcdlg1)
        "B_BTN0",
        "BTN_OK",
        "BTN_MODIFY",
        "BTN_RESET",
        "BTN_CANCEL",
        "BTN_QUERY_JOIN",
        "BTN_BEGIN",

        // NPC dialog (lnpcd)
        "TopBtn",
        "CloseBtn",
        "PrevBtn",
        "NextBtn",
        "UpArrow",
        "DownArrow",

        // Equipment tab (_nui_eq)
        "GroupBtn",

        // Status book (_nui) tabs + close
        "TAB_INTRO",
        "TAB_LEGEND",
        "TAB_SKILL",
        "TAB_EVENT",
        "TAB_ALBUM",
        "TAB_FAMILY",
        "TAB_CLOSE",

        // World list (_nusers)
        "CountryBtn",
        "MasterBtn",

        // Social status picker (lemot)
        "Emot0",
        "Emot1",
        "Emot2",
        "Emot3",
        "Emot4",
        "Emot5",
        "Emot6",
        "Emot7"
    };

    protected GraphicsDevice Device { get; }
    protected ControlPrefabSet PrefabSet { get; }

    protected PrefabPanel(GraphicsDevice device, string prefabName, bool center = true)
    {
        Device = device;

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

            // Most controls in the DAT archives are type 7 (DoesNotReturnValue) regardless of
            // actual function. The original client identifies buttons by name in code.
            // A control is a button if: type 3/4, OR its name is in the known button set.
            var isButton = prefab.Control.Type is ControlType.ReturnsValue or ControlType.Returns0
                           || ButtonNames.Contains(prefab.Control.Name);

            UIElement? element = isButton
                ? CreateButtonFromPrefab(prefab)
                : prefab.Control.Type == ControlType.EditableText
                    ? CreateTextBoxFromPrefab(prefab)
                    : CreateImageFromPrefab(prefab);

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

        var pressedTexture = prefab.Images.Count > 1 ? TextureConverter.ToTexture2D(Device, prefab.Images[1]) : null;

        return new UIButton
        {
            Name = prefab.Control.Name,
            X = (int)r.Left,
            Y = (int)r.Top,
            Width = (int)r.Width,
            Height = (int)r.Height,
            NormalTexture = prefab.Images.Count > 0 ? TextureConverter.ToTexture2D(Device, prefab.Images[0]) : null,
            PressedTexture = pressedTexture,
            SelectedTexture = pressedTexture
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

    public virtual void Hide() => Visible = false;

    public virtual void Show() => Visible = true;
}
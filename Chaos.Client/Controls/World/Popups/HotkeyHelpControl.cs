#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Hotkey/help overlay using _nhotkem prefab (full-screen 640x480). Background from _nhk_bk.spf, keyboard diagram
///     from _nhk_m.spf at MAIN rect. Hovering a key-group (C00-C13 from _nhotkey prefab) highlights that section on
///     the keyboard and shows a detail explanation image (_nhke##.spf) in the EX area. Key-group rects are in
///     keyboard-local space and offset by MAIN origin. Closed by Escape, Enter, or right-click.
/// </summary>
public sealed class HotkeyHelpControl : PrefabPanel
{
    private const int MAX_KEY_GROUPS = 14;
    private readonly UIImage? DetailDisplay;
    private readonly Texture2D?[] DetailImages = new Texture2D?[MAX_KEY_GROUPS];

    private readonly KeyGroupEntry[] KeyGroups = new KeyGroupEntry[MAX_KEY_GROUPS];
    private int HoveredIndex = -1;

    public HotkeyHelpControl()
        : base("_nhotkem")
    {
        Name = "HotkeyHelp";
        Visible = false;

        // MAIN rect — keyboard diagram position. C## key rects are relative to this.
        var mainRect = GetRect("MAIN");

        // EX area — where detail images appear on hover
        var exRect = GetRect("EX");

        if (exRect != Rectangle.Empty)
        {
            DetailDisplay = new UIImage
            {
                Name = "DetailImage",
                X = exRect.X,
                Y = exRect.Y,
                Width = exRect.Width,
                Height = exRect.Height,
                Visible = false
            };

            AddChild(DetailDisplay);
        }

        // Load the second control file (_nhotkey) which defines the key-group hit rects
        var keyPrefabSet = DataContext.UserControls.Get("_nhotkey");

        if (keyPrefabSet is null || (mainRect == Rectangle.Empty))
            return;

        for (var i = 0; i < MAX_KEY_GROUPS; i++)
        {
            var name = $"C{i:D2}";

            // Standard single-rect key group
            if (keyPrefabSet.Contains(name))
            {
                var element = CreateKeyElement(keyPrefabSet[name], mainRect);

                if (element is not null)
                {
                    KeyGroups[i] = new KeyGroupEntry(element, null);
                    DetailImages[i] = TryLoadDetailImage(i);
                }

                continue;
            }

            // Multi-rect key group (e.g. C02 → C021, C022)
            var subElements = new List<UIElement>();

            for (var s = 1; s <= 9; s++)
            {
                var subName = $"{name}{s}";

                if (!keyPrefabSet.Contains(subName))
                    continue;

                var sub = CreateKeyElement(keyPrefabSet[subName], mainRect);

                if (sub is not null)
                    subElements.Add(sub);
            }

            if (subElements.Count > 0)
            {
                KeyGroups[i] = new KeyGroupEntry(subElements[0], subElements.ToArray());
                DetailImages[i] = TryLoadDetailImage(i);
            }
        }
    }

    /// <summary>
    ///     Creates a UIImage element from a _nhotkey prefab control, offset by the MAIN rect origin to convert from
    ///     keyboard-local space to panel space. Starts hidden.
    /// </summary>
    private UIImage? CreateKeyElement(ControlPrefab prefab, Rectangle mainRect)
    {
        var rect = prefab.Control.Rect;

        if (rect is null || (prefab.Images.Count == 0))
            return null;

        var r = rect.Value;

        var image = new UIImage
        {
            Name = prefab.Control.Name,
            X = mainRect.X + (int)r.Left,
            Y = mainRect.Y + (int)r.Top,
            Width = (int)r.Width,
            Height = (int)r.Height,
            Texture = UiRenderer.Instance!.GetPrefabTexture("_nhotkey", prefab.Control.Name, 0),
            Visible = false
        };

        AddChild(image);

        return image;
    }

    public override void Dispose()
    {
        foreach (var tex in DetailImages)
            tex?.Dispose();

        base.Dispose();
    }

    private int HitTestKeyGroup(int mouseX, int mouseY)
    {
        for (var i = 0; i < MAX_KEY_GROUPS; i++)
        {
            var group = KeyGroups[i];

            if (group.Primary is null)
                continue;

            if (group.SubElements is not null)
            {
                foreach (var sub in group.SubElements)
                    if (sub.ContainsPoint(mouseX, mouseY))
                        return i;

                continue;
            }

            if (group.Primary.ContainsPoint(mouseX, mouseY))
                return i;
        }

        return -1;
    }

    public event Action? OnClose;

    private void SetKeyGroupVisible(int index, bool visible)
    {
        var group = KeyGroups[index];

        if (group.SubElements is not null)
            foreach (var sub in group.SubElements)
                sub.Visible = visible;
        else
            group.Primary?.Visible = visible;
    }

    private static Texture2D? TryLoadDetailImage(int index) => UiRenderer.Instance!.GetNationalSpfTexture($"_nhke{index:D2}.spf");

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        // Close on Escape, Enter, or right-click — NOT left-click
        if (input.WasKeyPressed(Keys.Escape) || input.WasKeyPressed(Keys.F1) || input.WasKeyPressed(Keys.Enter))
        {
            Hide();
            OnClose?.Invoke();

            return;
        }

        if (input.WasRightButtonPressed)
        {
            Hide();
            OnClose?.Invoke();

            return;
        }

        // Hover detection — find which key group the mouse is over
        var newHovered = HitTestKeyGroup(input.MouseX, input.MouseY);

        if (newHovered != HoveredIndex)
        {
            // Unhighlight previous
            if (HoveredIndex >= 0)
                SetKeyGroupVisible(HoveredIndex, false);

            HoveredIndex = newHovered;

            // Highlight new + show detail image
            if (HoveredIndex >= 0)
            {
                SetKeyGroupVisible(HoveredIndex, true);

                if (DetailDisplay is not null)
                {
                    DetailDisplay.Texture = DetailImages[HoveredIndex];
                    DetailDisplay.Visible = true;
                }
            } else if (DetailDisplay is not null)
            {
                DetailDisplay.Texture = null;
                DetailDisplay.Visible = false;
            }
        }

        base.Update(gameTime, input);
    }

    private record struct KeyGroupEntry(UIElement? Primary, UIElement[]? SubElements);
}
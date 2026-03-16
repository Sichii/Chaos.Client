#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using Chaos.DarkAges.Definitions;
using Chaos.Networking.Entities.Server;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Extended stats panel (Shift+G). Loaded from the "ExtraStatus" image in the _nstatus prefab. Shows AC, DMG, HIT,
///     offense/defense elements, and magic resistance. The _nstatus anchor is 640x480 (fullscreen transparent). This panel
///     uses only the "ExtraStatus" sub-image as its background, and offsets all e_ label positions so they are relative to
///     the ExtraStatus origin rather than the anchor origin.
/// </summary>
public class ExtendedStatsControl : UIPanel
{
    private readonly UILabel? AcLabel;
    private readonly UILabel? AttackLabel;
    private readonly UILabel? DefenseLabel;
    private readonly UILabel? DmgLabel;
    private readonly UILabel? HitLabel;
    private readonly UILabel? MagicLabel;

    public ExtendedStatsControl(GraphicsDevice device, ControlPrefabSet statusPrefabSet)
    {
        Name = "ExtendedStats";
        Visible = false;

        // Load background from "ExtraStatus" image in _nstatus prefab.
        // The ExtraStatus rect defines where this sub-image sits within the 640x480 anchor.
        // We use its dimensions for our panel size and its origin as the offset for all e_ labels.
        var extraLeft = 0;
        var extraTop = 0;

        if (statusPrefabSet.Contains("ExtraStatus"))
        {
            var prefab = statusPrefabSet["ExtraStatus"];
            var rect = prefab.Control.Rect;

            if (rect is not null)
            {
                var r = rect.Value;
                extraLeft = (int)r.Left;
                extraTop = (int)r.Top;
                Width = (int)r.Width;
                Height = (int)r.Height;
            }

            if (prefab.Images.Count > 0)
                Background = TextureConverter.ToTexture2D(device, prefab.Images[0]);
        }

        // Extended stat labels (e_ prefix) -- positioned relative to ExtraStatus origin.
        // The prefab rect coordinates are in anchor space (640x480), so subtract the
        // ExtraStatus origin to get coordinates relative to this panel.
        AttackLabel = CreateOffsetLabel(
            device,
            statusPrefabSet,
            "e_attack",
            extraLeft,
            extraTop);

        DefenseLabel = CreateOffsetLabel(
            device,
            statusPrefabSet,
            "e_defense",
            extraLeft,
            extraTop);

        MagicLabel = CreateOffsetLabel(
            device,
            statusPrefabSet,
            "e_magic",
            extraLeft,
            extraTop);

        AcLabel = CreateOffsetLabel(
            device,
            statusPrefabSet,
            "e_AC",
            extraLeft,
            extraTop);

        DmgLabel = CreateOffsetLabel(
            device,
            statusPrefabSet,
            "e_DMG",
            extraLeft,
            extraTop);

        HitLabel = CreateOffsetLabel(
            device,
            statusPrefabSet,
            "e_HIT",
            extraLeft,
            extraTop);
    }

    private UILabel? CreateOffsetLabel(
        GraphicsDevice device,
        ControlPrefabSet prefabSet,
        string name,
        int xOffset,
        int yOffset)
    {
        if (!prefabSet.Contains(name))
            return null;

        var rect = prefabSet[name].Control.Rect;

        if (rect is null)
            return null;

        var r = rect.Value;

        var label = new UILabel(device)
        {
            Name = name,
            X = (int)r.Left - xOffset,
            Y = (int)r.Top - yOffset,
            Width = (int)r.Width,
            Height = (int)r.Height,
            Alignment = TextAlignment.Right
        };

        AddChild(label);

        return label;
    }

    private static string FormatElement(Element element)
        => element switch
        {
            Element.None => "None",
            _            => element.ToString()
        };

    public void UpdateAttributes(AttributesArgs attrs)
    {
        AcLabel?.SetText($"{attrs.Ac}");
        DmgLabel?.SetText($"{attrs.Dmg}");
        HitLabel?.SetText($"{attrs.Hit}");
        MagicLabel?.SetText($"{attrs.MagicResistance}");
        AttackLabel?.SetText(FormatElement(attrs.OffenseElement));
        DefenseLabel?.SetText(FormatElement(attrs.DefenseElement));
    }
}
#region
using System.Collections.ObjectModel;
using DALib.Drawing;
using SkiaSharp;
#endregion

namespace Chaos.Client.Data.Models;

/// <summary>
///     A fully resolved UI control wrapping a DALib <see cref="Control" /> with pre-rendered images. Each image
///     corresponds to an entry in <see cref="DALib.Drawing.Control.Images" /> — the specific frame from the referenced
///     EPF/SPF file, rendered with the appropriate palette.
/// </summary>
public sealed class ControlPrefab : IDisposable
{
    /// <summary>
    ///     The parsed control definition from DALib (name, type, rect, return value, etc.).
    /// </summary>
    public required Control Control { get; init; }

    /// <summary>
    ///     Pre-rendered images, parallel to <see cref="Control.Images" />. Each entry is the specific frame referenced by
    ///     (imageName, frameIndex), rendered in order.
    /// </summary>
    public required IReadOnlyList<SKImage> Images { get; init; }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var image in Images)
            image.Dispose();
    }
}

/// <summary>
///     A named collection of control prefabs parsed from a single control file (.txt). The first control with type
///     <see cref="DALib.Definitions.ControlType.Anchor" /> defines the overall bounds.
/// </summary>
public sealed class ControlPrefabSet(string name) : KeyedCollection<string, ControlPrefab>(StringComparer.OrdinalIgnoreCase), IDisposable
{
    public string Name { get; } = name;

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var prefab in this)
            prefab.Dispose();
    }

    /// <inheritdoc />
    protected override string GetKeyForItem(ControlPrefab item) => item.Control.Name;
}
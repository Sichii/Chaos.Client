#region
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Models;

/// <summary>
///     Caches a tinted (highlight) version of an entity texture. Reuses the cached texture if the source and entity
///     haven't changed.
/// </summary>
public sealed class EntityHighlightState : IDisposable
{
    public uint? HoveredEntityId;
    public bool ShowTintHighlight;
    private uint? TintedEntityId;
    private Texture2D? TintedSource;
    private Texture2D? TintedTexture;

    public void Dispose()
    {
        ClearTint();
        GC.SuppressFinalize(this);
    }

    public void ClearTint()
    {
        TintedTexture?.Dispose();
        TintedTexture = null;
        TintedEntityId = null;
        TintedSource = null;
    }

    public Texture2D? GetOrCreateTinted(Texture2D source, uint entityId, Func<Texture2D, Texture2D> createTint)
    {
        if (TintedTexture is not null && (TintedEntityId == entityId) && (TintedSource == source))
            return TintedTexture;

        TintedTexture?.Dispose();
        TintedTexture = createTint(source);
        TintedEntityId = entityId;
        TintedSource = source;

        return TintedTexture;
    }
}
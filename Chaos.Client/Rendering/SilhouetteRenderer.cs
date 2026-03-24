#region
using Chaos.Client.Models;
using Chaos.Client.Systems.Animation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Renders translucent silhouettes and transparent aislings by compositing into offscreen render targets. Silhouettes
///     use a single viewport-sized RT — all entities are drawn at their world positions with correct inter-entity
///     occlusion, then the entire RT is overlaid with alpha. Transparent aislings use per-entity RTs to composite layers
///     opaquely, then draw inline during the stripe pass to preserve wall occlusion.
/// </summary>
public sealed class SilhouetteRenderer : IDisposable
{
    private const float SILHOUETTE_ALPHA = 0.50f;
    private const float TRANSPARENT_ALPHA = 0.30f;

    private readonly GraphicsDevice Device;
    public readonly List<SilhouetteEntry> SilhouetteEntries = [];
    private readonly List<TransparentEntry> TransparentEntries = [];
    private readonly List<RenderTarget2D> TransparentTargetPool = [];
    private SpriteBatch? Batch;
    private bool SilhouettesReady;
    private RenderTarget2D? SilhouetteTarget;
    private bool TransparentsReady;

    public SilhouetteRenderer(GraphicsDevice device) => Device = device;

    /// <inheritdoc />
    public void Dispose()
    {
        SilhouetteTarget?.Dispose();

        foreach (var rt in TransparentTargetPool)
            rt.Dispose();

        TransparentTargetPool.Clear();
        Batch?.Dispose();
    }

    /// <summary>
    ///     Adds any entity to be rendered as a silhouette this frame (drawn on top of world as a single alpha overlay).
    /// </summary>
    public void AddSilhouette(SilhouetteEntry entry) => SilhouetteEntries.Add(entry);

    /// <summary>
    ///     Adds a transparent aisling entity to be composited this frame. Draw inline via <see cref="DrawTransparentEntity" />
    ///     during the stripe pass.
    /// </summary>
    public void AddTransparent(WorldEntity entity, AislingRenderer aislingRenderer, bool isHighlighted)
    {
        if (entity.Appearance is null)
            return;

        var appearance = entity.Appearance.Value;
        (var frameIndex, var flip, var animSuffix, var isFrontFacing) = AnimationManager.GetAislingFrame(entity);
        var emotionFrame = entity.ActiveEmoteFrame;

        var drawData = aislingRenderer.GetLayerFrames(
            in appearance,
            frameIndex,
            animSuffix,
            flip,
            isFrontFacing,
            emotionFrame);

        if (!drawData.Layers[(int)LayerSlot.Body].HasValue)
            return;

        TransparentEntries.Add(new TransparentEntry(entity, drawData, isHighlighted));
    }

    /// <summary>
    ///     Clears all entries. Call at the start of each frame before adding entities.
    /// </summary>
    public void Clear()
    {
        SilhouetteEntries.Clear();
        TransparentEntries.Clear();
        SilhouettesReady = false;
        TransparentsReady = false;
    }

    /// <summary>
    ///     Draws the pre-rendered silhouette overlay onto the active SpriteBatch. Call after the world pass.
    /// </summary>
    public void DrawSilhouettes(SpriteBatch spriteBatch)
    {
        if (!SilhouettesReady || SilhouetteTarget is null)
            return;

        spriteBatch.Draw(
            SilhouetteTarget,
            Vector2.Zero,
            null,
            Color.White * SILHOUETTE_ALPHA,
            0f,
            Vector2.Zero,
            1f,
            SpriteEffects.None,
            0f);
    }

    /// <summary>
    ///     Draws a single transparent aisling's pre-rendered composite during the stripe pass. Returns true if drawn.
    /// </summary>
    public bool DrawTransparentEntity(
        SpriteBatch spriteBatch,
        WorldEntity entity,
        Camera camera,
        int mapHeight,
        int anchorX,
        int anchorY)
    {
        if (!TransparentsReady)
            return false;

        foreach (var entry in TransparentEntries)
        {
            if ((entry.Entity.Id != entity.Id) || entry.CompositeTarget is null)
                continue;

            var tileWorldPos = Camera.TileToWorld(entity.TileX, entity.TileY, mapHeight);
            var tileCenterX = tileWorldPos.X + DaLibConstants.HALF_TILE_WIDTH;
            var tileCenterY = tileWorldPos.Y + DaLibConstants.HALF_TILE_HEIGHT;
            var baseX = tileCenterX + entity.VisualOffset.X - anchorX;
            var baseY = tileCenterY + entity.VisualOffset.Y - anchorY;
            var screenPos = camera.WorldToScreen(new Vector2(baseX, baseY));

            spriteBatch.Draw(
                entry.CompositeTarget,
                screenPos,
                null,
                Color.White * TRANSPARENT_ALPHA,
                0f,
                Vector2.Zero,
                1f,
                SpriteEffects.None,
                0f);

            return true;
        }

        return false;
    }

    private void EnsureTransparentPoolSize(int count)
    {
        while (TransparentTargetPool.Count < count)
            TransparentTargetPool.Add(new RenderTarget2D(Device, AislingRenderer.COMPOSITE_WIDTH, AislingRenderer.COMPOSITE_HEIGHT));
    }

    /// <summary>
    ///     Composites all silhouette entities into a single viewport-sized RT at their world positions. Call from WorldScreen
    ///     after the normal entity draw pass, passing the same camera transform.
    /// </summary>
    public void PreRenderSilhouettes(Action<SpriteBatch> drawEntities)
    {
        if (SilhouetteEntries.Count == 0)
        {
            SilhouettesReady = false;

            return;
        }

        Batch ??= new SpriteBatch(Device);

        var screenWidth = Device.PresentationParameters.BackBufferWidth;
        var screenHeight = Device.PresentationParameters.BackBufferHeight;

        if (SilhouetteTarget is null || (SilhouetteTarget.Width != screenWidth) || (SilhouetteTarget.Height != screenHeight))
        {
            SilhouetteTarget?.Dispose();
            SilhouetteTarget = new RenderTarget2D(Device, screenWidth, screenHeight);
        }

        var bindings = Device.GetRenderTargets();

        var previousTarget = bindings.Length > 0 ? bindings[0].RenderTarget as RenderTarget2D : null;

        Device.SetRenderTarget(SilhouetteTarget);

        Device.Clear(
            new Color(
                0,
                0,
                0,
                0));

        // The caller draws all silhouetted entities using the same SpriteBatch/transform as the normal world pass
        drawEntities(Batch);

        Device.SetRenderTarget(previousTarget);
        SilhouettesReady = true;
    }

    /// <summary>
    ///     Composites all transparent aisling entries into per-entity offscreen RTs. Must be called before any
    ///     backbuffer/main-RT drawing begins.
    /// </summary>
    public void PreRenderTransparents(AislingRenderer aislingRenderer)
    {
        if (TransparentEntries.Count == 0)
        {
            TransparentsReady = false;

            return;
        }

        Batch ??= new SpriteBatch(Device);
        EnsureTransparentPoolSize(TransparentEntries.Count);

        var bindings = Device.GetRenderTargets();

        var previousTarget = bindings.Length > 0 ? bindings[0].RenderTarget as RenderTarget2D : null;

        var flipPivot = AislingRenderer.BODY_CENTER_X + AislingRenderer.LAYER_OFFSET_PADDING;

        for (var i = 0; i < TransparentEntries.Count; i++)
        {
            var entry = TransparentEntries[i];
            entry.CompositeTarget = TransparentTargetPool[i];

            Device.SetRenderTarget(entry.CompositeTarget);

            Device.Clear(
                new Color(
                    0,
                    0,
                    0,
                    0));

            Batch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend);

            foreach (var slot in entry.DrawData.DrawOrder)
            {
                if (entry.DrawData.Layers[(int)slot] is not { } layer)
                    continue;

                var layerOffsetX = AislingRenderer.GetLayerOffsetX(layer.TypeLetter) + AislingRenderer.LAYER_OFFSET_PADDING;

                float compositeX;

                if (entry.DrawData.FlipHorizontal)
                    compositeX = 2 * flipPivot - layerOffsetX - layer.Texture.Width;
                else
                    compositeX = layerOffsetX;

                var effects = entry.DrawData.FlipHorizontal ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

                var drawTexture = entry.IsHighlighted ? aislingRenderer.GetOrCreateTintedTexture(layer.Texture) : layer.Texture;

                Batch.Draw(
                    drawTexture,
                    new Vector2(compositeX, 0),
                    null,
                    Color.White,
                    0f,
                    Vector2.Zero,
                    1f,
                    effects,
                    0f);
            }

            Batch.End();
        }

        Device.SetRenderTarget(previousTarget);
        TransparentsReady = true;
    }

    /// <summary>
    ///     A silhouette entry representing any entity type to be drawn into the shared silhouette overlay.
    /// </summary>
    public readonly record struct SilhouetteEntry(WorldEntity Entity);

    /// <summary>
    ///     A transparent aisling entry with pre-composited layer data.
    /// </summary>
    public sealed class TransparentEntry(WorldEntity Entity, AislingDrawData DrawData, bool IsHighlighted)
    {
        public RenderTarget2D? CompositeTarget { get; set; }
        public AislingDrawData DrawData { get; } = DrawData;
        public WorldEntity Entity { get; } = Entity;
        public bool IsHighlighted { get; } = IsHighlighted;
    }
}
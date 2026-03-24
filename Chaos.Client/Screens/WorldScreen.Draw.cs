#region
using Chaos.Client.Controls.Generic;
using Chaos.Client.Controls.World.Hud.Panel;
using Chaos.Client.Data.Definitions;
using Chaos.Client.Models;
using Chaos.Client.Rendering;
using Chaos.Client.Systems;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Screens;

public sealed partial class WorldScreen
{
    public void Draw(SpriteBatch spriteBatch, GameTime gameTime)
    {
        // Pre-composite transparent aislings into per-entity RTs before world drawing
        if (MapFile is not null && MapPreloaded)
        {
            SilhouetteRenderer.Clear();

            var player = Game.World.GetPlayerEntity();

            // Collect silhouette entries (any entity type)
            if (player is not null)
                SilhouetteRenderer.AddSilhouette(new SilhouetteRenderer.SilhouetteEntry(player));

            // Collect transparent aisling entries (need per-entity compositing for uniform alpha)
            foreach (var entity in Game.World.GetSortedEntities())
                if ((entity.Type == ClientEntityType.Aisling) && entity.IsTransparent)
                    SilhouetteRenderer.AddTransparent(
                        entity,
                        Game.AislingRenderer,
                        Highlight.ShowTintHighlight && (Highlight.HoveredEntityId == entity.Id));

            SilhouetteRenderer.PreRenderTransparents(Game.AislingRenderer);

            // Pre-render silhouettes into a screen-sized RT (must happen before main RT drawing starts,
            // because RT switching discards the main RT's contents)
            SilhouetteRenderer.PreRenderSilhouettes(batch =>
            {
                batch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, GlobalSettings.Sampler);

                foreach (var entry in SilhouetteRenderer.SilhouetteEntries)
                    DrawEntity(batch, entry.Entity);

                batch.End();
            });
        }

        // Pass 1: World rendering — clipped to the HUD viewport area, camera transform
        if (MapFile is not null && MapPreloaded)
        {
            var viewportRect = WorldHud.ViewportBounds;
            Device.ScissorRectangle = viewportRect;

            var transform = Matrix.CreateTranslation(viewportRect.X, viewportRect.Y, 0);

            // Background tiles + tile cursor: batched (many draws, no blend changes)
            spriteBatch.Begin(samplerState: GlobalSettings.Sampler, rasterizerState: ScissorRasterizerState, transformMatrix: transform);
            MapRenderer.DrawBackground(spriteBatch, MapFile, Camera);
            DrawTileCursor(spriteBatch);
            spriteBatch.End();

            // Foreground, entities, effects: immediate mode (per-stripe ordering, blend switches for additive effects)
            spriteBatch.Begin(
                SpriteSortMode.Immediate,
                BlendState.AlphaBlend,
                GlobalSettings.Sampler,
                null,
                ScissorRasterizerState,
                null,
                transform);
            DrawForegroundAndEntities(spriteBatch);
            SilhouetteRenderer.DrawSilhouettes(spriteBatch);

            Overlays.Draw(
                spriteBatch,
                Camera,
                MapFile.Height,
                Game.World.GetSortedEntities(),
                Highlight.ShowTintHighlight,
                Highlight.HoveredEntityId);
            spriteBatch.End();

            // Darkness overlay — drawn over the world in screen space (no camera transform)
            if (DarknessRenderer.IsActive)
            {
                var viewport = WorldHud.ViewportBounds;
                DarknessRenderer.Update(Camera, viewport);

                spriteBatch.Begin(
                    blendState: BlendState.NonPremultiplied,
                    samplerState: GlobalSettings.Sampler,
                    rasterizerState: ScissorRasterizerState);
                DarknessRenderer.Draw(spriteBatch, viewport);
                spriteBatch.End();
            }

            // Snapshot draw count before debug draws so the reported count excludes debug visualizations
            DebugOverlay.SnapshotDrawCount();

            // Debug overlay: entity hitboxes, tile grid, etc.
            if (DebugOverlay.IsActive)
            {
                spriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    GlobalSettings.Sampler,
                    null,
                    ScissorRasterizerState,
                    null,
                    transform);

                DebugRenderer.Draw(
                    spriteBatch,
                    Camera,
                    MapFile,
                    MapRenderer.ForegroundExtraMargin,
                    Game.World.GetSortedEntities(),
                    Game.World.GetPlayerEntity(),
                    EntityHitBoxes,
                    Game.Input.MouseX,
                    Game.Input.MouseY,
                    WorldHud.ViewportBounds);
                spriteBatch.End();
            }
        }

        // Tab map overlay — drawn on top of world, under HUD
        // TabMapRenderer manages its own SpriteBatch Begin/End blocks (stencil passes for entity overlap)
        if (TabMapVisible && MapFile is not null)
        {
            var viewport = WorldHud.ViewportBounds;
            var player = Game.World.GetPlayerEntity();
            var px = player?.TileX ?? 0;
            var py = player?.TileY ?? 0;

            TabMapRenderer.Draw(
                spriteBatch,
                Device,
                viewport,
                px,
                py,
                Game.World.GetSortedEntities(),
                Game.World.PlayerEntityId);
        }

        // Pass 2: UI overlay — full screen, no transform
        spriteBatch.Begin(samplerState: GlobalSettings.Sampler);
        Root!.Draw(spriteBatch);
        DrawDragIcon(spriteBatch);
        spriteBatch.End();
    }

    #region Diagonal Stripe Rendering
    /// <summary>
    ///     Iterates foreground tiles, entities, and effects in diagonal stripe order (depth = x+y ascending). Per stripe draw
    ///     order: ground items → aislings → creatures → ground effects → entity effects → foreground tiles. Within each
    ///     category, entities draw in list order (arrival order — later arrivals on top).
    /// </summary>
    private void DrawForegroundAndEntities(SpriteBatch spriteBatch)
    {
        if (MapFile is null)
            return;

        EntityHitBoxes.Clear();

        var sortedEntities = Game.World.GetSortedEntities();

        (var fgMinX, var fgMinY, var fgMaxX, var fgMaxY) = Camera.GetVisibleTileBounds(
            MapFile.Width,
            MapFile.Height,
            MapRenderer.ForegroundExtraMargin);

        var minDepth = fgMinX + fgMinY;
        var maxDepth = fgMaxX + fgMaxY;
        var entityIndex = 0;
        var entityCount = sortedEntities.Count;

        // Skip entities before the visible depth range
        while ((entityIndex < entityCount) && (sortedEntities[entityIndex].SortDepth < minDepth))
            entityIndex++;

        for (var depth = minDepth; depth <= maxDepth; depth++)
        {
            // Collect entities at this depth stripe
            var stripeStart = entityIndex;

            while ((entityIndex < entityCount) && (sortedEntities[entityIndex].SortDepth == depth))
                entityIndex++;

            var stripeEnd = entityIndex;

            // 1. Ground items
            for (var i = stripeStart; i < stripeEnd; i++)
                if (sortedEntities[i].Type == ClientEntityType.GroundItem)
                    DrawEntity(spriteBatch, sortedEntities[i]);

            // 2. Aislings
            for (var i = stripeStart; i < stripeEnd; i++)
                if (sortedEntities[i].Type == ClientEntityType.Aisling)
                    DrawEntity(spriteBatch, sortedEntities[i]);

            // 3. Creatures
            for (var i = stripeStart; i < stripeEnd; i++)
                if (sortedEntities[i].Type == ClientEntityType.Creature)
                    DrawEntity(spriteBatch, sortedEntities[i]);

            // 4. Dying creature dissolves
            DrawDyingEffectsAtDepth(spriteBatch, depth);

            // 5. Ground-targeted effects
            DrawGroundEffectsAtDepth(spriteBatch, depth);

            // 5. Entity-attached effects
            for (var i = stripeStart; i < stripeEnd; i++)
                DrawEntityEffects(spriteBatch, sortedEntities[i]);

            // 6. Foreground tiles (on top — trees, buildings occlude entities behind them)
            var tileXStart = Math.Max(fgMinX, depth - fgMaxY);
            var tileXEnd = Math.Min(fgMaxX, depth - fgMinY);

            for (var tileX = tileXStart; tileX <= tileXEnd; tileX++)
                MapRenderer.DrawForegroundTile(
                    spriteBatch,
                    MapFile,
                    Camera,
                    tileX,
                    depth - tileX);
        }
    }

    private void DrawDyingEffectsAtDepth(SpriteBatch spriteBatch, int depth)
    {
        if (MapFile is null)
            return;

        foreach (var dying in Game.World.DyingEffects)
        {
            if (dying.IsComplete || ((dying.TileX + dying.TileY) != depth))
                continue;

            var tileWorld = Camera.TileToWorld(dying.TileX, dying.TileY, MapFile.Height);
            var tileCenterX = tileWorld.X + DaLibConstants.HALF_TILE_WIDTH;
            var tileCenterY = tileWorld.Y + DaLibConstants.HALF_TILE_HEIGHT;

            var texCenterX = dying.CenterX - Math.Min(0, (int)dying.Left);
            var texCenterY = dying.CenterY - Math.Min(0, (int)dying.Top);

            var anchorX = dying.Flip ? dying.Texture.Width - texCenterX : texCenterX;

            var drawX = tileCenterX - anchorX;
            var drawY = tileCenterY - texCenterY;
            var screenPos = Camera.WorldToScreen(new Vector2(drawX, drawY));

            var effects = dying.Flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            spriteBatch.Draw(
                dying.Texture,
                screenPos,
                null,
                Color.White * dying.Alpha,
                0f,
                Vector2.Zero,
                1f,
                effects,
                0f);
        }
    }

    private void DrawGroundEffectsAtDepth(SpriteBatch spriteBatch, int depth)
    {
        foreach (var effect in Game.World.ActiveEffects)
        {
            if (effect.TargetEntityId.HasValue || effect.IsComplete)
                continue;

            if (!effect.TileX.HasValue || !effect.TileY.HasValue)
                continue;

            if ((effect.TileX.Value + effect.TileY.Value) != depth)
                continue;

            var tileWorld = Camera.TileToWorld(effect.TileX.Value, effect.TileY.Value, MapFile!.Height);

            DrawSingleEffect(
                spriteBatch,
                effect,
                tileWorld.X + DaLibConstants.HALF_TILE_WIDTH,
                tileWorld.Y + DaLibConstants.HALF_TILE_HEIGHT,
                Vector2.Zero);
        }
    }

    private void DrawSingleEffect(
        SpriteBatch spriteBatch,
        ActiveEffect effect,
        float tileCenterX,
        float tileCenterY,
        Vector2 visualOffset)
    {
        var spriteFrame = Game.EffectRenderer.GetFrame(effect.EffectId, effect.CurrentFrame);

        if (spriteFrame is null)
            return;

        var frame = spriteFrame.Value;
        var drawX = tileCenterX + visualOffset.X - frame.CenterX + Math.Min(0, (int)frame.Left);
        var drawY = tileCenterY + visualOffset.Y - frame.CenterY + Math.Min(0, (int)frame.Top);
        var screenPos = Camera.WorldToScreen(new Vector2(drawX, drawY));

        // In immediate mode, blend state can be changed directly between draws
        if (effect.BlendMode != EffectBlendMode.Normal)
            Device.BlendState = effect.BlendMode switch
            {
                EffectBlendMode.Additive  => BlendState.Additive,
                EffectBlendMode.SelfAlpha => ScreenBlendState,
                _                         => BlendState.AlphaBlend
            };

        spriteBatch.Draw(frame.Texture, screenPos, Color.White);

        if (effect.BlendMode != EffectBlendMode.Normal)
            Device.BlendState = BlendState.AlphaBlend;
    }
    #endregion

    #region Entity Rendering
    private void DrawEntity(SpriteBatch spriteBatch, WorldEntity entity)
    {
        if (MapFile is null)
            return;

        var tileWorldPos = Camera.TileToWorld(entity.TileX, entity.TileY, MapFile.Height);
        var tileCenterX = tileWorldPos.X + DaLibConstants.HALF_TILE_WIDTH;
        var tileCenterY = tileWorldPos.Y + DaLibConstants.HALF_TILE_HEIGHT;

        var textureBottom = 0;

        switch (entity.Type)
        {
            case ClientEntityType.Aisling:
                textureBottom = DrawAisling(
                    spriteBatch,
                    entity,
                    tileCenterX,
                    tileCenterY);

                break;

            case ClientEntityType.Creature:
                textureBottom = DrawCreature(
                    spriteBatch,
                    entity,
                    tileCenterX,
                    tileCenterY);

                break;

            case ClientEntityType.GroundItem:
                DrawGroundItem(
                    spriteBatch,
                    entity,
                    tileCenterX,
                    tileCenterY);

                return; // Ground items don't get hitboxes
        }

        if (textureBottom <= 0)
            return;

        // Hitbox: 28px wide centered on tile screen X, 60px tall bottom-aligned to texture bottom
        var tileScreenPos = Camera.WorldToScreen(new Vector2(tileCenterX + entity.VisualOffset.X, tileCenterY + entity.VisualOffset.Y));
        var hitboxX = (int)tileScreenPos.X - HITBOX_WIDTH / 2;
        var hitboxY = textureBottom - HITBOX_HEIGHT;

        EntityHitBoxes.Add(
            new EntityHitBox(
                entity.Id,
                new Rectangle(
                    hitboxX,
                    hitboxY,
                    HITBOX_WIDTH,
                    HITBOX_HEIGHT)));
    }

    /// <summary>
    ///     Draws a creature entity. Returns the screen-space Y of the texture bottom edge, or 0 if not drawn.
    /// </summary>
    private int DrawCreature(
        SpriteBatch spriteBatch,
        WorldEntity entity,
        float tileCenterX,
        float tileCenterY)
    {
        var creatureRenderer = Game.CreatureRenderer;
        var animInfo = creatureRenderer.GetAnimInfo(entity.SpriteId);

        if (animInfo is null)
            return 0;

        var info = animInfo.Value;
        (var frameIndex, var flip) = AnimationSystem.GetCreatureFrame(entity, in info);

        var spriteFrame = creatureRenderer.GetFrame(entity.SpriteId, frameIndex);

        if (spriteFrame is null)
            return 0;

        var frame = spriteFrame.Value;

        // CenterX/CenterY in sprite-space. Convert to texture-space by subtracting Min(0, Left/Top)
        // (when Left/Top are negative, the rendered image has no padding — center shifts right/down).
        var texCenterX = frame.CenterX - Math.Min(0, (int)frame.Left);
        var texCenterY = frame.CenterY - Math.Min(0, (int)frame.Top);

        // When flipped, mirror the X anchor within the texture
        var anchorX = flip ? frame.Texture.Width - texCenterX : texCenterX;

        var drawX = tileCenterX + entity.VisualOffset.X - anchorX;
        var drawY = tileCenterY + entity.VisualOffset.Y - texCenterY;
        var screenPos = Camera.WorldToScreen(new Vector2(drawX, drawY));

        var effects = flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

        var shouldTint = Highlight.ShowTintHighlight && (Highlight.HoveredEntityId == entity.Id);
        var drawTexture = shouldTint ? GetOrCreateTintedTexture(frame.Texture, entity.Id) : frame.Texture;

        spriteBatch.Draw(
            drawTexture,
            screenPos,
            null,
            Color.White,
            0f,
            Vector2.Zero,
            1f,
            effects,
            0f);

        return (int)screenPos.Y + frame.Texture.Height;
    }

    /// <summary>
    ///     Draws an aisling entity. Returns the screen-space Y of the texture bottom edge, or 0 if not drawn.
    /// </summary>
    private int DrawAisling(
        SpriteBatch spriteBatch,
        WorldEntity entity,
        float tileCenterX,
        float tileCenterY)
    {
        // Morphed aislings (creature form) render as creatures
        if (entity.Appearance is null && (entity.SpriteId > 0))
            return DrawCreature(
                spriteBatch,
                entity,
                tileCenterX,
                tileCenterY);

        if (entity.Appearance is null)
            return 0;

        var appearance = entity.Appearance.Value;
        (var frameIndex, var flip, var animSuffix, var isFrontFacing) = AnimationSystem.GetAislingFrame(entity);

        var emotionFrame = entity.ActiveEmoteFrame;

        // Check cache — re-resolve layer frames if appearance, frame, flip, animation suffix, or emotion changed.
        // Individual layer textures are cached globally in AislingRenderer — this just tracks which draw data is current.
        if (!AislingCache.TryGetValue(entity.Id, out var cached)
            || (cached.Appearance != appearance)
            || (cached.FrameIndex != frameIndex)
            || (cached.Flip != flip)
            || (cached.IsFrontFacing != isFrontFacing)
            || (cached.AnimSuffix != animSuffix)
            || (cached.EmotionFrame != emotionFrame))
        {
            var drawData = Game.AislingRenderer.GetLayerFrames(
                in appearance,
                frameIndex,
                animSuffix,
                flip,
                isFrontFacing,
                emotionFrame);

            if (!drawData.Layers[(int)LayerSlot.Body].HasValue)
                return 0;

            cached = new AislingDrawDataEntry(
                appearance,
                frameIndex,
                flip,
                isFrontFacing,
                animSuffix,
                emotionFrame,
                drawData);
            AislingCache[entity.Id] = cached;
        }

        var cachedDrawData = cached.DrawData!;

        // Base position: composite canvas origin relative to tile center
        var baseX = tileCenterX + entity.VisualOffset.X - BODY_CENTER_X;
        var baseY = tileCenterY + entity.VisualOffset.Y - BODY_CENTER_Y;
        var flipPivot = AislingRenderer.BODY_CENTER_X + AislingRenderer.LAYER_OFFSET_PADDING;

        var isHighlighted = Highlight.ShowTintHighlight && (Highlight.HoveredEntityId == entity.Id);

        // Transparent aislings use the pre-composited render target for uniform alpha (no layer bleed-through)
        if (entity.IsTransparent
            && SilhouetteRenderer.DrawTransparentEntity(
                spriteBatch,
                entity,
                Camera,
                MapFile!.Height,
                BODY_CENTER_X,
                BODY_CENTER_Y))
        {
            var bodyScreenPos2 = Camera.WorldToScreen(new Vector2(baseX, baseY));

            return (int)(bodyScreenPos2.Y + AislingRenderer.COMPOSITE_HEIGHT);
        }

        // Draw each layer in order
        foreach (var slot in cachedDrawData.DrawOrder)
        {
            if (cachedDrawData.Layers[(int)slot] is not { } layer)
                continue;

            var layerOffsetX = AislingRenderer.GetLayerOffsetX(layer.TypeLetter) + AislingRenderer.LAYER_OFFSET_PADDING;

            float compositeX;

            if (cachedDrawData.FlipHorizontal)
                compositeX = 2 * flipPivot - layerOffsetX - layer.Texture.Width;
            else
                compositeX = layerOffsetX;

            var worldPos = new Vector2(baseX + compositeX, baseY);
            var screenPos = Camera.WorldToScreen(worldPos);
            var effects = cachedDrawData.FlipHorizontal ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            var drawTexture = isHighlighted ? Game.AislingRenderer.GetOrCreateTintedTexture(layer.Texture) : layer.Texture;

            spriteBatch.Draw(
                drawTexture,
                screenPos,
                null,
                Color.White,
                0f,
                Vector2.Zero,
                1f,
                effects,
                0f);
        }

        // Return bottom edge for hitbox calculation (body layer defines the aisling's visual bounds)
        var bodyScreenPos = Camera.WorldToScreen(new Vector2(baseX, baseY));

        return (int)bodyScreenPos.Y + AislingRenderer.COMPOSITE_HEIGHT;
    }

    /// <summary>
    ///     Creates a CPU-side tinted copy of a texture using the original DA client's highlight color transform.
    /// </summary>
    private Texture2D CreateTintedTexture(Texture2D source) => TextureConverter.CreateTintedTexture(source);

    /// <summary>
    ///     Returns a tinted texture for the given source, caching it for the current highlighted entity. Regenerates when the
    ///     entity or source texture changes.
    /// </summary>
    private Texture2D? GetOrCreateTintedTexture(Texture2D source, uint entityId)
        => Highlight.GetOrCreateTinted(source, entityId, CreateTintedTexture);

    private void ClearHighlightCache()
    {
        Highlight.ClearTint();
        Game.AislingRenderer.ClearTintedCache();
    }

    private void DrawEntityEffects(SpriteBatch spriteBatch, WorldEntity entity)
    {
        if (MapFile is null)
            return;

        var tileWorldPos = Camera.TileToWorld(entity.TileX, entity.TileY, MapFile.Height);
        var tileCenterX = tileWorldPos.X + DaLibConstants.HALF_TILE_WIDTH;
        var tileCenterY = tileWorldPos.Y + DaLibConstants.HALF_TILE_HEIGHT;

        foreach (var effect in Game.World.ActiveEffects)
        {
            if ((effect.TargetEntityId != entity.Id) || effect.IsComplete)
                continue;

            DrawSingleEffect(
                spriteBatch,
                effect,
                tileCenterX,
                tileCenterY,
                entity.VisualOffset);
        }
    }

    private void DrawGroundItem(
        SpriteBatch spriteBatch,
        WorldEntity entity,
        float tileCenterX,
        float tileCenterY)
    {
        var sprite = Game.ItemRenderer.GetSprite(entity.SpriteId, entity.ItemColor);

        if (sprite is null)
            return;

        var texture = sprite.Value.Texture;

        // Center the visual content (not the canvas) on the tile
        // The texture includes Left/Top transparent padding from SimpleRender,
        // so the content center is at (Left + PixelWidth/2, Top + PixelHeight/2)
        var contentWidth = texture.Width - sprite.Value.FrameLeft;
        var contentHeight = texture.Height - sprite.Value.FrameTop;
        var contentCenterX = sprite.Value.FrameLeft + contentWidth / 2f;
        var contentCenterY = sprite.Value.FrameTop + contentHeight / 2f;
        var drawX = tileCenterX - contentCenterX;
        var drawY = tileCenterY - contentCenterY;
        var screenPos = Camera.WorldToScreen(new Vector2(drawX, drawY));

        spriteBatch.Draw(texture, screenPos, Color.White);
    }

    /// <summary>
    ///     Creates a texture containing a dashed ellipse inscribed in the isometric tile diamond. Gaps at the 4 cardinal
    ///     directions (top, right, bottom, left of the ellipse).
    /// </summary>
    private static Texture2D CreateTileCursorTexture(GraphicsDevice device, Color color)
    {
        const int WIDTH = DaLibConstants.HALF_TILE_WIDTH * 2; // 56
        const int HEIGHT = DaLibConstants.HALF_TILE_HEIGHT * 2; // 28

        var pixels = new Color[WIDTH * HEIGHT];

        var cx = WIDTH / 2;
        var cy = HEIGHT / 2;

        // Top-right quarter only.
        // These are offsets from the center.
        // Tweak these until the shape matches exactly how you want.
        Span<Point> quarter =
        [
            new(-6, -8),
            new(-7, -8),
            new(-8, -8),
            new(-9, -8),
            new(-10, -8),
            new(-11, -7),
            new(-12, -7),
            new(-13, -6),
            new(-14, -6),
            new(-15, -5),
            new(-16, -5),
            new(-17, -4),
            new(-17, -3)
        ];

        foreach (var p in quarter)
            ProjectQuads(
                pixels,
                WIDTH,
                HEIGHT,
                cx,
                cy,
                p.X,
                p.Y,
                color);

        var texture = new Texture2D(device, WIDTH, HEIGHT);
        texture.SetData(pixels);

        return texture;
    }

    private static void ProjectQuads(
        Color[] pixels,
        int width,
        int height,
        int cx,
        int cy,
        int dx,
        int dy,
        Color color)
    {
        SetPixel(
            pixels,
            width,
            height,
            cx + dx,
            cy + dy,
            color); // top-right

        SetPixel(
            pixels,
            width,
            height,
            cx - dx,
            cy + dy,
            color); // top-left

        SetPixel(
            pixels,
            width,
            height,
            cx + dx,
            cy - dy,
            color); // bottom-right

        SetPixel(
            pixels,
            width,
            height,
            cx - dx,
            cy - dy,
            color); // bottom-left
    }

    private static void SetPixel(
        Color[] pixels,
        int width,
        int height,
        int x,
        int y,
        Color color)
    {
        if (((uint)x < width) && ((uint)y < height))
            pixels[y * width + x] = color;
    }

    private PanelBase? GetDraggingPanel()
    {
        if (WorldHud.Inventory.IsDragging)
            return WorldHud.Inventory;

        if (WorldHud.SkillBook.IsDragging)
            return WorldHud.SkillBook;

        if (WorldHud.SkillBookAlt.IsDragging)
            return WorldHud.SkillBookAlt;

        if (WorldHud.SpellBook.IsDragging)
            return WorldHud.SpellBook;

        if (WorldHud.SpellBookAlt.IsDragging)
            return WorldHud.SpellBookAlt;

        return null;
    }

    private void DrawDragIcon(SpriteBatch spriteBatch)
    {
        var dragging = GetDraggingPanel();

        if (dragging?.DragTexture is not { } icon)
            return;

        spriteBatch.Draw(icon, new Vector2(dragging.DragX - icon.Width / 2.0f, dragging.DragY - icon.Height / 2.0f), Color.White * 0.7f);
    }

    private void DrawTileCursor(SpriteBatch spriteBatch)
    {
        if (MapFile is null || TileCursorTexture is null)
            return;

        var input = Game.Input;
        var viewport = WorldHud.ViewportBounds;

        // Only draw when mouse is within the world viewport
        if ((input.MouseX < viewport.X)
            || (input.MouseX >= (viewport.X + viewport.Width))
            || (input.MouseY < viewport.Y)
            || (input.MouseY >= (viewport.Y + viewport.Height)))
            return;

        (var tileX, var tileY) = ScreenToTile(input.MouseX, input.MouseY);

        if ((tileX < 0) || (tileX >= MapFile.Width) || (tileY < 0) || (tileY >= MapFile.Height))
            return;

        var tileWorld = Camera.TileToWorld(tileX, tileY, MapFile.Height);
        var tileScreen = Camera.WorldToScreen(new Vector2(tileWorld.X, tileWorld.Y));

        var cursorTexture = GetDraggingPanel() is not null ? TileCursorDragTexture : TileCursorTexture;
        spriteBatch.Draw(cursorTexture!, new Vector2((int)tileScreen.X, (int)tileScreen.Y), Color.White);
    }
    #endregion
}
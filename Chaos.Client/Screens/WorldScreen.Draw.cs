#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Controls.World.Hud.Panel;
using Chaos.Client.Data;
using Chaos.Client.Models;
using Chaos.Client.Rendering.Models;
using Chaos.Client.Systems;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Screens;

public sealed partial class WorldScreen
{
    public void Draw(SpriteBatch spriteBatch, GameTime gameTime)
    {
        //sort once per frame — cached via dirty flag, reused by all draw sub-passes
        var sortedEntities = WorldState.GetSortedEntities();

        //pre-composite transparent aislings into per-entity rts before world drawing
        if (MapFile is not null && MapPreloaded)
        {
            SilhouetteRenderer.Clear();

            var player = WorldState.GetPlayerEntity();

            //collect silhouette entries (any entity type)
            if (player is not null)
                SilhouetteRenderer.AddSilhouette(player.Id);

            //collect transparent aisling entries (need per-entity compositing for uniform alpha)
            foreach (var entity in sortedEntities)
                if (entity is { Type: ClientEntityType.Aisling, IsTransparent: true })
                {
                    if (entity.Appearance is null)
                        continue;

                    var appearance = entity.Appearance.Value;
                    (var fIdx, var fFlip, var fAnimSuffix, var fIsFrontFacing) = AnimationSystem.GetAislingFrame(entity);
                    var fEmotionFrame = entity.ActiveEmoteFrame;

                    var drawData = Game.AislingRenderer.GetLayerFrames(
                        in appearance,
                        fIdx,
                        fAnimSuffix,
                        fFlip,
                        fIsFrontFacing,
                        fEmotionFrame);

                    SilhouetteRenderer.AddTransparent(
                        entity.Id,
                        entity.TileX,
                        entity.TileY,
                        entity.VisualOffset,
                        drawData,
                        Highlight.ShowTintHighlight && (Highlight.HoveredEntityId == entity.Id));
                }

            SilhouetteRenderer.PreRenderTransparents(Game.AislingRenderer);

            //pre-render silhouettes into a screen-sized rt (must happen before main rt drawing starts,
            //because rt switching discards the main rt's contents)
            SilhouetteRenderer.PreRenderSilhouettes(batch =>
            {
                batch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, GlobalSettings.Sampler);

                foreach (var entityId in SilhouetteRenderer.SilhouetteEntityIds)
                {
                    var silEntity = WorldState.GetEntity(entityId);

                    if (silEntity is not null)
                        DrawEntity(batch, silEntity);
                }

                batch.End();
            });
        }

        //pass 1: world rendering — clipped to the hud viewport area, camera transform
        if (MapFile is not null && MapPreloaded)
        {
            var viewportRect = WorldHud.ViewportBounds;
            Device.ScissorRectangle = viewportRect;

            var transform = Matrix.CreateTranslation(viewportRect.X, viewportRect.Y, 0);

            //background tiles + tile cursor: batched (many draws, no blend changes)
            spriteBatch.Begin(samplerState: GlobalSettings.Sampler, rasterizerState: ScissorRasterizerState, transformMatrix: transform);

            MapRenderer.DrawBackground(
                spriteBatch,
                MapFile,
                Camera,
                AnimationTick);
            DrawTileCursor(spriteBatch);
            spriteBatch.End();

            //foreground, entities, effects: immediate mode (per-stripe ordering, blend switches for additive effects)
            spriteBatch.Begin(
                SpriteSortMode.Immediate,
                BlendState.AlphaBlend,
                GlobalSettings.Sampler,
                null,
                ScissorRasterizerState,
                null,
                transform);
            DrawForegroundAndEntities(spriteBatch, sortedEntities);
            SilhouetteRenderer.DrawSilhouettes(spriteBatch);
            spriteBatch.End();

            //darkness overlay — drawn over the world in screen space (no camera transform)
            if (DarknessRenderer.IsActive)
            {
                spriteBatch.Begin(
                    blendState: BlendState.NonPremultiplied,
                    samplerState: GlobalSettings.Sampler,
                    rasterizerState: ScissorRasterizerState);
                var viewport = WorldHud.ViewportBounds;
                DarknessRenderer.Draw(spriteBatch, viewport);
                spriteBatch.End();
            }

            //weather overlay — drawn after darkness so snowflakes/rain remain visible on dark maps
            if (WeatherRenderer.IsActive)
            {
                spriteBatch.Begin(
                    blendState: BlendState.AlphaBlend,
                    samplerState: GlobalSettings.Sampler,
                    rasterizerState: ScissorRasterizerState);
                var weatherViewport = WorldHud.ViewportBounds;
                WeatherRenderer.Draw(spriteBatch, weatherViewport);
                spriteBatch.End();
            }

            //blind overlay — black out viewport, then redraw only the player character. drawn before
            //entity overlays so chat bubbles, name tags, chant text, etc. remain visible while blinded,
            //matching retail (which implements blind as a per-entity darkness mask rather than a
            //viewport fill, so its independent overlay panes are unaffected).
            if (WorldState.Attributes.Current?.Blind is true)
            {
                spriteBatch.Begin(
                    blendState: BlendState.AlphaBlend,
                    samplerState: GlobalSettings.Sampler,
                    rasterizerState: ScissorRasterizerState);
                RenderHelper.DrawRect(spriteBatch, WorldHud.ViewportBounds, Color.Black);
                spriteBatch.End();

                var player = WorldState.GetPlayerEntity();

                if (player is not null)
                {
                    spriteBatch.Begin(
                        SpriteSortMode.Immediate,
                        BlendState.AlphaBlend,
                        GlobalSettings.Sampler,
                        null,
                        ScissorRasterizerState,
                        null,
                        transform);
                    DrawEntity(spriteBatch, player);
                    spriteBatch.End();
                }
            }

            //entity overlays (chat bubbles, health bars, name tags, chant text) — drawn after darkness
            //so light level doesn't tint them, and after blind so they remain visible while blinded
            spriteBatch.Begin(
                SpriteSortMode.Immediate,
                BlendState.AlphaBlend,
                GlobalSettings.Sampler,
                null,
                ScissorRasterizerState,
                null,
                transform);

            Overlays.Draw(
                spriteBatch,
                Camera,
                MapFile.Height,
                sortedEntities,
                Highlight.ShowTintHighlight,
                Highlight.HoveredEntityId,
                WorldState.PlayerEntityId);
            spriteBatch.End();

            //snapshot draw count before debug draws so the reported count excludes debug visualizations
            DebugOverlay.SnapshotDrawCount();

            //debug overlay: entity hitboxes, tile grid, etc.
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
                    sortedEntities,
                    WorldState.GetPlayerEntity(),
                    EntityHitBoxes,
                    InputBuffer.MouseX,
                    InputBuffer.MouseY,
                    WorldHud.ViewportBounds);
                spriteBatch.End();
            }
        }

        //tab map overlay — drawn on top of world, under hud
        //tabmaprenderer manages its own spritebatch begin/end blocks (stencil passes for entity overlap)
        //NoTabMap map flag (0x40) suppresses both the toggle (InputHandlers) and the render
        if (TabMapVisible && MapFile is not null && !CurrentMapFlags.HasFlag(MapFlags.NoTabMap))
        {
            var player = WorldState.GetPlayerEntity();

            //no player → no tab map this frame (avoids stamping baseline at (0,0) during transitions)
            if (player is not null)
            {
                var viewport = WorldHud.ViewportBounds;
                var px = player.TileX;
                var py = player.TileY;

                var entityCount = sortedEntities.Count;

                if (TabMapEntities.Length < entityCount)
                    TabMapEntities = new TabMapEntity[entityCount];

                for (var i = 0; i < entityCount; i++)
                {
                    var e = sortedEntities[i];

                    TabMapEntities[i] = new TabMapEntity(
                        e.TileX,
                        e.TileY,
                        e.Type,
                        e.Id,
                        e.CreatureType);
                }

                TabMapRenderer.Draw(
                    spriteBatch,
                    Device,
                    viewport,
                    px,
                    py,
                    TabMapEntities,
                    entityCount,
                    WorldState.PlayerEntityId,
                    DarknessRenderer.IsFullBlackDark,
                    Lighting.Sources,
                    LightingSystem.BaselineVisibilityOffsets);
            }
        }

        //pass 2: ui overlay — full screen, no transform
        spriteBatch.Begin(samplerState: GlobalSettings.Sampler);
        Root!.Draw(spriteBatch);
        DrawDragIcon(spriteBatch);
        spriteBatch.End();
    }

    #region Swimming
    /// <summary>
    ///     Updates the entity's water tile state from the current map tile's gndattr data.
    /// </summary>
    private void UpdateEntityWaterState(WorldEntity entity)
    {
        if (MapFile is null
            || (entity.TileX < 0)
            || (entity.TileX >= MapFile.Width)
            || (entity.TileY < 0)
            || (entity.TileY >= MapFile.Height))
        {
            entity.GroundPaintHeight = 0;

            return;
        }

        var bgTileId = MapFile.Tiles[entity.TileX, entity.TileY].Background;

        if (DataContext.Tiles.GroundAttributes.TryGetValue(bgTileId, out var gndAttr))
        {
            entity.IsOnSwimmingTile = gndAttr.IsWalkBlocking;
            entity.GroundPaintHeight = gndAttr.PaintHeight;

            entity.GroundTintColor = new Color(
                gndAttr.R,
                gndAttr.G,
                gndAttr.B,
                gndAttr.A);

            //cache swim walk frame count for animation timing
            if (gndAttr.IsWalkBlocking)
            {
                var isFemale = entity.Appearance?.Gender == Gender.Female;
                var swimFrameCount = Game.AislingRenderer.GetSwimFrameCount(isFemale);
                var framesPerDir = swimFrameCount / 2;
                entity.SwimWalkFrames = Math.Max(framesPerDir - 1, 1);
            }
        } else
        {
            entity.IsOnSwimmingTile = false;
            entity.GroundPaintHeight = 0;
            entity.SwimWalkFrames = 0;
        }
    }
    #endregion

    #region Diagonal Stripe Rendering
    /// <summary>
    ///     Iterates foreground tiles, entities, and effects in diagonal stripe order (depth = x+y ascending). Per stripe draw
    ///     order: ground items → aislings → creatures → ground effects → entity effects → foreground tiles. Within each
    ///     category, entities draw in list order (arrival order — later arrivals on top).
    /// </summary>
    private void DrawForegroundAndEntities(SpriteBatch spriteBatch, IReadOnlyList<WorldEntity> sortedEntities)
    {
        if (MapFile is null)
            return;

        EntityHitBoxes.Clear();

        (var fgMinX, var fgMinY, var fgMaxX, var fgMaxY) = Camera.GetVisibleTileBounds(
            MapFile.Width,
            MapFile.Height,
            MapRenderer.ForegroundExtraMargin);

        var minDepth = fgMinX + fgMinY;
        var maxDepth = fgMaxX + fgMaxY;
        var entityIndex = 0;
        var entityCount = sortedEntities.Count;

        //skip entities before the visible depth range
        while ((entityIndex < entityCount) && (sortedEntities[entityIndex].SortDepth < minDepth))
            entityIndex++;

        for (var depth = minDepth; depth <= maxDepth; depth++)
        {
            //collect entities at this depth stripe
            var stripeStart = entityIndex;

            while ((entityIndex < entityCount) && (sortedEntities[entityIndex].SortDepth == depth))
                entityIndex++;

            var stripeEnd = entityIndex;

            //1. ground items
            for (var i = stripeStart; i < stripeEnd; i++)
                if (sortedEntities[i].Type == ClientEntityType.GroundItem)
                    DrawEntity(spriteBatch, sortedEntities[i]);

            //2. aislings
            for (var i = stripeStart; i < stripeEnd; i++)
                if (sortedEntities[i].Type == ClientEntityType.Aisling)
                    DrawEntity(spriteBatch, sortedEntities[i]);

            //3. creatures
            for (var i = stripeStart; i < stripeEnd; i++)
                if (sortedEntities[i].Type == ClientEntityType.Creature)
                    DrawEntity(spriteBatch, sortedEntities[i]);

            //4. dying creature dissolves
            DrawDyingEffectsAtDepth(spriteBatch, depth);

            //5. ground-targeted effects
            DrawGroundEffectsAtDepth(spriteBatch, depth);

            //5. entity-attached effects
            for (var i = stripeStart; i < stripeEnd; i++)
                DrawEntityEffects(spriteBatch, sortedEntities[i]);

            //6. foreground tiles (on top — trees, buildings occlude entities behind them)
            var tileXStart = Math.Max(fgMinX, depth - fgMaxY);
            var tileXEnd = Math.Min(fgMaxX, depth - fgMinY);

            for (var tileX = tileXStart; tileX <= tileXEnd; tileX++)
                MapRenderer.DrawForegroundTile(
                    spriteBatch,
                    Device,
                    MapFile,
                    Camera,
                    tileX,
                    depth - tileX,
                    AnimationTick);
        }
    }

    private void DrawDyingEffectsAtDepth(SpriteBatch spriteBatch, int depth)
    {
        if (MapFile is null)
            return;

        foreach (var dying in WorldState.DyingEffects)
        {
            if (dying.IsComplete || ((dying.TileX + dying.TileY) != depth))
                continue;

            var tileWorld = Camera.TileToWorld(dying.TileX, dying.TileY, MapFile.Height);
            var tileCenterX = tileWorld.X + DaLibConstants.HALF_TILE_WIDTH;
            var tileCenterY = tileWorld.Y + DaLibConstants.HALF_TILE_HEIGHT;

            var texCenterX = dying.CenterX - Math.Min(0, (int)dying.Left);
            var texCenterY = dying.CenterY - Math.Min(0, (int)dying.Top);

            var anchorX = dying.Flip
                ? dying.SourceWidth - texCenterX - dying.CenterXOffset
                : texCenterX + dying.CenterXOffset;

            var drawX = tileCenterX - anchorX;
            var drawY = tileCenterY - texCenterY;
            var screenPos = Camera.WorldToScreen(new Vector2(drawX, drawY));

            var effects = dying.Flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            var sourceRect = new Rectangle(0, 0, dying.SourceWidth, dying.TextureHeight);

            spriteBatch.Draw(
                dying.Texture,
                screenPos,
                sourceRect,
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
        foreach (var effect in WorldState.ActiveEffects)
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
        Animation effect,
        float tileCenterX,
        float tileCenterY,
        Vector2 visualOffset)
        => Game.EffectRenderer.Draw(
            spriteBatch,
            Device,
            Camera,
            effect.EffectId,
            effect.CurrentFrame,
            effect.BlendMode,
            tileCenterX,
            tileCenterY,
            visualOffset);
    #endregion

    #region Entity Rendering
    private void DrawEntity(SpriteBatch spriteBatch, WorldEntity entity)
    {
        if (MapFile is null)
            return;

        var tileWorldPos = Camera.TileToWorld(entity.TileX, entity.TileY, MapFile.Height);
        var tileCenterX = tileWorldPos.X + DaLibConstants.HALF_TILE_WIDTH;
        var tileCenterY = tileWorldPos.Y + DaLibConstants.HALF_TILE_HEIGHT;

        var entityTextureBottom = 0;

        switch (entity.Type)
        {
            case ClientEntityType.Aisling:
                entityTextureBottom = DrawAisling(
                    spriteBatch,
                    entity,
                    tileCenterX,
                    tileCenterY);

                break;

            case ClientEntityType.Creature:
                entityTextureBottom = DrawCreature(
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

                return; //ground items don't get hitboxes
        }

        if (entityTextureBottom <= 0)
            return;

        //hitbox: 28px wide centered on tile screen x, 60px tall bottom-aligned to texture bottom
        var tileScreenPos = Camera.WorldToScreen(new Vector2(tileCenterX + entity.VisualOffset.X, tileCenterY + entity.VisualOffset.Y));
        var hitboxX = (int)tileScreenPos.X - HITBOX_WIDTH / 2;
        var hitboxY = entityTextureBottom - HITBOX_HEIGHT;

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

        var tint = Highlight.ShowTintHighlight && (Highlight.HoveredEntityId == entity.Id)
            ? EntityTintType.Highlight
            : GroupHighlightedIds.Contains(entity.Id)
                ? EntityTintType.Group
                : EntityTintType.None;

        return creatureRenderer.Draw(
            spriteBatch,
            Camera,
            entity.SpriteId,
            frameIndex,
            flip,
            tileCenterX,
            tileCenterY,
            entity.VisualOffset,
            tint);
    }

    private int DrawAisling(
        SpriteBatch spriteBatch,
        WorldEntity entity,
        float tileCenterX,
        float tileCenterY)
    {
        //morphed aislings (creature form) render as creatures — swimming overrides morphs too
        if (entity.Appearance is null && entity is { SpriteId: > 0, IsOnSwimmingTile: false })
            return DrawCreature(
                spriteBatch,
                entity,
                tileCenterX,
                tileCenterY);

        if (entity.Appearance is null && !entity.IsOnSwimmingTile)
            return 0;

        var appearance = entity.Appearance ?? default;
        (var frameIndex, var flip, var animSuffix, var isFrontFacing) = AnimationSystem.GetAislingFrame(entity);

        //swimming override — single sprite replaces all aisling layers, driven by existing animation state
        if (entity.IsOnSwimmingTile)
        {
            var isFemale = entity.Appearance?.Gender == Gender.Female;
            var dirIndex = isFrontFacing ? 1 : 0;

            var swimFrameCount = Game.AislingRenderer.GetSwimFrameCount(isFemale);
            var framesPerDir = swimFrameCount / 2;

            if (framesPerDir <= 0)
                return 0;

            //walking: use walk frame index directly. idle: use idleanimtick for continuous cycling.
            //frame 0 is the idle/standing pose — skip it so the swim animation only cycles walk frames (1..n).
            var walkFrames = framesPerDir - 1;

            var animIndex = walkFrames > 0
                ? 1 + (entity.AnimState == EntityAnimState.Walking ? entity.AnimFrameIndex % walkFrames : entity.IdleAnimTick % walkFrames)
                : 0;

            var swimFrame = dirIndex * framesPerDir + animIndex;

            return Game.AislingRenderer.DrawSwimming(
                spriteBatch,
                Camera,
                isFemale,
                swimFrame,
                flip,
                tileCenterX,
                tileCenterY,
                entity.VisualOffset);
        }

        //rest position override — single spf sprite replaces all aisling layers
        if (entity.RestPosition != RestPosition.None)
            return Game.AislingRenderer.DrawResting(
                spriteBatch,
                Camera,
                entity.Appearance?.Gender == Gender.Female,
                entity.RestPosition,
                isFrontFacing,
                flip,
                tileCenterX,
                tileCenterY,
                entity.VisualOffset,
                entity.ActiveEmoteFrame);

        var emotionFrame = entity.ActiveEmoteFrame;
        var groundPaintHeight = entity.IsOnSwimmingTile ? 0 : entity.GroundPaintHeight;

        //base position: composite canvas origin relative to tile center
        var baseX = tileCenterX + entity.VisualOffset.X - BODY_CENTER_X;
        var baseY = tileCenterY + entity.VisualOffset.Y - BODY_CENTER_Y;

        //transparent aislings use the pre-composited render target for uniform alpha (no layer bleed-through)
        if (entity.IsTransparent
            && SilhouetteRenderer.DrawTransparentEntity(
                spriteBatch,
                entity.Id,
                entity.TileX,
                entity.TileY,
                entity.VisualOffset,
                Camera,
                MapFile!.Height,
                BODY_CENTER_X,
                BODY_CENTER_Y))
        {
            var bodyScreenPos2 = Camera.WorldToScreen(new Vector2(baseX, baseY));

            return (int)(bodyScreenPos2.Y + AislingRenderer.COMPOSITE_HEIGHT);
        }

        var tint = Highlight.ShowTintHighlight && (Highlight.HoveredEntityId == entity.Id)
            ? EntityTintType.Highlight
            : GroupHighlightedIds.Contains(entity.Id)
                ? EntityTintType.Group
                : EntityTintType.None;

        var drawParams = new AislingDrawParams(
            entity.Id,
            appearance,
            frameIndex,
            flip,
            isFrontFacing,
            animSuffix,
            emotionFrame,
            groundPaintHeight,
            entity.GroundTintColor,
            tileCenterX,
            tileCenterY,
            entity.VisualOffset,
            tint,
            entity.IsDead);

        return Game.AislingRenderer.Draw(spriteBatch, Camera, in drawParams);
    }

    private void ClearHighlightCache()
    {
        Highlight.ClearTint();
        Game.AislingRenderer.ClearTintedCache();
        Game.CreatureRenderer.ClearTintCaches();
    }

    private void DrawEntityEffects(SpriteBatch spriteBatch, WorldEntity entity)
    {
        if (MapFile is null)
            return;

        var tileWorldPos = Camera.TileToWorld(entity.TileX, entity.TileY, MapFile.Height);
        var tileCenterX = tileWorldPos.X + DaLibConstants.HALF_TILE_WIDTH;
        var tileCenterY = tileWorldPos.Y + DaLibConstants.HALF_TILE_HEIGHT;

        foreach (var effect in WorldState.ActiveEffects)
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
        => Game.ItemRenderer.Draw(
            spriteBatch,
            Camera,
            entity.SpriteId,
            entity.ItemColor,
            tileCenterX,
            tileCenterY);

    /// <summary>
    ///     Creates a texture containing a dashed ellipse inscribed in the isometric tile diamond. Gaps at the 4 cardinal
    ///     directions (top, right, bottom, left of the ellipse).
    /// </summary>
    private static Texture2D CreateTileCursorTexture(GraphicsDevice device, Color color)
    {
        const int WIDTH = DaLibConstants.HALF_TILE_WIDTH * 2; //56
        const int HEIGHT = DaLibConstants.HALF_TILE_HEIGHT * 2; //28

        var pixels = new Color[WIDTH * HEIGHT];

        var cx = WIDTH / 2;
        var cy = HEIGHT / 2;

        //top-right quarter only.
        //these are offsets from the center.
        //tweak these until the shape matches exactly how you want.
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
            color); //top-right

        SetPixel(
            pixels,
            width,
            height,
            cx - dx,
            cy + dy,
            color); //top-left

        SetPixel(
            pixels,
            width,
            height,
            cx + dx,
            cy - dy,
            color); //bottom-right

        SetPixel(
            pixels,
            width,
            height,
            cx - dx,
            cy - dy,
            color); //bottom-left
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

        if (WorldHud.Tools.WorldSkills.IsDragging)
            return WorldHud.Tools.WorldSkills;

        if (WorldHud.Tools.WorldSpells.IsDragging)
            return WorldHud.Tools.WorldSpells;

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

        var viewport = WorldHud.ViewportBounds;

        //only draw when mouse is within the world viewport
        if ((InputBuffer.MouseX < viewport.X)
            || (InputBuffer.MouseX >= (viewport.X + viewport.Width))
            || (InputBuffer.MouseY < viewport.Y)
            || (InputBuffer.MouseY >= (viewport.Y + viewport.Height)))
            return;

        (var tileX, var tileY) = ScreenToTile(InputBuffer.MouseX, InputBuffer.MouseY);

        if ((tileX < 0) || (tileX >= MapFile.Width) || (tileY < 0) || (tileY >= MapFile.Height))
            return;

        var tileWorld = Camera.TileToWorld(tileX, tileY, MapFile.Height);
        var tileScreen = Camera.WorldToScreen(new Vector2(tileWorld.X, tileWorld.Y));

        var cursorTexture = Game.Dispatcher.IsDragging ? TileCursorDragTexture : TileCursorTexture;
        spriteBatch.Draw(cursorTexture!, new Vector2((int)tileScreen.X, (int)tileScreen.Y), Color.White);
    }
    #endregion
}
#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Systems;
using Chaos.DarkAges.Definitions;
using Chaos.Geometry.Abstractions.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Pathfinder = Chaos.Client.Systems.Pathfinder;
#endregion

namespace Chaos.Client.Screens;

public sealed partial class WorldScreen
{
    public void Update(GameTime gameTime)
    {
        if (PendingLoginSwitch)
        {
            PendingLoginSwitch = false;
            Game.Screens.Switch(new LobbyLoginScreen(true));

            return;
        }

        var input = Game.Input;

        var elapsedMs = (float)gameTime.ElapsedGameTime.TotalMilliseconds;

        //global tile animation tick — 100ms resolution (matches tile animation table format)
        AnimationTick = (int)(gameTime.TotalGameTime.TotalMilliseconds / 100);
        MapRenderer.UpdatePaletteCycling(AnimationTick);

        //advance entity animations and active effects
        var smoothScroll = ClientSettings.ScrollLevel > 0;
        var player = WorldState.GetPlayerEntity();

        foreach (var entity in WorldState.GetSortedEntities())
        {
            //update water tile state before animation so swimming idle tick advances
            UpdateEntityWaterState(entity);

            //all entities step discretely by default. player gets smooth lerp only if setting enabled.
            var isSmooth = (entity == player) && smoothScroll;
            AnimationSystem.Advance(entity, elapsedMs, isSmooth);

            //update creature optional standing animation cycle
            if (entity.Type == ClientEntityType.Creature)
            {
                var animInfo = Game.CreatureRenderer.GetAnimInfo(entity.SpriteId);

                if (animInfo.HasValue)
                {
                    var info = animInfo.Value;
                    AnimationSystem.UpdateCreatureIdleCycle(entity, in info);
                }
            }

            //tick emote overlay timer and cycle animated emote frames
            if (entity.ActiveEmoteFrame >= 0)
            {
                entity.EmoteElapsedMs += elapsedMs;
                entity.EmoteRemainingMs -= elapsedMs;

                if (entity.EmoteRemainingMs <= 0)
                {
                    entity.ActiveEmoteFrame = -1;
                    entity.EmoteFrameCount = 0;
                } else if (entity.EmoteFrameCount > 1)
                {
                    var frameDuration = entity.EmoteDurationMs / entity.EmoteFrameCount;
                    var frameIndex = (int)(entity.EmoteElapsedMs / frameDuration) % entity.EmoteFrameCount;
                    entity.ActiveEmoteFrame = entity.EmoteStartFrame + frameIndex;
                }
            }
        }

        WorldState.UpdateEffects(elapsedMs);

        //group highlight auto-expire (1000ms flash)
        if (GroupHighlightedIds.Count > 0)
        {
            GroupHighlightTimer -= elapsedMs;

            if (GroupHighlightTimer <= 0)
            {
                GroupHighlightedIds.Clear();
                Game.AislingRenderer.ClearGroupTintCache();
                Game.CreatureRenderer.ClearTintCaches();
            }
        }

        //execute queued walk when player becomes idle after walk animation.
        var movementHandled = false;

        if (player is not null && (player.AnimState == EntityAnimState.Idle) && QueuedWalkDirection.HasValue)
        {
            var queuedDir = QueuedWalkDirection.Value;
            QueuedWalkDirection = null;

            if (player.Direction != queuedDir)
            {
                Game.Connection.Turn(queuedDir);
                player.Direction = queuedDir;
            } else
                PredictAndWalk(player, queuedDir);

            movementHandled = true;
        }

        //execute next pathfinding step when player becomes idle
        if (!movementHandled && player is not null && (player.AnimState == EntityAnimState.Idle))
        {
            if (Pathfinding.Path is { Count: > 0 })
            {
                //if chasing an entity that no longer exists, stop
                if (Pathfinding.TargetEntityId.HasValue && WorldState.GetEntity(Pathfinding.TargetEntityId.Value) is null)
                    Pathfinding.Clear();
                else
                {
                    var nextPoint = Pathfinding.Path.Pop();
                    var dx = nextPoint.X - player.TileX;
                    var dy = nextPoint.Y - player.TileY;

                    var pathDir = (dx, dy) switch
                    {
                        (0, -1) => Direction.Up,
                        (1, 0)  => Direction.Right,
                        (0, 1)  => Direction.Down,
                        (-1, 0) => Direction.Left,
                        _       => (Direction?)null
                    };

                    if (pathDir.HasValue)
                    {
                        if (player.Direction != pathDir.Value)
                        {
                            Game.Connection.Turn(pathDir.Value);
                            player.Direction = pathDir.Value;
                        }

                        PredictAndWalk(player, pathDir.Value);
                    } else
                        Pathfinding.Clear();
                }
            } else if (Pathfinding.TargetEntityId.HasValue)
            {
                //path exhausted with entity target — check if adjacent and assail, or re-pathfind
                var target = WorldState.GetEntity(Pathfinding.TargetEntityId.Value);

                if (target is null)
                    Pathfinding.Clear();
                else if (Pathfinder.IsAdjacent(
                             player.TileX,
                             player.TileY,
                             target.TileX,
                             target.TileY))
                {
                    //adjacent — turn toward target and assail
                    var faceDir = Pathfinder.DirectionToward(
                        player.TileX,
                        player.TileY,
                        target.TileX,
                        target.TileY);

                    if (faceDir.HasValue && (player.Direction != faceDir.Value))
                    {
                        Game.Connection.Turn(faceDir.Value);
                        player.Direction = faceDir.Value;
                    }

                    Game.Connection.Spacebar();
                } else
                {
                    //entity moved — re-pathfind on 100ms timer
                    Pathfinding.RetargetTimer += elapsedMs;

                    if (Pathfinding.RetargetTimer >= 100f)
                    {
                        Pathfinding.RetargetTimer = 0;
                        PathfindToEntity(player, target);

                        if (Pathfinding.Path is null)
                            Pathfinding.Clear();
                    }
                }
            }
        }

        //tick re-pathfind timer while walking toward an entity target
        if (Pathfinding.TargetEntityId.HasValue && player is not null && (player.AnimState == EntityAnimState.Walking))
            Pathfinding.RetargetTimer += elapsedMs;

        //camera follows player's visual position (tile + walk interpolation offset)
        FollowPlayerCamera();

        //viewport-layer updates — must always run regardless of which ui panel has input focus
        //so that the world keeps animating visually behind open windows.
        if (MapFile is not null)
            Overlays.Update(
                Camera,
                MapFile.Height,
                gameTime);

        if (DarknessRenderer.IsActive)
        {
            DarknessRenderer.SetLightSources(GatherLightSources());
            DarknessRenderer.Update(Camera, WorldHud.ViewportBounds);
        }

        //tooltip follows cursor — always reposition regardless of active panel
        if (ItemTooltip.Visible)
        {
            var rightX = input.MouseX + 15;

            ItemTooltip.X = (rightX + ItemTooltip.Width) <= ChaosGame.VIRTUAL_WIDTH ? rightX : input.MouseX - ItemTooltip.Width;

            ItemTooltip.Y = Math.Clamp(input.MouseY + 15, 0, ChaosGame.VIRTUAL_HEIGHT - ItemTooltip.Height);
        }

        //── tooltip dismissal — clear hovered slot when blocking panels are visible ──
        if (HoveredInventorySlot is not null
            && (OtherProfile.Visible || NpcSession.Visible || FindVisibleModal() is not null))
        {
            HoveredInventorySlot = null;
            ItemTooltip.Hide();
        }

        //── track which entity the mouse is hovering over ──
        var hoverEntity = GetEntityAtScreen(Game.Input.MouseX, Game.Input.MouseY);

        var isItemDrag = GetDraggingPanel() is { } dragPanel && (dragPanel == WorldHud.Inventory);

        var newHoveredId = hoverEntity?.Type is ClientEntityType.Aisling or ClientEntityType.Creature
                           && !(isItemDrag && (hoverEntity.Id == Game.Connection.AislingId))
            ? hoverEntity.Id
            : (uint?)null;

        if (newHoveredId != Highlight.HoveredEntityId)
            ClearHighlightCache();

        Highlight.HoveredEntityId = newHoveredId;
        Game.UseHandCursor = newHoveredId.HasValue;

        //tint highlight only shows during spell targeting or item dragging
        Highlight.ShowTintHighlight = CastingSystem.IsTargeting || Game.Dispatcher.IsDragging;

        //tick casting timer (chant lines are sent on a 1-second interval)
        CastingSystem.Update(elapsedMs, Game.Connection);

        //spacebar assail is handled in OnRootKeyDown — the dispatcher delivers both the
        //initial press and os key-repeat keydowns through the event pipeline, so dialogs
        //that consume spacebar (via e.Handled = true) naturally block it.

        var skipDispatch = false;

        foreach (var mbEvt in Game.Input.MouseButtonEvents)
        {
            if (mbEvt is not { Button: MouseButton.Left, IsPress: true })
                continue;

            var mx = Game.Input.MouseX;
            var my = Game.Input.MouseY;

            if (AislingContext.Visible && !AislingContext.ContainsPoint(mx, my))
            {
                AislingContext.Hide();
                skipDispatch = true;
            } else if (AbilityMetadataDetails.Visible && !AbilityMetadataDetails.ContainsPoint(mx, my))
            {
                AbilityMetadataDetails.Hide();
                skipDispatch = true;
            } else if (EventMetadataDetails.Visible && !EventMetadataDetails.ContainsPoint(mx, my))
            {
                EventMetadataDetails.Hide();
                skipDispatch = true;
            }

            break;
        }

        if (!skipDispatch)
            Game.Dispatcher.ProcessInput(Root!, gameTime);

        Root!.Update(gameTime);
    }

    /// <summary>
    ///     Returns the first visible modal panel among Root's children (highest ZIndex first), or null.
    /// </summary>
    private UIPanel? FindVisibleModal()
    {
        if (Root is null)
            return null;

        UIPanel? best = null;

        foreach (var child in Root.Children)
            if (child is UIPanel { Visible: true, IsModal: true } panel && (best is null || (panel.ZIndex > best.ZIndex)))
                best = panel;

        return best;
    }

    private ReadOnlySpan<LightSource> GatherLightSources()
    {
        if (MapFile is null || !CurrentMapFlags.HasFlag(MapFlags.Darkness))
            return ReadOnlySpan<LightSource>.Empty;

        var count = 0;

        foreach (var entity in WorldState.GetSortedEntities())
        {
            if (entity.LanternSize == LanternSize.None)
                continue;

            var mask = DataContext.LightMasks.Get(entity.LanternSize);

            if (mask is null)
                continue;

            var tileWorld = Camera.TileToWorld(entity.TileX, entity.TileY, MapFile.Height);
            var tileCenterX = tileWorld.X + DaLibConstants.HALF_TILE_WIDTH;
            var tileCenterY = tileWorld.Y + DaLibConstants.HALF_TILE_HEIGHT;
            var screenPos = Camera.WorldToScreen(new Vector2(tileCenterX + entity.VisualOffset.X, tileCenterY + entity.VisualOffset.Y));

            if (count >= LightSourceBuffer.Length)
                Array.Resize(ref LightSourceBuffer, LightSourceBuffer.Length * 2);

            LightSourceBuffer[count++] = new LightSource(screenPos, mask);
        }

        return LightSourceBuffer.AsSpan(0, count);
    }
}
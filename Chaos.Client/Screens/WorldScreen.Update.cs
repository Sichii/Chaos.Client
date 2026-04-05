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
        input.Suppressed = false;

        if (DisconnectPopup.Visible)
        {
            DisconnectPopup.Update(gameTime, input);

            return;
        }

        var elapsedMs = (float)gameTime.ElapsedGameTime.TotalMilliseconds;

        // Global tile animation tick — 100ms resolution (matches tile animation table format)
        AnimationTick = (int)(gameTime.TotalGameTime.TotalMilliseconds / 100);
        MapRenderer.UpdatePaletteCycling(AnimationTick);

        // Advance entity animations and active effects
        var smoothScroll = ClientSettings.ScrollLevel > 0;
        var player = WorldState.GetPlayerEntity();

        foreach (var entity in WorldState.GetSortedEntities())
        {
            // Update water tile state before animation so swimming idle tick advances
            UpdateEntityWaterState(entity);

            // All entities step discretely by default. Player gets smooth lerp only if setting enabled.
            var isSmooth = (entity == player) && smoothScroll;
            AnimationSystem.Advance(entity, elapsedMs, isSmooth);

            // Update creature optional standing animation cycle
            if (entity.Type == ClientEntityType.Creature)
            {
                var animInfo = Game.CreatureRenderer.GetAnimInfo(entity.SpriteId);

                if (animInfo.HasValue)
                {
                    var info = animInfo.Value;
                    AnimationSystem.UpdateCreatureIdleCycle(entity, in info);
                }
            }

            // Tick emote overlay timer and cycle animated emote frames
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

        // Group highlight auto-expire (1000ms flash)
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

        // Execute queued walk when player becomes idle after walk animation.
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

        // Execute next pathfinding step when player becomes idle
        if (!movementHandled && player is not null && (player.AnimState == EntityAnimState.Idle))
        {
            if (Pathfinding.Path is { Count: > 0 })
            {
                // If chasing an entity that no longer exists, stop
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
                        movementHandled = true;
                    } else
                        Pathfinding.Clear();
                }
            } else if (Pathfinding.TargetEntityId.HasValue)
            {
                // Path exhausted with entity target — check if adjacent and assail, or re-pathfind
                var target = WorldState.GetEntity(Pathfinding.TargetEntityId.Value);

                if (target is null)
                    Pathfinding.Clear();
                else if (Pathfinder.IsAdjacent(
                             player.TileX,
                             player.TileY,
                             target.TileX,
                             target.TileY))
                {
                    // Adjacent — turn toward target and assail
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
                    Pathfinding.Clear();
                    movementHandled = true;
                } else
                {
                    // Entity moved — re-pathfind on 100ms timer
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

        // Tick re-pathfind timer while walking toward an entity target
        if (Pathfinding.TargetEntityId.HasValue && player is not null && (player.AnimState == EntityAnimState.Walking))
            Pathfinding.RetargetTimer += elapsedMs;

        // Camera follows player's visual position (tile + walk interpolation offset)
        FollowPlayerCamera();

        // Viewport-layer updates — must always run regardless of which UI panel has input focus
        // so that the world keeps animating visually behind open windows.
        if (MapFile is not null)
            Overlays.Update(
                gameTime,
                input,
                Camera,
                MapFile.Height);

        if (DarknessRenderer.IsActive)
        {
            DarknessRenderer.SetLightSources(GatherLightSources());
            DarknessRenderer.Update(Camera, WorldHud.ViewportBounds);
        }

        // Tooltip follows cursor — always reposition regardless of active panel
        if (ItemTooltip.Visible)
        {
            var rightX = input.MouseX + 15;

            ItemTooltip.X = (rightX + ItemTooltip.Width) <= ChaosGame.VIRTUAL_WIDTH ? rightX : input.MouseX - ItemTooltip.Width;

            ItemTooltip.Y = Math.Clamp(input.MouseY + 15, 0, ChaosGame.VIRTUAL_HEIGHT - ItemTooltip.Height);
        }

        // Toggle hotkeys — processed before overlay blocks so they work when the toggled panel is the active overlay
        if (!WorldHud.ChatInput.IsFocused && !UITextBox.IsAnyFocused && !NpcSession.Visible)
        {
            // Q/W/E/R toggle group — mutual exclusion: opening one closes any other
            if (input.WasKeyPressed(Keys.Q))
            {
                ForceCloseOtherTogglePanels(Keys.Q);

                if (MainOptions.Visible)
                {
                    SettingsDialog.Hide();
                    MacroMenu.Hide();
                    FriendsList.Hide();
                    MainOptions.SlideClose();
                } else
                    MainOptions.Show();

            } else if (input.WasKeyPressed(Keys.W))
            {
                ForceCloseOtherTogglePanels(Keys.W);

                if (IsAnyBoardPanelVisible())
                {
                    if (BoardList.Visible)
                        BoardList.SlideClose();
                    else
                        WorldState.Board.CloseSession();
                } else
                    Game.Connection.SendBoardInteraction(BoardRequestType.BoardList);

            } else if (input.WasKeyPressed(Keys.E))
            {
                ForceCloseOtherTogglePanels(Keys.E);

                if (WorldList.Visible)
                    WorldList.SlideClose();
                else
                    Game.Connection.RequestWorldList();

            } else if (input.WasKeyPressed(Keys.R))
            {
                ForceCloseOtherTogglePanels(Keys.R);

                if (SocialStatusPicker.Visible)
                {
                    SocialStatusPicker.Visible = false;

                    if (WorldHud.EmoteButton is not null)
                        WorldHud.EmoteButton.IsSelected = false;
                } else
                    ToggleSocialStatusPicker();

            } else if (input.WasKeyPressed(Keys.T) && TownMap.Visible)
            {
                TownMap.Hide();
            }
        }

        // ─── Exclusive input panels ─── first visible panel gets input, everything else blocked

        if (UpdateExclusive(NpcSession, gameTime, input))
            return;

        // MainOptions has sub-panel routing — child panels get input priority when open
        if (MainOptions.Visible)
        {
            var subPanelOpen = false;

            if (MacroMenu.Visible)
            {
                MacroMenu.Update(gameTime, input);
                subPanelOpen = true;
            } else if (SettingsDialog.Visible)
            {
                SettingsDialog.Update(gameTime, input);
                subPanelOpen = true;
            } else if (FriendsList.Visible)
            {
                FriendsList.Update(gameTime, input);
                subPanelOpen = true;
            }

            if (!subPanelOpen)
                MainOptions.Update(gameTime, input);

            return;
        }

        if (UpdateExclusive(SettingsDialog, gameTime, input))
            return;

        if (UpdateExclusive(ChantEdit, gameTime, input))
            return;

        if (UpdateExclusive(MacroMenu, gameTime, input))
            return;

        if (UpdateExclusive(HotkeyHelp, gameTime, input))
            return;

        if (UpdateExclusive(GroupPanel, gameTime, input))
            return;

        if (UpdateExclusive(GroupBoxViewer, gameTime, input))
            return;

        if (UpdateExclusive(WorldList, gameTime, input))
            return;

        if (UpdateExclusive(FriendsList, gameTime, input))
            return;

        if (UpdateExclusive(WorldHud.Prompt, gameTime, input))
            return;

        if (UpdateExclusive(GoldDrop, gameTime, input))
            return;

        // Exchange also updates HUD for drag-and-drop and handles gold click
        if (Exchange.Visible)
        {
            ((UIPanel)WorldHud).Update(gameTime, input);
            Exchange.Update(gameTime, input);

            if (input.WasLeftButtonPressed && Exchange.IsMyMoneyClicked(input.MouseX, input.MouseY))
            {
                ExchangeAmountSlot = null;
                GoldDrop.ShowForTarget(Exchange.OtherUserId, 0, 0);
            }

            return;
        }

        if (UpdateExclusive(DeleteConfirm, gameTime, input))
            return;

        if (UpdateExclusive(ArticleSend, gameTime, input))
            return;

        if (UpdateExclusive(MailSend, gameTime, input))
            return;

        if (UpdateExclusive(ArticleRead, gameTime, input))
            return;

        if (UpdateExclusive(MailRead, gameTime, input))
            return;

        if (UpdateExclusive(ArticleList, gameTime, input))
            return;

        if (UpdateExclusive(MailList, gameTime, input))
            return;

        if (UpdateExclusive(BoardList, gameTime, input))
            return;

        if (UpdateExclusive(TextPopup, gameTime, input))
            return;

        if (UpdateExclusive(Notepad, gameTime, input))
            return;

        // ─── Modal popup check ─── topmost modal gets real input, everything below gets suppressed
        var modal = FindVisibleModal();

        if (modal is not null)
        {
            modal.Update(gameTime, input);
            input.Suppressed = true;
        }

        if (HoveredInventorySlot is not null && (modal is not null || StatusBook.Visible || OtherProfile.Visible || NpcSession.Visible))
        {
            HoveredInventorySlot = null;
            ItemTooltip.Hide();
        }

        // ─── Post-modal panels ─── may receive suppressed input when a modal is on top

        // StatusBook also updates HUD for drag-and-drop
        if (StatusBook.Visible)
        {
            ((UIPanel)WorldHud).Update(gameTime, input);
            StatusBook.Update(gameTime, input);

            return;
        }

        if (UpdateExclusive(OtherProfile, gameTime, input))
            return;

        if (UpdateExclusive(WorldMap, gameTime, input))
            return;

        if (UpdateExclusive(TownMap, gameTime, input))
            return;

        if (UpdateExclusive(SocialStatusPicker, gameTime, input))
            return;

        if (UpdateExclusive(AislingPopup, gameTime, input))
            return;

        if (UpdateExclusive(ContextMenu, gameTime, input))
            return;

        // Track which entity the mouse is hovering over (for name tags, tint highlight, targeting)
        var hoverEntity = GetEntityAtScreen(input.MouseX, input.MouseY);

        var newHoveredId = hoverEntity?.Type is ClientEntityType.Aisling or ClientEntityType.Creature
            ? hoverEntity.Id
            : (uint?)null;

        if (newHoveredId != Highlight.HoveredEntityId)
            ClearHighlightCache();

        Highlight.HoveredEntityId = newHoveredId;
        Game.UseHandCursor = newHoveredId.HasValue;

        // Tint highlight only shows during spell targeting or item dragging
        Highlight.ShowTintHighlight = CastingSystem.IsTargeting || GetDraggingPanel() is not null;

        // Tick casting timer (chant lines are sent on a 1-second interval)
        CastingSystem.Update(elapsedMs, Game.Connection);

        // Cast mode — blocks all other input, only allows target selection or cancel
        if (CastingSystem.IsTargeting)
        {
            if (input.WasKeyPressed(Keys.Escape))
            {
                CastingSystem.Reset();

                return;
            }

            // Click to select target, or cancel if clicking on nothing
            if (input.WasLeftButtonPressed)
            {
                if (hoverEntity?.Type is ClientEntityType.Aisling or ClientEntityType.Creature)
                    CastingSystem.SelectTarget(
                        hoverEntity.Id,
                        hoverEntity.TileX,
                        hoverEntity.TileY,
                        Game.Connection);
                else
                    CastingSystem.Reset();
            }

            ((UIPanel)WorldHud).Update(gameTime, input);

            return;
        }

        // Escape — close overlays, unfocus chat
        if (input.WasKeyPressed(Keys.Escape))
            if (WorldHud.ChatInput.IsFocused)
                Chat.Unfocus();

        // Up/Down — cycle whisper targets during name selection phase
        if (WorldHud.ChatInput.IsFocused && Chat.IsWhisperNamePhase)
        {
            if (input.WasKeyPressed(Keys.Up))
                Chat.CycleWhisperTarget(1);
            else if (input.WasKeyPressed(Keys.Down))
                Chat.CycleWhisperTarget(-1);
        }

        // Ignore mode — phase 1: single-key mode selection (a/d/?)
        if (WorldHud.ChatInput.IsFocused && (Chat.IgnorePhase == IgnorePhase.ModeSelect))
        {
            var textInput = input.TextInput;

            if (textInput.Length > 0)
            {
                var c = textInput[0];

                switch (c)
                {
                    case 'a' or 'A':
                        Chat.TransitionIgnoreAdd();

                        break;
                    case 'd' or 'D':
                        Chat.TransitionIgnoreRemove();

                        break;
                    case '?':
                        Game.Connection.SendIgnoreRequest();
                        Chat.Unfocus();

                        break;
                }

                // Suppress all text input during mode selection so characters don't reach the textbox
                input.Suppressed = true;
            }
        }

        // Enter — toggle chat focus / send message
        if (input.WasKeyPressed(Keys.Enter))
        {
            if (WorldHud.ChatInput.IsFocused)
            {
                var message = WorldHud.ChatInput.Text.Trim();

                // Ignore phase 2: submit the typed name for add/remove
                if (Chat.IgnorePhase is IgnorePhase.AddName or IgnorePhase.RemoveName)
                {
                    if (message.Length > 0)
                    {
                        if (Chat.IgnorePhase == IgnorePhase.AddName)
                            Game.Connection.SendAddIgnore(message);
                        else
                            Game.Connection.SendRemoveIgnore(message);
                    }

                    Chat.Unfocus();
                }

                // Whisper phase 1: "to [name]? " → resolve target name, transition to phase 2
                else if (Chat.IsWhisperNamePhase)
                {
                    // Typed name overrides the bracketed default
                    var targetName = message.Length > 0 ? message : Chat.GetBracketedWhisperTarget();

                    if (targetName.Length > 0)
                    {
                        WorldHud.ChatInput.Prefix = $"-> {targetName}: ";
                        WorldHud.ChatInput.Text = string.Empty;
                    }
                } else
                {
                    Chat.Dispatch(message);
                    WorldHud.ChatInput.Text = string.Empty;
                    Chat.Unfocus();
                }
            } else
                Chat.Focus($"{WorldHud.PlayerName}: ", Color.White);
        }

        // Hotkeys and movement — only when no text box is focused
        if (!WorldHud.ChatInput.IsFocused && !UITextBox.IsAnyFocused)
        {
            // Shout hotkey (!) — opens chat in shout mode
            if (input.WasKeyPressed(Keys.D1) && (input.IsKeyHeld(Keys.LeftShift) || input.IsKeyHeld(Keys.RightShift)))
            {
                Chat.Focus($"{WorldHud.PlayerName}! ", Color.Yellow);

                return;
            }

            // Whisper hotkey (") — opens chat in whisper mode, skipping phase 1 if history exists
            if (input.WasKeyPressed(Keys.OemQuotes) && (input.IsKeyHeld(Keys.LeftShift) || input.IsKeyHeld(Keys.RightShift)))
            {
                Chat.FocusWhisper();

                return;
            }

            // Tab panel switching — blocked while dragging the orange bar
            var shift = input.IsKeyHeld(Keys.LeftShift) || input.IsKeyHeld(Keys.RightShift);

            if (WorldHud.IsOrangeBarDragging)
            {
                // Suppress all panel switching / expand while dragging
            } else if (input.WasKeyPressed(Keys.A))
            {
                if (shift)
                {
                    if (WorldHud.ActiveTab != HudTab.Inventory)
                        WorldHud.ShowTab(HudTab.Inventory);

                    WorldHud.ToggleExpand();
                } else if (WorldHud.ActiveTab == HudTab.Inventory)
                {
                    SelfProfileRequested = true;
                    Game.Connection.RequestSelfProfile();
                } else
                    WorldHud.ShowTab(HudTab.Inventory);
            } else if (input.WasKeyPressed(Keys.S))
            {
                var useShift = ClientSettings.UseShiftKeyForAltPanels;
                var alt = useShift ? shift : WorldHud.ActiveTab == HudTab.Skills;
                WorldHud.ShowTab(alt ? HudTab.SkillsAlt : HudTab.Skills);
            } else if (input.WasKeyPressed(Keys.D))
            {
                var useShift = ClientSettings.UseShiftKeyForAltPanels;
                var alt = useShift ? shift : WorldHud.ActiveTab == HudTab.Spells;
                WorldHud.ShowTab(alt ? HudTab.SpellsAlt : HudTab.Spells);
            } else if (input.WasKeyPressed(Keys.F))
                WorldHud.ShowTab(shift ? HudTab.MessageHistory : HudTab.Chat);
            else if (input.WasKeyPressed(Keys.G))
                WorldHud.ShowTab(shift ? HudTab.ExtendedStats : HudTab.Stats);
            else if (input.WasKeyPressed(Keys.H))
                WorldHud.ShowTab(HudTab.Tools);

            // Tab — toggle tab map overlay
            if (input.WasKeyPressed(Keys.Tab))
                TabMapVisible = !TabMapVisible;

            // PageUp/PageDown — tab map zoom
            if (TabMapVisible)
            {
                if (input.WasKeyPressed(Keys.PageUp))
                    TabMapRenderer.ZoomIn();

                if (input.WasKeyPressed(Keys.PageDown))
                    TabMapRenderer.ZoomOut();
            }

            // F1 — hotkey help
            // F1 — help merchant (server-side)
            if (input.WasKeyPressed(Keys.F1))
                Game.Connection.ClickEntity(uint.MaxValue);

            // F3 — macro menu
            if (input.WasKeyPressed(Keys.F3))
                MacroMenu.Show();

            // F4 — settings
            if (input.WasKeyPressed(Keys.F4))
                SettingsDialog.Show();

            // F5 — refresh
            if (input.WasKeyPressed(Keys.F5))
                Game.Connection.RequestRefresh();

            // F7 — board list
            if (input.WasKeyPressed(Keys.F7))
                Game.Connection.SendBoardInteraction(BoardRequestType.BoardList);

            // F8 — group panel
            if (input.WasKeyPressed(Keys.F8))
                GroupPanel.ShowMembers();

            // F9 — ignore list management
            if (input.WasKeyPressed(Keys.F9))
                Chat.FocusIgnore();

            // F10 — friends list
            if (input.WasKeyPressed(Keys.F10))
                FriendsList.Show();

            // / — swap HUD layout (small ↔ large)
            if (input.WasKeyPressed(Keys.OemQuestion) && !shift)
                SwapHudLayout();

            // ` — unequip weapon and shield
            if (input.WasKeyPressed(Keys.OemTilde))
            {
                if (WorldState.Equipment.GetSlot(EquipmentSlot.Weapon) is not null)
                    Game.Connection.Unequip(EquipmentSlot.Weapon);

                if (WorldState.Equipment.GetSlot(EquipmentSlot.Shield) is not null)
                    Game.Connection.Unequip(EquipmentSlot.Shield);
            }

            // Spacebar — assail (repeats while held)
            SpacebarTimer -= elapsedMs;

            if (input.IsKeyHeld(Keys.Space) && (SpacebarTimer <= 0))
            {
                Game.Connection.Spacebar();
                SpacebarTimer = SPACEBAR_INTERVAL_MS;
                Pathfinding.Clear();
            } else if (!input.IsKeyHeld(Keys.Space))
                SpacebarTimer = 0;

            // J — flash group member highlighting (1000ms, gated while pending or active)
            if (input.WasKeyPressed(Keys.J) && !GroupHighlightRequested && (GroupHighlightedIds.Count == 0))
            {
                GroupHighlightRequested = true;
                Game.Connection.RequestSelfProfile();
            }

            // B — pick up item from under player, or from the tile in front
            if (input.WasKeyPressed(Keys.B))
                TryPickupItem();

            // T — town map toggle
            if (input.WasKeyPressed(Keys.T) && player is not null)
                TownMap.Show(CurrentMapId, player.TileX, player.TileY);

            // Emote hotkeys: Ctrl/Alt/Ctrl+Alt + number row → body animations 9-44
            if (HandleEmoteHotkeys(input))
                return;

            // Slot hotkeys: 1-9, 0, -, = → use slot 1-12 of the active panel
            HandleSlotHotkeys(input);

            // Click handling — left click in viewport area
            if (input.WasLeftButtonPressed)
            {
                // Ctrl+click — context menu on aisling entities
                if (input.IsKeyHeld(Keys.LeftControl) || input.IsKeyHeld(Keys.RightControl))
                    HandleCtrlClick(input.MouseX, input.MouseY);

                // Alt+click on self — open self profile
                else if (input.IsKeyHeld(Keys.LeftAlt) || input.IsKeyHeld(Keys.RightAlt))
                {
                    if (hoverEntity is not null && (hoverEntity.Id == Game.Connection.AislingId))
                    {
                        SelfProfileRequested = true;
                        Game.Connection.RequestSelfProfile();
                    } else
                        HandleWorldClick(input.MouseX, input.MouseY);
                } else
                    HandleWorldClick(input.MouseX, input.MouseY);
            }

            // Right-click — pathfind to clicked tile
            if (input.WasRightButtonPressed)
                HandleWorldRightClick(input.MouseX, input.MouseY);

            // Player movement — each WasKeyPressed (initial press + OS key repeat) goes through:
            // - Idle → walk (with client-side prediction) or turn immediately
            // - Walking at >= 75% → queue one walk
            Direction? direction = null;

            if (input.WasKeyPressed(Keys.Up))
                direction = Direction.Up;
            else if (input.WasKeyPressed(Keys.Right))
                direction = Direction.Right;
            else if (input.WasKeyPressed(Keys.Down))
                direction = Direction.Down;
            else if (input.WasKeyPressed(Keys.Left))
                direction = Direction.Left;

            // Arrow key press cancels any active pathfinding
            if (direction.HasValue)
                Pathfinding.Clear();

            if (direction.HasValue && player is not null && !movementHandled)
            {
                if (player.AnimState == EntityAnimState.Idle)
                {
                    if (player.Direction != direction.Value)
                    {
                        Game.Connection.Turn(direction.Value);
                        player.Direction = direction.Value;
                    } else
                    {
                        PredictAndWalk(player, direction.Value);
                        QueuedWalkDirection = null;
                    }
                } else if (player.AnimState == EntityAnimState.Walking)
                {
                    // Must match AdvanceWalk's totalDuration formula: frameCount * interval
                    var totalDuration = Math.Max(1f, player.AnimFrameCount * player.AnimFrameIntervalMs);
                    var progress = player.AnimElapsedMs / totalDuration;

                    if (progress >= WALK_QUEUE_THRESHOLD)
                        QueuedWalkDirection = direction.Value;
                }
            }
        }

        ((UIPanel)WorldHud).Update(gameTime, input);

        // Track highlighted entity when dragging a panel item over the world viewport
        UpdateDragHighlight(input);

    }

    /// <summary>
    ///     If the element is visible, gives it input focus (calls Update) and signals the caller to return.
    /// </summary>
    private static bool UpdateExclusive(UIElement element, GameTime gameTime, InputBuffer input)
    {
        if (!element.Visible)
            return false;

        element.Update(gameTime, input);

        return true;
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
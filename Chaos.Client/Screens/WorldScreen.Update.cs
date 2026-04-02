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
    private static readonly string[] TEST_TEXTS =
    [
        "Hello adventurer, what brings you here today?",
        "The darkness grows stronger in the west...",
        "Would you like to hear a tale of old?",
        "I have wares if you have coin.",
        "Be careful out there, the wolves are restless.",
        "Welcome to my shop! Take a look around."
    ];

    private static readonly string[] TEST_OPTIONS =
    [
        "Buy supplies",
        "Sell items",
        "Ask about the dungeon",
        "Request a quest",
        "Learn a skill",
        "Hear a rumor",
        "Trade gems",
        "Repair equipment",
        "Check the board",
        "Say goodbye"
    ];

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

        // Toggle hotkeys — processed before overlay blocks so they work when the toggled panel is the active overlay
        var toggleCloseHandled = false;

        if (!WorldHud.ChatInput.IsFocused && !UITextBox.IsAnyFocused && !NpcSession.Visible)
        {
            if (input.WasKeyPressed(Keys.Q) && MainOptions.Visible)
            {
                SettingsDialog.Hide();
                MacroMenu.Hide();
                FriendsList.Hide();
                MainOptions.SlideClose();
                toggleCloseHandled = true;
            } else if (input.WasKeyPressed(Keys.W)
                       && (BoardList.Visible
                           || ArticleList.Visible
                           || ArticleRead.Visible
                           || ArticleSend.Visible
                           || MailList.Visible
                           || MailRead.Visible
                           || MailSend.Visible))
            {
                if (BoardList.Visible)
                    BoardList.SlideClose();
                else
                    WorldState.Board.CloseSession();

                toggleCloseHandled = true;
            } else if (input.WasKeyPressed(Keys.E) && WorldList.Visible)
            {
                WorldList.SlideClose();
                toggleCloseHandled = true;
            } else if (input.WasKeyPressed(Keys.R) && SocialStatusPicker.Visible)
            {
                SocialStatusPicker.Visible = false;

                if (WorldHud.EmoteButton is not null)
                    WorldHud.EmoteButton.IsSelected = false;

                toggleCloseHandled = true;
            }
        }

        // Overlay panels get first priority for input
        if (NpcSession.Visible)
        {
            NpcSession.Update(gameTime, input);

            // Update tooltip position to follow cursor (merchant item hover)
            if (ItemTooltip.Visible)
            {
                var rightX = input.MouseX + 15;

                ItemTooltip.X = (rightX + ItemTooltip.Width) <= ChaosGame.VIRTUAL_WIDTH ? rightX : input.MouseX - ItemTooltip.Width;

                ItemTooltip.Y = Math.Clamp(input.MouseY + 15, 0, ChaosGame.VIRTUAL_HEIGHT - ItemTooltip.Height);
            }

            return;
        }

        if (MainOptions.Visible)
        {
            // Sub-panels slide out from MainOptions — update them alongside it
            // If a sub-panel is open, it gets input priority (consumes Escape before MainOptions)
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

        if (SettingsDialog.Visible)
        {
            SettingsDialog.Update(gameTime, input);

            return;
        }

        if (ChantEdit.Visible)
        {
            ChantEdit.Update(gameTime, input);

            return;
        }

        if (MacroMenu.Visible)
        {
            MacroMenu.Update(gameTime, input);

            return;
        }

        if (HotkeyHelp.Visible)
        {
            HotkeyHelp.Update(gameTime, input);

            return;
        }

        if (GroupPanel.Visible)
        {
            GroupPanel.Update(gameTime, input);

            return;
        }

        if (WorldList.Visible)
        {
            WorldList.Update(gameTime, input);

            return;
        }

        if (FriendsList.Visible)
        {
            FriendsList.Update(gameTime, input);

            return;
        }

        if (WorldHud.Prompt.Visible)
        {
            WorldHud.Prompt.Update(gameTime, input);

            return;
        }

        if (GoldDrop.Visible)
        {
            GoldDrop.Update(gameTime, input);

            return;
        }

        if (Exchange.Visible)
        {
            ((UIPanel)WorldHud).Update(gameTime, input);
            Exchange.Update(gameTime, input);

            // Allow setting gold by clicking MyMoney area — shows gold amount popup
            if (input.WasLeftButtonPressed && Exchange.IsMyMoneyClicked(input.MouseX, input.MouseY))
            {
                ExchangeAmountSlot = null;
                GoldDrop.ShowForTarget(Exchange.OtherUserId, 0, 0);
            }

            return;
        }

        if (DeleteConfirm.Visible)
        {
            DeleteConfirm.Update(gameTime, input);

            return;
        }

        if (ArticleSend.Visible)
        {
            ArticleSend.Update(gameTime, input);

            return;
        }

        if (MailSend.Visible)
        {
            MailSend.Update(gameTime, input);

            return;
        }

        if (ArticleRead.Visible)
        {
            ArticleRead.Update(gameTime, input);

            return;
        }

        if (MailRead.Visible)
        {
            MailRead.Update(gameTime, input);

            return;
        }

        if (ArticleList.Visible)
        {
            ArticleList.Update(gameTime, input);

            return;
        }

        if (MailList.Visible)
        {
            MailList.Update(gameTime, input);

            return;
        }

        if (BoardList.Visible)
        {
            BoardList.Update(gameTime, input);

            return;
        }

        if (TextPopup.Visible)
        {
            TextPopup.Update(gameTime, input);

            return;
        }

        if (Notepad.Visible)
        {
            Notepad.Update(gameTime, input);

            return;
        }

        // Modal popup check — find the topmost visible modal and give it exclusive input.
        // All other controls still update (animations, cooldowns) but with suppressed input.
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

        if (StatusBook.Visible)
        {
            // HUD panels still receive input while the status book is open (drag-and-drop)
            ((UIPanel)WorldHud).Update(gameTime, input);
            StatusBook.Update(gameTime, input);

            return;
        }

        if (OtherProfile.Visible)
        {
            OtherProfile.Update(gameTime, input);

            return;
        }

        if (WorldMap.Visible)
        {
            WorldMap.Update(gameTime, input);

            return;
        }

        if (SocialStatusPicker.Visible)
        {
            SocialStatusPicker.Update(gameTime, input);

            return;
        }

        if (AislingPopup.Visible)
        {
            AislingPopup.Update(gameTime, input);

            return;
        }

        // Context menu gets priority when visible
        if (ContextMenu.Visible)
        {
            ContextMenu.Update(gameTime, input);

            return;
        }

        // Track which entity the mouse is hovering over (for name tags, tint highlight, targeting)
        var hoverEntity = GetEntityAtScreen(input.MouseX, input.MouseY);

        var newHoveredId = hoverEntity is not null && hoverEntity.Type is ClientEntityType.Aisling or ClientEntityType.Creature
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
                if (hoverEntity is not null && hoverEntity.Type is ClientEntityType.Aisling or ClientEntityType.Creature)
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

                if (c is 'a' or 'A')
                    Chat.TransitionIgnoreAdd();
                else if (c is 'd' or 'D')
                    Chat.TransitionIgnoreRemove();
                else if (c == '?')
                {
                    Game.Connection.SendIgnoreRequest();
                    Chat.Unfocus();
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
                var prefix = WorldHud.ChatInput.Prefix;

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
            else if (!toggleCloseHandled && input.WasKeyPressed(Keys.Q))
                MainOptions.Show();
            else if (!toggleCloseHandled && input.WasKeyPressed(Keys.W))
                Game.Connection.SendBoardInteraction(BoardRequestType.BoardList);
            else if (!toggleCloseHandled && input.WasKeyPressed(Keys.E))
                Game.Connection.RequestWorldList();
            else if (!toggleCloseHandled && input.WasKeyPressed(Keys.R))
                ToggleSocialStatusPicker();

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
                GroupPanel.Show();

            // F9 — ignore list management
            if (input.WasKeyPressed(Keys.F9))
                Chat.FocusIgnore();

            // F10 — friends list
            if (input.WasKeyPressed(Keys.F10))
                FriendsList.Show();

            // F11 — debug dialog test
            if (input.WasKeyPressed(Keys.F11))
                FireTestDialog();

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

        // Update tooltip position to follow cursor
        if (ItemTooltip.Visible)
        {
            var rightX = input.MouseX + 15;

            ItemTooltip.X = (rightX + ItemTooltip.Width) <= ChaosGame.VIRTUAL_WIDTH ? rightX : input.MouseX - ItemTooltip.Width;

            ItemTooltip.Y = Math.Clamp(input.MouseY + 15, 0, ChaosGame.VIRTUAL_HEIGHT - ItemTooltip.Height);
        }

        // Track highlighted entity when dragging a panel item over the world viewport
        UpdateDragHighlight(input);

        // Update overlays — tick timers, update screen positions, remove expired
        if (MapFile is not null)
            Overlays.Update(
                gameTime,
                input,
                Camera,
                MapFile.Height);

        // Darkness overlay state — must update before Draw
        if (DarknessRenderer.IsActive)
        {
            DarknessRenderer.SetLightSources(GatherLightSources());
            DarknessRenderer.Update(Camera, WorldHud.ViewportBounds);
        }
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

    private void FireTestDialog()
        => TextPopup.Show(
            "This is a test NonScrollWindow popup.\n\nNonScrollWindow and ScrollWindow are identical in the original client — same dialog frame, scrollbar, and close button.\n\nLine 1\nLine 2\nLine 3\nLine 4\nLine 5\nLine 6\nLine 7\nLine 8\nLine 9\nLine 10\nLine 11\nLine 12\nLine 13\nLine 14\nLine 15\nLine 16\nLine 17\nLine 18\nLine 19\nLine 20",
            PopupStyle.NonScroll);

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
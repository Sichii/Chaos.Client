#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.World.Hud;
using Chaos.Client.Controls.World.Hud.Panel;
using Chaos.Client.Controls.World.Hud.Panel.Slots;
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Chaos.Client.Extensions;
using Chaos.Client.Models;
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
    #region UI Event Handlers
    //--- inventory ---

    private void HandleInventorySlotClicked(byte slot)
    {
        Game.Connection.UseItem(slot);
        HoveredInventorySlot = null;
        ItemTooltip.Hide();
    }

    private void HandleInventoryHoverEnter(PanelSlot slot)
    {
        HoveredInventorySlot = slot;

        ItemTooltip.Show(
            slot.SlotName ?? string.Empty,
            slot.CurrentDurability,
            slot.MaxDurability,
            Game.Input.MouseX + 15,
            Game.Input.MouseY + 15);
    }

    private void HandleInventoryHoverExit()
    {
        HoveredInventorySlot = null;
        ItemTooltip.Hide();
    }

    /// <summary>
    ///     Returns true if the point is over any visible popup window, preventing drops from passing through.
    /// </summary>
    private bool IsOverVisiblePopup(int screenX, int screenY)
    {
        if (Root is null)
            return false;

        foreach (var child in Root.Children)
        {
            if (child is not UIPanel { Visible: true, IsPassThrough: false } panel)
                continue;

            if ((panel == SmallHud) || (panel == LargeHud))
                continue;

            if (panel.ContainsPoint(screenX, screenY))
                return true;
        }

        return false;
    }

    private void HandleInventoryDropInViewport(byte slot, int mouseX, int mouseY)
    {
        //dropped onto the exchange window — add item to exchange
        if ((slot != 0) && Exchange.Visible && Exchange.ContainsPoint(mouseX, mouseY))
        {
            Game.Connection.SendExchangeInteraction(ExchangeRequestType.AddItem, Exchange.OtherUserId, slot);

            return;
        }

        //dropped onto an equipment slot — equip the item
        if ((slot != 0) && StatusBook.Visible && StatusBook.ContainsEquipmentSlotPoint(mouseX, mouseY))
        {
            Game.Connection.UseItem(slot);

            return;
        }

        //block drops that land on any visible popup window
        if (IsOverVisiblePopup(mouseX, mouseY))
            return;

        var viewport = WorldHud.ViewportBounds;

        //only drop if released within the world viewport
        if ((mouseX < viewport.X)
            || (mouseX >= (viewport.X + viewport.Width))
            || (mouseY < viewport.Y)
            || (mouseY >= (viewport.Y + viewport.Height)))
            return;

        if (MapFile is null)
            return;

        //check if dropped on an entity (give item/gold to npc/player) — skip self (drop on ground instead)
        (var tileX, var tileY) = ScreenToTile(mouseX, mouseY);
        var entity = GetEntityAtScreen(mouseX, mouseY);

        var droppedOnEntity = entity?.Type is ClientEntityType.Aisling or ClientEntityType.Creature
                              && (entity.Id != Game.Connection.AislingId);

        //gold bag (slot 0) — show the gold amount popup
        if (slot == 0)
        {
            GoldDrop.CenterVerticallyIn(viewport);
            GoldDrop.ShowForTarget(droppedOnEntity ? entity!.Id : null, tileX, tileY);

            return;
        }

        if (droppedOnEntity)
        {
            var itemSlot = WorldState.Inventory.GetSlot(slot);
            Game.Connection.DropItemOnCreature(slot, entity!.Id, itemSlot.Stackable ? (byte)0 : (byte)1);

            return;
        }

        //stackable items — prompt for count before dropping
        var invSlot = WorldState.Inventory.GetSlot(slot);

        if (invSlot.Stackable)
        {
            var capturedSlot = slot;
            var capturedX = tileX;
            var capturedY = tileY;

            WorldHud.ChatInput.ShowPrompt(
                $"Number of items to drop [ 0 - {(int)invSlot.Count} ]: ",
                12,
                text =>
                {
                    if (int.TryParse(text, out var count) && (count > 0))
                        Game.Connection.DropItem(capturedSlot, capturedX, capturedY, count);
                });

            return;
        }

        Game.Connection.DropItem(slot, tileX, tileY);
    }

    //--- skills / spells ---

    private void HandleSkillSlotClicked(byte slot)
    {
        var skillSlot = WorldHud.SkillBook.GetSkillSlot(slot) ?? WorldHud.SkillBookAlt.GetSkillSlot(slot);

        if (skillSlot is not null && (skillSlot.CooldownPercent > 0))
            return;

        //send chant line if one is set for this skill
        if (skillSlot is not null && !string.IsNullOrEmpty(skillSlot.Chant))
            Game.Connection.SendChant(skillSlot.Chant);

        Game.Connection.UseSkill(slot);
    }

    private void HandleSpellSlotClicked(byte slot)
    {
        //determine which panel the slot came from
        var spellSlot = WorldHud.ActiveTab switch
        {
            HudTab.Spells    => WorldHud.SpellBook.GetSpellSlot(slot),
            HudTab.SpellsAlt => WorldHud.SpellBookAlt.GetSpellSlot(slot),
            _                => WorldHud.SpellBook.GetSpellSlot(slot) ?? WorldHud.SpellBookAlt.GetSpellSlot(slot)
        };

        if (spellSlot is null || string.IsNullOrEmpty(spellSlot.AbilityName))
            return;

        if (spellSlot.CooldownPercent > 0)
            return;

        //notarget spells cast immediately (no cast mode)
        if (spellSlot.SpellType == SpellType.NoTarget)
        {
            if (spellSlot.CastLines == 0)
                Game.Connection.UseSpell(slot);
            else
            {
                //notarget with lines: begin chant sequence targeting self
                CastingSystem.BeginTargeting(spellSlot);

                var player = WorldState.GetPlayerEntity();

                CastingSystem.SelectTarget(
                    Game.Connection.AislingId,
                    player?.TileX ?? 0,
                    player?.TileY ?? 0,
                    Game.Connection);
            }

            return;
        }

        //enter cast mode — wait for target selection
        CastingSystem.BeginTargeting(spellSlot);
    }

    private void HandleSpellSlotDropped(byte slot, int mouseX, int mouseY)
    {
        if (IsOverVisiblePopup(mouseX, mouseY))
            return;

        var entity = GetEntityAtScreen(mouseX, mouseY);

        if (entity?.Type is not (ClientEntityType.Aisling or ClientEntityType.Creature))
            return;

        HandleSpellSlotClicked(slot);

        if (CastingSystem.IsTargeting)
            CastingSystem.SelectTarget(
                entity.Id,
                entity.TileX,
                entity.TileY,
                Game.Connection);
    }

    //--- hotkeys ---

    private static readonly Keys[] EmoteKeys =
    [
        Keys.D1,
        Keys.D2,
        Keys.D3,
        Keys.D4,
        Keys.D5,
        Keys.D6,
        Keys.D7,
        Keys.D8,
        Keys.D9,
        Keys.D0,
        Keys.OemMinus
    ];

    //ctrl+key emotes: 9-17 then 21-22 (skips 18-20 which don't exist in bodyanimation)
    private static readonly BodyAnimation[] CtrlEmotes =
    [
        BodyAnimation.Smile,
        BodyAnimation.Cry,
        BodyAnimation.Frown,
        BodyAnimation.Wink,
        BodyAnimation.Surprise,
        BodyAnimation.Tongue,
        BodyAnimation.Pleasant,
        BodyAnimation.Snore,
        BodyAnimation.Mouth,
        BodyAnimation.BlowKiss,
        BodyAnimation.Wave
    ];

    private bool HandleEmoteHotkey(KeyDownEvent e)
    {
        if (e is { Ctrl: false, Alt: false })
            return false;

        var keyIndex = Array.IndexOf(EmoteKeys, e.Key);

        if (keyIndex < 0)
            return false;

        BodyAnimation bodyAnimation;

        if (e is { Ctrl: true, Alt: false })
            bodyAnimation = CtrlEmotes[keyIndex];
        else if (e is { Ctrl: true, Alt: true })
            bodyAnimation = (BodyAnimation)(23 + keyIndex);
        else
            bodyAnimation = (BodyAnimation)(34 + keyIndex);

        Game.Connection.SendEmote(bodyAnimation);
        e.Handled = true;

        return true;
    }

    private bool HandleSlotHotkey(KeyDownEvent e)
    {
        var slot = e.Key switch
        {
            Keys.D1       => 1,
            Keys.D2       => 2,
            Keys.D3       => 3,
            Keys.D4       => 4,
            Keys.D5       => 5,
            Keys.D6       => 6,
            Keys.D7       => 7,
            Keys.D8       => 8,
            Keys.D9       => 9,
            Keys.D0       => 10,
            Keys.OemMinus => 11,
            Keys.OemPlus  => 12,
            _             => -1
        };

        if (slot < 0)
            return false;

        var byteSlot = (byte)slot;

        switch (WorldHud.ActiveTab)
        {
            case HudTab.Inventory:
                Game.Connection.UseItem(byteSlot);

                break;

            case HudTab.Skills:
                HandleSkillSlotClicked(byteSlot);

                break;

            case HudTab.SkillsAlt:
                HandleSkillSlotClicked((byte)(byteSlot + 36));

                break;

            case HudTab.Spells:
                HandleSpellSlotClicked(byteSlot);

                break;

            case HudTab.SpellsAlt:
                HandleSpellSlotClicked((byte)(byteSlot + 36));

                break;

            case HudTab.Tools:
                //todo: left half = world skills (slots 73-78), right half = world spells (slots 73-78)
                return false;

            case HudTab.Chat:
            case HudTab.MessageHistory:
            {
                var macroText = MacrosList.GetMacroValue(slot - 1);

                if (macroText.Length > 0)
                {
                    WorldHud.ChatInput.Focus($"{WorldHud.PlayerName}: ", Color.White);
                    WorldHud.ChatInput.SetText(macroText, macroText.Length);
                }

                break;
            }

            default:
                return false;
        }

        e.Handled = true;

        return true;
    }

    //--- chant editing ---

    private void WireAbilityRightClicks(PanelBase panel)
    {
        for (byte i = 1; i <= 36; i++)
            if (panel.GetSlotControl(i) is AbilitySlotControl ability)
                ability.OnRightClick += OpenChantEdit;
    }

    private void OpenChantEdit(byte slot)
    {
        //determine which panel this slot belongs to based on active tab
        AbilitySlotControl? abilitySlot = WorldHud.ActiveTab switch
        {
            HudTab.Skills    => WorldHud.SkillBook.GetSkillSlot(slot),
            HudTab.SkillsAlt => WorldHud.SkillBookAlt.GetSkillSlot(slot),
            HudTab.Spells    => WorldHud.SpellBook.GetSpellSlot(slot),
            HudTab.SpellsAlt => WorldHud.SpellBookAlt.GetSpellSlot(slot),
            _                => null
        };

        if (abilitySlot is null || string.IsNullOrEmpty(abilitySlot.AbilityName))
            return;

        var isSpell = abilitySlot is SpellSlot;

        string[] currentChants;
        int lineCount;

        if (abilitySlot is SpellSlot spell)
        {
            currentChants = spell.Chants;
            lineCount = spell.CastLines;
        } else if (abilitySlot is SkillSlot skill)
        {
            currentChants = [skill.Chant];
            lineCount = 1;
        } else
            return;

        ChantEdit.Show(
            slot,
            abilitySlot.AbilityName,
            abilitySlot.AbilityLevel ?? string.Empty,
            abilitySlot.NormalTexture,
            currentChants,
            lineCount,
            isSpell);
    }

    private void HandleChantSet(byte slot, string[] chantLines, bool isSpell)
    {
        if (isSpell)
        {
            foreach (var panel in new[]
                     {
                         WorldHud.SpellBook,
                         WorldHud.SpellBookAlt
                     })
            {
                var spellSlot = panel.GetSpellSlot(slot);

                if (spellSlot is null)
                    continue;

                for (var i = 0; i < Math.Min(chantLines.Length, spellSlot.Chants.Length); i++)
                    spellSlot.Chants[i] = chantLines[i];
            }

            SaveSpellChants();
            WorldState.ReloadChants();
        } else
        {
            foreach (var panel in new[]
                     {
                         WorldHud.SkillBook,
                         WorldHud.SkillBookAlt
                     })
            {
                var skillSlot = panel.GetSkillSlot(slot);

                if (skillSlot is null)
                    continue;

                skillSlot.Chant = chantLines.Length > 0 ? chantLines[0] : string.Empty;
            }

            SaveSkillChants();
            WorldState.ReloadChants();
        }
    }

    //--- cache / persistence helpers ---

    private void LoadPlayerFamilyList()
    {
        var family = DataContext.LocalPlayerSettings.LoadFamilyList();
        StatusBook.SetFamilyMembers(family);
        WorldList.SetFamilyNames(family);
    }

    private void SavePlayerFamilyList()
    {
        var family = StatusBook.GetFamilyMembers();

        if (family is not null)
        {
            DataContext.LocalPlayerSettings.SaveFamilyList(family);
            WorldList.SetFamilyNames(family);
        }
    }

    private void LoadPlayerFriendList()
    {
        var names = DataContext.LocalPlayerSettings.LoadFriendList();

        var entries = names.Select(n => new FriendEntry(n, false))
                           .ToList();
        FriendsList.SetFriends(entries);
        WorldList.SetFriendNames(names);
    }

    private void SavePlayerFriendList()
    {
        var names = FriendsList.GetFriendNames();
        DataContext.LocalPlayerSettings.SaveFriendList(names);
        WorldList.SetFriendNames(names);
    }

    private void LoadPlayerMacros()
    {
        var macros = DataContext.LocalPlayerSettings.LoadMacros();
        MacrosList.SetMacros(macros);
    }

    private void SaveSkillChants()
    {
        var entries = new List<SkillChantEntry>();

        for (byte i = 1; i <= 89; i++)
        {
            var slot = WorldHud.SkillBook.GetSkillSlot(i);

            if (slot is null || string.IsNullOrEmpty(slot.AbilityName))
                continue;

            entries.Add(
                new SkillChantEntry
                {
                    Name = slot.AbilityName,
                    Chant = slot.Chant
                });
        }

        DataContext.LocalPlayerSettings.SaveSkillChants(entries);
    }

    private void SaveSpellChants()
    {
        var entries = new List<SpellChantEntry>();

        for (byte i = 1; i <= 89; i++)
        {
            var slot = WorldHud.SpellBook.GetSpellSlot(i);

            if (slot is null || string.IsNullOrEmpty(slot.AbilityName))
                continue;

            var entry = new SpellChantEntry
            {
                Name = slot.AbilityName
            };
            Array.Copy(slot.Chants, entry.Chants, 10);
            entries.Add(entry);
        }

        DataContext.LocalPlayerSettings.SaveSpellChants(entries);
    }
    #endregion

    #region Root Event Handlers

    /// <summary>
    ///     Handles keyboard input that bubbles up to the root panel (no focused element consumed it).
    ///     Contains all game hotkeys, chat focus, movement, emotes, and slot actions.
    /// </summary>
    private void OnRootKeyDown(KeyDownEvent e)
    {
        //casting mode: escape cancels targeting
        if (CastingSystem.IsTargeting && (e.Key == Keys.Escape))
        {
            CastingSystem.Reset();
            e.Handled = true;

            return;
        }

        //enter — toggle chat focus
        if (e.Key == Keys.Enter)
        {
            if (!WorldHud.ChatInput.IsFocused)
                WorldHud.ChatInput.Focus($"{WorldHud.PlayerName}: ", Color.White);

            e.Handled = true;

            return;
        }

        //q/w/e/r toggle group — must be above the stack guard because these panels
        //use the control stack themselves and need to toggle while open
        if (e.Key == Keys.Q)
        {
            ForceCloseOtherTogglePanels(Keys.Q);

            if (MainOptions.Visible)
            {
                SettingsDialog.Hide();
                MacrosList.Hide();
                FriendsList.Hide();
                MainOptions.SlideClose();
            } else
            {
                WorldHud.OptionButton?.IsSelected = true;

                MainOptions.Show();
            }

            e.Handled = true;

            return;
        }

        if (e.Key == Keys.W)
        {
            ForceCloseOtherTogglePanels(Keys.W);

            if (IsAnyBoardPanelVisible())
            {
                if (BoardList.Visible)
                    BoardList.SlideClose();
                else
                    WorldState.Board.CloseSession();
            } else
            {
                WorldHud.BulletinButton?.IsSelected = true;

                Game.Connection.SendBoardInteraction(BoardRequestType.BoardList);
            }

            e.Handled = true;

            return;
        }

        if (e.Key == Keys.E)
        {
            ForceCloseOtherTogglePanels(Keys.E);

            if (WorldList.Visible)
                WorldList.SlideClose();
            else
            {
                WorldHud.UsersButton?.IsSelected = true;

                Game.Connection.RequestWorldList();
            }

            e.Handled = true;

            return;
        }

        if (e.Key == Keys.R)
        {
            ForceCloseOtherTogglePanels(Keys.R);
            ToggleSocialStatusPicker();

            e.Handled = true;

            return;
        }

        //stack guard: suppress all game hotkeys when a popup is active
        if (Game.Dispatcher.ControlStackCount > 0)
            return;

        if ((e.Key == Keys.T) && TownMapControl.Visible)
        {
            TownMapControl.Hide();
            e.Handled = true;

            return;
        }

        //shout hotkey (shift+1)
        if (e is { Key: Keys.D1, Shift: true })
        {
            WorldHud.ChatInput.Focus($"{WorldHud.PlayerName}! ", Color.Yellow);
            e.Handled = true;

            return;
        }

        //whisper hotkey (shift+")
        if (e is { Key: Keys.OemQuotes, Shift: true })
        {
            WorldHud.ChatInput.FocusWhisper();
            e.Handled = true;

            return;
        }

        //tab panel switching — blocked while dragging the orange bar
        if (!WorldHud.IsOrangeBarDragging)
        {
            if (e.Key == Keys.A)
            {
                if (e.Shift)
                {
                    if (WorldHud.ActiveTab != HudTab.Inventory)
                        WorldHud.ShowTab(HudTab.Inventory);

                    WorldHud.ToggleExpand();
                } else if (WorldHud.ActiveTab == HudTab.Inventory)
                {
                    SelfProfileRequested = true;
                    SelfProfileRequestedTab = StatusBookTab.Equipment;
                    Game.Connection.RequestSelfProfile();
                } else
                    WorldHud.ShowTab(HudTab.Inventory);

                e.Handled = true;

                return;
            }

            if (e.Key == Keys.S)
            {
                var alt = e.Shift || (!ClientSettings.UseShiftKeyForAltPanels && (WorldHud.ActiveTab == HudTab.Skills));
                WorldHud.ShowTab(alt ? HudTab.SkillsAlt : HudTab.Skills);
                e.Handled = true;

                return;
            }

            if (e.Key == Keys.D)
            {
                var alt = e.Shift || (!ClientSettings.UseShiftKeyForAltPanels && (WorldHud.ActiveTab == HudTab.Spells));
                WorldHud.ShowTab(alt ? HudTab.SpellsAlt : HudTab.Spells);
                e.Handled = true;

                return;
            }

            if (e.Key == Keys.F)
            {
                if (e.Shift)
                {
                    WorldHud.ShowTab(HudTab.MessageHistory);
                    WorldHud.MessageHistory.ScrollToBottom();
                } else
                {
                    WorldHud.ShowTab(HudTab.Chat);
                    WorldHud.ChatDisplay.ScrollToBottom();
                }

                e.Handled = true;

                return;
            }

            if (e.Key == Keys.G)
            {
                WorldHud.ShowTab(e.Shift ? HudTab.ExtendedStats : HudTab.Stats);
                e.Handled = true;

                return;
            }

            if (e.Key == Keys.H)
            {
                WorldHud.ShowTab(HudTab.Tools);
                e.Handled = true;

                return;
            }
        }

        //tab — toggle tab map overlay
        if (e.Key == Keys.Tab)
        {
            TabMapVisible = !TabMapVisible;
            e.Handled = true;

            return;
        }

        //pageup/pagedown — tab map zoom
        if (TabMapVisible)
        {
            if (e.Key == Keys.PageUp)
            {
                TabMapRenderer.ZoomIn();
                e.Handled = true;

                return;
            }

            if (e.Key == Keys.PageDown)
            {
                TabMapRenderer.ZoomOut();
                e.Handled = true;

                return;
            }
        }

        //f1 — help merchant (server-side)
        if (e.Key == Keys.F1)
        {
            Game.Connection.ClickEntity(uint.MaxValue);
            e.Handled = true;

            return;
        }

        //f3 — macro menu
        if (e.Key == Keys.F3)
        {
            if (!SettingsDialog.Visible && !FriendsList.Visible)
                MacrosList.Show();

            e.Handled = true;

            return;
        }

        //f4 — settings
        if (e.Key == Keys.F4)
        {
            if (!MacrosList.Visible && !FriendsList.Visible)
                SettingsDialog.Show();

            e.Handled = true;

            return;
        }

        //f5 — refresh
        if (e.Key == Keys.F5)
        {
            Game.Connection.RequestRefresh();
            e.Handled = true;

            return;
        }

        //f7 — board list
        if (e.Key == Keys.F7)
        {
            Game.Connection.SendBoardInteraction(BoardRequestType.BoardList);
            e.Handled = true;

            return;
        }

        //f8 — unused (group panel moved to y key)

        //f9 — ignore list management (toggle)
        if (e.Key == Keys.F9)
        {
            if (WorldHud.ChatInput.Mode != ChatMode.None)
                WorldHud.ChatInput.Unfocus();
            else
                WorldHud.ChatInput.FocusIgnore();

            e.Handled = true;

            return;
        }

        //f10 — friends list
        if (e.Key == Keys.F10)
        {
            if (!MacrosList.Visible && !SettingsDialog.Visible)
                FriendsList.Show();

            e.Handled = true;

            return;
        }

        // — swap hud layout (small <-> large)
        if (e is { Key: Keys.OemQuestion, Shift: false })
        {
            SwapHudLayout();
            e.Handled = true;

            return;
        }

        //` — unequip weapon and shield
        if (e.Key == Keys.OemTilde)
        {
            if (WorldState.Equipment.GetSlot(EquipmentSlot.Weapon) is not null)
                Game.Connection.Unequip(EquipmentSlot.Weapon);

            if (WorldState.Equipment.GetSlot(EquipmentSlot.Shield) is not null)
                Game.Connection.Unequip(EquipmentSlot.Shield);

            e.Handled = true;

            return;
        }

        //j — flash group member highlighting (1000ms, gated while pending or active)
        if ((e.Key == Keys.J) && !GroupHighlightRequested && (GroupHighlightedIds.Count == 0))
        {
            GroupHighlightRequested = true;
            Game.Connection.RequestSelfProfile();
            e.Handled = true;

            return;
        }

        //b — pick up item from under player, or from the tile in front
        if (e.Key == Keys.B)
        {
            TryPickupItem();
            e.Handled = true;

            return;
        }

        //t — town map toggle
        if (e.Key == Keys.T)
        {
            if (TownMapControl.Visible)
                TownMapControl.Hide();
            else
            {
                var player = WorldState.GetPlayerEntity();

                if (player is not null)
                    TownMapControl.Show(CurrentMapId, player.TileX, player.TileY);
            }

            e.Handled = true;

            return;
        }

        //y — group panel (members tab)
        if (e.Key == Keys.Y)
        {
            GroupPanel.ShowMembers();
            e.Handled = true;

            return;
        }

        //emote hotkeys: ctrl/alt/ctrl+alt + number row
        if (HandleEmoteHotkey(e))
            return;

        //slot hotkeys: 1-9, 0, -, =
        if (HandleSlotHotkey(e))
            return;

        //player movement — arrow keys and zxcv
        Direction? direction = e.Key switch
        {
            Keys.Up    => Direction.Up,
            Keys.Right => Direction.Right,
            Keys.Down  => Direction.Down,
            Keys.Left  => Direction.Left,
            Keys.C     => Direction.Up,
            Keys.V     => Direction.Right,
            Keys.X     => Direction.Down,
            Keys.Z     => Direction.Left,
            _          => null
        };

        if (direction.HasValue)
        {
            Pathfinding.Clear();
            var player = WorldState.GetPlayerEntity();

            if (player is not null)
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
                    var totalDuration = Math.Max(1f, player.AnimFrameCount * player.AnimFrameIntervalMs);
                    var progress = player.AnimElapsedMs / totalDuration;

                    if (progress >= WALK_QUEUE_THRESHOLD)
                        QueuedWalkDirection = direction.Value;
                }
            }

            e.Handled = true;
        }
    }

    /// <summary>
    ///     Handles mouse clicks that bubble up to the root panel (no child element consumed them).
    ///     Contains cast-mode target selection, Ctrl/Alt-click, world click, and right-click pathfinding.
    /// </summary>
    private void OnRootClick(ClickEvent e)
    {

        //exchange gold-click coordination — clicking the money label opens the gold amount popup
        if (Exchange.Visible && (e.Button == MouseButton.Left) && Exchange.IsMyMoneyClicked(e.ScreenX, e.ScreenY))
        {
            GoldDrop.ShowForTarget(Exchange.OtherUserId, 0, 0);
            e.Handled = true;

            return;
        }

        //cast mode — target selection or cancel
        if (CastingSystem.IsTargeting)
        {
            if (e.Button == MouseButton.Left)
            {
                var hoverEntity = GetEntityAtScreen(e.ScreenX, e.ScreenY);

                if (hoverEntity?.Type is ClientEntityType.Aisling or ClientEntityType.Creature)
                    CastingSystem.SelectTarget(
                        hoverEntity.Id,
                        hoverEntity.TileX,
                        hoverEntity.TileY,
                        Game.Connection);
                else
                    CastingSystem.Reset();

                e.Handled = true;
            }

            return;
        }

        if (e.Button == MouseButton.Left)
        {
            //ctrl+click — context menu on aisling entities
            if (e.Ctrl)
            {
                HandleCtrlClick(e.ScreenX, e.ScreenY);
                e.Handled = true;

                return;
            }

            //alt+click on self — open self profile
            if (e.Alt)
            {
                var hoverEntity = GetEntityAtScreen(e.ScreenX, e.ScreenY);

                if (hoverEntity is not null && (hoverEntity.Id == Game.Connection.AislingId))
                {
                    SelfProfileRequested = true;
                    Game.Connection.RequestSelfProfile();
                } else
                    HandleWorldClick(e.ScreenX, e.ScreenY);

                e.Handled = true;

                return;
            }

            HandleWorldClick(e.ScreenX, e.ScreenY);
            e.Handled = true;
        } else if (e.Button == MouseButton.Right)
        {
            if (e.Shift)
                HandleShiftRightClick(e.ScreenX, e.ScreenY);
            else if (!e.Ctrl)
                HandleWorldRightClick(e.ScreenX, e.ScreenY);

            e.Handled = true;
        }
    }

    /// <summary>
    ///     Handles double-click events that bubble up to the root panel.
    ///     Left double-click: interact with entities (pickup ground items, click NPC/aisling).
    ///     Right double-click: follow and assail entity.
    ///     Uses TileClickTracker same-tile guard since the dispatcher only checks same-element (Root),
    ///     not same-tile.
    /// </summary>
    private void OnRootDoubleClick(DoubleClickEvent e)
    {
        if (MapFile is null)
            return;

        var viewport = WorldHud.ViewportBounds;

        if ((e.ScreenX < viewport.X)
            || (e.ScreenX >= (viewport.X + viewport.Width))
            || (e.ScreenY < viewport.Y)
            || (e.ScreenY >= (viewport.Y + viewport.Height)))
            return;

        (var tileX, var tileY) = ScreenToTile(e.ScreenX, e.ScreenY);

        if (e.Button == MouseButton.Left)
        {
            var sameTile = LeftClickTracker.Click(tileX, tileY);

            if (!sameTile)
                return;

            //shift+doubleclick bypasses hitboxes and only picks up ground items
            if (e.Shift)
            {
                var groundItem = WorldState.GetGroundItemAt(tileX, tileY);

                if (groundItem is not null)
                {
                    var firstEmptySlot = WorldState.Inventory.GetFirstEmptySlot();
                    Game.Connection.PickupItem(groundItem.TileX, groundItem.TileY, firstEmptySlot);
                }
            } else
            {
                var entity = GetEntityAtScreen(e.ScreenX, e.ScreenY);

                if (entity is not null)
                {
                    if (entity.Type == ClientEntityType.GroundItem)
                    {
                        var firstEmptySlot = WorldState.Inventory.GetFirstEmptySlot();
                        Game.Connection.PickupItem(entity.TileX, entity.TileY, firstEmptySlot);
                    } else if ((entity.Type != ClientEntityType.Aisling) || ClientSettings.EnableProfileClick)
                        Game.Connection.ClickEntity(entity.Id);
                }
            }

            e.Handled = true;
        } else if (e.Button == MouseButton.Right)
        {
            if (MapPathfinder is null)
                return;

            var player = WorldState.GetPlayerEntity();

            if (player is null)
                return;

            tileX = Math.Clamp(tileX, 0, MapFile.Width - 1);
            tileY = Math.Clamp(tileY, 0, MapFile.Height - 1);

            var sameTile = RightClickTracker.Click(tileX, tileY);

            if (!sameTile)
                return;

            var entity = WorldState.GetEntityAt(tileX, tileY);

            if (entity?.Type is ClientEntityType.Aisling or ClientEntityType.Creature)
            {
                Pathfinding.SetEntityTarget(entity.Id);
                PathfindToEntity(player, entity);
            }

            e.Handled = true;
        }
    }

    /// <summary>
    ///     Tracks the cursor position during a drag so the ghost icon follows the mouse.
    ///     Fires on every DragMove that bubbles to root (i.e. any DragMove not consumed by a child).
    /// </summary>
    private void OnRootDragMove(DragMoveEvent e)
    {
        var dragging = GetDraggingPanel();

        dragging?.UpdateDragPosition(e.ScreenX, e.ScreenY);
    }

    /// <summary>
    ///     Handles drag-drop events that bubble up to the root panel.
    ///     If the drop was not consumed by a PanelSlot (slot swap), it means the user dropped
    ///     into the world viewport or onto a non-slot UI element — fire OnSlotDroppedOutside.
    /// </summary>
    private void OnRootDragDrop(DragDropEvent e)
    {
        if (e.Payload is not SlotDragPayload payload)
            return;

        if (payload.Source.Parent is not PanelBase { IsDragging: true } panel)
            return;

        panel.CompleteDragOutside(e.ScreenX, e.ScreenY);
        e.Handled = true;
    }

    #endregion

    #region Click Handling
    /// <summary>
    ///     Converts screen mouse coordinates to tile coordinates, accounting for the HUD viewport offset. The world is
    ///     rendered with a translation matrix for the viewport origin, so mouse coords must be adjusted to match.
    /// </summary>
    private WorldEntity? GetEntityAtScreen(int mouseX, int mouseY)
    {
        if (MapFile is null)
            return null;

        //iterate hitboxes back-to-front (last drawn = closest to camera = highest priority)
        for (var i = EntityHitBoxes.Count - 1; i >= 0; i--)
        {
            var hitbox = EntityHitBoxes[i];

            if (hitbox.ScreenRect.Contains(mouseX, mouseY))
                return WorldState.GetEntity(hitbox.EntityId);
        }

        //fallback: tile-based lookup for ground items
        (var tileX, var tileY) = ScreenToTile(mouseX, mouseY);

        return WorldState.GetGroundItemAt(tileX, tileY);
    }

    private (int TileX, int TileY) ScreenToTile(int mouseX, int mouseY)
    {
        var viewport = WorldHud.ViewportBounds;
        var worldPos = Camera.ScreenToWorld(new Vector2(mouseX - viewport.X, mouseY - viewport.Y));
        var tile = Camera.WorldToTile(worldPos.X, worldPos.Y, MapFile!.Height);

        return (tile.X, tile.Y);
    }

    private void TryPickupItem()
    {
        var player = WorldState.GetPlayerEntity();

        if (player is null)
            return;

        var slot = WorldState.Inventory.GetFirstEmptySlot();

        if (slot == 0)
            return;

        //first try the player's own tile
        if (WorldState.HasGroundItemAt(player.TileX, player.TileY))
        {
            Game.Connection.PickupItem(player.TileX, player.TileY, slot);

            return;
        }

        //then try the tile in front (direction the player is facing)
        (var dx, var dy) = player.Direction.ToTileOffset();
        var frontX = player.TileX + dx;
        var frontY = player.TileY + dy;

        if (WorldState.HasGroundItemAt(frontX, frontY))
            Game.Connection.PickupItem(frontX, frontY, slot);
    }

    private void HandleWorldClick(int mouseX, int mouseY)
    {
        if (MapFile is null)
            return;

        var viewport = WorldHud.ViewportBounds;

        if ((mouseX < viewport.X)
            || (mouseX >= (viewport.X + viewport.Width))
            || (mouseY < viewport.Y)
            || (mouseY >= (viewport.Y + viewport.Height)))
            return;

        (var tileX, var tileY) = ScreenToTile(mouseX, mouseY);

        //track tile for same-tile guard used by onrootdoubleclick
        LeftClickTracker.Click(tileX, tileY);

        //check group box text overlays first — they sit above entity hitboxes
        var groupBoxHit = Overlays.GetGroupBoxAtScreen(mouseX, mouseY);

        if (groupBoxHit.HasValue)
        {
            (_, var entityName) = groupBoxHit.Value;

            Game.Connection.SendGroupInvite(ClientGroupSwitch.ViewGroupBox, entityName);

            return;
        }

        //single click: check for entity at hitbox first, then tile interaction
        var entity = GetEntityAtScreen(mouseX, mouseY);

        if (entity?.Type is ClientEntityType.Creature)
            Game.Connection.ClickEntity(entity.Id);
        else if (TileHasForeground(tileX, tileY))
            Game.Connection.ClickTile(tileX, tileY);
    }

    private void HandleCtrlClick(int mouseX, int mouseY)
    {
        if (MapFile is null)
            return;

        var viewport = WorldHud.ViewportBounds;

        if ((mouseX < viewport.X)
            || (mouseX >= (viewport.X + viewport.Width))
            || (mouseY < viewport.Y)
            || (mouseY >= (viewport.Y + viewport.Height)))
            return;

        var entity = GetEntityAtScreen(mouseX, mouseY);

        if (entity is null)
            return;

        if ((entity.Type == ClientEntityType.Aisling) && (entity.Id != Game.Connection.AislingId))
        {
            var name = entity.Name;
            var id = entity.Id;

            AislingContext.Show(
                mouseX,
                mouseY,
                name,
                () => Game.Connection.ClickEntity(id),
                () => Game.Connection.SendGroupInvite(ClientGroupSwitch.TryInvite, name),
                () => WorldHud.ChatInput.Focus($"-> {name}: ", TextColors.Whisper));
        }
    }

    private void HandleWorldRightClick(int mouseX, int mouseY)
    {
        if (MapFile is null || MapPathfinder is null)
            return;

        var viewport = WorldHud.ViewportBounds;

        if ((mouseX < viewport.X)
            || (mouseX >= (viewport.X + viewport.Width))
            || (mouseY < viewport.Y)
            || (mouseY >= (viewport.Y + viewport.Height)))
            return;

        var player = WorldState.GetPlayerEntity();

        if (player is null)
            return;

        (var tileX, var tileY) = ScreenToTile(mouseX, mouseY);

        //clamp to map bounds
        tileX = Math.Clamp(tileX, 0, MapFile.Width - 1);
        tileY = Math.Clamp(tileY, 0, MapFile.Height - 1);

        //track tile for same-tile guard used by onrootdoubleclick
        RightClickTracker.Click(tileX, tileY);

        //don't pathfind to current position
        if ((tileX == player.TileX) && (tileY == player.TileY))
        {
            Pathfinding.Clear();

            return;
        }

        //single right-click — pathfind to ground tile
        Pathfinding.TargetEntityId = null;
        PathfindToTile(player, tileX, tileY);
    }

    /// <summary>
    ///     Shift+right-click: cancel pathfinding/auto-assailing, and if idle, turn toward the clicked tile.
    /// </summary>
    private void HandleShiftRightClick(int mouseX, int mouseY)
    {
        Pathfinding.Clear();

        if (MapFile is null)
            return;

        var player = WorldState.GetPlayerEntity();

        if (player is null || (player.AnimState != EntityAnimState.Idle))
            return;

        var viewport = WorldHud.ViewportBounds;

        if ((mouseX < viewport.X)
            || (mouseX >= (viewport.X + viewport.Width))
            || (mouseY < viewport.Y)
            || (mouseY >= (viewport.Y + viewport.Height)))
            return;

        (var tileX, var tileY) = ScreenToTile(mouseX, mouseY);

        tileX = Math.Clamp(tileX, 0, MapFile.Width - 1);
        tileY = Math.Clamp(tileY, 0, MapFile.Height - 1);

        if ((tileX == player.TileX) && (tileY == player.TileY))
            return;

        var dx = tileX - player.TileX;
        var dy = tileY - player.TileY;

        var direction = Math.Abs(dx) >= Math.Abs(dy)
            ? dx > 0 ? Direction.Right : Direction.Left
            : dy > 0 ? Direction.Down : Direction.Up;

        if (player.Direction != direction)
        {
            Game.Connection.Turn(direction);
            player.Direction = direction;
        }
    }

    private void PathfindToTile(WorldEntity player, int tileX, int tileY)
    {
        if (MapPathfinder is null)
            return;

        Pathfinding.Path = Pathfinder.FindPathToTile(
            MapPathfinder,
            player.TileX,
            player.TileY,
            tileX,
            tileY,
            IsGameMaster ? [] : WorldState.GetBlockedPoints(),
            IsGameMaster);
    }

    private void PathfindToEntity(WorldEntity player, WorldEntity target)
    {
        if (MapPathfinder is null)
            return;

        var path = Pathfinder.FindPathToEntity(
            MapPathfinder,
            player.TileX,
            player.TileY,
            target.TileX,
            target.TileY,
            IsGameMaster ? [] : WorldState.GetBlockedPoints(),
            IsGameMaster,
            out var alreadyAdjacent);

        if (alreadyAdjacent)
            Pathfinding.Clear();
        else
            Pathfinding.Path = path;
    }
    #endregion
}
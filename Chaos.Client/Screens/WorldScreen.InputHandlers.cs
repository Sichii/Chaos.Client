#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.World.Hud.Panel;
using Chaos.Client.Controls.World.Hud.Panel.Slots;
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Chaos.Client.Extensions;
using Chaos.Client.Models;
using Chaos.Client.Systems;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Pathfinder = Chaos.Client.Systems.Pathfinder;
#endregion

namespace Chaos.Client.Screens;

public sealed partial class WorldScreen
{
    #region UI Event Handlers
    // --- Inventory ---

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

    private void HandleInventoryDropInViewport(byte slot, int mouseX, int mouseY)
    {
        // Dropped onto the exchange window — add item to exchange
        if ((slot != 0) && Exchange.Visible && Exchange.ContainsPoint(mouseX, mouseY))
        {
            Game.Connection.SendExchangeInteraction(ExchangeRequestType.AddItem, Exchange.OtherUserId, slot);

            return;
        }

        // Dropped onto an equipment slot — equip the item
        if ((slot != 0) && StatusBook.Visible && StatusBook.ContainsEquipmentSlotPoint(mouseX, mouseY))
        {
            Game.Connection.UseItem(slot);

            return;
        }

        var viewport = WorldHud.ViewportBounds;

        // Only drop if released within the world viewport
        if ((mouseX < viewport.X)
            || (mouseX >= (viewport.X + viewport.Width))
            || (mouseY < viewport.Y)
            || (mouseY >= (viewport.Y + viewport.Height)))
            return;

        if (MapFile is null)
            return;

        // Check if dropped on an entity (give item/gold to NPC/player) — skip self (drop on ground instead)
        (var tileX, var tileY) = ScreenToTile(mouseX, mouseY);
        var entity = GetEntityAtScreen(mouseX, mouseY);

        var droppedOnEntity = entity is not null
                              && entity.Type is ClientEntityType.Aisling or ClientEntityType.Creature
                              && (entity.Id != Game.Connection.AislingId);

        // Gold bag (slot 0) — show the gold amount popup
        if (slot == 0)
        {
            GoldDrop.CenterVerticallyIn(viewport);
            GoldDrop.ShowForTarget(droppedOnEntity ? entity!.Id : null, tileX, tileY);

            return;
        }

        if (droppedOnEntity)
        {
            Game.Connection.DropItemOnCreature(slot, entity!.Id);

            return;
        }

        // Stackable items — prompt for count before dropping
        var invSlot = WorldState.Inventory.GetSlot(slot);

        if (invSlot.Stackable)
        {
            WorldHud.Prompt.ShowPrompt($"Number of items to drop [ 0 - {(int)invSlot.Count} ]: ");

            var capturedSlot = slot;
            var capturedX = tileX;
            var capturedY = tileY;

            void OnPromptConfirm(string text)
            {
                WorldHud.Prompt.OnConfirm -= OnPromptConfirm;

                if (int.TryParse(text, out var count) && (count > 0))
                    Game.Connection.DropItem(
                        capturedSlot,
                        capturedX,
                        capturedY,
                        count);
            }

            WorldHud.Prompt.OnConfirm += OnPromptConfirm;

            return;
        }

        Game.Connection.DropItem(slot, tileX, tileY);
    }

    // --- Skills / spells ---

    private void HandleSkillSlotClicked(byte slot)
    {
        var skillSlot = WorldHud.SkillBook.GetSkillSlot(slot) ?? WorldHud.SkillBookAlt.GetSkillSlot(slot);

        if (skillSlot is not null && (skillSlot.CooldownPercent > 0))
            return;

        // Send chant line if one is set for this skill
        if (skillSlot is not null && !string.IsNullOrEmpty(skillSlot.Chant))
            Game.Connection.SendChant(skillSlot.Chant);

        Game.Connection.UseSkill(slot);
    }

    private void HandleSpellSlotClicked(byte slot)
    {
        // Determine which panel the slot came from
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

        // NoTarget spells cast immediately (no cast mode)
        if (spellSlot.SpellType == SpellType.NoTarget)
        {
            if (spellSlot.CastLines == 0)
                Game.Connection.UseSpell(slot);
            else
            {
                // NoTarget with lines: begin chant sequence targeting self
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

        // Enter cast mode — wait for target selection
        CastingSystem.BeginTargeting(spellSlot);
    }

    private void HandleSpellSlotDropped(byte slot, int mouseX, int mouseY)
    {
        var entity = GetEntityAtScreen(mouseX, mouseY);

        if (entity is null || entity.Type is not (ClientEntityType.Aisling or ClientEntityType.Creature))
            return;

        HandleSpellSlotClicked(slot);

        if (CastingSystem.IsTargeting)
            CastingSystem.SelectTarget(
                entity.Id,
                entity.TileX,
                entity.TileY,
                Game.Connection);
    }

    // --- Hotkeys ---

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

    // Ctrl+key emotes: 9-17 then 21-22 (skips 18-20 which don't exist in BodyAnimation)
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

    private bool HandleEmoteHotkeys(InputBuffer input)
    {
        var ctrl = input.IsKeyHeld(Keys.LeftControl) || input.IsKeyHeld(Keys.RightControl);
        var alt = input.IsKeyHeld(Keys.LeftAlt) || input.IsKeyHeld(Keys.RightAlt);

        if (!ctrl && !alt)
            return false;

        var keyIndex = -1;

        for (var i = 0; i < EmoteKeys.Length; i++)
            if (input.WasKeyPressed(EmoteKeys[i]))
            {
                keyIndex = i;

                break;
            }

        if (keyIndex < 0)
            return false;

        BodyAnimation bodyAnimation;

        if (ctrl && !alt)
            bodyAnimation = CtrlEmotes[keyIndex];
        else if (ctrl && alt)
            bodyAnimation = (BodyAnimation)(23 + keyIndex);
        else
            bodyAnimation = (BodyAnimation)(34 + keyIndex);

        Game.Connection.SendEmote(bodyAnimation);

        return true;
    }

    private void HandleSlotHotkeys(InputBuffer input)
    {
        var slot = -1;

        if (input.WasKeyPressed(Keys.D1))
            slot = 1;
        else if (input.WasKeyPressed(Keys.D2))
            slot = 2;
        else if (input.WasKeyPressed(Keys.D3))
            slot = 3;
        else if (input.WasKeyPressed(Keys.D4))
            slot = 4;
        else if (input.WasKeyPressed(Keys.D5))
            slot = 5;
        else if (input.WasKeyPressed(Keys.D6))
            slot = 6;
        else if (input.WasKeyPressed(Keys.D7))
            slot = 7;
        else if (input.WasKeyPressed(Keys.D8))
            slot = 8;
        else if (input.WasKeyPressed(Keys.D9))
            slot = 9;
        else if (input.WasKeyPressed(Keys.D0))
            slot = 10;
        else if (input.WasKeyPressed(Keys.OemMinus))
            slot = 11;
        else if (input.WasKeyPressed(Keys.OemPlus))
            slot = 12;

        if (slot < 0)
            return;

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
                // TODO: left half = world skills (slots 73-78), right half = world spells (slots 73-78)
                break;

            case HudTab.Chat:
            case HudTab.MessageHistory:
            {
                var macroText = MacroMenu.GetMacroValue(slot - 1);

                if (macroText.Length > 0)
                {
                    Chat.Focus(string.Empty, Color.White);
                    WorldHud.ChatInput.Text = macroText;
                    WorldHud.ChatInput.CursorPosition = macroText.Length;
                }

                break;
            }
        }
    }

    // --- Chant editing ---

    private void WireAbilityRightClicks(PanelBase panel)
    {
        for (byte i = 1; i <= 36; i++)
            if (panel.GetSlotControl(i) is AbilitySlotControl ability)
                ability.OnRightClick += OpenChantEdit;
    }

    private void OpenChantEdit(byte slot)
    {
        // Determine which panel this slot belongs to based on active tab
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
            SaveSkillChants();
            WorldState.ReloadChants();
        }
    }

    // --- Cache / persistence helpers ---

    private void LoadPlayerFamilyList()
    {
        var family = DataContext.LocalPlayerSettings.LoadFamilyList();
        StatusBook.SetFamilyMembers(family);
    }

    private void SavePlayerFamilyList()
    {
        var family = StatusBook.GetFamilyMembers();

        if (family is not null)
            DataContext.LocalPlayerSettings.SaveFamilyList(family);
    }

    private void LoadPlayerFriendList()
    {
        var names = DataContext.LocalPlayerSettings.LoadFriendList();

        var entries = names.Select(n => new FriendEntry(n, false))
                           .ToList();
        FriendsList.SetFriends(entries);
    }

    private void SavePlayerFriendList()
    {
        var names = FriendsList.GetFriendNames();
        DataContext.LocalPlayerSettings.SaveFriendList(names);
    }

    private void LoadPlayerMacros()
    {
        var macros = DataContext.LocalPlayerSettings.LoadMacros();
        MacroMenu.SetMacros(macros);
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

    #region Click Handling
    /// <summary>
    ///     Handles left-click within the viewport area — picks the entity at the click position.
    /// </summary>
    private void UpdateDragHighlight(InputBuffer input)
    {
        if (GetDraggingPanel() is null || MapFile is null)
            return;

        var entity = GetEntityAtScreen(input.MouseX, input.MouseY);

        // When dragging inventory items, don't highlight the player (drop goes to ground instead)
        var isItemDrag = WorldHud.Inventory.IsDragging;
        var playerId = Game.Connection.AislingId;

        uint? newHighlight
            = entity?.Type is ClientEntityType.Aisling or ClientEntityType.Creature && !(isItemDrag && (entity.Id == playerId))
                ? entity.Id
                : null;

        if (newHighlight != Highlight.HoveredEntityId)
            ClearHighlightCache();

        Highlight.HoveredEntityId = newHighlight;
    }

    /// <summary>
    ///     Converts screen mouse coordinates to tile coordinates, accounting for the HUD viewport offset. The world is
    ///     rendered with a translation matrix for the viewport origin, so mouse coords must be adjusted to match.
    /// </summary>
    private WorldEntity? GetEntityAtScreen(int mouseX, int mouseY)
    {
        if (MapFile is null)
            return null;

        // Iterate hitboxes back-to-front (last drawn = closest to camera = highest priority)
        for (var i = EntityHitBoxes.Count - 1; i >= 0; i--)
        {
            var hitbox = EntityHitBoxes[i];

            if (hitbox.ScreenRect.Contains(mouseX, mouseY))
                return WorldState.GetEntity(hitbox.EntityId);
        }

        // Fallback: tile-based lookup for ground items
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

        // First try the player's own tile
        if (WorldState.HasGroundItemAt(player.TileX, player.TileY))
        {
            Game.Connection.PickupItem(player.TileX, player.TileY, slot);

            return;
        }

        // Then try the tile in front (direction the player is facing)
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

        // Check for double-click (same tile within time window)
        var sameTile = LeftClickTracker.Click(tileX, tileY);
        var isDoubleClick = Game.Input.WasLeftButtonDoubleClicked && sameTile;

        // Check group box text overlays first — they sit above entity hitboxes
        var groupBoxHit = Overlays.GetGroupBoxAtScreen(mouseX, mouseY);

        if (groupBoxHit.HasValue)
        {
            (var entityId, var entityName) = groupBoxHit.Value;

            Game.Connection.SendGroupInvite(ClientGroupSwitch.ViewGroupBox, entityName);

            return;
        }

        if (isDoubleClick)
        {
            // Double-click: interact with entities (use hitbox detection, not tile lookup)
            var entity = GetEntityAtScreen(mouseX, mouseY);

            if (entity is not null)
            {
                if (entity.Type == ClientEntityType.GroundItem)
                {
                    var firstEmptySlot = WorldState.Inventory.GetFirstEmptySlot();
                    Game.Connection.PickupItem(entity.TileX, entity.TileY, firstEmptySlot);
                } else if ((entity.Type != ClientEntityType.Aisling) || ClientSettings.EnableProfileClick)
                    Game.Connection.ClickEntity(entity.Id);
            }
        } else
        {
            // Single click: check for entity at hitbox first, then tile interaction
            var entity = GetEntityAtScreen(mouseX, mouseY);

            if (entity is not null && entity.Type is ClientEntityType.Aisling or ClientEntityType.Creature)
                Game.Connection.ClickEntity(entity.Id);
            else if (TileHasForeground(tileX, tileY))
                Game.Connection.ClickTile(tileX, tileY);
        }
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

            AislingPopup.Show(
                mouseX,
                mouseY,
                name,
                () => Game.Connection.ClickEntity(id),
                () => Game.Connection.SendGroupInvite(ClientGroupSwitch.TryInvite, name),
                () => Chat.Focus($"-> {name}: ", TextColors.Whisper));
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

        // Clamp to map bounds
        tileX = Math.Clamp(tileX, 0, MapFile.Width - 1);
        tileY = Math.Clamp(tileY, 0, MapFile.Height - 1);

        var sameTile = RightClickTracker.Click(tileX, tileY);
        var isDoubleRightClick = Game.Input.WasRightButtonDoubleClicked && sameTile;

        // Don't pathfind to current position
        if ((tileX == player.TileX) && (tileY == player.TileY))
        {
            Pathfinding.Clear();

            return;
        }

        // Double right-click on entity — follow and assail
        if (isDoubleRightClick)
        {
            var entity = WorldState.GetEntityAt(tileX, tileY);

            if (entity is not null && entity.Type is ClientEntityType.Aisling or ClientEntityType.Creature)
            {
                Pathfinding.SetEntityTarget(entity.Id);
                PathfindToEntity(player, entity);

                return;
            }
        }

        // Single right-click — pathfind to ground tile
        Pathfinding.TargetEntityId = null;
        PathfindToTile(player, tileX, tileY);
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
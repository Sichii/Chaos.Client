#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.World.ViewPort;
using Chaos.Client.Models;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Manages all entity-anchored overlays: chat bubbles, health bars, name tags, group box texts, and chant overlays.
///     Each overlay is keyed by entity ID. Chat bubbles and chant overlays interlock — adding one replaces the other.
/// </summary>
public sealed class EntityOverlayManager
{
    // Health bar Y offset from entity tile center (higher = further above entity)
    private const int HEALTH_BAR_Y_OFFSET = 61;

    // Name tag Y offset from entity tile center (above health bars)
    private const int NAME_TAG_Y_OFFSET = 72;

    // Group box Y offset — sits 2px above name tags
    private const int GROUP_BOX_Y_OFFSET = 74;
    private static readonly Color NAME_TAG_SHADOW_COLOR = new(20, 20, 20);
    private readonly Dictionary<uint, ChantText> ChantOverlays = new();

    private readonly Dictionary<uint, ChatBubble> ChatBubbles = new();
    private readonly Dictionary<uint, GroupBox> GroupBoxes = new();
    private readonly Dictionary<uint, HealthBar> HealthBars = new();
    private readonly Dictionary<uint, TextElement> NameTagCache = new();

    /// <summary>
    ///     Adds a chant overlay for the given entity, replacing any existing chant or chat bubble. A null/empty message clears
    ///     the existing chant without creating a new one.
    /// </summary>
    public void AddChantOverlay(uint entityId, string? message)
    {
        RemoveChantOverlay(entityId);

        if (string.IsNullOrEmpty(message))
            return;

        // Chant replaces any active chat bubble
        RemoveChatBubble(entityId);

        ChantOverlays[entityId] = ChantText.Create(entityId, message);
    }

    /// <summary>
    ///     Adds a chat bubble for the given entity, replacing any existing bubble or chant overlay.
    /// </summary>
    public void AddChatBubble(uint entityId, string message, bool isShout)
    {
        // Chat bubble replaces any active chant overlay
        RemoveChantOverlay(entityId);

        if (ChatBubbles.TryGetValue(entityId, out var existing))
            existing.Dispose();

        ChatBubbles[entityId] = ChatBubble.Create(entityId, message, isShout);
    }

    /// <summary>
    ///     Adds or resets a health bar for the given entity.
    /// </summary>
    public void AddOrResetHealthBar(uint entityId, byte healthPercent)
    {
        if (HealthBars.TryGetValue(entityId, out var existing))
            existing.Reset(healthPercent);
        else
            HealthBars[entityId] = new HealthBar(entityId, healthPercent);
    }

    /// <summary>
    ///     Disposes and clears all overlay caches. Call on map change or unload.
    /// </summary>
    public void Clear()
    {
        foreach (var bubble in ChatBubbles.Values)
            bubble.Dispose();
        ChatBubbles.Clear();

        foreach (var bar in HealthBars.Values)
            bar.Dispose();
        HealthBars.Clear();

        foreach (var overlay in ChantOverlays.Values)
            overlay.Dispose();
        ChantOverlays.Clear();

        NameTagCache.Clear();
        GroupBoxes.Clear();
    }

    /// <summary>
    ///     Draws all overlays. Call within a SpriteBatch Begin/End block with camera transform applied. Draw order: chant
    ///     overlays → health bars → name tags → group box texts → chat bubbles (top-most last).
    /// </summary>
    public void Draw(
        SpriteBatch spriteBatch,
        Camera camera,
        int mapHeight,
        IReadOnlyList<WorldEntity> sortedEntities,
        bool showTintHighlight,
        uint? hoveredEntityId,
        uint playerEntityId)
    {
        foreach (var overlay in ChantOverlays.Values)
            overlay.Draw(spriteBatch);

        foreach (var bar in HealthBars.Values)
            bar.Draw(spriteBatch);

        DrawNameTags(
            spriteBatch,
            camera,
            mapHeight,
            sortedEntities,
            showTintHighlight,
            hoveredEntityId,
            playerEntityId);

        DrawGroupBoxTexts(
            spriteBatch,
            camera,
            mapHeight,
            sortedEntities);

        foreach (var bubble in ChatBubbles.Values)
            bubble.Draw(spriteBatch);
    }

    private void DrawGroupBoxTexts(
        SpriteBatch spriteBatch,
        Camera camera,
        int mapHeight,
        IReadOnlyList<WorldEntity> sortedEntities)
    {
        // Determine which group box (if any) the mouse is over, using positions from previous frame
        var mouseState = Mouse.GetState();

        var hoveredGroupBoxId = GetGroupBoxAtScreen(mouseState.X, mouseState.Y)
            ?.EntityId;

        for (var i = 0; i < sortedEntities.Count; i++)
        {
            var entity = sortedEntities[i];

            if (entity.Type != ClientEntityType.Aisling)
                continue;

            if (string.IsNullOrEmpty(entity.GroupBoxText))
                continue;

            if (!GroupBoxes.TryGetValue(entity.Id, out var groupBox))
            {
                groupBox = new GroupBox(entity.Id);
                GroupBoxes[entity.Id] = groupBox;
            }

            groupBox.UpdateText(entity.GroupBoxText);
            groupBox.IsHovered = hoveredGroupBoxId == entity.Id;

            // Position panel centered on entity, bottom edge at Y offset
            var tileWorld = Camera.TileToWorld(entity.TileX, entity.TileY, mapHeight);
            var entityWorldX = tileWorld.X + DaLibConstants.HALF_TILE_WIDTH + entity.VisualOffset.X;
            var entityWorldY = tileWorld.Y + DaLibConstants.HALF_TILE_HEIGHT + entity.VisualOffset.Y - GROUP_BOX_Y_OFFSET;

            var screenPos = camera.WorldToScreen(
                new Vector2(entityWorldX - GroupBox.PANEL_WIDTH / 2f, entityWorldY - GroupBox.PANEL_HEIGHT));

            groupBox.X = (int)screenPos.X;
            groupBox.Y = (int)screenPos.Y;
            groupBox.Draw(spriteBatch);
        }
    }

    private void DrawNameTags(
        SpriteBatch spriteBatch,
        Camera camera,
        int mapHeight,
        IReadOnlyList<WorldEntity> sortedEntities,
        bool showTintHighlight,
        uint? hoveredEntityId,
        uint playerEntityId)
    {
        for (var i = 0; i < sortedEntities.Count; i++)
        {
            var entity = sortedEntities[i];

            var isMerchant = entity is { Type: ClientEntityType.Creature, NameTagStyle: NameTagStyle.NeutralHover };

            if ((entity.Type != ClientEntityType.Aisling) && !isMerchant)
                continue;

            if (string.IsNullOrEmpty(entity.Name))
                continue;

            // NeutralHover/FriendlyHover: only show on hover, and not during targeting/dragging
            // Never show hover nametag for the player's own character
            var isHoverOnly = entity.NameTagStyle is NameTagStyle.NeutralHover or NameTagStyle.FriendlyHover;

            if (isHoverOnly && (showTintHighlight || (hoveredEntityId != entity.Id) || (entity.Id == playerEntityId)))
                continue;

            var nameColor = entity.NameTagStyle switch
            {
                NameTagStyle.Hostile       => LegendColors.Red,
                _ when isMerchant          => LegendColors.CornflowerBlue,
                NameTagStyle.FriendlyHover => LegendColors.Lime,
                _                          => TextColors.Default
            };

            if (!NameTagCache.TryGetValue(entity.Id, out var cachedText))
            {
                cachedText = new TextElement();
                NameTagCache[entity.Id] = cachedText;
            }

            cachedText.UpdateShadowed(entity.Name, nameColor, NAME_TAG_SHADOW_COLOR);

            if (!cachedText.HasContent)
                continue;

            var tileWorld = Camera.TileToWorld(entity.TileX, entity.TileY, mapHeight);
            var entityWorldX = tileWorld.X + DaLibConstants.HALF_TILE_WIDTH + entity.VisualOffset.X;
            var entityWorldY = tileWorld.Y + DaLibConstants.HALF_TILE_HEIGHT + entity.VisualOffset.Y - NAME_TAG_Y_OFFSET;
            var screenPos = camera.WorldToScreen(new Vector2(entityWorldX - cachedText.Width / 2f, entityWorldY));

            cachedText.Draw(spriteBatch, screenPos);
        }
    }

    /// <summary>
    ///     Returns the entity ID and name if the given screen point hits a group box overlay. Used for click-to-view.
    /// </summary>
    public (uint EntityId, string EntityName)? GetGroupBoxAtScreen(int screenX, int screenY)
    {
        foreach ((var entityId, var groupBox) in GroupBoxes)
        {
            var rect = new Rectangle(
                groupBox.ScreenX,
                groupBox.ScreenY,
                GroupBox.PANEL_WIDTH,
                GroupBox.PANEL_HEIGHT);

            if (!rect.Contains(screenX, screenY))
                continue;

            var entity = WorldState.GetEntity(entityId);

            if (entity is not null && !string.IsNullOrEmpty(entity.Name))
                return (entityId, entity.Name);
        }

        return null;
    }

    private void RemoveChantOverlay(uint entityId)
    {
        if (ChantOverlays.Remove(entityId, out var existing))
            existing.Dispose();
    }

    private void RemoveChatBubble(uint entityId)
    {
        if (ChatBubbles.Remove(entityId, out var existing))
            existing.Dispose();
    }

    /// <summary>
    ///     Removes all overlays for a given entity (name tag, group box, chat bubble, health bar, chant). Call when an entity
    ///     is removed from the world.
    /// </summary>
    public void RemoveEntity(uint entityId)
    {
        NameTagCache.Remove(entityId);
        GroupBoxes.Remove(entityId);
        RemoveChatBubble(entityId);
        RemoveChantOverlay(entityId);

        if (HealthBars.Remove(entityId, out var bar))
            bar.Dispose();
    }

    /// <summary>
    ///     Removes a single entity's cached name tag. Call when an entity is removed from the world.
    /// </summary>
    public void RemoveNameTag(uint entityId) => NameTagCache.Remove(entityId);

    /// <summary>
    ///     Ticks bubble/bar/overlay timers, updates screen positions from entity world positions, and removes expired entries.
    /// </summary>
    public void Update(
        GameTime gameTime,
        Camera camera,
        int mapHeight)
    {
        UpdateChatBubbles(
            gameTime,
            camera,
            mapHeight);

        UpdateChantOverlays(
            gameTime,
            camera,
            mapHeight);

        UpdateHealthBars(
            gameTime,
            camera,
            mapHeight);
    }

    private void UpdateChantOverlays(
        GameTime gameTime,
        Camera camera,
        int mapHeight)
    {
        List<uint>? expired = null;

        foreach ((var entityId, var overlay) in ChantOverlays)
        {
            if (overlay.IsExpired)
            {
                (expired ??= []).Add(entityId);

                continue;
            }

            var entity = WorldState.GetEntity(entityId);

            if (entity is null)
            {
                (expired ??= []).Add(entityId);

                continue;
            }

            var tileWorld = Camera.TileToWorld(entity.TileX, entity.TileY, mapHeight);
            var tileCenterX = tileWorld.X + DaLibConstants.HALF_TILE_WIDTH;
            var tileCenterY = tileWorld.Y + DaLibConstants.HALF_TILE_HEIGHT;

            var entityWorldX = tileCenterX + entity.VisualOffset.X;
            var entityWorldY = tileCenterY + entity.VisualOffset.Y - 60;

            var overlayX = entityWorldX - overlay.Width / 2f;
            var overlayY = entityWorldY - overlay.Height;

            var screenPos = camera.WorldToScreen(new Vector2(overlayX, overlayY));
            overlay.X = (int)screenPos.X;
            overlay.Y = (int)screenPos.Y;
        }

        if (expired is not null)
            foreach (var id in expired)
            {
                ChantOverlays[id]
                    .Dispose();
                ChantOverlays.Remove(id);
            }
    }

    private void UpdateChatBubbles(
        GameTime gameTime,
        Camera camera,
        int mapHeight)
    {
        List<uint>? expired = null;

        foreach ((var entityId, var bubble) in ChatBubbles)
        {
            if (bubble.IsExpired)
            {
                (expired ??= []).Add(entityId);

                continue;
            }

            var entity = WorldState.GetEntity(entityId);

            if (entity is null)
            {
                (expired ??= []).Add(entityId);

                continue;
            }

            var tileWorld = Camera.TileToWorld(entity.TileX, entity.TileY, mapHeight);
            var tileCenterX = tileWorld.X + DaLibConstants.HALF_TILE_WIDTH;
            var tileCenterY = tileWorld.Y + DaLibConstants.HALF_TILE_HEIGHT;

            var entityWorldX = tileCenterX + entity.VisualOffset.X;
            var entityWorldY = tileCenterY + entity.VisualOffset.Y - 64;

            var bubbleX = entityWorldX - bubble.Width / 2f;
            var bubbleY = entityWorldY - bubble.Height;

            var screenPos = camera.WorldToScreen(new Vector2(bubbleX, bubbleY));
            bubble.X = (int)screenPos.X;
            bubble.Y = (int)screenPos.Y;
        }

        if (expired is not null)
            foreach (var id in expired)
            {
                ChatBubbles[id]
                    .Dispose();
                ChatBubbles.Remove(id);
            }
    }

    private void UpdateHealthBars(
        GameTime gameTime,
        Camera camera,
        int mapHeight)
    {
        List<uint>? expired = null;

        foreach ((var entityId, var bar) in HealthBars)
        {
            if (bar.IsExpired)
            {
                (expired ??= []).Add(entityId);

                continue;
            }

            var entity = WorldState.GetEntity(entityId);

            if (entity is null)
            {
                (expired ??= []).Add(entityId);

                continue;
            }

            var tileWorld = Camera.TileToWorld(entity.TileX, entity.TileY, mapHeight);
            var entityWorldX = tileWorld.X + DaLibConstants.HALF_TILE_WIDTH + entity.VisualOffset.X;
            var entityWorldY = tileWorld.Y + DaLibConstants.HALF_TILE_HEIGHT + entity.VisualOffset.Y - HEALTH_BAR_Y_OFFSET;

            var screenPos = camera.WorldToScreen(new Vector2(entityWorldX - bar.Width / 2f, entityWorldY));
            bar.X = (int)screenPos.X + 1;
            bar.Y = (int)screenPos.Y;
        }

        if (expired is not null)
            foreach (var id in expired)
            {
                HealthBars[id]
                    .Dispose();
                HealthBars.Remove(id);
            }
    }
}
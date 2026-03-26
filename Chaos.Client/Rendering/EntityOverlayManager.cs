#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.World.ViewPort;
using Chaos.Client.Models;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Manages all entity-anchored overlays: chat bubbles, health bars, name tags, and chant overlays. Each overlay is
///     keyed by entity ID. Chat bubbles and chant overlays interlock — adding one replaces the other.
/// </summary>
public sealed class EntityOverlayManager
{
    // Health bar Y offset from entity tile center (higher = further above entity)
    private const int HEALTH_BAR_Y_OFFSET = 61;

    // Name tag Y offset from entity tile center (above health bars)
    private const int NAME_TAG_Y_OFFSET = 72;
    private static readonly Color NAME_TAG_SHADOW_COLOR = new(20, 20, 20);
    private readonly Dictionary<uint, ChantOverlay> ChantOverlays = new();

    private readonly Dictionary<uint, ChatBubble> ChatBubbles = new();
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

        ChantOverlays[entityId] = ChantOverlay.Create(entityId, message);
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
    }

    /// <summary>
    ///     Draws all overlays. Call within a SpriteBatch Begin/End block with camera transform applied. Draw order: chant
    ///     overlays → health bars → name tags → chat bubbles (top-most last).
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

        foreach (var bubble in ChatBubbles.Values)
            bubble.Draw(spriteBatch);
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

            if (entity.Type != ClientEntityType.Aisling)
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
                NameTagStyle.Hostile       => new Color(255, 128, 0),
                NameTagStyle.FriendlyHover => Color.LimeGreen,
                _                          => Color.White
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
    ///     Removes a single entity's cached name tag. Call when an entity is removed from the world.
    /// </summary>
    public void RemoveNameTag(uint entityId) => NameTagCache.Remove(entityId);

    /// <summary>
    ///     Ticks bubble/bar/overlay timers, updates screen positions from entity world positions, and removes expired entries.
    /// </summary>
    public void Update(
        GameTime gameTime,
        InputBuffer input,
        WorldState world,
        Camera camera,
        int mapHeight)
    {
        UpdateChatBubbles(
            gameTime,
            input,
            world,
            camera,
            mapHeight);

        UpdateChantOverlays(
            gameTime,
            input,
            world,
            camera,
            mapHeight);

        UpdateHealthBars(
            gameTime,
            input,
            world,
            camera,
            mapHeight);
    }

    private void UpdateChantOverlays(
        GameTime gameTime,
        InputBuffer input,
        WorldState world,
        Camera camera,
        int mapHeight)
    {
        List<uint>? expired = null;

        foreach ((var entityId, var overlay) in ChantOverlays)
        {
            overlay.Update(gameTime, input);

            if (overlay.IsExpired)
            {
                (expired ??= []).Add(entityId);

                continue;
            }

            var entity = world.GetEntity(entityId);

            if (entity is null)
                continue;

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
        InputBuffer input,
        WorldState world,
        Camera camera,
        int mapHeight)
    {
        List<uint>? expired = null;

        foreach ((var entityId, var bubble) in ChatBubbles)
        {
            bubble.Update(gameTime, input);

            if (bubble.IsExpired)
            {
                (expired ??= []).Add(entityId);

                continue;
            }

            var entity = world.GetEntity(entityId);

            if (entity is null)
                continue;

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
        InputBuffer input,
        WorldState world,
        Camera camera,
        int mapHeight)
    {
        List<uint>? expired = null;

        foreach ((var entityId, var bar) in HealthBars)
        {
            bar.Update(gameTime, input);

            if (bar.IsExpired)
            {
                (expired ??= []).Add(entityId);

                continue;
            }

            var entity = world.GetEntity(entityId);

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
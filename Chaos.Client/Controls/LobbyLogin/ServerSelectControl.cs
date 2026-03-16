#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Networking;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.LobbyLogin;

public class ServerSelectControl : UIPanel
{
    private const float SERVER_FONT_SIZE = 12f;
    private const int ROW_HEIGHT = 20;
    private const int FIRST_ROW_Y = 37;
    private const int NAME_X = 28;
    private const int DESC_X = 108;

    private readonly GraphicsDevice Device;
    private List<ServerTableEntry> Servers = [];
    private List<(CachedText Name, CachedText Description)> ServerTextCache = [];

    public ServerSelectControl(GraphicsDevice device)
    {
        Device = device;
        Name = "ServerSelect";
        Visible = false;

        var prefabSet = DataContext.UserControls.Get("_nsvr");

        if (prefabSet is null)
            throw new InvalidOperationException("Failed to load _nsvr control prefab set");

        // Load the panel images
        var topPrefab = prefabSet["ServerTopImage"];
        var midPrefab = prefabSet["ServerMidImage"];
        var botPrefab = prefabSet["ServerBotImage"];

        var topTexture = topPrefab.Images.Count > 0 ? TextureConverter.ToTexture2D(device, topPrefab.Images[0]) : null;

        var midTexture = midPrefab.Images.Count > 0 ? TextureConverter.ToTexture2D(device, midPrefab.Images[0]) : null;

        var botTexture = botPrefab.Images.Count > 0 ? TextureConverter.ToTexture2D(device, botPrefab.Images[0]) : null;

        // Panel dimensions from the top image
        var panelWidth = topTexture?.Width ?? 421;
        var panelHeight = (topTexture?.Height ?? 133) + (midTexture?.Height ?? 20) + (botTexture?.Height ?? 23);

        Width = panelWidth;
        Height = panelHeight;

        // Center on screen
        X = (640 - panelWidth) / 2;
        Y = (480 - panelHeight) / 2;

        // Top image
        if (topTexture is not null)
            AddChild(
                new UIImage
                {
                    Name = "TopImage",
                    X = 0,
                    Y = 0,
                    Width = topTexture.Width,
                    Height = topTexture.Height,
                    Texture = topTexture
                });

        // Mid image
        if (midTexture is not null)
            AddChild(
                new UIImage
                {
                    Name = "MidImage",
                    X = 0,
                    Y = topTexture?.Height ?? 133,
                    Width = midTexture.Width,
                    Height = midTexture.Height,
                    Texture = midTexture
                });

        // Bot image
        if (botTexture is not null)
            AddChild(
                new UIImage
                {
                    Name = "BotImage",
                    X = 0,
                    Y = (topTexture?.Height ?? 133) + (midTexture?.Height ?? 20),
                    Width = botTexture.Width,
                    Height = botTexture.Height,
                    Texture = botTexture
                });
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        // Draw server names and descriptions from cache
        var sx = ScreenX;
        var sy = ScreenY;

        for (var i = 0; i < ServerTextCache.Count; i++)
        {
            var rowY = sy + FIRST_ROW_Y + i * ROW_HEIGHT;
            (var nameCache, var descCache) = ServerTextCache[i];

            nameCache.Draw(spriteBatch, new Vector2(sx + NAME_X, rowY));
            descCache.Draw(spriteBatch, new Vector2(sx + DESC_X, rowY));
        }
    }

    public event Action<byte>? OnServerSelected;

    public void SetServers(List<ServerTableEntry> entries)
    {
        Servers = entries;

        // Dispose old cached text
        foreach ((var name, var desc) in ServerTextCache)
        {
            name.Dispose();
            desc.Dispose();
        }

        // Build new cache
        ServerTextCache = [];

        foreach (var server in entries)
        {
            var nameCache = new CachedText(Device);
            var descCache = new CachedText(Device);

            nameCache.Update(server.Name, SERVER_FONT_SIZE, Color.White);
            descCache.Update(server.Description, SERVER_FONT_SIZE, Color.LightGray);

            ServerTextCache.Add((nameCache, descCache));
        }
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        base.Update(gameTime, input);

        // Check for click on a server row
        if (!input.WasLeftButtonPressed)
            return;

        var mouseX = input.MouseX;
        var mouseY = input.MouseY;
        var panelScreenX = ScreenX;
        var panelScreenY = ScreenY;

        for (var i = 0; i < Servers.Count; i++)
        {
            var rowY = panelScreenY + FIRST_ROW_Y + i * ROW_HEIGHT;

            if ((mouseX >= (panelScreenX + NAME_X))
                && (mouseX < (panelScreenX + Width))
                && (mouseY >= rowY)
                && (mouseY < (rowY + ROW_HEIGHT)))
            {
                OnServerSelected?.Invoke(Servers[i].Id);

                return;
            }
        }
    }
}
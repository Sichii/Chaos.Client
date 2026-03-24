#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.LobbyLogin;

public sealed class ServerSelectControl : PrefabPanel
{
    private const int ROW_HEIGHT = 20;
    private const int FIRST_ROW_Y = 37;
    private const int NAME_X = 28;
    private const int DESC_X = 108;

    private List<ServerTableEntry> Servers = [];
    private List<(CachedText Name, CachedText Description)> ServerTextCache = [];

    public ServerSelectControl()
        : base("_nsvr")
    {
        Name = "ServerSelect";
        Visible = false;
    }

    public override void Dispose()
    {
        foreach ((var name, var desc) in ServerTextCache)
        {
            name.Dispose();
            desc.Dispose();
        }

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

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
            var nameCache = new CachedText();
            var descCache = new CachedText();

            nameCache.Update(server.Name, Color.White);
            descCache.Update(server.Description, Color.LightGray);

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
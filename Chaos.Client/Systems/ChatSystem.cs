#region
using Chaos.Client.Controls.World.Hud;
using Chaos.Client.Networking;
using Chaos.Extensions.Common;
using Microsoft.Xna.Framework;
#endregion

namespace Chaos.Client.Systems;

/// <summary>
///     Manages chat input focus state and dispatches outbound chat messages (public, shout, whisper, slash commands)
///     through the connection manager.
/// </summary>
public sealed class ChatSystem
{
    private readonly ConnectionManager Connection;
    private readonly Func<IWorldHud> GetHud;

    private IWorldHud Hud => GetHud();

    public ChatSystem(ConnectionManager connection, Func<IWorldHud> getHud)
    {
        Connection = connection;
        GetHud = getHud;
    }

    /// <summary>
    ///     Dispatches a chat message based on the current prefix mode (normal, shout, whisper, or slash command).
    /// </summary>
    public void Dispatch(string message)
    {
        var prefix = Hud.ChatInput.Prefix;

        if (prefix.EndsWithI("! "))
            Connection.SendShout(message);
        else if (prefix.StartsWithI("-> ") && prefix.EndsWithI(": "))
        {
            // Whisper phase 2: prefix is "-> targetName: "
            var targetName = prefix[3..^2];
            Connection.SendWhisper(targetName, message);
        } else
            Connection.SendPublicMessage(message);
    }

    /// <summary>
    ///     Focuses the chat input with the given prefix and text color.
    /// </summary>
    public void Focus(string prefix, Color textColor)
    {
        Hud.ChatInput.FocusedBackgroundColor = new Color(
            0,
            0,
            0,
            128);
        Hud.ChatInput.IsFocused = true;
        Hud.ChatInput.Prefix = prefix;
        Hud.ChatInput.ForegroundColor = textColor;
        Hud.SetDescription(null);
    }

    /// <summary>
    ///     Unfocuses the chat input and clears its text/prefix.
    /// </summary>
    public void Unfocus()
    {
        Hud.ChatInput.IsFocused = false;
        Hud.ChatInput.Text = string.Empty;
        Hud.ChatInput.Prefix = string.Empty;
        Hud.ChatInput.ForegroundColor = Color.White;
    }
}
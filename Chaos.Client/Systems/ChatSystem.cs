#region
using Chaos.Client.Controls.World.Hud;
using Chaos.Client.Networking;
using Chaos.Extensions.Common;
using Microsoft.Xna.Framework;
#endregion

namespace Chaos.Client.Systems;

public enum IgnorePhase
{
    None,
    ModeSelect,
    AddName,
    RemoveName
}

/// <summary>
///     Manages chat input focus state and dispatches outbound chat messages (public, shout, whisper, slash commands)
///     through the connection manager.
/// </summary>
public sealed class ChatSystem
{
    private const int MAX_WHISPER_HISTORY = 5;

    private readonly ConnectionManager Connection;
    private readonly Func<IWorldHud> GetHud;
    private readonly List<string> WhisperHistory = [];
    private int WhisperHistoryIndex;
    public IgnorePhase IgnorePhase { get; private set; }

    public bool HasWhisperHistory => WhisperHistory.Count > 0;

    private IWorldHud Hud => GetHud();

    public bool IsWhisperNamePhase => Hud.ChatInput.Prefix.StartsWithI("to [") && Hud.ChatInput.Prefix.EndsWithI("]? ");

    public ChatSystem(ConnectionManager connection, Func<IWorldHud> getHud)
    {
        Connection = connection;
        GetHud = getHud;
    }

    private void AddWhisperTarget(string name)
    {
        WhisperHistory.Remove(name);
        WhisperHistory.Insert(0, name);

        if (WhisperHistory.Count > MAX_WHISPER_HISTORY)
            WhisperHistory.RemoveAt(WhisperHistory.Count - 1);
    }

    /// <summary>
    ///     Cycles through whisper history targets during the name selection phase. Updates the name shown in the prefix
    ///     brackets.
    /// </summary>
    public void CycleWhisperTarget(int direction)
    {
        if ((WhisperHistory.Count == 0) || !IsWhisperNamePhase)
            return;

        WhisperHistoryIndex = (WhisperHistoryIndex + direction + WhisperHistory.Count) % WhisperHistory.Count;
        Hud.ChatInput.Prefix = $"to [{WhisperHistory[WhisperHistoryIndex]}]? ";
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
            AddWhisperTarget(targetName);
        } else
            Connection.SendPublicMessage(message);
    }

    /// <summary>
    ///     Focuses the chat input with the given prefix and text color.
    /// </summary>
    public void Focus(string prefix, Color textColor)
    {
        Hud.ChatInput.FocusedBackgroundColor = Color.Black;
        Hud.ChatInput.IsFocused = true;
        Hud.ChatInput.Prefix = prefix;
        Hud.ChatInput.ForegroundColor = textColor;
        Hud.SetDescription(null);
    }

    /// <summary>
    ///     Opens the ignore list management flow. Phase 1 shows a mode-selection prefix; single-keypress (a/d/?) advances to
    ///     the appropriate phase.
    /// </summary>
    public void FocusIgnore()
    {
        IgnorePhase = IgnorePhase.ModeSelect;
        Focus("a: add, d: delete, ?: see list>", TextColors.Default);
    }

    /// <summary>
    ///     Opens whisper mode in name selection phase. The most recent whisper target is shown in the prefix brackets. The
    ///     text field is left empty for the user to type a new name or press Enter to accept the bracketed default.
    /// </summary>
    public void FocusWhisper()
    {
        WhisperHistoryIndex = 0;
        var defaultName = WhisperHistory.Count > 0 ? WhisperHistory[0] : string.Empty;
        Focus($"to [{defaultName}]? ", TextColors.Whisper);
    }

    /// <summary>
    ///     Extracts the default whisper target name from the bracket prefix (e.g. "to [abcd]? " → "abcd").
    /// </summary>
    public string GetBracketedWhisperTarget()
    {
        var prefix = Hud.ChatInput.Prefix;
        var start = prefix.IndexOf('[') + 1;
        var end = prefix.IndexOf(']');

        if ((start <= 0) || (end < start))
            return string.Empty;

        return prefix[start..end];
    }

    /// <summary>
    ///     Transitions from ignore mode-select to the "add name" phase.
    /// </summary>
    public void TransitionIgnoreAdd()
    {
        IgnorePhase = IgnorePhase.AddName;
        Hud.ChatInput.Prefix = "ID of people you wish to reject whisper >";
        Hud.ChatInput.Text = string.Empty;
    }

    /// <summary>
    ///     Transitions from ignore mode-select to the "remove name" phase.
    /// </summary>
    public void TransitionIgnoreRemove()
    {
        IgnorePhase = IgnorePhase.RemoveName;
        Hud.ChatInput.Prefix = "ID of people you wish to cancel rejection of whisper >";
        Hud.ChatInput.Text = string.Empty;
    }

    /// <summary>
    ///     Unfocuses the chat input and clears its text/prefix.
    /// </summary>
    public void Unfocus()
    {
        IgnorePhase = IgnorePhase.None;
        Hud.ChatInput.IsFocused = false;
        Hud.ChatInput.Text = string.Empty;
        Hud.ChatInput.Prefix = string.Empty;
        Hud.ChatInput.ForegroundColor = Color.White;
    }
}
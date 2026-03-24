namespace Chaos.Client.Models;

/// <summary>
///     A single entry in the friends list.
/// </summary>
public sealed record FriendEntry(string Name, bool IsOnline);
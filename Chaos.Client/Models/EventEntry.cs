namespace Chaos.Client.Models;

/// <summary>
///     A single event/quest entry for the Events tab page.
/// </summary>
public record EventEntry(string Name, ushort IconSprite = 0, string Description = "");
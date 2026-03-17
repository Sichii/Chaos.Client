namespace Chaos.Client.Models;

/// <summary>
///     A single entry in the mail list.
/// </summary>
public record MailEntry(
    short PostId,
    string Author,
    int Month,
    int Day,
    string Subject,
    bool IsHighlighted);
#region
using Chaos.DarkAges.Definitions;
#endregion

namespace Chaos.Client.Models;

/// <summary>
///     A single entry in the online users world list.
/// </summary>
public sealed record WorldListEntry(
    string Name,
    string? Title,
    BaseClass BaseClass,
    bool IsMaster,
    bool IsGuilded,
    WorldListColor Color,
    SocialStatus SocialStatus);
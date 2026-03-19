namespace Chaos.Client.Data.Models;

/// <summary>
///     A skill or spell name paired with its chant line.
/// </summary>
public sealed class SkillChantEntry
{
    public string Chant { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
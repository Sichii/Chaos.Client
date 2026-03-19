namespace Chaos.Client.Data.Models;

/// <summary>
///     Family member names loaded from Familylist.cfg. Order: Mother, Father, Son1, Son2, Brother1-6.
/// </summary>
public sealed class FamilyList
{
    public string Brother1 { get; set; } = string.Empty;
    public string Brother2 { get; set; } = string.Empty;
    public string Brother3 { get; set; } = string.Empty;
    public string Brother4 { get; set; } = string.Empty;
    public string Brother5 { get; set; } = string.Empty;
    public string Brother6 { get; set; } = string.Empty;
    public string Father { get; set; } = string.Empty;
    public string Mother { get; set; } = string.Empty;
    public string Son1 { get; set; } = string.Empty;
    public string Son2 { get; set; } = string.Empty;
}
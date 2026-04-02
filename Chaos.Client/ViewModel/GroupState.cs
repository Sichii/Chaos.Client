#region
using Chaos.Client.Collections;
using Chaos.Extensions.Common;
#endregion

namespace Chaos.Client.ViewModel;

public delegate void GroupChangedHandler();

/// <summary>
///     Tracks the player's current group membership — members and leader.
/// </summary>
public sealed class GroupState
{
    public string? LeaderName { get; private set; }
    public List<string> Members { get; private set; } = [];
    public bool InGroup => Members.Count > 0;
    public bool IsLeader => InGroup && LeaderName?.EqualsI(WorldState.PlayerName) is true;

    public event GroupChangedHandler? Changed;

    public void Clear()
    {
        Members = [];
        LeaderName = null;
        Changed?.Invoke();
    }

    /// <summary>
    ///     Parses the server's GroupString format and updates state. Format: "Group members\n* Leader\n  Member2\nTotal N"
    /// </summary>
    public void ParseAndSet(string groupString)
    {
        var members = new List<string>();
        string? leader = null;

        foreach (var line in groupString.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWithI("Group members") || trimmed.StartsWithI("Total ") || (trimmed.Length == 0))
                continue;

            if (trimmed.StartsWithI("* "))
            {
                var name = trimmed[2..];
                leader = name;
                members.Add(name);
            } else
                members.Add(trimmed);
        }

        Members = members;
        LeaderName = leader;
        Changed?.Invoke();
    }
}
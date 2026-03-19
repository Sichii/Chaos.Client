#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data.Models;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud.SelfProfile;

/// <summary>
///     Family tab page (_nui_fm). Displays player and spouse name, plus 10 family stat labels.
/// </summary>
public sealed class SelfProfileFamilyTab : PrefabPanel
{
    private readonly UILabel? FamilyLabel;
    private readonly UILabel? SelfLabel;
    private readonly UILabel?[] TextLabels = new UILabel?[10];

    public SelfProfileFamilyTab(GraphicsDevice device, string prefabName)
        : base(device, prefabName, false)
    {
        Name = prefabName;
        Visible = false;

        AutoPopulate();

        SelfLabel = CreateLabel("Self");
        FamilyLabel = CreateLabel("Family");

        for (var i = 0; i < 10; i++)
            TextLabels[i] = CreateLabel($"Text{i}");
    }

    public FamilyList GetFamilyMembers()
        => new()
        {
            Mother = TextLabels[0]?.Text ?? string.Empty,
            Father = TextLabels[1]?.Text ?? string.Empty,
            Son1 = TextLabels[2]?.Text ?? string.Empty,
            Son2 = TextLabels[3]?.Text ?? string.Empty,
            Brother1 = TextLabels[4]?.Text ?? string.Empty,
            Brother2 = TextLabels[5]?.Text ?? string.Empty,
            Brother3 = TextLabels[6]?.Text ?? string.Empty,
            Brother4 = TextLabels[7]?.Text ?? string.Empty,
            Brother5 = TextLabels[8]?.Text ?? string.Empty,
            Brother6 = TextLabels[9]?.Text ?? string.Empty
        };

    /// <summary>
    ///     Updates the player and spouse names.
    /// </summary>
    public void SetFamilyInfo(string selfName, string spouseName)
    {
        SelfLabel?.SetText(selfName);
        FamilyLabel?.SetText(spouseName);
    }

    /// <summary>
    ///     Sets a specific family stat text field (0-9).
    /// </summary>
    public void SetTextField(int index, string text)
    {
        if (index is >= 0 and < 10)
            TextLabels[index]
                ?.SetText(text);
    }
}
#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World;

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
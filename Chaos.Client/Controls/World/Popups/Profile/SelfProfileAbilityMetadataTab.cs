#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Scrolling;
using Chaos.Client.Data.Models;
using Chaos.Client.ViewModel;
using Chaos.Extensions.Common;
using Microsoft.Xna.Framework;
#endregion

namespace Chaos.Client.Controls.World.Popups.Profile;

/// <summary>
///     Skills tab page (_nui_sk). Two-column layout: SPELL (left) and SKILL (right). Each column
///     holds rows of AbilityMetadataEntryControl instances with a scrollbar on the right edge. When more
///     entries exist below the visible area, the top of the next entry peeks at the bottom.
/// </summary>
public sealed class SelfProfileAbilityMetadataTab : PrefabPanel
{
    private const int ROW_HEIGHT = 45;

    private readonly VirtualizedRowList<AbilityMetadataEntry> SkillList;
    private readonly VirtualizedRowList<AbilityMetadataEntry> SpellList;

    public SelfProfileAbilityMetadataTab(string prefabName)
        : base(prefabName, false)
    {
        Name = prefabName;
        Visible = false;

        var spellRect = GetRect("SPELL");
        var skillRect = GetRect("SKILL");

        if (spellRect == Rectangle.Empty)
            spellRect = new Rectangle(
                32,
                33,
                233,
                239);

        if (skillRect == Rectangle.Empty)
            skillRect = new Rectangle(
                331,
                33,
                233,
                239);

        SpellList = CreateColumn(spellRect);
        SkillList = CreateColumn(skillRect);

        //the per-entry icon state depends on the player's books/stats, which can change while the tab is hidden, so
        //re-bind both columns whenever the tab becomes visible again.
        VisibilityChanged += visible =>
        {
            if (visible)
            {
                SpellList.Invalidate();
                SkillList.Invalidate();
            }
        };
    }

    private VirtualizedRowList<AbilityMetadataEntry> CreateColumn(Rectangle columnRect)
    {
        //overscanRows:1 gives the bottom "peek" row; the generic clips it to the column bounds.
        var list = new VirtualizedRowList<AbilityMetadataEntry>(
            columnRect.Width,
            columnRect.Height,
            ROW_HEIGHT,
            () =>
            {
                var row = new AbilityMetadataEntryControl();
                row.OnClicked += entry => OnEntryClicked?.Invoke(entry);

                return row;
            },
            BindRow,
            overscanRows: 1);

        var viewer = new ScrollViewerControl(list)
        {
            X = columnRect.X,
            Y = columnRect.Y,
            Width = columnRect.Width,
            Height = columnRect.Height
        };

        AddChild(viewer);

        return list;
    }

    private void BindRow(UIElement row, AbilityMetadataEntry entry, bool selected)
        => ((AbilityMetadataEntryControl)row).SetEntry(entry, ResolveIconState(entry));

    /// <summary>
    ///     Clears all entries from both columns.
    /// </summary>
    public void ClearAll()
    {
        SpellList.SetItems([]);
        SkillList.SetItems([]);
    }

    private static bool HasPreRequisite(string? name, byte requiredLevel)
    {
        if (name is null)
            return true;

        //check spell book
        for (byte i = 1; i <= SpellBook.MAX_SLOTS; i++)
        {
            ref readonly var slot = ref WorldState.SpellBook.GetSlot(i);

            if (slot.IsOccupied && (slot.AbilityName?.EqualsI(name) == true) && (slot.CurrentLevel >= requiredLevel))
                return true;
        }

        //check skill book
        for (byte i = 1; i <= SkillBook.MAX_SLOTS; i++)
        {
            ref readonly var slot = ref WorldState.SkillBook.GetSlot(i);

            if (slot.IsOccupied && (slot.AbilityName?.EqualsI(name) == true) && (slot.CurrentLevel >= requiredLevel))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Fired when any entry row is clicked.
    /// </summary>
    public event AbilityMetadataClickedHandler? OnEntryClicked;

    private static AbilityIconState ResolveIconState(AbilityMetadataEntry entry)
    {
        //check if player already knows this ability
        if (entry.IsSpell)
            for (byte i = 1; i <= SpellBook.MAX_SLOTS; i++)
            {
                ref readonly var slot = ref WorldState.SpellBook.GetSlot(i);

                if (slot.IsOccupied && (slot.AbilityName?.EqualsI(entry.Name) == true))
                    return AbilityIconState.Known;
            }
        else
            for (byte i = 1; i <= SkillBook.MAX_SLOTS; i++)
            {
                ref readonly var slot = ref WorldState.SkillBook.GetSlot(i);

                if (slot.IsOccupied && (slot.AbilityName?.EqualsI(entry.Name) == true))
                    return AbilityIconState.Known;
            }

        //check if player meets the requirements to learn it
        if (WorldState.Attributes.Current is not { } attrs)
            return AbilityIconState.Locked;

        if (attrs.Level < entry.Level)
            return AbilityIconState.Locked;

        if ((attrs.Str < entry.Str)
            || (attrs.Int < entry.Int)
            || (attrs.Wis < entry.Wis)
            || (attrs.Dex < entry.Dex)
            || (attrs.Con < entry.Con))
            return AbilityIconState.Locked;

        //check prerequisite abilities
        if (!HasPreRequisite(entry.PreReq1Name, entry.PreReq1Level))
            return AbilityIconState.Locked;

        if (!HasPreRequisite(entry.PreReq2Name, entry.PreReq2Level))
            return AbilityIconState.Locked;

        return AbilityIconState.Learnable;
    }

    /// <summary>
    ///     Populates both columns from parsed ability metadata.
    /// </summary>
    public void SetAbilityMetadata(AbilityMetadata metadata)
    {
        SpellList.SetItems(metadata.Spells);
        SkillList.SetItems(metadata.Skills);
    }
}

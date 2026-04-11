#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Data.Models;
using Chaos.Client.ViewModel;
using Chaos.Extensions.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Popups.Profile;

/// <summary>
///     Skills tab page (_nui_sk). Two-column layout: SPELL (left) and SKILL (right). Each column
///     holds rows of AbilityEntryControl instances with a scrollbar on the right edge. When more
///     entries exist below the visible area, the top of the next entry peeks at the bottom.
/// </summary>
public sealed class SelfProfileAbilityMetadataTab : PrefabPanel
{
    private const int ROW_HEIGHT = 45;
    private const int MAX_VISIBLE_ROWS = 5;

    //one extra row for the peek effect at the bottom of each column
    private const int DISPLAY_ROWS = MAX_VISIBLE_ROWS + 1;

    private static readonly RasterizerState ScissorRasterizer = new()
    {
        ScissorTestEnable = true
    };

    private readonly Rectangle SkillRect;

    private readonly AbilityMetadataEntryControl[] SkillRows;
    private readonly ScrollBarControl SkillScrollBar;
    private readonly Rectangle SpellRect;
    private readonly AbilityMetadataEntryControl[] SpellRows;
    private readonly ScrollBarControl SpellScrollBar;

    private bool Dirty = true;
    private IReadOnlyList<AbilityMetadataEntry> SkillEntries = [];
    private int SkillScrollOffset;
    private IReadOnlyList<AbilityMetadataEntry> SpellEntries = [];
    private int SpellScrollOffset;

    public SelfProfileAbilityMetadataTab(string prefabName)
        : base(prefabName, false)
    {
        Name = prefabName;
        Visible = false;

        VisibilityChanged += visible =>
        {
            if (visible)
                Dirty = true;
        };

        SpellRect = GetRect("SPELL");
        SkillRect = GetRect("SKILL");

        if (SpellRect == Rectangle.Empty)
            SpellRect = new Rectangle(
                32,
                33,
                233,
                239);

        if (SkillRect == Rectangle.Empty)
            SkillRect = new Rectangle(
                331,
                33,
                233,
                239);

        SpellRows = CreateColumn(SpellRect);
        SkillRows = CreateColumn(SkillRect);

        SpellScrollBar = CreateScrollBar(
            SpellRect,
            v =>
            {
                SpellScrollOffset = v;
                Dirty = true;
            });

        SkillScrollBar = CreateScrollBar(
            SkillRect,
            v =>
            {
                SkillScrollOffset = v;
                Dirty = true;
            });
    }

    /// <summary>
    ///     Clears all entries from both columns.
    /// </summary>
    public void ClearAll()
    {
        SpellEntries = [];
        SkillEntries = [];
        SpellScrollOffset = 0;
        SkillScrollOffset = 0;
        SpellScrollBar.Value = 0;
        SpellScrollBar.TotalItems = 0;
        SpellScrollBar.MaxValue = 0;
        SkillScrollBar.Value = 0;
        SkillScrollBar.TotalItems = 0;
        SkillScrollBar.MaxValue = 0;
        Dirty = true;
    }

    private AbilityMetadataEntryControl[] CreateColumn(Rectangle columnRect)
    {
        var rows = new AbilityMetadataEntryControl[DISPLAY_ROWS];
        var columnBottom = columnRect.Y + columnRect.Height;

        for (var i = 0; i < DISPLAY_ROWS; i++)
        {
            var row = new AbilityMetadataEntryControl
            {
                X = columnRect.X,
                Y = columnRect.Y + i * ROW_HEIGHT,
                Visible = false
            };

            //clip the peek row's hit-test area to the column bounds
            var maxHeight = columnBottom - row.Y;

            if (maxHeight < row.Height)
                row.Height = maxHeight;

            row.OnClicked += entry => OnEntryClicked?.Invoke(entry);
            AddChild(row);
            rows[i] = row;
        }

        return rows;
    }

    private ScrollBarControl CreateScrollBar(Rectangle columnRect, ScrollValueChangedHandler onValueChanged)
    {
        var scrollBar = new ScrollBarControl
        {
            X = columnRect.X + columnRect.Width - ScrollBarControl.DEFAULT_WIDTH,
            Y = columnRect.Y,
            Height = columnRect.Height,
            VisibleItems = MAX_VISIBLE_ROWS
        };

        scrollBar.OnValueChanged += onValueChanged;
        AddChild(scrollBar);

        return scrollBar;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        //hide entry rows so base.draw() only renders background + scrollbars
        SetRowVisibilityForBaseDraw(false);
        base.Draw(spriteBatch);
        SetRowVisibilityForBaseDraw(true);

        //draw each column's rows clipped to the column rect
        var device = spriteBatch.GraphicsDevice;
        var sx = ScreenX;
        var sy = ScreenY;

        DrawClippedColumn(
            spriteBatch,
            device,
            SpellRows,
            new Rectangle(
                sx + SpellRect.X,
                sy + SpellRect.Y,
                SpellRect.Width,
                SpellRect.Height));

        DrawClippedColumn(
            spriteBatch,
            device,
            SkillRows,
            new Rectangle(
                sx + SkillRect.X,
                sy + SkillRect.Y,
                SkillRect.Width,
                SkillRect.Height));
    }

    private static void DrawClippedColumn(
        SpriteBatch spriteBatch,
        GraphicsDevice device,
        AbilityMetadataEntryControl[] rows,
        Rectangle clipRect)
    {
        spriteBatch.End();

        var prevScissor = device.ScissorRectangle;
        device.ScissorRectangle = clipRect;

        spriteBatch.Begin(samplerState: GlobalSettings.Sampler, rasterizerState: ScissorRasterizer);

        foreach (var row in rows)
            if (row.Visible)
                row.Draw(spriteBatch);

        spriteBatch.End();

        device.ScissorRectangle = prevScissor;
        spriteBatch.Begin(samplerState: GlobalSettings.Sampler);
    }

    private static bool HasPreRequisite(string? name, byte requiredLevel)
    {
        if (name is null)
            return true;

        //check spell book
        for (byte i = 1; i <= SpellBook.MAX_SLOTS; i++)
        {
            ref readonly var slot = ref WorldState.SpellBook.GetSlot(i);

            if (slot.IsOccupied && slot.AbilityName?.EqualsI(name) == true && slot.CurrentLevel >= requiredLevel)
                return true;
        }

        //check skill book
        for (byte i = 1; i <= SkillBook.MAX_SLOTS; i++)
        {
            ref readonly var slot = ref WorldState.SkillBook.GetSlot(i);

            if (slot.IsOccupied && slot.AbilityName?.EqualsI(name) == true && slot.CurrentLevel >= requiredLevel)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Fired when any entry row is clicked.
    /// </summary>
    public event AbilityMetadataClickedHandler? OnEntryClicked;

    private static void RefreshColumn(AbilityMetadataEntryControl[] rows, IReadOnlyList<AbilityMetadataEntry> entries, int scrollOffset)
    {
        for (var i = 0; i < rows.Length; i++)
        {
            var entryIndex = scrollOffset + i;

            if (entryIndex < entries.Count)
            {
                var entry = entries[entryIndex];
                var state = ResolveIconState(entry);

                rows[i]
                    .SetEntry(entry, state);
            } else
                rows[i]
                    .Clear();
        }
    }

    private void RefreshRows()
    {
        if (!Dirty)
            return;

        Dirty = false;

        RefreshColumn(SpellRows, SpellEntries, SpellScrollOffset);

        RefreshColumn(SkillRows, SkillEntries, SkillScrollOffset);
    }

    private static AbilityIconState ResolveIconState(AbilityMetadataEntry entry)
    {
        //check if player already knows this ability
        if (entry.IsSpell)
            for (byte i = 1; i <= SpellBook.MAX_SLOTS; i++)
            {
                ref readonly var slot = ref WorldState.SpellBook.GetSlot(i);

                if (slot.IsOccupied && slot.AbilityName?.EqualsI(entry.Name) == true)
                    return AbilityIconState.Known;
            }
        else
            for (byte i = 1; i <= SkillBook.MAX_SLOTS; i++)
            {
                ref readonly var slot = ref WorldState.SkillBook.GetSlot(i);

                if (slot.IsOccupied && slot.AbilityName?.EqualsI(entry.Name) == true)
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
        SpellEntries = metadata.Spells;
        SkillEntries = metadata.Skills;

        SpellScrollOffset = 0;
        SpellScrollBar.Value = 0;
        SpellScrollBar.TotalItems = SpellEntries.Count;
        SpellScrollBar.MaxValue = Math.Max(0, SpellEntries.Count - MAX_VISIBLE_ROWS);

        SkillScrollOffset = 0;
        SkillScrollBar.Value = 0;
        SkillScrollBar.TotalItems = SkillEntries.Count;
        SkillScrollBar.MaxValue = Math.Max(0, SkillEntries.Count - MAX_VISIBLE_ROWS);
        Dirty = true;
    }

    /// <summary>
    ///     Temporarily hides/restores entry rows that have data so base.Draw() skips them. Rows without data stay hidden.
    /// </summary>
    private void SetRowVisibilityForBaseDraw(bool visible)
    {
        foreach (var row in SpellRows)
            if (row.Entry is not null)
                row.Visible = visible;

        foreach (var row in SkillRows)
            if (row.Entry is not null)
                row.Visible = visible;
    }

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        var sx = ScreenX;
        var sy = ScreenY;
        var spellScreenRect = new Rectangle(sx + SpellRect.X, sy + SpellRect.Y, SpellRect.Width, SpellRect.Height);
        var skillScreenRect = new Rectangle(sx + SkillRect.X, sy + SkillRect.Y, SkillRect.Width, SkillRect.Height);

        if (spellScreenRect.Contains(e.ScreenX, e.ScreenY) && (SpellEntries.Count > MAX_VISIBLE_ROWS))
        {
            SpellScrollOffset = Math.Clamp(SpellScrollOffset - e.Delta, 0, SpellEntries.Count - MAX_VISIBLE_ROWS);
            SpellScrollBar.Value = SpellScrollOffset;
            Dirty = true;
            e.Handled = true;
        } else if (skillScreenRect.Contains(e.ScreenX, e.ScreenY) && (SkillEntries.Count > MAX_VISIBLE_ROWS))
        {
            SkillScrollOffset = Math.Clamp(SkillScrollOffset - e.Delta, 0, SkillEntries.Count - MAX_VISIBLE_ROWS);
            SkillScrollBar.Value = SkillScrollOffset;
            Dirty = true;
            e.Handled = true;
        }
    }

    public override void Update(GameTime gameTime)
    {
        if (!Visible || !Enabled)
            return;

        RefreshRows();
        base.Update(gameTime);
    }

}
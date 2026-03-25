#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Data.Models;
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

    // One extra row for the peek effect at the bottom of each column
    private const int DISPLAY_ROWS = MAX_VISIBLE_ROWS + 1;

    private static readonly RasterizerState ScissorRasterizer = new()
    {
        ScissorTestEnable = true
    };

    private readonly Rectangle SkillRect;

    private readonly AbilityEntryControl[] SkillRows;
    private readonly ScrollBarControl SkillScrollBar;
    private readonly Rectangle SpellRect;
    private readonly AbilityEntryControl[] SpellRows;
    private readonly ScrollBarControl SpellScrollBar;

    private int DataVersion;

    private Func<AbilityMetadataEntry, AbilityIconState>? IconStateResolver;
    private int RenderedVersion = -1;

    private IReadOnlyList<AbilityMetadataEntry> SkillEntries = [];
    private int SkillScrollOffset;
    private IReadOnlyList<AbilityMetadataEntry> SpellEntries = [];
    private int SpellScrollOffset;

    public SelfProfileAbilityMetadataTab(string prefabName)
        : base(prefabName, false)
    {
        Name = prefabName;
        Visible = false;

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
                DataVersion++;
            });

        SkillScrollBar = CreateScrollBar(
            SkillRect,
            v =>
            {
                SkillScrollOffset = v;
                DataVersion++;
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
        DataVersion++;
    }

    private AbilityEntryControl[] CreateColumn(Rectangle columnRect)
    {
        var rows = new AbilityEntryControl[DISPLAY_ROWS];

        for (var i = 0; i < DISPLAY_ROWS; i++)
        {
            var row = new AbilityEntryControl
            {
                X = columnRect.X,
                Y = columnRect.Y + i * ROW_HEIGHT,
                Visible = false
            };

            row.OnClicked += entry => OnEntryClicked?.Invoke(entry);
            AddChild(row);
            rows[i] = row;
        }

        return rows;
    }

    private ScrollBarControl CreateScrollBar(Rectangle columnRect, Action<int> onValueChanged)
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

        RefreshRows();

        // Hide entry rows so base.Draw() only renders background + scrollbars
        SetRowVisibilityForBaseDraw(false);
        base.Draw(spriteBatch);
        SetRowVisibilityForBaseDraw(true);

        // Draw each column's rows clipped to the column rect
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
        AbilityEntryControl[] rows,
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

    /// <summary>
    ///     Fired when any entry row is clicked.
    /// </summary>
    public event Action<AbilityMetadataEntry>? OnEntryClicked;

    private static void RefreshColumn(
        AbilityEntryControl[] rows,
        IReadOnlyList<AbilityMetadataEntry> entries,
        int scrollOffset,
        Func<AbilityMetadataEntry, AbilityIconState>? iconStateResolver)
    {
        for (var i = 0; i < rows.Length; i++)
        {
            var entryIndex = scrollOffset + i;

            if (entryIndex < entries.Count)
            {
                var entry = entries[entryIndex];
                var state = iconStateResolver?.Invoke(entry) ?? AbilityIconState.Locked;

                rows[i]
                    .SetEntry(entry, state);
            } else
                rows[i]
                    .Clear();
        }
    }

    private void RefreshRows()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        RefreshColumn(
            SpellRows,
            SpellEntries,
            SpellScrollOffset,
            IconStateResolver);

        RefreshColumn(
            SkillRows,
            SkillEntries,
            SkillScrollOffset,
            IconStateResolver);
    }

    /// <summary>
    ///     Populates both columns from parsed ability metadata.
    /// </summary>
    public void SetAbilityMetadata(AbilityMetadata metadata, Func<AbilityMetadataEntry, AbilityIconState> iconStateResolver)
    {
        IconStateResolver = iconStateResolver;
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

        DataVersion++;
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

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        // Update click clip bounds so peek rows ignore clicks in the clipped area
        var sx = ScreenX;
        var sy = ScreenY;

        var spellClip = new Rectangle(
            sx + SpellRect.X,
            sy + SpellRect.Y,
            SpellRect.Width,
            SpellRect.Height);

        var skillClip = new Rectangle(
            sx + SkillRect.X,
            sy + SkillRect.Y,
            SkillRect.Width,
            SkillRect.Height);

        foreach (var row in SpellRows)
            row.ClickClipBounds = spellClip;

        foreach (var row in SkillRows)
            row.ClickClipBounds = skillClip;

        base.Update(gameTime, input);

        // Scroll wheel — determine which column based on mouse position
        if (input.ScrollDelta != 0)
        {
            var mx = input.MouseX - ScreenX;

            if ((mx >= SpellRect.X) && (mx < (SpellRect.X + SpellRect.Width)) && (SpellEntries.Count > MAX_VISIBLE_ROWS))
            {
                SpellScrollOffset = Math.Clamp(SpellScrollOffset - input.ScrollDelta, 0, SpellEntries.Count - MAX_VISIBLE_ROWS);
                SpellScrollBar.Value = SpellScrollOffset;
                DataVersion++;
            } else if ((mx >= SkillRect.X) && (mx < (SkillRect.X + SkillRect.Width)) && (SkillEntries.Count > MAX_VISIBLE_ROWS))
            {
                SkillScrollOffset = Math.Clamp(SkillScrollOffset - input.ScrollDelta, 0, SkillEntries.Count - MAX_VISIBLE_ROWS);
                SkillScrollBar.Value = SkillScrollOffset;
                DataVersion++;
            }
        }
    }
}
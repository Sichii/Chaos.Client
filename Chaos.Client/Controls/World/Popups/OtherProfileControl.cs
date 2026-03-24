#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Controls.World.Hud.SelfProfile;
using Chaos.Client.Models;
using Chaos.Client.Rendering;
using Chaos.Client.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SkiaSharp;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Read-only profile viewer popup shown when clicking another player. Displays player info header (name, class, guild,
///     title), scrollable legend marks, and word-wrapped profile text. Composites the entire panel as a single texture
///     with DialogFrame border, then overlays CachedText labels and LegendMarkControl rows on top.
/// </summary>
public sealed class OtherProfileControl : UIPanel
{
    private const int PANEL_WIDTH = 350;
    private const int PANEL_MAX_HEIGHT = 420;

    // Layout constants
    private const int PADDING = 16;
    private const int HEADER_TOP = 20;
    private const int SECTION_GAP = 8;
    private const int LINE_HEIGHT = TextRenderer.CHAR_HEIGHT;

    // Legend section
    private const int MAX_VISIBLE_LEGEND_ROWS = 8;
    private const int LEGEND_ROW_HEIGHT = 20;
    private const int LEGEND_LIST_HEIGHT = MAX_VISIBLE_LEGEND_ROWS * LEGEND_ROW_HEIGHT;

    // Profile text section
    private const int MAX_PROFILE_HEIGHT = 80;
    private const int PROFILE_TEXT_WIDTH = PANEL_WIDTH - PADDING * 2;
    private readonly CachedText ClassLabel;

    private readonly CachedText GroupStatusLabel;
    private readonly CachedText GuildLabel;
    private readonly CachedText LegendHeaderLabel;
    private readonly Texture2D[] LegendIconFrames;
    private readonly LegendMarkControl[] LegendRows;
    private readonly ScrollBarControl LegendScrollBar;
    private readonly CachedText NameLabel;
    private readonly CachedText TitleLabel;
    private int ComputedHeight;
    private int LegendDataVersion;
    private int LegendRenderedVersion = -1;
    private int LegendScrollOffset;

    // Computed layout positions (set during Show)
    private int LegendSectionY;

    private List<LegendMarkEntry> Marks = [];
    private int ProfileSectionY;
    private int ProfileTextHeight;

    private Texture2D? ProfileTextTexture;

    public OtherProfileControl()
    {
        Name = "OtherProfile";
        Visible = false;

        NameLabel = new CachedText();
        ClassLabel = new CachedText();
        GuildLabel = new CachedText();
        TitleLabel = new CachedText();
        GroupStatusLabel = new CachedText();
        LegendHeaderLabel = new CachedText();
        LegendHeaderLabel.Update("Legend", new Color(255, 200, 100));

        // Legend mark rows
        LegendRows = new LegendMarkControl[MAX_VISIBLE_LEGEND_ROWS];

        for (var i = 0; i < MAX_VISIBLE_LEGEND_ROWS; i++)
        {
            LegendRows[i] = new LegendMarkControl
            {
                Name = $"LegendRow{i}",
                Width = PANEL_WIDTH - PADDING * 2 - ScrollBarControl.DEFAULT_WIDTH,
                Height = LEGEND_ROW_HEIGHT
            };

            AddChild(LegendRows[i]);
        }

        // Legend scrollbar
        LegendScrollBar = new ScrollBarControl
        {
            Name = "LegendScroll",
            Height = LEGEND_LIST_HEIGHT,
            VisibleItems = MAX_VISIBLE_LEGEND_ROWS
        };

        LegendScrollBar.OnValueChanged += v =>
        {
            LegendScrollOffset = v;
            LegendDataVersion++;
        };

        AddChild(LegendScrollBar);

        // Load legend icons (same asset as SelfProfileLegendTab)
        var cache = UiRenderer.Instance!;
        var frameCount = cache.GetEpfFrameCount("legends.epf");
        LegendIconFrames = new Texture2D[frameCount];

        for (var i = 0; i < frameCount; i++)
            LegendIconFrames[i] = cache.GetEpfTexture("legends.epf", i);
    }

    private void ComputeLayout()
    {
        var y = HEADER_TOP;

        // Name line
        y += LINE_HEIGHT + 2;

        // Class line
        y += LINE_HEIGHT + 2;

        // Guild line (only if present)
        if (GuildLabel.Texture is not null)
            y += LINE_HEIGHT + 2;

        // Title line (only if present)
        if (TitleLabel.Texture is not null)
            y += LINE_HEIGHT + 2;

        // Group status
        y += LINE_HEIGHT;

        // Gap before legend section
        y += SECTION_GAP;

        // Legend section header ("Legend")
        y += LINE_HEIGHT + 4;

        LegendSectionY = y;

        // Legend list area
        var legendHeight = Marks.Count > 0 ? Math.Min(Marks.Count, MAX_VISIBLE_LEGEND_ROWS) * LEGEND_ROW_HEIGHT : LEGEND_ROW_HEIGHT;

        // Always reserve full height when scrollable
        if (Marks.Count > MAX_VISIBLE_LEGEND_ROWS)
            legendHeight = LEGEND_LIST_HEIGHT;

        y += legendHeight;

        // Profile text section
        if (ProfileTextHeight > 0)
        {
            y += SECTION_GAP;
            ProfileSectionY = y;
            y += ProfileTextHeight;
        } else
            ProfileSectionY = y;

        // Bottom padding
        y += PADDING;

        ComputedHeight = Math.Min(y, PANEL_MAX_HEIGHT);
    }

    public override void Dispose()
    {
        NameLabel.Dispose();
        ClassLabel.Dispose();
        GuildLabel.Dispose();
        TitleLabel.Dispose();
        GroupStatusLabel.Dispose();
        LegendHeaderLabel.Dispose();
        ProfileTextTexture?.Dispose();

        foreach (var t in LegendIconFrames)
            t.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        // Draw background (from UIPanel base)
        RefreshLegendRows();
        base.Draw(spriteBatch);

        // Draw header labels manually (not children — they're CachedText, not UIElements)
        var sx = ScreenX + PADDING;
        var sy = ScreenY + HEADER_TOP;

        // Name
        NameLabel.Draw(spriteBatch, new Vector2(sx, sy));
        sy += LINE_HEIGHT + 2;

        // Class
        ClassLabel.Draw(spriteBatch, new Vector2(sx, sy));
        sy += LINE_HEIGHT + 2;

        // Guild
        if (GuildLabel.Texture is not null)
        {
            GuildLabel.Draw(spriteBatch, new Vector2(sx, sy));
            sy += LINE_HEIGHT + 2;
        }

        // Title
        if (TitleLabel.Texture is not null)
        {
            TitleLabel.Draw(spriteBatch, new Vector2(sx, sy));
            sy += LINE_HEIGHT + 2;
        }

        // Group status
        GroupStatusLabel.Draw(spriteBatch, new Vector2(sx, sy));
        sy += LINE_HEIGHT;

        // Separator line before legend
        sy += SECTION_GAP / 2;

        DrawRect(
            spriteBatch,
            new Rectangle(
                ScreenX + PADDING,
                sy,
                Width - PADDING * 2,
                1),
            new Color(80, 80, 100));
        sy += SECTION_GAP / 2;

        // Legend section header
        LegendHeaderLabel.Draw(spriteBatch, new Vector2(sx, sy));

        // Profile text
        if (ProfileTextTexture is not null)
        {
            // Separator before profile text
            var sepY = ScreenY + ProfileSectionY - SECTION_GAP / 2;

            DrawRect(
                spriteBatch,
                new Rectangle(
                    ScreenX + PADDING,
                    sepY,
                    Width - PADDING * 2,
                    1),
                new Color(80, 80, 100));

            spriteBatch.Draw(ProfileTextTexture, new Vector2(ScreenX + PADDING, ScreenY + ProfileSectionY), Color.White);
        }
    }

    public void Hide() => Visible = false;

    private void RebuildBackground()
    {
        Background?.Dispose();

        using var bgImage = DialogFrame.Composite(
            new SKColor(
                10,
                10,
                20,
                240),
            Width,
            Height);

        if (bgImage is not null)
            Background = TextureConverter.ToTexture2D(bgImage);
    }

    private void RefreshLegendRows()
    {
        if (LegendRenderedVersion == LegendDataVersion)
            return;

        LegendRenderedVersion = LegendDataVersion;

        for (var i = 0; i < MAX_VISIBLE_LEGEND_ROWS; i++)
        {
            var markIndex = LegendScrollOffset + i;

            if (markIndex < Marks.Count)
            {
                var mark = Marks[markIndex];
                var icon = mark.Icon < LegendIconFrames.Length ? LegendIconFrames[mark.Icon] : null;
                var iconWidth = icon?.Width ?? 21;
                var iconHeight = icon?.Height ?? 20;

                LegendRows[i]
                    .SetMark(
                        icon,
                        mark.Text,
                        mark.Color,
                        iconWidth,
                        iconHeight);
                LegendRows[i].Visible = true;
            } else
            {
                LegendRows[i]
                    .Clear();
                LegendRows[i].Visible = false;
            }
        }
    }

    /// <summary>
    ///     Populates and shows the profile viewer with the given player data.
    /// </summary>
    public void Show(
        string name,
        string displayClass,
        string? guildName,
        string? guildRank,
        string? title,
        bool groupOpen,
        List<LegendMarkEntry> legendMarks,
        string? profileText)
    {
        // Update header labels
        NameLabel.Update(name, Color.White);
        ClassLabel.Update(displayClass, new Color(200, 200, 200));

        var guildText = string.Empty;

        if (!string.IsNullOrEmpty(guildName))
        {
            guildText = guildName;

            if (!string.IsNullOrEmpty(guildRank))
                guildText = $"{guildRank} of {guildName}";
        }

        GuildLabel.Update(guildText, new Color(100, 149, 237));

        TitleLabel.Update(title ?? string.Empty, new Color(255, 200, 100));

        GroupStatusLabel.Update(
            groupOpen ? "Group: Open" : "Group: Closed",
            groupOpen ? new Color(150, 255, 150) : new Color(200, 200, 200));

        // Legend marks
        Marks = legendMarks;
        LegendScrollOffset = 0;
        LegendScrollBar.Value = 0;
        LegendScrollBar.TotalItems = legendMarks.Count;
        LegendScrollBar.MaxValue = Math.Max(0, legendMarks.Count - MAX_VISIBLE_LEGEND_ROWS);
        LegendDataVersion++;

        // Profile text — render wrapped text as a texture
        ProfileTextTexture?.Dispose();
        ProfileTextTexture = null;
        ProfileTextHeight = 0;

        if (!string.IsNullOrWhiteSpace(profileText))
        {
            ProfileTextTexture = TextRenderer.RenderWrappedText(
                profileText,
                PROFILE_TEXT_WIDTH,
                MAX_PROFILE_HEIGHT,
                new Color(220, 220, 220));
            ProfileTextHeight = Math.Min(ProfileTextTexture.Height, MAX_PROFILE_HEIGHT);
        }

        // Compute layout
        ComputeLayout();

        // Panel dimensions
        Width = PANEL_WIDTH;
        Height = ComputedHeight;
        X = (ChaosGame.VIRTUAL_WIDTH - Width) / 2;
        Y = (ChaosGame.VIRTUAL_HEIGHT - Height) / 2;

        // Position legend rows and scrollbar based on computed layout
        for (var i = 0; i < MAX_VISIBLE_LEGEND_ROWS; i++)
        {
            LegendRows[i].X = PADDING;
            LegendRows[i].Y = LegendSectionY + i * LEGEND_ROW_HEIGHT;
        }

        LegendScrollBar.X = PANEL_WIDTH - PADDING - ScrollBarControl.DEFAULT_WIDTH;
        LegendScrollBar.Y = LegendSectionY;

        // Composite background texture
        RebuildBackground();

        Visible = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        // Escape or right-click to close
        if (input.WasKeyPressed(Keys.Escape) || input.WasRightButtonPressed)
        {
            Hide();

            return;
        }

        // Left-click outside panel bounds to close
        if (input.WasLeftButtonPressed && !ContainsPoint(input.MouseX, input.MouseY))
        {
            Hide();

            return;
        }

        base.Update(gameTime, input);

        // Scroll wheel for legend marks
        if ((input.ScrollDelta != 0) && (Marks.Count > MAX_VISIBLE_LEGEND_ROWS))
        {
            LegendScrollOffset = Math.Clamp(LegendScrollOffset - input.ScrollDelta, 0, Marks.Count - MAX_VISIBLE_LEGEND_ROWS);
            LegendScrollBar.Value = LegendScrollOffset;
            LegendDataVersion++;
        }
    }
}
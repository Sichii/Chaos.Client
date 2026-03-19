#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud.SelfProfile;

/// <summary>
///     Skills tab page (_nui_sk). Two-column layout: SPELL (left) and SKILL (right). Each column holds rows of skill/spell
///     entries rendered using _nui_ski template (32x32 icon, name, level text, 43px per row).
/// </summary>
public sealed class SelfProfileAbilityMetadataTab : PrefabPanel
{
    private const int ROW_HEIGHT = 43;
    private const int ICON_SIZE = 32;
    private const int ICON_X = 7;
    private const int ICON_Y = 7;
    private const int NAME_X = 48;
    private const int NAME_Y = 7;
    private const int LEVEL_X = 48;
    private const int LEVEL_Y = 27;
    private const int MAX_ENTRIES_PER_COLUMN = 5;

    private readonly List<SkillSpellEntry> SkillEntries = [];
    private readonly Texture2D?[] SkillIcons;
    private readonly CachedText[] SkillLevelCaches;
    private readonly CachedText[] SkillNameCaches;
    private readonly Rectangle SkillRect;

    private readonly List<SkillSpellEntry> SpellEntries = [];
    private readonly Texture2D?[] SpellIcons;
    private readonly CachedText[] SpellLevelCaches;

    private readonly CachedText[] SpellNameCaches;
    private readonly Rectangle SpellRect;

    private int DataVersion;
    private int RenderedVersion = -1;

    public SelfProfileAbilityMetadataTab(GraphicsDevice device, string prefabName)
        : base(device, prefabName, false)
    {
        Name = prefabName;
        Visible = false;

        AutoPopulate();

        SpellRect = GetRect("SPELL");
        SkillRect = GetRect("SKILL");

        // Default rects if not found
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

        SpellNameCaches = new CachedText[MAX_ENTRIES_PER_COLUMN];
        SpellLevelCaches = new CachedText[MAX_ENTRIES_PER_COLUMN];
        SkillNameCaches = new CachedText[MAX_ENTRIES_PER_COLUMN];
        SkillLevelCaches = new CachedText[MAX_ENTRIES_PER_COLUMN];
        SpellIcons = new Texture2D?[MAX_ENTRIES_PER_COLUMN];
        SkillIcons = new Texture2D?[MAX_ENTRIES_PER_COLUMN];

        for (var i = 0; i < MAX_ENTRIES_PER_COLUMN; i++)
        {
            SpellNameCaches[i] = new CachedText(device);
            SpellLevelCaches[i] = new CachedText(device);
            SkillNameCaches[i] = new CachedText(device);
            SkillLevelCaches[i] = new CachedText(device);
        }
    }

    /// <summary>
    ///     Clears all entries from both columns.
    /// </summary>
    public void ClearAll()
    {
        SpellEntries.Clear();
        SkillEntries.Clear();

        for (var i = 0; i < MAX_ENTRIES_PER_COLUMN; i++)
        {
            SpellIcons[i]
                ?.Dispose();
            SpellIcons[i] = null;

            SkillIcons[i]
                ?.Dispose();
            SkillIcons[i] = null;
        }

        DataVersion++;
    }

    public override void Dispose()
    {
        foreach (var c in SpellNameCaches)
            c.Dispose();

        foreach (var c in SpellLevelCaches)
            c.Dispose();

        foreach (var c in SkillNameCaches)
            c.Dispose();

        foreach (var c in SkillLevelCaches)
            c.Dispose();

        foreach (var t in SpellIcons)
            t?.Dispose();

        foreach (var t in SkillIcons)
            t?.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        RefreshCaches();

        var sx = ScreenX;
        var sy = ScreenY;

        DrawColumn(
            spriteBatch,
            SpellEntries,
            SpellIcons,
            SpellNameCaches,
            SpellLevelCaches,
            sx + SpellRect.X,
            sy + SpellRect.Y);

        DrawColumn(
            spriteBatch,
            SkillEntries,
            SkillIcons,
            SkillNameCaches,
            SkillLevelCaches,
            sx + SkillRect.X,
            sy + SkillRect.Y);
    }

    private void DrawColumn(
        SpriteBatch spriteBatch,
        List<SkillSpellEntry> entries,
        Texture2D?[] icons,
        CachedText[] nameCaches,
        CachedText[] levelCaches,
        int colX,
        int colY)
    {
        for (var i = 0; (i < MAX_ENTRIES_PER_COLUMN) && (i < entries.Count); i++)
        {
            var rowY = colY + i * ROW_HEIGHT;

            if (icons[i] is { } icon)
                spriteBatch.Draw(icon, new Vector2(colX + ICON_X, rowY + ICON_Y), Color.White);

            nameCaches[i]
                .Draw(spriteBatch, new Vector2(colX + NAME_X, rowY + NAME_Y));

            levelCaches[i]
                .Draw(spriteBatch, new Vector2(colX + LEVEL_X, rowY + LEVEL_Y));
        }
    }

    private void RefreshCaches()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        RefreshColumnCaches(
            SpellEntries,
            SpellIcons,
            SpellNameCaches,
            SpellLevelCaches);

        RefreshColumnCaches(
            SkillEntries,
            SkillIcons,
            SkillNameCaches,
            SkillLevelCaches);
    }

    private void RefreshColumnCaches(
        List<SkillSpellEntry> entries,
        Texture2D?[] icons,
        CachedText[] nameCaches,
        CachedText[] levelCaches)
    {
        for (var i = 0; i < MAX_ENTRIES_PER_COLUMN; i++)
            if ((i < entries.Count) && entries[i].Name is not null)
            {
                nameCaches[i]
                    .Update(entries[i].Name!, Color.White);

                levelCaches[i]
                    .Update(entries[i].Level ?? string.Empty, new Color(200, 200, 200));

                if (entries[i].IconSprite > 0)
                {
                    var newIcon = TextureConverter.RenderSprite(Device, DataContext.PanelIcons.GetSkillIcon(entries[i].IconSprite));

                    if (newIcon != icons[i])
                    {
                        icons[i]
                            ?.Dispose();
                        icons[i] = newIcon;
                    }
                } else if (icons[i] is not null)
                {
                    icons[i]
                        ?.Dispose();
                    icons[i] = null;
                }
            } else
            {
                nameCaches[i]
                    .Update(string.Empty, Color.White);

                levelCaches[i]
                    .Update(string.Empty, Color.White);
            }
    }

    /// <summary>
    ///     Adds or updates a skill/spell entry. Spells go in the left column, skills in the right.
    /// </summary>
    public void SetEntry(
        int index,
        ushort iconSprite,
        string name,
        string level,
        bool isSpell)
    {
        var list = isSpell ? SpellEntries : SkillEntries;

        while (list.Count <= index)
            list.Add(new SkillSpellEntry());

        list[index] = new SkillSpellEntry
        {
            IconSprite = iconSprite,
            Name = name,
            Level = level
        };
        DataVersion++;
    }

    private struct SkillSpellEntry
    {
        public ushort IconSprite;
        public string? Name;
        public string? Level;
    }
}
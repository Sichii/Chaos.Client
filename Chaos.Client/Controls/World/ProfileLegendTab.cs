#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Models;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Legend tab page (_nui_dr). Full-width LegendList area (524x237) displaying legend mark entries with icons, colored
///     text, and timestamps. Legend marks come from SelfProfile/OtherProfile packets.
/// </summary>
public sealed class ProfileLegendTab : PrefabPanel
{
    private const int MAX_VISIBLE_ROWS = 12;
    private const int ICON_X = 6;
    private const int TEXT_X = 28;

    private readonly Texture2D[] IconFrames;
    private readonly Rectangle LegendListRect;
    private readonly int RowHeight;

    private readonly CachedText[] TextCaches;
    private int DataVersion;
    private List<LegendMarkEntry> Marks = [];
    private int RenderedVersion = -1;
    private int ScrollOffset;

    public ProfileLegendTab(GraphicsDevice device, string prefabName)
        : base(device, prefabName, false)
    {
        Name = prefabName;
        Visible = false;

        var elements = AutoPopulate();

        // Hide the template icon element — we render icons per-row manually
        if (elements.TryGetValue("LegendIcon", out var iconElement))
            iconElement.Visible = false;

        LegendListRect = GetRect("LegendList");

        if (LegendListRect == Rectangle.Empty)
            LegendListRect = new Rectangle(
                38,
                33,
                524,
                237);

        RowHeight = LegendListRect.Height / MAX_VISIBLE_ROWS;
        TextCaches = new CachedText[MAX_VISIBLE_ROWS];

        for (var i = 0; i < MAX_VISIBLE_ROWS; i++)
            TextCaches[i] = new CachedText(device);

        // Legend mark icons from _nui_leg.spf (one frame per MarkIcon type)
        IconFrames = TextureConverter.LoadSpfTextures(device, "_nui_leg.spf");
    }

    public override void Dispose()
    {
        foreach (var c in TextCaches)
            c.Dispose();

        foreach (var t in IconFrames)
            t.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        RefreshCaches();

        var listX = ScreenX + LegendListRect.X;
        var listY = ScreenY + LegendListRect.Y;

        for (var i = 0; i < MAX_VISIBLE_ROWS; i++)
        {
            var markIndex = ScrollOffset + i;

            if (markIndex >= Marks.Count)
                break;

            var mark = Marks[markIndex];
            var rowY = listY + i * RowHeight;

            // Legend icon
            if ((mark.Icon < IconFrames.Length) && IconFrames[mark.Icon] is { } icon)
                spriteBatch.Draw(icon, new Vector2(listX + ICON_X, rowY), Color.White);

            // Legend text (colored)
            TextCaches[i]
                .Draw(spriteBatch, new Vector2(listX + TEXT_X, rowY + 2));
        }
    }

    private void RefreshCaches()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        for (var i = 0; i < MAX_VISIBLE_ROWS; i++)
        {
            var markIndex = ScrollOffset + i;

            if (markIndex < Marks.Count)
            {
                var mark = Marks[markIndex];

                TextCaches[i]
                    .Update(mark.Text, mark.Color);
            } else
                TextCaches[i]
                    .Update(string.Empty, Color.White);
        }
    }

    /// <summary>
    ///     Sets the legend mark entries to display.
    /// </summary>
    public void SetMarks(List<LegendMarkEntry> marks)
    {
        Marks = marks;
        ScrollOffset = 0;
        DataVersion++;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        base.Update(gameTime, input);

        // Scroll wheel
        if ((input.ScrollDelta != 0) && (Marks.Count > MAX_VISIBLE_ROWS))
        {
            ScrollOffset = Math.Clamp(ScrollOffset - input.ScrollDelta, 0, Marks.Count - MAX_VISIBLE_ROWS);
            DataVersion++;
        }
    }
}
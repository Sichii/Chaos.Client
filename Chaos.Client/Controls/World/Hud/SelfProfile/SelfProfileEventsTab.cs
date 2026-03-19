#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud.SelfProfile;

/// <summary>
///     Events/quest tab page (_nui_ev). Two-column layout: EV1 (left) and EV2 (right). NEXT/PREV pagination buttons for
///     multi-page event lists. Each event entry shows an icon and name.
/// </summary>
public sealed class SelfProfileEventsTab : PrefabPanel
{
    private const int ROW_HEIGHT = 16;
    private const int MAX_PER_COLUMN = 14;
    private const int MAX_PER_PAGE = 28;

    private readonly CachedText[] Ev1Caches;
    private readonly Rectangle Ev1Rect;
    private readonly CachedText[] Ev2Caches;
    private readonly Rectangle Ev2Rect;
    private readonly UIButton? NextButton;
    private readonly UIButton? PrevButton;
    private int CurrentPage;
    private int DataVersion;

    private List<EventEntry> Events = [];
    private int RenderedVersion = -1;

    public SelfProfileEventsTab(GraphicsDevice device, string prefabName)
        : base(device, prefabName, false)
    {
        Name = prefabName;
        Visible = false;

        var elements = AutoPopulate();

        Ev1Rect = GetRect("EV1");
        Ev2Rect = GetRect("EV2");

        if (Ev1Rect == Rectangle.Empty)
            Ev1Rect = new Rectangle(
                32,
                33,
                233,
                239);

        if (Ev2Rect == Rectangle.Empty)
            Ev2Rect = new Rectangle(
                331,
                33,
                233,
                239);

        NextButton = elements.GetValueOrDefault("NEXT") as UIButton;
        PrevButton = elements.GetValueOrDefault("PREV") as UIButton;

        if (NextButton is not null)
            NextButton.OnClick += () =>
            {
                if (((CurrentPage + 1) * MAX_PER_PAGE) < Events.Count)
                {
                    CurrentPage++;
                    DataVersion++;
                }
            };

        if (PrevButton is not null)
            PrevButton.OnClick += () =>
            {
                if (CurrentPage > 0)
                {
                    CurrentPage--;
                    DataVersion++;
                }
            };

        Ev1Caches = new CachedText[MAX_PER_COLUMN];
        Ev2Caches = new CachedText[MAX_PER_COLUMN];

        for (var i = 0; i < MAX_PER_COLUMN; i++)
        {
            Ev1Caches[i] = new CachedText(device);
            Ev2Caches[i] = new CachedText(device);
        }
    }

    public override void Dispose()
    {
        foreach (var c in Ev1Caches)
            c.Dispose();

        foreach (var c in Ev2Caches)
            c.Dispose();

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
            Ev1Caches,
            sx + Ev1Rect.X,
            sy + Ev1Rect.Y);

        DrawColumn(
            spriteBatch,
            Ev2Caches,
            sx + Ev2Rect.X,
            sy + Ev2Rect.Y);
    }

    private void DrawColumn(
        SpriteBatch spriteBatch,
        CachedText[] caches,
        int colX,
        int colY)
    {
        for (var i = 0; i < MAX_PER_COLUMN; i++)
            caches[i]
                .Draw(spriteBatch, new Vector2(colX + 4, colY + i * ROW_HEIGHT + 2));
    }

    private void RefreshCaches()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        var pageStart = CurrentPage * MAX_PER_PAGE;

        for (var i = 0; i < MAX_PER_COLUMN; i++)
        {
            var leftIndex = pageStart + i;
            var rightIndex = pageStart + MAX_PER_COLUMN + i;

            Ev1Caches[i]
                .Update(leftIndex < Events.Count ? Events[leftIndex].Name : string.Empty, Color.White);

            Ev2Caches[i]
                .Update(rightIndex < Events.Count ? Events[rightIndex].Name : string.Empty, Color.White);
        }
    }

    /// <summary>
    ///     Sets the event/quest entries.
    /// </summary>
    public void SetEvents(List<EventEntry> events)
    {
        Events = events;
        CurrentPage = 0;
        DataVersion++;
    }
}
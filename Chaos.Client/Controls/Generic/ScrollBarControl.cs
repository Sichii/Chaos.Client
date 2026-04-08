#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Generic;

/// <summary>
///     Vertical scrollbar using scroll.epf assets from setoa.dat. Up/down arrows (16x16), tiled track, fixed 16x16 thumb.
///     5 hit zones: up arrow, page up, thumb (draggable), page down, down arrow.
/// </summary>
public sealed class ScrollBarControl : UIElement
{
    public const int DEFAULT_WIDTH = 16;
    private const int BUTTON_SIZE = 16;

    //scroll.epf frame order: left(0,1), right(2,3), up(4,5), down(6,7), thumb(8), track(9)
    //each pair: [normal, active]
    private const int FRAME_UP_NORMAL = 4;
    private const int FRAME_UP_ACTIVE = 5;
    private const int FRAME_DOWN_NORMAL = 6;
    private const int FRAME_DOWN_ACTIVE = 7;
    private const int FRAME_THUMB = 8;
    private const int FRAME_TRACK = 9;

    private const float REPEAT_DELAY_MS = 50f;
    private const string SCROLL_EPF = "scroll.epf";

    private int ActiveZone = -1;

    private bool Dragging;
    private int DragOffsetY;
    private float RepeatTimer;

    public int MaxValue { get; set; }
    public int TotalItems { get; set; }
    public int Value { get; set; }
    public int VisibleItems { get; set; }

    public ScrollBarControl() => Width = BUTTON_SIZE;

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        var sx = ScreenX;
        var sy = ScreenY;
        var trackStart = sy + BUTTON_SIZE;
        var trackEnd = sy + Height - BUTTON_SIZE;
        var scrollable = TotalItems > VisibleItems;

        //tiled track background
        var trackTex = GetFrame(FRAME_TRACK);

        for (var tileY = trackStart; tileY < trackEnd; tileY += BUTTON_SIZE)
        {
            var tileH = Math.Min(BUTTON_SIZE, trackEnd - tileY);

            AtlasHelper.Draw(
                spriteBatch,
                trackTex,
                new Vector2(sx, tileY),
                new Rectangle(
                    0,
                    0,
                    BUTTON_SIZE,
                    tileH),
                Color.White);
        }

        //up arrow — normal when idle, active when pressed or disabled (active frame doubles as disabled)
        var upFrame = !scrollable || (ActiveZone == 0) ? FRAME_UP_ACTIVE : FRAME_UP_NORMAL;

        AtlasHelper.Draw(
            spriteBatch,
            GetFrame(upFrame),
            new Vector2(sx, sy),
            Color.White);

        //down arrow — normal when idle, active when pressed or disabled
        var downFrame = !scrollable || (ActiveZone == 4) ? FRAME_DOWN_ACTIVE : FRAME_DOWN_NORMAL;

        AtlasHelper.Draw(
            spriteBatch,
            GetFrame(downFrame),
            new Vector2(sx, trackEnd),
            Color.White);

        //thumb (only when scrollable)
        if (scrollable)
        {
            var thumbY = GetThumbY(trackStart, trackEnd);

            AtlasHelper.Draw(
                spriteBatch,
                GetFrame(FRAME_THUMB),
                new Vector2(sx, thumbY),
                Color.White);
        }
    }

    private static Texture2D GetFrame(int index) => UiRenderer.Instance!.GetEpfTexture(SCROLL_EPF, index);

    private int GetThumbY(int trackStart, int trackEnd)
    {
        if (MaxValue <= 0)
            return trackStart;

        var usableTrack = trackEnd - trackStart - BUTTON_SIZE;
        var ratio = (float)Value / MaxValue;

        return trackStart + (int)(ratio * usableTrack);
    }

    public event Action<int>? OnValueChanged;

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (!Dragging && ActiveZone is 0 or 4)
        {
            RepeatTimer += (float)gameTime.ElapsedGameTime.TotalMilliseconds;

            if (RepeatTimer >= REPEAT_DELAY_MS)
            {
                RepeatTimer -= REPEAT_DELAY_MS;

                if (ActiveZone == 0)
                {
                    Value = Math.Max(0, Value - 1);
                    OnValueChanged?.Invoke(Value);
                } else if (ActiveZone == 4)
                {
                    Value = Math.Min(MaxValue, Value + 1);
                    OnValueChanged?.Invoke(Value);
                }
            }
        }
    }

    public override void OnMouseDown(MouseDownEvent e)
    {
        if (e.Button != MouseButton.Left || (TotalItems <= VisibleItems))
            return;

        var sy = ScreenY;
        var trackStart = sy + BUTTON_SIZE;
        var trackEnd = sy + Height - BUTTON_SIZE;
        var thumbY = GetThumbY(trackStart, trackEnd);
        var my = e.ScreenY;

        if (my < trackStart)
        {
            ActiveZone = 0;
            Value = Math.Max(0, Value - 1);
            OnValueChanged?.Invoke(Value);
        } else if (my >= trackEnd)
        {
            ActiveZone = 4;
            Value = Math.Min(MaxValue, Value + 1);
            OnValueChanged?.Invoke(Value);
        } else if ((my >= thumbY) && (my < (thumbY + BUTTON_SIZE)))
        {
            ActiveZone = 2;
            Dragging = true;
            DragOffsetY = my - thumbY;
        } else if (my < thumbY)
        {
            ActiveZone = 1;
            Value = Math.Max(0, Value - 1);
            OnValueChanged?.Invoke(Value);
        } else
        {
            ActiveZone = 3;
            Value = Math.Min(MaxValue, Value + 1);
            OnValueChanged?.Invoke(Value);
        }

        e.Handled = true;
    }

    public override void OnMouseMove(MouseMoveEvent e)
    {
        if (!Dragging)
            return;

        var sy = ScreenY;
        var trackStart = sy + BUTTON_SIZE;
        var trackEnd = sy + Height - BUTTON_SIZE;
        var usableTrack = trackEnd - trackStart - BUTTON_SIZE;

        if (usableTrack > 0)
        {
            var mouseY = e.ScreenY - DragOffsetY;
            var ratio = Math.Clamp((float)(mouseY - trackStart) / usableTrack, 0f, 1f);
            var newValue = (int)(ratio * MaxValue);

            if (newValue != Value)
            {
                Value = newValue;
                OnValueChanged?.Invoke(Value);
            }
        }
    }

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        if (TotalItems <= VisibleItems)
            return;

        var newValue = Math.Clamp(Value - e.Delta, 0, MaxValue);

        if (newValue != Value)
        {
            Value = newValue;
            OnValueChanged?.Invoke(Value);
        }

        e.Handled = true;
    }

    public override void OnMouseUp(MouseUpEvent e)
    {
        if (e.Button == MouseButton.Left)
        {
            Dragging = false;
            ActiveZone = -1;
            RepeatTimer = 0;
        }
    }
}
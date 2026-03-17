#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
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
    private const int BUTTON_SIZE = 16;

    // scroll.epf frame order: left(0,1), right(2,3), up(4,5), down(6,7), thumb(8), track(9)
    // Each pair: [active, normal]
    private const int FRAME_UP_ACTIVE = 4;
    private const int FRAME_UP_NORMAL = 5;
    private const int FRAME_DOWN_ACTIVE = 6;
    private const int FRAME_DOWN_NORMAL = 7;
    private const int FRAME_THUMB = 8;
    private const int FRAME_TRACK = 9;

    private static Texture2D?[]? SharedFrames;
    private static int SharedFrameRefCount;
    private int ActiveZone = -1;

    private bool Dragging;
    private int DragOffsetY;

    public int MaxValue { get; set; }
    public int TotalItems { get; set; }
    public int Value { get; set; }
    public int VisibleItems { get; set; }

    public ScrollBarControl(GraphicsDevice device)
    {
        Width = BUTTON_SIZE;

        SharedFrames ??= TextureConverter.LoadEpfTextures(device, "scroll.epf");

        SharedFrameRefCount++;
    }

    public override void Dispose()
    {
        SharedFrameRefCount--;

        if ((SharedFrameRefCount <= 0) && SharedFrames is not null)
        {
            foreach (var tex in SharedFrames)
                tex?.Dispose();

            SharedFrames = null;
            SharedFrameRefCount = 0;
        }

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || SharedFrames is null || (SharedFrames.Length < 10))
            return;

        var sx = ScreenX;
        var sy = ScreenY;
        var trackStart = sy + BUTTON_SIZE;
        var trackEnd = sy + Height - BUTTON_SIZE;
        var scrollable = TotalItems > VisibleItems;

        // Tiled track background
        if (SharedFrames[FRAME_TRACK] is { } trackTex)
            for (var tileY = trackStart; tileY < trackEnd; tileY += BUTTON_SIZE)
            {
                var tileH = Math.Min(BUTTON_SIZE, trackEnd - tileY);

                spriteBatch.Draw(
                    trackTex,
                    new Vector2(sx, tileY),
                    new Rectangle(
                        0,
                        0,
                        BUTTON_SIZE,
                        tileH),
                    Color.White);
            }

        // Up arrow
        var upFrame = scrollable && (ActiveZone == 0) ? FRAME_UP_ACTIVE : FRAME_UP_NORMAL;

        if (SharedFrames[upFrame] is { } upTex)
            spriteBatch.Draw(upTex, new Vector2(sx, sy), Color.White);

        // Down arrow
        var downFrame = scrollable && (ActiveZone == 4) ? FRAME_DOWN_ACTIVE : FRAME_DOWN_NORMAL;

        if (SharedFrames[downFrame] is { } downTex)
            spriteBatch.Draw(downTex, new Vector2(sx, trackEnd), Color.White);

        // Thumb (only when scrollable)
        if (scrollable && SharedFrames[FRAME_THUMB] is { } thumbTex)
        {
            var thumbY = GetThumbY(trackStart, trackEnd);
            spriteBatch.Draw(thumbTex, new Vector2(sx, thumbY), Color.White);
        }
    }

    private int GetThumbY(int trackStart, int trackEnd)
    {
        if (MaxValue <= 0)
            return trackStart;

        var usableTrack = trackEnd - trackStart - BUTTON_SIZE;
        var ratio = (float)Value / MaxValue;

        return trackStart + (int)(ratio * usableTrack);
    }

    public event Action<int>? OnValueChanged;

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled || (TotalItems <= VisibleItems))
            return;

        var sy = ScreenY;
        var trackStart = sy + BUTTON_SIZE;
        var trackEnd = sy + Height - BUTTON_SIZE;
        var thumbY = GetThumbY(trackStart, trackEnd);

        if (Dragging)
        {
            if (input.IsLeftButtonHeld)
            {
                var mouseY = input.MouseY - DragOffsetY;
                var usableTrack = trackEnd - trackStart - BUTTON_SIZE;

                if (usableTrack > 0)
                {
                    var ratio = Math.Clamp((float)(mouseY - trackStart) / usableTrack, 0f, 1f);
                    var newValue = (int)(ratio * MaxValue);

                    if (newValue != Value)
                    {
                        Value = newValue;
                        OnValueChanged?.Invoke(Value);
                    }
                }
            } else
            {
                Dragging = false;
                ActiveZone = -1;
            }

            return;
        }

        if (input.WasLeftButtonPressed)
        {
            var mx = input.MouseX;
            var my = input.MouseY;
            var sx = ScreenX;

            if ((mx < sx) || (mx >= (sx + Width)) || (my < sy) || (my >= (sy + Height)))
                return;

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
                Value = Math.Max(0, Value - VisibleItems);
                OnValueChanged?.Invoke(Value);
            } else
            {
                ActiveZone = 3;
                Value = Math.Min(MaxValue, Value + VisibleItems);
                OnValueChanged?.Invoke(Value);
            }
        }

        if (input.WasLeftButtonReleased)
            ActiveZone = -1;
    }
}
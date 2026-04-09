#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Options;

/// <summary>
///     Options dialog using _noptdlg prefab. Positioned at bottom-left (X=2, Y=480-Height). Contains: Sound/Music volume
///     sliders (0-10 range), Friends/Macro/Setting buttons, ExitGame button, and Close button. Slider thumb from
///     option04.epf, tracks from SoundRect/MusicRect rects.
/// </summary>
public sealed class MainOptionsControl : PrefabPanel
{
    private const int VOLUME_MIN = 0;
    private const int VOLUME_MAX = 10;
    private const int THUMB_WIDTH = 12;
    private const int THUMB_HEIGHT = 12;
    private readonly Rectangle MusicTrackRect;

    //slider state
    private readonly Rectangle SoundTrackRect;
    private readonly Texture2D? ThumbTexture;
    private bool DraggingMusic;
    private bool DraggingSound;
    private int MusicVolume = 10;

    //slide animation
    private SlideAnimator Slide;
    private int SoundVolume = 10;

    public UIButton? CloseButton { get; }
    public UIButton? ExitButton { get; }
    public UIButton? FriendsButton { get; }
    public UIButton? MacroButton { get; }
    public UIButton? SettingsButton { get; }

    public MainOptionsControl()
        : base("_noptdlg", false)
    {
        Name = "OptionsDialog";
        Visible = false;
        UsesControlStack = true;

        //right-aligned, slides in from right edge
        Slide.SetViewportBounds(
            new Rectangle(
                0,
                0,
                640,
                480),
            Width);
        X = Slide.OffScreenX;

        //slider track rects
        SoundTrackRect = GetRect("SoundRect");
        MusicTrackRect = GetRect("MusicRect");

        //slider thumb from option04.epf (tick control) — extract texture only, remove the child
        var tickImage = CreateImage("Tick");

        if (tickImage is not null)
        {
            ThumbTexture = tickImage.Texture;
            Children.Remove(tickImage);
        }

        //buttons
        MacroButton = CreateButton("Macro");
        SettingsButton = CreateButton("Setting");
        FriendsButton = CreateButton("Friends");
        ExitButton = CreateButton("ExitGame");
        CloseButton = CreateButton("CLOSE");

        if (MacroButton is not null)
            MacroButton.Clicked += () => OnMacro?.Invoke();

        if (SettingsButton is not null)
            SettingsButton.Clicked += () => OnSettings?.Invoke();

        if (FriendsButton is not null)
            FriendsButton.Clicked += () => OnFriends?.Invoke();

        if (ExitButton is not null)
            ExitButton.Clicked += () => OnExit?.Invoke();

        if (CloseButton is not null)
            CloseButton.Clicked += () => Slide.SlideOut();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        //draw slider thumbs
        if (ThumbTexture is not null)
        {
            DrawThumb(spriteBatch, SoundTrackRect, SoundVolume);
            DrawThumb(spriteBatch, MusicTrackRect, MusicVolume);
        }
    }

    private void DrawThumb(SpriteBatch spriteBatch, Rectangle trackRect, int volume)
    {
        if ((trackRect == Rectangle.Empty) || ThumbTexture is null)
            return;

        var sx = ScreenX;
        var sy = ScreenY;
        var thumbX = sx + GetThumbX(trackRect, volume);
        var thumbY = sy + trackRect.Y - (THUMB_HEIGHT - trackRect.Height) / 2;

        spriteBatch.Draw(ThumbTexture, new Vector2(thumbX, thumbY), Color.White);
    }

    public int GetMusicVolume() => MusicVolume;

    public int GetSoundVolume() => SoundVolume;

    private static int GetThumbX(Rectangle trackRect, int volume)
        => trackRect.X + (int)((float)volume / VOLUME_MAX * trackRect.Width) - THUMB_WIDTH / 2;

    public override void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);
        Slide.Hide(this);
    }

    public event Action? OnClose;
    public event Action? OnExit;
    public event Action? OnFriends;
    public event Action? OnMacro;
    public event Action<int>? OnMusicVolumeChanged;
    public event Action? OnSettings;
    public event Action<int>? OnSoundVolumeChanged;
    public void SetMusicVolume(int volume) => MusicVolume = Math.Clamp(volume, VOLUME_MIN, VOLUME_MAX);

    public void SetSoundVolume(int volume) => SoundVolume = Math.Clamp(volume, VOLUME_MIN, VOLUME_MAX);

    public void SetViewportBounds(Rectangle viewport)
    {
        Slide.SetViewportBounds(viewport, Width);
        Y = viewport.Y;
    }

    public override void Show()
    {
        if (!Visible)
        {
            InputDispatcher.Instance?.PushControl(this);
            Slide.SlideIn(this);
        }
    }

    public void SlideClose()
    {
        InputDispatcher.Instance?.RemoveControl(this);
        Slide.SlideOut();
    }

    public override void Update(GameTime gameTime)
    {
        if (!Visible || !Enabled)
            return;

        if (Slide.Update(gameTime, this))
        {
            OnClose?.Invoke();

            return;
        }

        base.Update(gameTime);
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (Slide.Sliding)
            return;

        if (e.Key == Keys.Escape)
        {
            InputDispatcher.Instance?.RemoveControl(this);
            Slide.SlideOut();
            e.Handled = true;
        } else if (e.Key == Keys.X)
        {
            OnExit?.Invoke();
            e.Handled = true;
        }
    }

    public override void OnMouseDown(MouseDownEvent e)
    {
        if (Slide.Sliding || e.Button != MouseButton.Left)
            return;

        var localX = e.ScreenX - ScreenX;
        var localY = e.ScreenY - ScreenY;

        if (HitsTrack(SoundTrackRect, localX, localY))
        {
            DraggingSound = true;
            UpdateVolumeFromMouse(SoundTrackRect, localX, true);
            e.Handled = true;
        } else if (HitsTrack(MusicTrackRect, localX, localY))
        {
            DraggingMusic = true;
            UpdateVolumeFromMouse(MusicTrackRect, localX, false);
            e.Handled = true;
        }
    }

    public override void OnMouseMove(MouseMoveEvent e)
    {
        if (!DraggingSound && !DraggingMusic)
            return;

        var localX = e.ScreenX - ScreenX;

        if (DraggingSound)
            UpdateVolumeFromMouse(SoundTrackRect, localX, true);
        else if (DraggingMusic)
            UpdateVolumeFromMouse(MusicTrackRect, localX, false);

        e.Handled = true;
    }

    public override void OnMouseUp(MouseUpEvent e)
    {
        if (e.Button != MouseButton.Left)
            return;

        DraggingSound = false;
        DraggingMusic = false;
    }

    private static bool HitsTrack(Rectangle trackRect, int localX, int localY)
    {
        if (trackRect == Rectangle.Empty)
            return false;

        //generous vertical hit area around the track for easier clicking
        var hitY = trackRect.Y - THUMB_HEIGHT / 2;
        var hitH = trackRect.Height + THUMB_HEIGHT;

        return (localX >= trackRect.X)
               && (localX <= (trackRect.X + trackRect.Width))
               && (localY >= hitY)
               && (localY <= (hitY + hitH));
    }

    private void UpdateVolumeFromMouse(Rectangle trackRect, int localX, bool isSound)
    {
        if (trackRect == Rectangle.Empty)
            return;

        var ratio = (float)(localX - trackRect.X) / trackRect.Width;
        var volume = (int)Math.Round(ratio * VOLUME_MAX);
        volume = Math.Clamp(volume, VOLUME_MIN, VOLUME_MAX);

        if (isSound)
        {
            if (volume == SoundVolume)
                return;

            SoundVolume = volume;
            OnSoundVolumeChanged?.Invoke(volume);
        } else
        {
            if (volume == MusicVolume)
                return;

            MusicVolume = volume;
            OnMusicVolumeChanged?.Invoke(volume);
        }
    }

}
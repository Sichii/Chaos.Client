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

    // Slider state
    private readonly Rectangle SoundTrackRect;
    private readonly Texture2D? ThumbTexture;
    private bool DraggingMusic;
    private bool DraggingSound;
    private int MusicVolume = 10;

    // Slide animation
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

        // Right-aligned, slides in from right edge
        Slide.SetViewportBounds(
            new Rectangle(
                0,
                0,
                640,
                480),
            Width);
        X = Slide.OffScreenX;

        // Slider track rects
        SoundTrackRect = GetRect("SoundRect");
        MusicTrackRect = GetRect("MusicRect");

        // Slider thumb from option04.epf (Tick control) — extract texture only, remove the child
        var tickImage = CreateImage("Tick");

        if (tickImage is not null)
        {
            ThumbTexture = tickImage.Texture;
            Children.Remove(tickImage);
        }

        // Buttons
        MacroButton = CreateButton("Macro");
        SettingsButton = CreateButton("Setting");
        FriendsButton = CreateButton("Friends");
        ExitButton = CreateButton("ExitGame");
        CloseButton = CreateButton("CLOSE");

        if (MacroButton is not null)
            MacroButton.OnClick += () => OnMacro?.Invoke();

        if (SettingsButton is not null)
            SettingsButton.OnClick += () => OnSettings?.Invoke();

        if (FriendsButton is not null)
            FriendsButton.OnClick += () => OnFriends?.Invoke();

        if (ExitButton is not null)
            ExitButton.OnClick += () => OnExit?.Invoke();

        if (CloseButton is not null)
            CloseButton.OnClick += () => Slide.SlideOut();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        // Draw slider thumbs
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

    private void HandleSlider(
        InputBuffer input,
        Rectangle trackRect,
        ref int volume,
        ref bool dragging)
    {
        if (trackRect == Rectangle.Empty)
            return;

        var sx = ScreenX;
        var sy = ScreenY;
        var trackX = sx + trackRect.X;
        var trackY = sy + trackRect.Y - (THUMB_HEIGHT - trackRect.Height) / 2;
        var trackWidth = trackRect.Width;

        if (dragging)
        {
            if (input.IsLeftButtonHeld)
            {
                var mouseX = input.MouseX - trackX;
                var newVolume = (int)Math.Round((float)mouseX / trackWidth * VOLUME_MAX);
                volume = Math.Clamp(newVolume, VOLUME_MIN, VOLUME_MAX);
            } else
                dragging = false;

            return;
        }

        if (input.WasLeftButtonPressed)
        {
            var thumbX = GetThumbX(trackRect, volume) + sx;

            // Hit test the thumb area
            if ((input.MouseX >= (thumbX - 2))
                && (input.MouseX <= (thumbX + THUMB_WIDTH + 2))
                && (input.MouseY >= trackY)
                && (input.MouseY <= (trackY + THUMB_HEIGHT)))
                dragging = true;

            // Click on the track — jump to position
            else if ((input.MouseX >= trackX)
                     && (input.MouseX <= (trackX + trackWidth))
                     && (input.MouseY >= trackY)
                     && (input.MouseY <= (trackY + THUMB_HEIGHT)))
            {
                var mouseX = input.MouseX - trackX;
                var newVolume = (int)Math.Round((float)mouseX / trackWidth * VOLUME_MAX);
                volume = Math.Clamp(newVolume, VOLUME_MIN, VOLUME_MAX);
            }
        }
    }

    public override void Hide() => Slide.Hide(this);

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
            Slide.SlideIn(this);
    }

    public void SlideClose() => Slide.SlideOut();

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (Slide.Update(gameTime, this))
        {
            OnClose?.Invoke();

            return;
        }

        if (input.WasKeyPressed(Keys.Escape))
        {
            Slide.SlideOut();

            return;
        }

        // Slider interaction
        var prevSound = SoundVolume;
        var prevMusic = MusicVolume;

        HandleSlider(
            input,
            SoundTrackRect,
            ref SoundVolume,
            ref DraggingSound);

        HandleSlider(
            input,
            MusicTrackRect,
            ref MusicVolume,
            ref DraggingMusic);

        if (SoundVolume != prevSound)
            OnSoundVolumeChanged?.Invoke(SoundVolume);

        if (MusicVolume != prevMusic)
            OnMusicVolumeChanged?.Invoke(MusicVolume);

        base.Update(gameTime, input);
    }
}
#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Settings panel using _nsett prefab. Triggered by F4 key. Shows a list of toggle settings with checkboxes. TopButton
///     is a checkbox toggle (16x16, _nsettb.spf: 0=unchecked, 1=checked). TopText/BottomText/RightText are setting labels.
///     OK/Cancel buttons at bottom.
/// </summary>
public class SettingsControl : PrefabPanel
{
    private const int SETTING_COUNT = 8;
    private const int ROW_HEIGHT = 21;
    private const int CHECKBOX_X = 15;
    private const int CHECKBOX_START_Y = 40;
    private const int CHECKBOX_SIZE = 16;
    private const int LABEL_OFFSET_X = 35;
    private readonly Texture2D? CheckedTexture;

    private readonly GraphicsDevice DeviceRef;

    private readonly CachedText[] SettingNameCaches = new CachedText[SETTING_COUNT];

    private readonly string[] SettingNames =
    [
        "Show Names",
        "Show Spell Effects",
        "Show Whisper Names",
        "Filter Profanity",
        "Auto-Join Group",
        "Auto-Run",
        "Fast Walk",
        "Show Damage Numbers"
    ];

    private readonly bool[] SettingValues = new bool[SETTING_COUNT];
    private readonly Texture2D? UncheckedTexture;
    private int DataVersion;
    private int RenderedVersion = -1;
    public UIButton? CancelButton { get; }

    public UIButton? OkButton { get; }

    public SettingsControl(GraphicsDevice device)
        : base(device, "_nsett")
    {
        DeviceRef = device;
        Name = "Settings";
        Visible = false;

        var elements = AutoPopulate();

        OkButton = elements.GetValueOrDefault("OK") as UIButton;
        CancelButton = elements.GetValueOrDefault("Cancel") as UIButton;

        if (OkButton is not null)
            OkButton.OnClick += () =>
            {
                Hide();
                OnClose?.Invoke();
            };

        if (CancelButton is not null)
            CancelButton.OnClick += () =>
            {
                Hide();
                OnClose?.Invoke();
            };

        // Load checkbox textures from _nsettb.spf
        var checkboxFrames = TextureConverter.LoadSpfTextures(device, "_nsettb.spf");

        if (checkboxFrames.Length > 0)
            UncheckedTexture = checkboxFrames[0];

        if (checkboxFrames.Length > 1)
            CheckedTexture = checkboxFrames[1];

        for (var i = 0; i < SETTING_COUNT; i++)
            SettingNameCaches[i] = new CachedText(device);

        DataVersion++;
    }

    public override void Dispose()
    {
        foreach (var c in SettingNameCaches)
            c.Dispose();

        // Don't dispose checkbox textures — they may be shared SPF frames
        // (TextureConverter.LoadSpfTextures returns owned textures though, so dispose them)
        UncheckedTexture?.Dispose();
        CheckedTexture?.Dispose();

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

        for (var i = 0; i < SETTING_COUNT; i++)
        {
            var rowY = sy + CHECKBOX_START_Y + i * ROW_HEIGHT;

            // Checkbox
            var checkboxTex = SettingValues[i] ? CheckedTexture : UncheckedTexture;

            if (checkboxTex is not null)
                spriteBatch.Draw(checkboxTex, new Vector2(sx + CHECKBOX_X, rowY), Color.White);

            // Label
            SettingNameCaches[i]
                .Draw(spriteBatch, new Vector2(sx + LABEL_OFFSET_X, rowY + 2));
        }
    }

    /// <summary>
    ///     Gets or sets a setting toggle value by index.
    /// </summary>
    public bool GetSetting(int index) => (index >= 0) && (index < SETTING_COUNT) && SettingValues[index];

    public event Action? OnClose;

    private void RefreshCaches()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        for (var i = 0; i < SETTING_COUNT; i++)
            SettingNameCaches[i]
                .Update(SettingNames[i], 0, Color.White);
    }

    public void SetSetting(int index, bool value)
    {
        if ((index < 0) || (index >= SETTING_COUNT))
            return;

        SettingValues[index] = value;
        DataVersion++;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (input.WasKeyPressed(Keys.Escape) || input.WasKeyPressed(Keys.F4))
        {
            Hide();
            OnClose?.Invoke();

            return;
        }

        base.Update(gameTime, input);

        // Click to toggle checkboxes
        if (input.WasLeftButtonPressed)
        {
            var localX = input.MouseX - ScreenX;
            var localY = input.MouseY - ScreenY - CHECKBOX_START_Y;

            if ((localX >= CHECKBOX_X) && (localX < (CHECKBOX_X + 200)) && (localY >= 0))
            {
                var index = localY / ROW_HEIGHT;

                if ((index >= 0) && (index < SETTING_COUNT))
                {
                    SettingValues[index] = !SettingValues[index];
                    DataVersion++;
                }
            }
        }
    }
}
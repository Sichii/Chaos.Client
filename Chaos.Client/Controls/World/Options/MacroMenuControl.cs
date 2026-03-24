#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Options;

/// <summary>
///     Macro pulldown menu using _nmacro prefab. Triggered by F3 key. Displays configurable macro slots with text labels.
///     Two text rows per macro entry (TextTop/TextBottom), OK/Cancel at bottom. Macros map function keys (F5-F12) to chat
///     commands or spell/skill usage.
/// </summary>
public sealed class MacroMenuControl : PrefabPanel
{
    private const int MAX_MACROS = 10;
    private const int ROW_HEIGHT = 21;
    private const int LABEL_START_Y = 40;
    private const int LABEL_X = 40;
    private const int LABEL_WIDTH = 385;

    private readonly CachedText[] MacroNameCaches = new CachedText[MAX_MACROS];

    private readonly string[] MacroNames = new string[MAX_MACROS];
    private readonly CachedText[] MacroValueCaches = new CachedText[MAX_MACROS];
    private readonly string[] MacroValues = new string[MAX_MACROS];
    private bool ClosedWithOk;
    private int DataVersion;
    private int RenderedVersion = -1;
    private int SelectedIndex = -1;
    private SlideAnimator Slide;
    private int SlideAnchorY;
    private bool SlideMode;

    public UIButton? CancelButton { get; }

    public UIButton? OkButton { get; }

    public MacroMenuControl()
        : base("_nmacro", false)
    {
        Name = "MacroMenu";
        Visible = false;

        OkButton = CreateButton("OK");
        CancelButton = CreateButton("Cancel");

        if (OkButton is not null)
            OkButton.OnClick += CloseWithOk;

        if (CancelButton is not null)
            CancelButton.OnClick += Close;

        // Initialize macro caches
        for (var i = 0; i < MAX_MACROS; i++)
        {
            MacroNameCaches[i] = new CachedText();
            MacroValueCaches[i] = new CachedText();
            MacroNames[i] = $"F{i + 5}";
            MacroValues[i] = string.Empty;
        }

        DataVersion++;
    }

    private void Close()
    {
        ClosedWithOk = false;

        if (SlideMode)
            Slide.SlideOut();
        else
        {
            Hide();
            OnClose?.Invoke();
        }
    }

    private void CloseWithOk()
    {
        ClosedWithOk = true;

        if (SlideMode)
            Slide.SlideOut();
        else
        {
            OnOk?.Invoke();
            Hide();
            OnClose?.Invoke();
        }
    }

    public override void Dispose()
    {
        foreach (var c in MacroNameCaches)
            c.Dispose();

        foreach (var c in MacroValueCaches)
            c.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        RefreshCaches();

        var sx = ScreenX + LABEL_X;
        var sy = ScreenY + LABEL_START_Y;

        for (var i = 0; i < MAX_MACROS; i++)
        {
            var rowY = sy + i * ROW_HEIGHT;

            // Name (function key label)
            MacroNameCaches[i]
                .Draw(spriteBatch, new Vector2(sx, rowY));

            // Value (command text) — offset to the right
            MacroValueCaches[i]
                .Draw(spriteBatch, new Vector2(sx + 60, rowY));
        }
    }

    /// <summary>
    ///     Returns the macro values for all slots.
    /// </summary>
    public string[] GetMacroValues() => (string[])MacroValues.Clone();

    public override void Hide()
    {
        if (SlideMode)
            Slide.Hide(this);
        else
            Visible = false;
    }

    public event Action? OnClose;
    #pragma warning disable CS0067 // not yet wired
    public event Action<int, string, string>? OnMacroChanged;
    #pragma warning restore CS0067
    public event Action? OnOk;

    private void RefreshCaches()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        for (var i = 0; i < MAX_MACROS; i++)
        {
            var nameColor = i == SelectedIndex ? Color.Yellow : Color.White;

            MacroNameCaches[i]
                .Update(MacroNames[i], nameColor);

            MacroValueCaches[i]
                .Update(MacroValues[i], nameColor);
        }
    }

    /// <summary>
    ///     Sets a macro slot's display name and command value.
    /// </summary>
    public void SetMacro(int index, string name, string value)
    {
        if ((index < 0) || (index >= MAX_MACROS))
            return;

        MacroNames[index] = name;
        MacroValues[index] = value;
        DataVersion++;
    }

    /// <summary>
    ///     Sets the slide anchor to the left edge of the parent panel (MainOptionsControl). The sub-panel slides left out of
    ///     the anchor and back into it on close.
    /// </summary>
    public void SetSlideAnchor(int anchorX, int anchorY)
    {
        Slide.SetSlideAnchor(anchorX, Width);
        SlideAnchorY = anchorY;
    }

    /// <summary>
    ///     Shows immediately at top-center of screen (hotkey mode).
    /// </summary>
    public override void Show()
    {
        X = (640 - Width) / 2;
        Y = 0;
        Visible = true;
        SlideMode = false;
    }

    /// <summary>
    ///     Slides out from the left edge of MainOptionsControl (button mode).
    /// </summary>
    public void SlideIn()
    {
        if (Visible)
            return;

        Y = SlideAnchorY;
        Slide.SlideIn(this);
        SlideMode = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (Slide.Update(gameTime, this))
        {
            if (ClosedWithOk)
                OnOk?.Invoke();

            OnClose?.Invoke();

            return;
        }

        if (input.WasKeyPressed(Keys.Escape) || input.WasKeyPressed(Keys.F3))
        {
            Close();

            return;
        }

        base.Update(gameTime, input);

        // Click to select a macro slot
        if (input.WasLeftButtonPressed)
        {
            var localY = input.MouseY - ScreenY - LABEL_START_Y;
            var localX = input.MouseX - ScreenX - LABEL_X;

            if (localX is >= 0 and < LABEL_WIDTH && (localY >= 0))
            {
                var index = localY / ROW_HEIGHT;

                if (index < MAX_MACROS)
                    SelectedIndex = index;
            }
        }
    }
}
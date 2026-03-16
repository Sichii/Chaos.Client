#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Options dialog using _noptdlg prefab. Presents navigation buttons: Macro, Settings, Friends, Sounds, Exit game.
///     Triggered by the Option button on the HUD.
/// </summary>
public class OptionsDialogControl : PrefabPanel
{
    public UIButton? CloseButton { get; }
    public UIButton? ExitButton { get; }
    public UIButton? FriendsButton { get; }
    public UIButton? MacroButton { get; }
    public UIButton? SettingsButton { get; }
    public UIButton? SoundsButton { get; }

    public OptionsDialogControl(GraphicsDevice device)
        : base(device, "_noptdlg")
    {
        Name = "OptionsDialog";
        Visible = false;

        var elements = AutoPopulate();

        MacroButton = elements.GetValueOrDefault("Macro") as UIButton;
        SettingsButton = elements.GetValueOrDefault("Setting") as UIButton;
        FriendsButton = elements.GetValueOrDefault("Friends") as UIButton;
        SoundsButton = elements.GetValueOrDefault("Sounds") as UIButton;
        ExitButton = elements.GetValueOrDefault("ExitGame") as UIButton;
        CloseButton = elements.GetValueOrDefault("CLOSE") as UIButton;

        if (MacroButton is not null)
            MacroButton.OnClick += () => OnMacro?.Invoke();

        if (SettingsButton is not null)
            SettingsButton.OnClick += () => OnSettings?.Invoke();

        if (FriendsButton is not null)
            FriendsButton.OnClick += () => OnFriends?.Invoke();

        if (SoundsButton is not null)
            SoundsButton.OnClick += () => OnSounds?.Invoke();

        if (ExitButton is not null)
            ExitButton.OnClick += () => OnExit?.Invoke();

        if (CloseButton is not null)
            CloseButton.OnClick += () => OnClose?.Invoke();
    }

    public event Action? OnClose;
    public event Action? OnExit;
    public event Action? OnFriends;

    public event Action? OnMacro;
    public event Action? OnSettings;
    public event Action? OnSounds;

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (input.WasKeyPressed(Keys.Escape))
        {
            Hide();
            OnClose?.Invoke();

            return;
        }

        base.Update(gameTime, input);
    }
}
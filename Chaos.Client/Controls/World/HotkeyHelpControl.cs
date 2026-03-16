#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Hotkey/help overlay using _nhotkem prefab (full-screen container 640x480). Background from _nhk_bk.spf, contains
///     MAIN (keyboard diagram) and EX (text explanation area). The keyboard diagram is auto-populated from the prefab —
///     each C## control is a key-group image from the corresponding _nhkk##.spf file, positioned to form a visual keyboard
///     layout. Triggered by the Help button or F1 key.
/// </summary>
public class HotkeyHelpControl : PrefabPanel
{
    public HotkeyHelpControl(GraphicsDevice device)
        : base(device, "_nhotkem")
    {
        Name = "HotkeyHelp";
        Visible = false;

        // AutoPopulate creates all the keyboard key images (C00-C13) and the MAIN/EX areas
        AutoPopulate();
    }

    public event Action? OnClose;

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (input.WasKeyPressed(Keys.Escape) || input.WasKeyPressed(Keys.F1))
        {
            Hide();
            OnClose?.Invoke();

            return;
        }

        // Click anywhere to close
        if (input.WasLeftButtonPressed)
        {
            Hide();
            OnClose?.Invoke();

            return;
        }

        base.Update(gameTime, input);
    }
}
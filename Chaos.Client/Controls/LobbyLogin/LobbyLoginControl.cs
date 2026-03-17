#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Definitions;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.LobbyLogin;

public sealed class LobbyLoginControl : PrefabPanel
{
    public UIAnimatedImage? AnimatedLogo { get; }
    public UIButton? ContinueButton { get; }
    public UIButton? CreditButton { get; }
    public UIButton? ExitButton { get; }
    public UIButton? HomepageButton { get; }
    public UIButton? PasswordButton { get; }
    public UIButton? SubmitCreateButton { get; }
    public UILabel? VersionLabel { get; }

    public LobbyLoginControl(GraphicsDevice device)
        : base(device, "_nstart", false)
    {
        Name = "StartScreen";
        X = 0;
        Y = 0;

        var elements = AutoPopulate();

        // Buttons — AutoPopulate creates these as UIButtons (2 images each, type 7)
        SubmitCreateButton = elements.GetValueOrDefault("Create") as UIButton;
        ContinueButton = elements.GetValueOrDefault("Continue") as UIButton;
        PasswordButton = elements.GetValueOrDefault("Password") as UIButton;
        CreditButton = elements.GetValueOrDefault("Credit") as UIButton;
        HomepageButton = elements.GetValueOrDefault("Homepage") as UIButton;
        ExitButton = elements.GetValueOrDefault("Exit") as UIButton;

        // Start screen buttons use hover effect instead of press
        UIButton?[] allButtons =
        [
            SubmitCreateButton,
            ContinueButton,
            PasswordButton,
            CreditButton,
            HomepageButton,
            ExitButton
        ];

        foreach (var btn in allButtons)
        {
            if (btn is null)
                continue;

            btn.HoverTexture = btn.PressedTexture;
            btn.PressedTexture = null;
            btn.Enabled = false;
        }

        // Animated logo — LOGO control has 20 frames, AutoPopulate created it as UIImage (5+ images).
        // Replace with UIAnimatedImage using the prefab's frames.
        if (elements.TryGetValue("LOGO", out var logoElement) && PrefabSet.Contains("LOGO"))
        {
            var logoPrefab = PrefabSet["LOGO"];
            var animFrames = new Texture2D[logoPrefab.Images.Count];

            for (var i = 0; i < logoPrefab.Images.Count; i++)
                animFrames[i] = TextureConverter.ToTexture2D(device, logoPrefab.Images[i]);

            AnimatedLogo = new UIAnimatedImage
            {
                Name = "AnimatedLogo",
                X = logoElement.X,
                Y = logoElement.Y,
                Width = logoElement.Width,
                Height = logoElement.Height,
                Frames = animFrames,
                FrameIntervalMs = 150,
                Looping = true,
                PingPong = true
            };

            // Replace the static image with the animated one
            Children.Remove(logoElement);
            logoElement.Dispose();
            AddChild(AnimatedLogo);
        }

        // Version label — type 7, 0 images, skipped by AutoPopulate
        VersionLabel = CreateLabel("Version", TextAlignment.Right);
        VersionLabel?.SetText("Chaos v0.1.0");
    }

    public void EnableButtons() => SetButtonsEnabled(true);

    public void SetButtonsEnabled(bool enabled)
    {
        SubmitCreateButton?.SetEnabled(enabled);
        ContinueButton?.SetEnabled(enabled);
        PasswordButton?.SetEnabled(enabled);
        CreditButton?.SetEnabled(enabled);
        HomepageButton?.SetEnabled(enabled);
        ExitButton?.SetEnabled(enabled);
    }
}

file static class ButtonExtensions
{
    public static void SetEnabled(this UIButton? button, bool enabled) => button?.Enabled = enabled;
}
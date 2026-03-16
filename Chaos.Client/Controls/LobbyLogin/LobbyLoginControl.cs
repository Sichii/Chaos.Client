#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.LobbyLogin;

public class LobbyLoginControl : UIPanel
{
    public UIAnimatedImage AnimatedLogo { get; }
    public UIButton ContinueButton { get; }
    public UIButton CreateButton { get; }
    public UIButton CreditButton { get; }
    public UIButton ExitButton { get; }
    public UIButton HomepageButton { get; }
    public UIButton PasswordButton { get; }
    public UIImage StaticLogo { get; }

    public LobbyLoginControl(GraphicsDevice device)
    {
        Name = "StartScreen";
        X = 0;
        Y = 0;
        Width = 640;
        Height = 480;

        var prefabSet = DataContext.UserControls.Get("_nstart");

        if (prefabSet is null)
            throw new InvalidOperationException("Failed to load _nstart control prefab set");

        // Background — the anchor control (first control) rendered as the panel's background texture
        var bgPrefab = prefabSet[0];
        Background = ConvertImage(device, bgPrefab, 0);

        // Static logo — standalone SPF file
        var logoPrefab = prefabSet["LOGO"];
        var logoRect = logoPrefab.Control.Rect!.Value;

        StaticLogo = new UIImage
        {
            Name = "StaticLogo",
            X = (int)logoRect.Left,
            Y = (int)logoRect.Top,
            Width = (int)logoRect.Width,
            Height = (int)logoRect.Height,
            Texture = TextureConverter.LoadSpfTexture(device, "_nslogo1.spf")
        };
        AddChild(StaticLogo);

        // Animated logo — 20 frames from _nslogo.spf in the prefab
        var animFrames = new Texture2D[logoPrefab.Images.Count];

        for (var i = 0; i < logoPrefab.Images.Count; i++)
            animFrames[i] = TextureConverter.ToTexture2D(device, logoPrefab.Images[i]);

        AnimatedLogo = new UIAnimatedImage
        {
            Name = "AnimatedLogo",
            X = (int)logoRect.Left,
            Y = (int)logoRect.Top,
            Width = (int)logoRect.Width,
            Height = (int)logoRect.Height,
            Frames = animFrames,
            FrameIntervalMs = 150,
            Looping = true,
            PingPong = true
        };
        AddChild(AnimatedLogo);

        // Buttons — all start disabled
        CreateButton = CreateButtonFromPrefab(device, prefabSet, "Create");
        CreateButton.Enabled = false;
        AddChild(CreateButton);

        ContinueButton = CreateButtonFromPrefab(device, prefabSet, "Continue");
        ContinueButton.Enabled = false;
        AddChild(ContinueButton);

        PasswordButton = CreateButtonFromPrefab(device, prefabSet, "Password");
        PasswordButton.Enabled = false;
        AddChild(PasswordButton);

        CreditButton = CreateButtonFromPrefab(device, prefabSet, "Credit");
        CreditButton.Enabled = false;
        AddChild(CreditButton);

        HomepageButton = CreateButtonFromPrefab(device, prefabSet, "Homepage");
        HomepageButton.Enabled = false;
        AddChild(HomepageButton);

        ExitButton = CreateButtonFromPrefab(device, prefabSet, "Exit");
        ExitButton.Enabled = false;
        AddChild(ExitButton);
    }

    private static Texture2D? ConvertImage(GraphicsDevice device, ControlPrefab prefab, int imageIndex)
    {
        if (imageIndex >= prefab.Images.Count)
            return null;

        return TextureConverter.ToTexture2D(device, prefab.Images[imageIndex]);
    }

    private static UIButton CreateButtonFromPrefab(GraphicsDevice device, ControlPrefabSet prefabSet, string controlName)
    {
        var prefab = prefabSet[controlName];
        var rect = prefab.Control.Rect!.Value;

        var normalTexture = prefab.Images.Count > 0 ? TextureConverter.ToTexture2D(device, prefab.Images[0]) : null;

        var hoverTexture = prefab.Images.Count > 1 ? TextureConverter.ToTexture2D(device, prefab.Images[1]) : null;

        return new UIButton
        {
            Name = controlName,
            X = (int)rect.Left,
            Y = (int)rect.Top,
            Width = (int)rect.Width,
            Height = (int)rect.Height,
            NormalTexture = normalTexture,
            HoverTexture = hoverTexture
        };
    }

    public void EnableButtons() => SetButtonsEnabled(true);

    public void SetButtonsEnabled(bool enabled)
    {
        CreateButton.Enabled = enabled;
        ContinueButton.Enabled = enabled;
        PasswordButton.Enabled = enabled;
        CreditButton.Enabled = enabled;
        HomepageButton.Enabled = enabled;
        ExitButton.Enabled = enabled;
    }
}
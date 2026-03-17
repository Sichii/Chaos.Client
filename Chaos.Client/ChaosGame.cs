#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Networking;
using Chaos.Client.Rendering;
using Chaos.Client.Screens;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client;

public sealed class ChaosGame : Game
{
    public const int VIRTUAL_WIDTH = 640;
    public const int VIRTUAL_HEIGHT = 480;
    private const float ASPECT_RATIO = (float)VIRTUAL_WIDTH / VIRTUAL_HEIGHT;

    private readonly GraphicsDeviceManager Graphics;
    private readonly List<ServerPacket> PacketBuffer = [];
    private int CursorOffsetX;
    private int CursorOffsetY;
    private Texture2D? CursorTexture;
    private RenderTarget2D RenderTarget = null!;
    private bool ResizingInProgress;
    private SpriteBatch SpriteBatch = null!;

    /// <summary>
    ///     Event-driven input buffer that captures all keyboard and mouse input between frames. Screens should read from this
    ///     instead of polling Keyboard/Mouse.GetState() directly.
    /// </summary>
    public InputBuffer Input { get; private set; } = null!;

    /// <summary>
    ///     The screen manager that owns the active screen stack.
    /// </summary>
    public ScreenManager Screens { get; private set; } = null!;

    /// <summary>
    ///     Shared aisling renderer for compositing player/NPC equipment layers.
    /// </summary>
    public AislingRenderer AislingRenderer { get; } = new();

    /// <summary>
    ///     The connection manager that orchestrates lobby, login, and world connections.
    /// </summary>
    public ConnectionManager Connection { get; }

    /// <summary>
    ///     Shared creature sprite renderer with per-frame texture cache.
    /// </summary>
    public CreatureRenderer CreatureRenderer { get; } = new();

    /// <summary>
    ///     Tracks all visible entities in the current map.
    /// </summary>
    public WorldState World { get; } = new();

    public ChaosGame()
    {
        Graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = VIRTUAL_WIDTH,
            PreferredBackBufferHeight = VIRTUAL_HEIGHT
        };

        Connection = new ConnectionManager();

        Window.Title = "Darkages";
        Window.AllowUserResizing = true;
        IsMouseVisible = true;
    }

    protected override void Draw(GameTime gameTime)
    {
        // Render everything at virtual resolution
        GraphicsDevice.SetRenderTarget(RenderTarget);
        GraphicsDevice.Clear(Color.Black);
        Screens.Draw(SpriteBatch, gameTime);

        if (DebugOverlay.IsActive && Screens.ActiveScreen?.Root is { } root)
            DebugOverlay.Draw(SpriteBatch, GraphicsDevice, root);

        // Custom cursor — drawn in virtual space so it aligns with game content
        if (CursorTexture is not null)
        {
            SpriteBatch.Begin(samplerState: GlobalSettings.Sampler);

            SpriteBatch.Draw(CursorTexture, new Vector2(Input.MouseX - CursorOffsetX, Input.MouseY - CursorOffsetY), Color.White);

            SpriteBatch.End();
        }

        // Scale to window (aspect ratio is locked, so it always fills perfectly)
        GraphicsDevice.SetRenderTarget(null);
        SpriteBatch.Begin(samplerState: GlobalSettings.Sampler);
        SpriteBatch.Draw(RenderTarget, GraphicsDevice.Viewport.Bounds, Color.White);
        SpriteBatch.End();

        base.Draw(gameTime);
    }

    protected override void Initialize()
    {
        base.Initialize();

        Window.ClientSizeChanged += OnClientSizeChanged;
    }

    protected override void LoadContent()
    {
        SpriteBatch = new SpriteBatch(GraphicsDevice);
        RenderTarget = new RenderTarget2D(GraphicsDevice, VIRTUAL_WIDTH, VIRTUAL_HEIGHT);
        Input = new InputBuffer(Window);
        Screens = new ScreenManager(this);

        LoadCustomCursor();

        Screens.Switch(new LobbyLoginScreen());
    }

    private void LoadCustomCursor()
    {
        var frames = TextureConverter.LoadEpfTextures(GraphicsDevice, "mouse.epf");

        if (frames.Length > 0)
        {
            CursorTexture = frames[0];

            for (var i = 1; i < frames.Length; i++)
                frames[i]
                    .Dispose();

            IsMouseVisible = false;

            // Scan for the top-left most non-transparent pixel to find the arrow tip
            var pixels = new Color[CursorTexture.Width * CursorTexture.Height];
            CursorTexture.GetData(pixels);

            CursorOffsetX = CursorTexture.Width;
            CursorOffsetY = CursorTexture.Height;

            for (var y = 0; y < CursorTexture.Height; y++)
                for (var x = 0; x < CursorTexture.Width; x++)
                    if (pixels[y * CursorTexture.Width + x].A > 0)
                    {
                        if (x < CursorOffsetX)
                            CursorOffsetX = x;

                        if (y < CursorOffsetY)
                            CursorOffsetY = y;
                    }
        }
    }

    #region Aspect Ratio Constraint
    /// <summary>
    ///     Corrects the window size after a resize to enforce 4:3 aspect ratio.
    ///     Uses the larger dimension as the reference and adjusts the other.
    /// </summary>
    private void OnClientSizeChanged(object? sender, EventArgs e)
    {
        if (ResizingInProgress)
            return;

        var width = Window.ClientBounds.Width;
        var height = Window.ClientBounds.Height;

        if ((width <= 0) || (height <= 0))
            return;

        // Determine corrected dimensions preserving 4:3
        var correctedWidth = (int)(height * ASPECT_RATIO);
        var correctedHeight = (int)(width / ASPECT_RATIO);

        int newWidth,
            newHeight;

        if (correctedWidth <= width)
        {
            // Height is the constraining dimension
            newWidth = correctedWidth;
            newHeight = height;
        } else
        {
            // Width is the constraining dimension
            newWidth = width;
            newHeight = correctedHeight;
        }

        if ((newWidth == width) && (newHeight == height))
            return;

        ResizingInProgress = true;

        Graphics.PreferredBackBufferWidth = newWidth;
        Graphics.PreferredBackBufferHeight = newHeight;
        Graphics.ApplyChanges();

        ResizingInProgress = false;
    }
    #endregion

    protected override void UnloadContent()
    {
        Window.ClientSizeChanged -= OnClientSizeChanged;
        CursorTexture?.Dispose();
        RenderTarget.Dispose();
        Screens.Dispose();
        Connection.Dispose();
        Input.Dispose();
        CreatureRenderer.Dispose();
        AislingRenderer.Dispose();
        base.UnloadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        // Compute scale for mouse coordinate transform (window is always 4:3, so uniform scale)
        var scale = (float)GraphicsDevice.PresentationParameters.BackBufferWidth / VIRTUAL_WIDTH;
        Input.SetVirtualScale(scale);

        // Freeze buffered input for this frame before anything reads it
        Input.Update();

        // F12 — toggle debug overlay (handled globally before screen update)
        if (Input.WasKeyPressed(Keys.F12))
            DebugOverlay.Toggle();

        // Drain and process network packets each frame
        PacketBuffer.Clear();
        Connection.ProcessPackets(PacketBuffer);

        Screens.Update(gameTime);

        base.Update(gameTime);
    }
}
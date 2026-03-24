#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Data;
using Chaos.Client.Networking;
using Chaos.Client.Rendering;
using Chaos.Client.Screens;
using Chaos.Client.Systems;
using Chaos.Client.Systems.Sound;
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
    ///     Shared spell/effect animation renderer with per-frame texture cache.
    /// </summary>
    public EffectRenderer EffectRenderer { get; } = new();

    /// <summary>
    ///     Shared item sprite renderer with frame offset metadata. Evicted on map change.
    /// </summary>
    public ItemRenderer ItemRenderer { get; } = new();

    /// <summary>
    ///     Manages metadata file synchronization with the server.
    /// </summary>
    public MetaDataManager MetaData { get; }

    /// <summary>
    ///     Client settings loaded from the DarkAges config file.
    /// </summary>
    public ClientSettings Settings { get; } = ClientSettings.Load();

    /// <summary>
    ///     Manages sound effect and music playback.
    /// </summary>
    public SoundManager SoundManager { get; } = new();

    /// <summary>
    ///     Tracks all visible entities in the current map.
    /// </summary>
    public WorldState World { get; } = new();

    public static GraphicsDevice Device => TextureConverter.Device;

    public ChaosGame()
    {
        Graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = VIRTUAL_WIDTH,
            PreferredBackBufferHeight = VIRTUAL_HEIGHT,
            PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8,
            SynchronizeWithVerticalRetrace = false
        };

        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
        InactiveSleepTime = TimeSpan.Zero;

        Connection = new ConnectionManager();
        MetaData = new MetaDataManager(Connection, GlobalSettings.DataPath);
        Connection.OnMetaData += MetaData.HandleMetaData;
        Connection.OnWorldEntryComplete += MetaData.RequestSync;

        MetaData.OnMetaDataUpdated += updatedFiles =>
        {
            foreach (var name in updatedFiles)
                DataContext.MetaFiles.Invalidate(name);
        };

        // Wire state events to WorldState at startup so state is tracked
        // even during world entry (before WorldScreen is created)
        World.SubscribeTo(Connection);
        Connection.OnDisplayVisibleEntities += args => World.AddOrUpdateVisibleEntities(args);
        Connection.OnDisplayAisling += args => World.AddOrUpdateAisling(args);

        // RemoveEntity wired in WorldScreen — it needs to capture the creature sprite for
        // the death dissolve animation before removing the entity from WorldState.
        // Fallback for non-world screens (e.g., during world entry before WorldScreen exists).
        Connection.OnRemoveEntity += id =>
        {
            if (Screens.ActiveScreen is not WorldScreen)
                World.RemoveEntity(id);
        };

        Connection.OnCreatureWalk += (
            id,
            oldX,
            oldY,
            dir) =>
        {
            var entity = World.GetEntity(id);
            var walkFrames = entity is not null && (entity.SpriteId > 0) ? CreatureRenderer.GetWalkFrameCount(entity.SpriteId) : null;

            World.HandleCreatureWalk(
                id,
                oldX,
                oldY,
                dir,
                walkFrames);
        };
        Connection.OnCreatureTurn += (id, dir) => World.HandleCreatureTurn(id, dir);

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
            DebugOverlay.Draw(SpriteBatch, root);

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

        RenderTarget = new RenderTarget2D(
            GraphicsDevice,
            VIRTUAL_WIDTH,
            VIRTUAL_HEIGHT,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24Stencil8);
        Input = new InputBuffer(this);
        Screens = new ScreenManager(this);

        TextureConverter.Device = GraphicsDevice;
        UiRenderer.Instance = new UiRenderer(GraphicsDevice);

        LoadCustomCursor();

        Screens.Switch(new LobbyLoginScreen());
    }

    private void LoadCustomCursor()
    {
        CursorTexture = UiRenderer.Instance!.GetEpfTexture("mouse.epf", 0);

        if (CursorTexture is not null)
        {
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
        EffectRenderer.Dispose();
        ItemRenderer.Dispose();
        SoundManager.Dispose();
        UiRenderer.Instance?.Dispose();
        UiRenderer.Instance = null;
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

        DebugOverlay.Update(gameTime);

        // Drain and process network packets each frame
        PacketBuffer.Clear();
        Connection.ProcessPackets(PacketBuffer);

        Screens.Update(gameTime);

        base.Update(gameTime);
    }
}
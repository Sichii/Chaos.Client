#region
using System.IO.Compression;
using Chaos.Client.Collections;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Networking;
using Chaos.Client.Screens;
using Chaos.Client.Systems;
using Chaos.Cryptography;
using Chaos.DarkAges.Definitions;
using Chaos.Networking.Entities.Server;
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
    private readonly string MetaFilePath = Path.Combine(GlobalSettings.DataPath, "metafile");
    private readonly Dictionary<string, uint> MetaPendingChecksums = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ServerPacket> PacketBuffer = [];
    private int CursorOffsetX;
    private int CursorOffsetY;
    private Texture2D? CursorTexture;
    internal volatile bool GcRequested;
    private int HandCursorOffsetX;
    private int HandCursorOffsetY;
    private Texture2D? HandCursorTexture;
    private bool MetaSyncStarted;
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

    public bool UseHandCursor { get; set; }

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
    ///     Manages sound effect and music playback.
    /// </summary>
    public SoundSystem SoundSystem { get; } = new();

    public static GraphicsDevice Device => TextureConverter.Device;

    public ChaosGame()
    {
        ClientSettings.Load();

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
        Directory.CreateDirectory(MetaFilePath);
        Connection.OnMetaData += HandleMetaData;
        Connection.OnWorldEntryComplete += () => Connection.SendMetaDataRequest(MetaDataRequestType.AllCheckSums);

        // Wire state events to WorldState at startup so state is tracked
        // even during world entry (before WorldScreen is created)
        WorldState.SubscribeTo(Connection);
        Connection.OnDisplayVisibleEntities += WorldState.AddOrUpdateVisibleEntities;
        Connection.OnDisplayAisling += WorldState.AddOrUpdateAisling;

        // RemoveEntity wired in WorldScreen — it needs to capture the creature sprite for
        // the death dissolve animation before removing the entity from WorldState.
        // Fallback for non-world screens (e.g., during world entry before WorldScreen exists).
        Connection.OnRemoveEntity += id =>
        {
            if (Screens.ActiveScreen is not WorldScreen)
                WorldState.RemoveEntity(id);
        };

        Connection.OnCreatureWalk += (
            id,
            oldX,
            oldY,
            dir) =>
        {
            var entity = WorldState.GetEntity(id);
            var walkFrames = entity is not null && (entity.SpriteId > 0) ? CreatureRenderer.GetWalkFrameCount(entity.SpriteId) : null;

            WorldState.HandleCreatureWalk(
                id,
                oldX,
                oldY,
                dir,
                walkFrames);
        };
        Connection.OnCreatureTurn += (id, dir) => WorldState.HandleCreatureTurn(id, dir);

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

        if (DebugOverlay.IsActive)
            DebugOverlay.DrawStats(SpriteBatch);

        // Custom cursor — drawn in virtual space so it aligns with game content
        if (CursorTexture is not null)
        {
            var activeCursor = UseHandCursor && HandCursorTexture is not null ? HandCursorTexture : CursorTexture;
            var offsetX = UseHandCursor && HandCursorTexture is not null ? HandCursorOffsetX : CursorOffsetX;
            var offsetY = UseHandCursor && HandCursorTexture is not null ? HandCursorOffsetY : CursorOffsetY;

            SpriteBatch.Begin(samplerState: GlobalSettings.Sampler);
            SpriteBatch.Draw(activeCursor, new Vector2(Input.MouseX - offsetX, Input.MouseY - offsetY), Color.White);
            SpriteBatch.End();
        }

        // Scale to window (aspect ratio is locked, so it always fills perfectly)
        GraphicsDevice.SetRenderTarget(null);
        SpriteBatch.Begin(samplerState: GlobalSettings.Sampler);
        SpriteBatch.Draw(RenderTarget, GraphicsDevice.Viewport.Bounds, Color.White);
        SpriteBatch.End();

        base.Draw(gameTime);

        DebugOverlay.EndFrame();
    }

    protected override void EndDraw()
    {
        base.EndDraw();

        if (GcRequested)
        {
            GcRequested = false;

            GC.Collect(
                2,
                GCCollectionMode.Aggressive,
                true,
                true);

            GC.WaitForPendingFinalizers();
        }
    }

    private static (int X, int Y) FindCursorHotspot(Texture2D texture)
    {
        var pixels = new Color[texture.Width * texture.Height];
        texture.GetData(pixels);

        var hotX = texture.Width;
        var hotY = texture.Height;

        for (var y = 0; y < texture.Height; y++)
            for (var x = 0; x < texture.Width; x++)
                if (pixels[y * texture.Width + x].A > 0)
                {
                    if (x < hotX)
                        hotX = x;

                    if (y < hotY)
                        hotY = y;
                }

        return (hotX, hotY);
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
        FontAtlas.Initialize(GraphicsDevice);
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
            (CursorOffsetX, CursorOffsetY) = FindCursorHotspot(CursorTexture);
        }

        HandCursorTexture = UiRenderer.Instance!.GetEpfTexture("mouse.epf", 1);

        if (HandCursorTexture is not null)
            (HandCursorOffsetX, HandCursorOffsetY) = FindCursorHotspot(HandCursorTexture);
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

    /// <summary>
    ///     Fired when all metadata files are up to date with the server.
    /// </summary>
    public event Action? OnMetaDataSyncComplete;

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
        SoundSystem.Dispose();
        UiRenderer.Instance?.Dispose();
        UiRenderer.Instance = null;
        base.UnloadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        DebugOverlay.BeginFrame();

        // Compute scale for mouse coordinate transform (window is always 4:3, so uniform scale)
        var scale = (float)GraphicsDevice.PresentationParameters.BackBufferWidth / VIRTUAL_WIDTH;
        Input.SetVirtualScale(scale);

        // Freeze buffered input for this frame before anything reads it
        Input.Update(gameTime);

        // F12 — toggle debug overlay (handled globally before screen update)
        if (Input.WasKeyPressed(Keys.F12))
            DebugOverlay.Toggle();

        DebugOverlay.Update(gameTime);

        // Drain and process network packets each frame
        PacketBuffer.Clear();
        Connection.ProcessPackets(PacketBuffer);

        SoundSystem.Update();

        Screens.Update(gameTime);

        base.Update(gameTime);
    }

    #region Metadata Sync
    private uint ComputeLocalMetaCheckSum(string name)
    {
        var filePath = Path.Combine(MetaFilePath, name);

        if (!File.Exists(filePath))
            return 0;

        try
        {
            using var fileStream = File.OpenRead(filePath);
            using var zlibStream = new ZLibStream(fileStream, CompressionMode.Decompress);
            using var memoryStream = new MemoryStream();

            zlibStream.CopyTo(memoryStream);

            return Crc.Generate32(memoryStream.ToArray());
        } catch
        {
            return 0;
        }
    }

    private void HandleMetaData(MetaDataArgs args)
    {
        switch (args.MetaDataRequestType)
        {
            case MetaDataRequestType.AllCheckSums:
                HandleMetaDataCheckSums(args.MetaDataCollection);

                break;

            case MetaDataRequestType.DataByName:
                HandleMetaDataFileData(args.MetaDataInfo);

                break;
        }
    }

    private void HandleMetaDataCheckSums(ICollection<MetaDataInfo>? collection)
    {
        if (collection is null || (collection.Count == 0))
        {
            OnMetaDataSyncComplete?.Invoke();

            return;
        }

        MetaPendingChecksums.Clear();
        MetaSyncStarted = true;

        foreach (var info in collection)
        {
            var localCheckSum = ComputeLocalMetaCheckSum(info.Name);

            if (localCheckSum != info.CheckSum)
                MetaPendingChecksums[info.Name] = info.CheckSum;
        }

        foreach (var name in MetaPendingChecksums.Keys)
            Connection.SendMetaDataRequest(MetaDataRequestType.DataByName, name);

        if (MetaPendingChecksums.Count == 0)
            OnMetaDataSyncComplete?.Invoke();
    }

    private void HandleMetaDataFileData(MetaDataInfo? info)
    {
        if (info is null || string.IsNullOrEmpty(info.Name) || (info.Data.Length == 0))
            return;

        File.WriteAllBytes(Path.Combine(MetaFilePath, info.Name), info.Data);
        MetaPendingChecksums.Remove(info.Name);

        if (MetaSyncStarted && (MetaPendingChecksums.Count == 0))
            OnMetaDataSyncComplete?.Invoke();
    }
    #endregion
}
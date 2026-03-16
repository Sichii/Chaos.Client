#region
using System.Runtime.InteropServices;
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

public class ChaosGame : Game
{
    public const int VIRTUAL_WIDTH = 640;
    public const int VIRTUAL_HEIGHT = 480;
    private const float ASPECT_RATIO = (float)VIRTUAL_WIDTH / VIRTUAL_HEIGHT;

    private readonly GraphicsDeviceManager Graphics;
    private readonly List<ServerPacket> PacketBuffer = [];
    private nint Hwnd;
    private nint OriginalWndProc;
    private RenderTarget2D RenderTarget = null!;
    private SpriteBatch SpriteBatch = null!;

    // Win32 window subclass for aspect ratio constraint — prevents delegate from being GC'd
    private WndProcDelegate? WndProcInstance;

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

        // Scale to window (aspect ratio is locked, so it always fills perfectly)
        GraphicsDevice.SetRenderTarget(null);
        SpriteBatch.Begin(samplerState: SamplerState.PointClamp);
        SpriteBatch.Draw(RenderTarget, GraphicsDevice.Viewport.Bounds, Color.White);
        SpriteBatch.End();

        base.Draw(gameTime);
    }

    protected override void Initialize()
    {
        base.Initialize();

        InstallAspectRatioConstraint();
    }

    protected override void LoadContent()
    {
        SpriteBatch = new SpriteBatch(GraphicsDevice);
        RenderTarget = new RenderTarget2D(GraphicsDevice, VIRTUAL_WIDTH, VIRTUAL_HEIGHT);
        Input = new InputBuffer(Window);
        Screens = new ScreenManager(this);

        Screens.Switch(new LobbyLoginScreen());
    }

    protected override void UnloadContent()
    {
        RemoveAspectRatioConstraint();
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

    #region Win32 Aspect Ratio Constraint
    /// <summary>
    ///     Subclasses the native window to intercept WM_SIZING, constraining the drag rect to 4:3 aspect ratio in real-time as
    ///     the user drags the window border. MonoGame DesktopGL's Window.Handle returns the SDL_Window pointer, not the Win32
    ///     HWND, so we extract the HWND via SDL_GetWindowWMInfo.
    /// </summary>
    private void InstallAspectRatioConstraint()
    {
        Hwnd = GetHwndFromSdlWindow(Window.Handle);

        if (Hwnd == nint.Zero)
            return;

        WndProcInstance = AspectRatioWndProc;
        OriginalWndProc = SetWindowLongPtrW(Hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(WndProcInstance));
    }

    private void RemoveAspectRatioConstraint()
    {
        if ((OriginalWndProc == nint.Zero) || (Hwnd == nint.Zero))
            return;

        SetWindowLongPtrW(Hwnd, GWLP_WNDPROC, OriginalWndProc);
        OriginalWndProc = nint.Zero;
        WndProcInstance = null;
    }

    private nint AspectRatioWndProc(
        nint hWnd,
        uint msg,
        nint wParam,
        nint lParam)
    {
        if (msg == WM_SIZING)
        {
            var rect = Marshal.PtrToStructure<RECT>(lParam);

            // Compute frame (non-client) dimensions so we enforce aspect ratio on the client area
            GetClientRect(hWnd, out var clientRect);
            GetWindowRect(hWnd, out var windowRect);

            var frameW = windowRect.Right - windowRect.Left - clientRect.Right;
            var frameH = windowRect.Bottom - windowRect.Top - clientRect.Bottom;

            var clientW = rect.Right - rect.Left - frameW;
            var clientH = rect.Bottom - rect.Top - frameH;

            var edge = (int)wParam;

            switch (edge)
            {
                case WMSZ_LEFT:
                case WMSZ_RIGHT:
                {
                    // Horizontal drag — adjust height to match width
                    var correctedH = (int)(clientW / ASPECT_RATIO);
                    rect.Bottom = rect.Top + correctedH + frameH;

                    break;
                }

                case WMSZ_TOP:
                case WMSZ_BOTTOM:
                {
                    // Vertical drag — adjust width to match height
                    var correctedW = (int)(clientH * ASPECT_RATIO);
                    rect.Right = rect.Left + correctedW + frameW;

                    break;
                }

                case WMSZ_TOPLEFT:
                case WMSZ_TOPRIGHT:
                case WMSZ_BOTTOMLEFT:
                case WMSZ_BOTTOMRIGHT:
                {
                    // Project the proposed size onto the 4:3 diagonal using radial distance.
                    // The diagonal unit vector for a 4:3 rect is (4, 3) normalized.
                    // Dot the proposed (clientW, clientH) onto it to get a single scalar,
                    // then derive both dimensions from that scalar.
                    var diagonal = MathF.Sqrt(ASPECT_RATIO * ASPECT_RATIO + 1f);
                    var dot = (clientW * ASPECT_RATIO + clientH) / diagonal;
                    var newW = (int)(dot * ASPECT_RATIO / diagonal);
                    var newH = (int)(dot / diagonal);

                    switch (edge)
                    {
                        case WMSZ_TOPLEFT:
                            rect.Left = rect.Right - newW - frameW;
                            rect.Top = rect.Bottom - newH - frameH;

                            break;

                        case WMSZ_TOPRIGHT:
                            rect.Right = rect.Left + newW + frameW;
                            rect.Top = rect.Bottom - newH - frameH;

                            break;

                        case WMSZ_BOTTOMLEFT:
                            rect.Left = rect.Right - newW - frameW;
                            rect.Bottom = rect.Top + newH + frameH;

                            break;

                        case WMSZ_BOTTOMRIGHT:
                            rect.Right = rect.Left + newW + frameW;
                            rect.Bottom = rect.Top + newH + frameH;

                            break;
                    }

                    break;
                }
            }

            Marshal.StructureToPtr(rect, lParam, false);

            return 1;
        }

        return CallWindowProcW(
            OriginalWndProc,
            hWnd,
            msg,
            wParam,
            lParam);
    }

    /// <summary>
    ///     Extracts the Win32 HWND from an SDL_Window pointer via SDL_GetWindowWMInfo.
    /// </summary>
    private static nint GetHwndFromSdlWindow(nint sdlWindow)
    {
        if (sdlWindow == nint.Zero)
            return nint.Zero;

        var info = new SDL_SysWMinfo();
        SDL_GetVersion(out info.Version);

        if (!SDL_GetWindowWMInfo(sdlWindow, ref info))
            return nint.Zero;

        return info.Hwnd;
    }

    // Win32 constants
    private const int WM_SIZING = 0x0214;
    private const int WMSZ_LEFT = 1;
    private const int WMSZ_RIGHT = 2;
    private const int WMSZ_TOP = 3;
    private const int WMSZ_TOPLEFT = 4;
    private const int WMSZ_TOPRIGHT = 5;
    private const int WMSZ_BOTTOM = 6;
    private const int WMSZ_BOTTOMLEFT = 7;
    private const int WMSZ_BOTTOMRIGHT = 8;
    private const int GWLP_WNDPROC = -4;

    // Win32 interop
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left,
                   Top,
                   Right,
                   Bottom;
    }

    private delegate nint WndProcDelegate(
        nint hWnd,
        uint msg,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    private static extern nint CallWindowProcW(
        nint lpPrevWndFunc,
        nint hWnd,
        uint msg,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    // SDL2 interop — extract HWND from SDL_Window
    [StructLayout(LayoutKind.Sequential)]
    private struct SDL_version
    {
        public byte Major,
                    Minor,
                    Patch;
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct SDL_SysWMinfo
    {
        [FieldOffset(0)]
        public SDL_version Version;

        [FieldOffset(4)]
        public int Subsystem;

        [FieldOffset(8)]
        public IntPtr Hwnd;
    }

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_GetVersion(out SDL_version ver);

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SDL_GetWindowWMInfo(nint window, ref SDL_SysWMinfo info);
    #endregion
}
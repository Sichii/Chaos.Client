#region
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Controls.LobbyLogin;
using Chaos.Client.Data;
using Chaos.Client.Definitions;
using Chaos.Client.Networking;
using Chaos.Client.Rendering;
using Chaos.Cryptography;
using Chaos.DarkAges.Definitions;
using Chaos.Networking.Entities.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Screens;

public sealed class LobbyLoginScreen : IScreen
{
    private readonly bool ReturningFromWorld;
    private bool AwaitingCharFinalize;

    private uint? CachedNoticeCheckSum;
    private bool ChangingPassword;
    private CharacterCreationControl CharCreateControl = null!;

    // Flow state
    private bool Connecting;
    private bool CreatingCharacter;
    private GraphicsDevice Device = null!;
    private EulaNoticeControl EulaNoticeControl = null!;

    private ChaosGame Game = null!;
    private string? HomepageUrl;
    private LoginControl LoginControl = null!;
    private PasswordChangeControl PasswordChangeControl = null!;
    private bool PendingWorldSwitch;
    private OkPopupMessageControl PopupMessage = null!;
    private IReadOnlyList<ServerTableEntry> ServerList = [];
    private ServerSelectControl ServerSelectControl = null!;

    // UI panels
    private LobbyLoginControl StartPanel = null!;
    private UILabel StatusLabel = null!;

    /// <inheritdoc />
    public UIPanel? Root { get; private set; }

    public LobbyLoginScreen(bool returningFromWorld = false) => ReturningFromWorld = returningFromWorld;

    /// <inheritdoc />
    public void Dispose() => Root?.Dispose();

    /// <inheritdoc />
    public void Draw(SpriteBatch spriteBatch, GameTime gameTime)
    {
        spriteBatch.Begin(samplerState: GlobalSettings.Sampler);
        Root!.Draw(spriteBatch);
        spriteBatch.End();

        DebugOverlay.SnapshotDrawCount(spriteBatch.GraphicsDevice);
    }

    /// <inheritdoc />
    public void Initialize(ChaosGame game)
    {
        Game = game;

        Game.Connection.StateChanged += OnConnectionStateChanged;
        Game.Connection.OnError += OnConnectionError;
        Game.Connection.OnServerTableReceived += OnServerTableReceived;
        Game.Connection.OnRedirectReceived += OnRedirectReceived;
        Game.Connection.OnLoginMessage += OnLoginMessage;
        Game.Connection.OnLoginNotice += OnLoginNotice;
        Game.Connection.OnLoginControl += OnLoginControlReceived;
    }

    /// <inheritdoc />
    public void LoadContent(GraphicsDevice graphicsDevice)
    {
        Device = graphicsDevice;

        StartPanel = new LobbyLoginControl(Device);
        LoginControl = new LoginControl(Device);
        ServerSelectControl = new ServerSelectControl(Device);
        EulaNoticeControl = new EulaNoticeControl(Device);
        CharCreateControl = new CharacterCreationControl(Device, Game.AislingRenderer);
        PasswordChangeControl = new PasswordChangeControl(Device);

        // Wire button events
        StartPanel.ContinueButton?.OnClick += OnContinueClicked;
        StartPanel.ExitButton?.OnClick += OnExitClicked;
        StartPanel.SubmitCreateButton?.OnClick += OnCreateClicked;
        StartPanel.PasswordButton?.OnClick += OnPasswordClicked;
        StartPanel.CreditButton?.OnClick += OnCreditClicked;
        StartPanel.HomepageButton?.OnClick += OnHomepageClicked;

        LoginControl.OkButton?.OnClick += OnLoginOkClicked;
        LoginControl.CancelButton?.OnClick += OnLoginCancelClicked;

        ServerSelectControl.OnServerSelected += OnServerSelected;

        EulaNoticeControl.OnOk += OnEulaAccepted;
        EulaNoticeControl.OnCancel += OnEulaCancelled;

        CharCreateControl.OnOk += OnCharCreateOkClicked;
        CharCreateControl.OnCancel += OnCharCreateCancelClicked;

        PasswordChangeControl.OnOk += OnPasswordChangeOkClicked;
        PasswordChangeControl.OnCancel += OnPasswordChangeCancelClicked;

        PopupMessage = new OkPopupMessageControl(Device)
        {
            ZIndex = 1
        };
        PopupMessage.OnOk += OnPopupMessageOk;

        StatusLabel = new UILabel(Device)
        {
            Name = "StatusText",
            X = 0,
            Y = ChaosGame.VIRTUAL_HEIGHT - 20,
            Width = ChaosGame.VIRTUAL_WIDTH,
            Height = 12,
            Alignment = TextAlignment.Center,
            ZIndex = 2
        };

        Root = new UIPanel
        {
            Name = "LobbyRoot",
            Width = ChaosGame.VIRTUAL_WIDTH,
            Height = ChaosGame.VIRTUAL_HEIGHT
        };
        Root.AddChild(StartPanel);
        Root.AddChild(LoginControl);
        Root.AddChild(ServerSelectControl);
        Root.AddChild(EulaNoticeControl);
        Root.AddChild(CharCreateControl);
        Root.AddChild(PasswordChangeControl);
        Root.AddChild(PopupMessage);
        Root.AddChild(StatusLabel);

        // Build UI atlas after all login controls are constructed
        UiRenderer.Instance?.BuildAtlas();

        if (ReturningFromWorld)
        {
            // Already connected to login server via redirect — skip lobby handshake, show login directly
            StartPanel.Visible = false;
            LoginControl.Show();
            SetStatus("Logged out.", Color.LightBlue);
        } else

            // Fresh start — connect to lobby
            BeginLobbyConnect();
    }

    /// <inheritdoc />
    public void UnloadContent()
    {
        Game.Connection.StateChanged -= OnConnectionStateChanged;
        Game.Connection.OnError -= OnConnectionError;
        Game.Connection.OnServerTableReceived -= OnServerTableReceived;
        Game.Connection.OnRedirectReceived -= OnRedirectReceived;
        Game.Connection.OnLoginMessage -= OnLoginMessage;
        Game.Connection.OnLoginNotice -= OnLoginNotice;
        Game.Connection.OnLoginControl -= OnLoginControlReceived;
    }

    /// <inheritdoc />
    public void Update(GameTime gameTime)
    {
        if (PendingWorldSwitch)
        {
            PendingWorldSwitch = false;
            Game.Screens.Switch(new WorldScreen());

            return;
        }

        var input = Game.Input;

        // Popup message takes absolute priority
        if (PopupMessage.Visible)
        {
            PopupMessage.Update(gameTime, input);

            return;
        }

        // Escape dismisses the topmost dialog (but not the EULA — that requires an explicit button)
        if (input.WasKeyPressed(Keys.Escape))
        {
            if (EulaNoticeControl.Visible)
                return;

            if (PasswordChangeControl.Visible)
            {
                OnPasswordChangeCancelClicked();

                return;
            }

            if (CharCreateControl.Visible)
            {
                OnCharCreateCancelClicked();

                return;
            }

            if (LoginControl.Visible)
            {
                LoginControl.Hide();
                StartPanel.SetButtonsEnabled(true);

                return;
            }

            if (ServerSelectControl.Visible)
                ServerSelectControl.Visible = false;

            // Escape with nothing open — do nothing (Exit button exists for quitting)
            return;
        }

        // EULA panel takes priority over all other controls
        if (EulaNoticeControl.Visible)
        {
            EulaNoticeControl.Update(gameTime, input);

            return;
        }

        // Character creation is a full-screen overlay
        if (CharCreateControl.Visible)
        {
            CharCreateControl.Update(gameTime, input);

            return;
        }

        StartPanel.Update(gameTime, input);

        if (PasswordChangeControl.Visible)
            PasswordChangeControl.Update(gameTime, input);

        if (LoginControl.Visible)
            LoginControl.Update(gameTime, input);

        if (ServerSelectControl.Visible)
            ServerSelectControl.Update(gameTime, input);
    }

    private void SetStatus(string message, Color color) => StatusLabel.SetText(message, color);

    #region Button Handlers
    private void OnContinueClicked()
    {
        if (Connecting || LoginControl.Visible || PasswordChangeControl.Visible)
            return;

        LoginControl.Show();
        StartPanel.SetButtonsEnabled(false);
    }

    private void OnExitClicked() => Game.Exit();

    private void OnCreateClicked()
    {
        if (Connecting || LoginControl.Visible || PasswordChangeControl.Visible)
            return;

        CharCreateControl.Show();
    }

    private void OnCharCreateOkClicked()
    {
        var name = CharCreateControl.NameField?.Text;
        var password = CharCreateControl.PasswordField?.Text;
        var passwordConfirm = CharCreateControl.PasswordConfirmField?.Text;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(password))
        {
            PopupMessage.Show("Name and password are required.");

            return;
        }

        if (password != passwordConfirm)
        {
            PopupMessage.Show("Passwords do not match.");
            CharCreateControl.PasswordField?.Text = string.Empty;
            CharCreateControl.PasswordConfirmField?.Text = string.Empty;

            return;
        }

        Connecting = true;
        CreatingCharacter = true;
        AwaitingCharFinalize = false;
        SetStatus("Creating character...", Color.LightBlue);
        Game.Connection.CreateCharInitial(name, password);
    }

    private void OnCharCreateCancelClicked()
    {
        CharCreateControl.Hide();
        CreatingCharacter = false;
        AwaitingCharFinalize = false;
    }

    private void OnPasswordClicked()
    {
        if (Connecting || LoginControl.Visible || PasswordChangeControl.Visible)
            return;

        PasswordChangeControl.Show();
        StartPanel.SetButtonsEnabled(false);
    }

    private void OnPasswordChangeOkClicked()
    {
        var name = PasswordChangeControl.NameField?.Text ?? string.Empty;
        var currentPassword = PasswordChangeControl.CurrentPasswordField?.Text ?? string.Empty;
        var newPassword = PasswordChangeControl.NewPasswordField?.Text ?? string.Empty;
        var confirmPassword = PasswordChangeControl.ConfirmPasswordField?.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
        {
            PopupMessage.Show("All fields are required.");

            return;
        }

        if (newPassword != confirmPassword)
        {
            PopupMessage.Show("New passwords do not match.");
            PasswordChangeControl.NewPasswordField?.Text = string.Empty;
            PasswordChangeControl.ConfirmPasswordField?.Text = string.Empty;

            return;
        }

        Connecting = true;
        ChangingPassword = true;
        SetStatus("Changing password...", Color.LightBlue);
        Game.Connection.ChangePassword(name, currentPassword, newPassword);
    }

    private void OnPasswordChangeCancelClicked()
    {
        PasswordChangeControl.Hide();
        ChangingPassword = false;
        StartPanel.SetButtonsEnabled(true);
    }

    private void OnCreditClicked() => SetStatus("Credits panel not yet implemented.", Color.Yellow);

    private void OnHomepageClicked()
    {
        if (string.IsNullOrWhiteSpace(HomepageUrl))
        {
            SetStatus("Homepage URL not yet received.", Color.Yellow);

            return;
        }

        try
        {
            Process.Start(
                new ProcessStartInfo(HomepageUrl)
                {
                    UseShellExecute = true
                });
        } catch
        {
            SetStatus("Could not open browser.", Color.Yellow);
        }
    }

    private void OnLoginOkClicked()
    {
        var username = LoginControl.UsernameField?.Text ?? string.Empty;
        var password = LoginControl.PasswordField?.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            SetStatus("Username and password are required.", Color.IndianRed);

            return;
        }

        Connecting = true;
        LoginControl.Visible = false;
        SetStatus("Logging in...", Color.LightBlue);
        Game.Connection.Login(username, password);
    }

    private void OnLoginCancelClicked()
    {
        LoginControl.Hide();
        StartPanel.SetButtonsEnabled(true);
    }

    private void OnPopupMessageOk() => PopupMessage.Hide();

    private void OnServerSelected(byte serverId)
    {
        ServerSelectControl.Visible = false;
        SetStatus("Selecting server...", Color.LightBlue);

        var server = ServerList.FirstOrDefault(s => s.Id == serverId);

        if (server is not null)
            Game.Connection.ServerName = server.Name;

        Game.Connection.SelectServer(serverId);
    }
    #endregion

    #region Connection Flow
    private async void BeginLobbyConnect()
    {
        Connecting = true;
        SetStatus("Connecting to lobby...", Color.LightBlue);

        await Game.Connection.ConnectToLobbyAsync(DataContext.LobbyHost, DataContext.LobbyPort, DataContext.ClientVersion);
    }

    private void OnConnectionStateChanged(ConnectionState oldState, ConnectionState newState)
    {
        switch (newState)
        {
            case ConnectionState.Lobby:
                SetStatus("Connected. Negotiating...", Color.LightBlue);

                break;

            case ConnectionState.Login:
                Connecting = false;
                SetStatus("Waiting for server notice...", Color.LightBlue);

                // Buttons are enabled after EULA acceptance (or checksum cache hit) in OnLoginNotice
                break;

            case ConnectionState.World:
                PendingWorldSwitch = true;

                break;

            case ConnectionState.Disconnected when Connecting:
                Connecting = false;
                SetStatus("Disconnected.", Color.IndianRed);

                break;
        }
    }

    private void OnConnectionError(string error)
    {
        Connecting = false;
        SetStatus(error, Color.IndianRed);
    }

    private void OnServerTableReceived(ServerTableData data)
    {
        ServerList = data.Servers;

        if (data is { ShowServerList: true, Servers.Count: > 1 })
        {
            SetStatus("Select a server.", Color.LightBlue);
            ServerSelectControl.SetServers(data.Servers);
            ServerSelectControl.Visible = true;
        } else if (data.Servers.Count > 0)
        {
            // Auto-select the first (or only) server
            SetStatus("Selecting server...", Color.LightBlue);
            Game.Connection.ServerName = data.Servers[0].Name;
            Game.Connection.SelectServer(data.Servers[0].Id);
        } else
            SetStatus("No servers available.", Color.IndianRed);
    }

    private void OnRedirectReceived(RedirectInfo _) => SetStatus("Following redirect...", Color.LightBlue);

    private void OnLoginMessage(LoginMessageArgs args)
    {
        if (CreatingCharacter)
        {
            HandleCharCreateMessage(args);

            return;
        }

        if (ChangingPassword)
        {
            HandlePasswordChangeMessage(args);

            return;
        }

        if (args.LoginMessageType == LoginMessageType.Confirm)
        {
            SetStatus("Login accepted. Waiting for redirect...", Color.LightGreen);

            return;
        }

        // Login failed — show login again for retry, clear password
        Connecting = false;
        LoginControl.Visible = true;

        if (LoginControl.PasswordField is not null)
        {
            LoginControl.PasswordField.Text = string.Empty;
            LoginControl.PasswordField.IsFocused = true;
        }

        PopupMessage.Show(args.Message ?? "Login failed.");
    }

    private void HandleCharCreateMessage(LoginMessageArgs args)
    {
        if (args.LoginMessageType == LoginMessageType.Confirm)
        {
            if (!AwaitingCharFinalize)
            {
                // Initial step confirmed — send finalize with appearance
                AwaitingCharFinalize = true;
                SetStatus("Setting appearance...", Color.LightBlue);

                Game.Connection.CreateCharFinalize(
                    CharCreateControl.SelectedHairStyle,
                    CharCreateControl.SelectedGender,
                    CharCreateControl.SelectedHairColor);
            } else
            {
                // Finalize confirmed — character created, show popup
                Connecting = false;
                CreatingCharacter = false;
                AwaitingCharFinalize = false;
                CharCreateControl.Hide();
                SetStatus(string.Empty, Color.White);
                PopupMessage.Show("Character has been created. Choose \"CONTINUE\".");
            }

            return;
        }

        // Creation failed — show error popup and clear the relevant field
        Connecting = false;
        AwaitingCharFinalize = false;

        switch (args.LoginMessageType)
        {
            case LoginMessageType.ClearNameMessage:
                CharCreateControl.NameField?.Text = string.Empty;

                break;
            case LoginMessageType.ClearPswdMessage:
                CharCreateControl.PasswordField?.Text = string.Empty;
                CharCreateControl.PasswordConfirmField?.Text = string.Empty;

                break;
            case LoginMessageType.Confirm:
                break;
            case LoginMessageType.CharacterDoesntExist:
                break;
            case LoginMessageType.WrongPassword:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        PopupMessage.Show(args.Message ?? "Character creation failed.");
    }

    private void HandlePasswordChangeMessage(LoginMessageArgs args)
    {
        Connecting = false;
        ChangingPassword = false;

        if (args.LoginMessageType == LoginMessageType.Confirm)
        {
            PasswordChangeControl.Hide();
            SetStatus(string.Empty, Color.White);
            PopupMessage.Show("Password has been changed.");

            return;
        }

        PopupMessage.Show(args.Message ?? "Password change failed.");
    }

    private void OnLoginNotice(LoginNoticeArgs args)
    {
        if (!args.IsFullResponse)
        {
            // Checksum-only probe — request full notice if we don't have a cached match
            if (CachedNoticeCheckSum.HasValue && (CachedNoticeCheckSum.Value == args.CheckSum))
            {
                // Already accepted this notice, skip display and enable buttons
                StartPanel.EnableButtons();

                return;
            }

            Game.Connection.RequestNotice();

            return;
        }

        // Full response — decompress and display
        if (args.Data is null or { Length: 0 })
            return;

        var noticeText = DecompressNotice(args.Data);

        EulaNoticeControl.Show(noticeText);
    }

    private void OnEulaAccepted()
    {
        EulaNoticeControl.Hide();
        StartPanel.EnableButtons();
    }

    private void OnEulaCancelled() => Game.Exit();

    private string DecompressNotice(byte[] compressedData)
    {
        using var compressed = new MemoryStream(compressedData);
        using var decompressor = new ZLibStream(compressed, CompressionMode.Decompress);
        using var decompressed = new MemoryStream();
        decompressor.CopyTo(decompressed);

        var rawBytes = decompressed.ToArray();
        CachedNoticeCheckSum = Crc.Generate32(rawBytes);

        return Encoding.GetEncoding(949)
                       .GetString(rawBytes);
    }

    private void OnLoginControlReceived(LoginControlArgs args)
    {
        if (args.LoginControlsType == LoginControlsType.Homepage)
            HomepageUrl = args.Message;
    }
    #endregion
}
#region
using System.Buffers;
using System.Net;
using Chaos.Cryptography;
using Chaos.DarkAges.Definitions;
using Chaos.Geometry;
using Chaos.Geometry.Abstractions.Definitions;
using Chaos.Networking.Abstractions.Definitions;
using Chaos.Networking.Entities.Client;
using Chaos.Networking.Entities.Server;
using Chaos.Packets;
#endregion

namespace Chaos.Client.Networking;

/// <summary>
///     Manages the connection lifecycle: Lobby handshake, login redirect, and world entry. Drives state transitions and
///     orchestrates the GameClient through each phase.
/// </summary>
public sealed class ConnectionManager : IDisposable
{
    private readonly Dictionary<EquipmentSlot, EquipmentInfo> EquipmentState = new();
    private readonly Dictionary<byte, InventorySlotInfo> InventoryState = new();
    private readonly Action<ServerPacket>?[] PacketHandlers = new Action<ServerPacket>?[byte.MaxValue + 1];
    private readonly Dictionary<byte, (ushort Sprite, string Name)> SkillState = new();
    private readonly Dictionary<byte, SpellInfo> SpellState = new();
    private WorldEntryState EntryState;
    private RedirectInfo? PendingRedirect;

    /// <summary>
    ///     The aisling's ID, assigned by the server after world entry.
    /// </summary>
    public uint AislingId { get; private set; }

    /// <summary>
    ///     The character name, set during login.
    /// </summary>
    public string AislingName { get; private set; } = string.Empty;

    /// <summary>
    ///     The most recently received attributes.
    /// </summary>
    public AttributesArgs? Attributes { get; private set; }

    /// <summary>
    ///     The current map info received from the server.
    /// </summary>
    public MapInfoArgs? MapInfo { get; private set; }

    /// <summary>
    ///     The player's current X position.
    /// </summary>
    public int PlayerX { get; private set; }

    /// <summary>
    ///     The player's current Y position.
    /// </summary>
    public int PlayerY { get; private set; }

    public string ServerName { get; set; } = string.Empty;

    /// <summary>
    ///     The current connection state.
    /// </summary>
    public ConnectionState State
    {
        get;

        private set
        {
            if (field == value)
                return;

            var old = field;
            field = value;
            StateChanged?.Invoke(old, value);
        }
    } = ConnectionState.Disconnected;

    /// <summary>
    ///     The underlying game client.
    /// </summary>
    public GameClient Client { get; }

    /// <summary>
    ///     Buffered equipment state (slot → info), populated during world entry.
    /// </summary>
    public IReadOnlyDictionary<EquipmentSlot, EquipmentInfo> Equipment => EquipmentState;

    /// <summary>
    ///     Buffered inventory sprites (slot → sprite), populated during world entry.
    /// </summary>
    public IReadOnlyDictionary<byte, InventorySlotInfo> InventorySlots => InventoryState;

    /// <summary>
    ///     Buffered skill data (slot → sprite + name), populated during world entry.
    /// </summary>
    public IReadOnlyDictionary<byte, (ushort Sprite, string Name)> SkillSlots => SkillState;

    /// <summary>
    ///     Buffered spell data (slot → sprite + name), populated during world entry.
    /// </summary>
    public IReadOnlyDictionary<byte, SpellInfo> SpellSlots => SpellState;

    public ConnectionManager()
    {
        Client = new GameClient();
        Client.OnDisconnected += HandleDisconnected;
        IndexHandlers();
    }

    /// <inheritdoc />
    public void Dispose() => Client.Dispose();

    /// <summary>
    ///     Sends a password change request to the login server.
    /// </summary>
    public void ChangePassword(string name, string currentPassword, string newPassword)
    {
        if (State != ConnectionState.Login)
            return;

        Client.Send(
            new PasswordChangeArgs
            {
                Name = name,
                CurrentPassword = currentPassword,
                NewPassword = newPassword
            });
    }

    /// <summary>
    ///     Sends a click on an entity (NPC, creature, aisling, ground item).
    /// </summary>
    public void ClickEntity(uint targetId)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new ClickArgs
            {
                ClickType = ClickType.TargetId,
                TargetId = targetId
            });
    }

    /// <summary>
    ///     Sends a click on a map tile.
    /// </summary>
    public void ClickTile(int x, int y)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new ClickArgs
            {
                ClickType = ClickType.TargetPoint,
                TargetPoint = new Point(x, y)
            });
    }

    /// <summary>
    ///     Sends a world map node click.
    /// </summary>
    public void ClickWorldMapNode(
        ushort mapId,
        int x,
        int y,
        ushort checkSum)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new WorldMapClickArgs
            {
                MapId = mapId,
                Point = new Point(x, y),
                CheckSum = checkSum
            });
    }

    /// <summary>
    ///     Connects to the lobby server, performs the Version/ConnectionInfo handshake.
    /// </summary>
    public async Task ConnectToLobbyAsync(
        string host,
        int port,
        ushort clientVersion,
        CancellationToken ct = default)
    {
        State = ConnectionState.Connecting;
        Client.Crypto = new Crypto();
        Client.SetSequence(0);

        // Set all AcceptConnection handler state BEFORE ConnectAsync starts the receive loop.
        // The receive loop starts inside ConnectAsync and AcceptConnection can arrive immediately,
        // so the handler must have everything it needs before the socket connects.
        LobbyClientVersion = clientVersion;
        PendingLobbyVersion = true;
        PendingTargetState = ConnectionState.Lobby;

        try
        {
            await Client.ConnectAsync(host, port, ct);
        } catch (Exception ex)
        {
            PendingLobbyVersion = false;
            State = ConnectionState.Disconnected;
            OnError?.Invoke($"Failed to connect to lobby: {ex.Message}");

            return;
        }
    }

    /// <summary>
    ///     Sends the finalized character creation request (appearance choices) to the login server.
    /// </summary>
    public void CreateCharFinalize(byte hairStyle, Gender gender, DisplayColor hairColor)
    {
        if (State != ConnectionState.Login)
            return;

        Client.Send(
            new CreateCharFinalizeArgs
            {
                HairStyle = hairStyle,
                Gender = gender,
                HairColor = hairColor
            });
    }

    /// <summary>
    ///     Sends the initial character creation request (name and password) to the login server.
    /// </summary>
    public void CreateCharInitial(string name, string password)
    {
        if (State != ConnectionState.Login)
            return;

        Client.Send(
            new CreateCharInitialArgs
            {
                Name = name,
                Password = password
            });
    }

    /// <summary>
    ///     Sends a gold drop request onto the ground.
    /// </summary>
    public void DropGold(int amount, int x, int y)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new GoldDropArgs
            {
                Amount = amount,
                DestinationPoint = new Point(x, y)
            });
    }

    /// <summary>
    ///     Sends a gold give request to a creature/NPC.
    /// </summary>
    public void DropGoldOnCreature(int amount, uint targetId)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new GoldDroppedOnCreatureArgs
            {
                Amount = amount,
                TargetId = targetId
            });
    }

    /// <summary>
    ///     Sends an item drop request onto the ground.
    /// </summary>
    public void DropItem(
        byte sourceSlot,
        int x,
        int y,
        int count = 1)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new ItemDropArgs
            {
                SourceSlot = sourceSlot,
                DestinationPoint = new Point(x, y),
                Count = count
            });
    }

    /// <summary>
    ///     Sends an item give request to a creature/NPC.
    /// </summary>
    public void DropItemOnCreature(byte sourceSlot, uint targetId, byte count = 1)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new ItemDroppedOnCreatureArgs
            {
                SourceSlot = sourceSlot,
                TargetId = targetId,
                Count = count
            });
    }

    private async void FollowPendingRedirect()
    {
        if (PendingRedirect is not { } redirect)
            return;

        PendingRedirect = null;

        State = ConnectionState.Connecting;

        // Lobby→Login uses empty keySaltSeed (falls back to "default"), Login→World uses character name
        var keySaltSeed = redirect.TargetState == ConnectionState.Login ? string.Empty : redirect.Name;
        Client.Crypto = new Crypto(redirect.Seed, redirect.Key, keySaltSeed);
        Client.SetSequence(0);
        EntryState = WorldEntryState.None;
        InventoryState.Clear();
        SkillState.Clear();
        SpellState.Clear();
        EquipmentState.Clear();
        PendingTargetState = redirect.TargetState;

        try
        {
            await Client.ConnectAsync(redirect.EndPoint.Address.ToString(), redirect.EndPoint.Port);
        } catch (Exception ex)
        {
            State = ConnectionState.Disconnected;
            OnError?.Invoke($"Failed to connect after redirect: {ex.Message}");

            return;
        }

        // Send ClientRedirected immediately after connecting.
        // Not all servers send AcceptConnection before expecting this packet.
        State = PendingTargetState;

        Client.Send(
            new ClientRedirectedArgs
            {
                Id = redirect.Id,
                Seed = redirect.Seed,
                Key = redirect.Key,
                Name = redirect.Name
            });

        if (State == ConnectionState.Login)
            RequestHomepage();
    }

    /// <summary>
    ///     Returns the first empty inventory slot (1-based), or 0 if inventory is full.
    /// </summary>
    public byte GetFirstEmptyInventorySlot()
    {
        for (byte i = 1; i <= 59; i++)
            if (!InventoryState.ContainsKey(i))
                return i;

        return 0;
    }

    /// <summary>
    ///     Sends login credentials to the login server.
    /// </summary>
    public void Login(
        string name,
        string password,
        uint clientId1 = 1,
        ushort clientId2 = 1)
    {
        if (State != ConnectionState.Login)
            return;

        AislingName = name;

        Client.Send(
            new LoginArgs
            {
                Name = name,
                Password = password,
                ClientId1 = clientId1,
                ClientId2 = clientId2
            });
    }

    // --- Inventory ---

    /// <summary>
    ///     Fired when an item is added to the inventory pane.
    /// </summary>
    public event Action<AddItemToPaneArgs>? OnAddItemToPane;

    // --- Skills / Spells ---

    /// <summary>
    ///     Fired when a skill is added to the skill pane.
    /// </summary>
    public event Action<AddSkillToPaneArgs>? OnAddSkillToPane;

    /// <summary>
    ///     Fired when a spell is added to the spell pane.
    /// </summary>
    public event Action<AddSpellToPaneArgs>? OnAddSpellToPane;

    /// <summary>
    ///     Fired when a spell/effect animation should play.
    /// </summary>
    public event Action<AnimationArgs>? OnAnimation;

    /// <summary>
    ///     Fired when player attributes are updated.
    /// </summary>
    public event Action<AttributesArgs>? OnAttributes;

    /// <summary>
    ///     Fired when a body animation is triggered on an entity.
    /// </summary>
    public event Action<BodyAnimationArgs>? OnBodyAnimation;

    /// <summary>
    ///     Fired when a casting animation should be cancelled.
    /// </summary>
    public event Action? OnCancelCasting;

    /// <summary>
    ///     Fired when the server confirms the player's own walk. Args: (direction, oldX, oldY).
    /// </summary>
    public event Action<Direction, int, int>? OnClientWalkResponse;

    /// <summary>
    ///     Fired when a skill or spell cooldown starts.
    /// </summary>
    public event Action<CooldownArgs>? OnCooldown;

    /// <summary>
    ///     Fired when another entity changes facing direction. Args: (sourceId, direction).
    /// </summary>
    public event Action<uint, Direction>? OnCreatureTurn;

    /// <summary>
    ///     Fired when another entity walks. Args: (sourceId, oldX, oldY, direction).
    /// </summary>
    public event Action<uint, int, int, Direction>? OnCreatureWalk;

    /// <summary>
    ///     Fired when an aisling display is received.
    /// </summary>
    public event Action<DisplayAislingArgs>? OnDisplayAisling;

    /// <summary>
    ///     Fired when a bulletin board should be displayed.
    /// </summary>
    public event Action<DisplayBoardArgs>? OnDisplayBoard;

    /// <summary>
    ///     Fired when an NPC dialog should be displayed.
    /// </summary>
    public event Action<DisplayDialogArgs>? OnDisplayDialog;

    /// <summary>
    ///     Fired when an editable notepad should be displayed.
    /// </summary>
    public event Action<DisplayEditableNotepadArgs>? OnDisplayEditableNotepad;

    /// <summary>
    ///     Fired when an exchange/trade window should be displayed.
    /// </summary>
    public event Action<DisplayExchangeArgs>? OnDisplayExchange;

    /// <summary>
    ///     Fired when a group invite is received.
    /// </summary>
    public event Action<DisplayGroupInviteArgs>? OnDisplayGroupInvite;

    // --- NPC Interaction ---

    /// <summary>
    ///     Fired when an NPC menu should be displayed.
    /// </summary>
    public event Action<DisplayMenuArgs>? OnDisplayMenu;

    /// <summary>
    ///     Fired when a public chat message is displayed.
    /// </summary>
    public event Action<DisplayPublicMessageArgs>? OnDisplayPublicMessage;

    /// <summary>
    ///     Fired when a read-only notepad should be displayed.
    /// </summary>
    public event Action<DisplayReadonlyNotepadArgs>? OnDisplayReadonlyNotepad;

    /// <summary>
    ///     Fired when an equipment slot is cleared.
    /// </summary>
    public event Action<DisplayUnequipArgs>? OnDisplayUnequip;

    /// <summary>
    ///     Fired when a visible entity (non-aisling) is received.
    /// </summary>
    public event Action<DisplayVisibleEntitiesArgs>? OnDisplayVisibleEntities;

    /// <summary>
    ///     Fired when door states are updated.
    /// </summary>
    public event Action<DoorArgs>? OnDoor;

    /// <summary>
    ///     Fired when a status effect is applied or removed.
    /// </summary>
    public event Action<EffectArgs>? OnEffect;

    // --- Equipment ---

    /// <summary>
    ///     Fired when an equipment slot is updated.
    /// </summary>
    public event Action<EquipmentArgs>? OnEquipment;

    /// <summary>
    ///     Fired when an error occurs during connection or handshake.
    /// </summary>
    public event Action<string>? OnError;

    /// <summary>
    ///     Fired when a logout response is received.
    /// </summary>
    public event Action<ExitResponseArgs>? OnExitResponse;

    /// <summary>
    ///     Fired when the server forces the client to send a packet.
    /// </summary>
    public event Action<ForceClientPacketArgs>? OnForceClientPacket;

    // --- Visual / Audio ---

    /// <summary>
    ///     Fired when an entity's health bar should be displayed.
    /// </summary>
    public event Action<HealthBarArgs>? OnHealthBar;

    // --- World State ---

    /// <summary>
    ///     Fired when the ambient light level changes (time of day).
    /// </summary>
    public event Action<LightLevelArgs>? OnLightLevel;

    /// <summary>
    ///     Fired when the player's location changes.
    /// </summary>
    public event Action<int, int>? OnLocationChanged;

    /// <summary>
    ///     Fired when a login control is received (e.g. homepage URL).
    /// </summary>
    public event Action<LoginControlArgs>? OnLoginControl;

    /// <summary>
    ///     Fired when a login message is received (success, failure, or informational).
    /// </summary>
    public event Action<LoginMessageArgs>? OnLoginMessage;

    /// <summary>
    ///     Fired when a login notice (EULA) is received.
    /// </summary>
    public event Action<LoginNoticeArgs>? OnLoginNotice;

    /// <summary>
    ///     Fired when a map change is about to begin.
    /// </summary>
    public event Action? OnMapChangePending;

    /// <summary>
    ///     Fired for each row of map data received.
    /// </summary>
    public event Action<MapDataArgs>? OnMapData;

    /// <summary>
    ///     Fired when map info is received, before map data arrives.
    /// </summary>
    public event Action<MapInfoArgs>? OnMapInfo;

    /// <summary>
    ///     Fired when the server signals that map loading is complete.
    /// </summary>
    public event Action? OnMapLoadComplete;

    /// <summary>
    ///     Fired when metadata is received.
    /// </summary>
    public event Action<MetaDataArgs>? OnMetaData;

    /// <summary>
    ///     Fired when another player's profile is received.
    /// </summary>
    public event Action<OtherProfileArgs>? OnOtherProfile;

    /// <summary>
    ///     Fired when a redirect is received and the client needs to connect to a new server.
    /// </summary>
    public event Action<RedirectInfo>? OnRedirectReceived;

    /// <summary>
    ///     Fired when a viewport refresh response is received.
    /// </summary>
    public event Action? OnRefreshResponse;

    /// <summary>
    ///     Fired when an entity is removed from the viewport.
    /// </summary>
    public event Action<uint>? OnRemoveEntity;

    /// <summary>
    ///     Fired when an item is removed from the inventory pane.
    /// </summary>
    public event Action<RemoveItemFromPaneArgs>? OnRemoveItemFromPane;

    /// <summary>
    ///     Fired when a skill is removed from the skill pane.
    /// </summary>
    public event Action<RemoveSkillFromPaneArgs>? OnRemoveSkillFromPane;

    /// <summary>
    ///     Fired when a spell is removed from the spell pane.
    /// </summary>
    public event Action<RemoveSpellFromPaneArgs>? OnRemoveSpellFromPane;

    /// <summary>
    ///     Fired when the player's own profile is received.
    /// </summary>
    public event Action<SelfProfileArgs>? OnSelfProfile;

    // --- Chat / Messages ---

    /// <summary>
    ///     Fired when a system message is received (yellow text, overhead, etc.).
    /// </summary>
    public event Action<ServerMessageArgs>? OnServerMessage;

    /// <summary>
    ///     Fired when the lobby handshake completes and the server table is received.
    /// </summary>
    public event Action<ServerTableData>? OnServerTableReceived;

    /// <summary>
    ///     Fired when a sound or music track should play.
    /// </summary>
    public event Action<SoundArgs>? OnSound;

    /// <summary>
    ///     Fired when world entry is complete (all essential data received).
    /// </summary>
    public event Action<uint>? OnUserId;

    public event Action? OnWorldEntryComplete;

    /// <summary>
    ///     Fired when a world list (online players) is received.
    /// </summary>
    public event Action<WorldListArgs>? OnWorldList;

    /// <summary>
    ///     Fired when the world map should be displayed.
    /// </summary>
    public event Action<WorldMapArgs>? OnWorldMap;

    /// <summary>
    ///     Sends a pickup request from a tile.
    /// </summary>
    public void PickupItem(int x, int y, byte destinationSlot)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new PickupArgs
            {
                SourcePoint = new Point(x, y),
                DestinationSlot = destinationSlot
            });
    }

    /// <summary>
    ///     Processes queued inbound packets, driving state transitions. Call this from the game loop's Update method.
    /// </summary>
    public void ProcessPackets(List<ServerPacket> buffer)
    {
        Client.DrainPackets(buffer);

        foreach (var pkt in buffer)
        {
            try
            {
                HandlePacket(pkt);
            } catch
            {
                // Malformed packet — skip
            }
        }

        // Follow pending redirect once the old connection is fully torn down
        if (PendingRedirect is not null && !Client.Connected)
            FollowPendingRedirect();
    }

    /// <summary>
    ///     Sends a raise stat request.
    /// </summary>
    public void RaiseStat(Stat stat)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new RaiseStatArgs
            {
                Stat = stat
            });
    }

    /// <summary>
    ///     Sends an exit/logout request.
    /// </summary>
    public void RequestExit(bool isRequest = true)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new ExitRequestArgs
            {
                IsRequest = isRequest
            });
    }

    /// <summary>
    ///     Requests the homepage URL from the login server.
    /// </summary>
    public void RequestHomepage()
    {
        if (State != ConnectionState.Login)
            return;

        Client.Send(new HomepageRequestArgs());
    }

    /// <summary>
    ///     Sends a map data request to the server, requesting tile data for the current map.
    /// </summary>
    public void RequestMapData()
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(new MapDataRequestArgs());
    }

    /// <summary>
    ///     Requests the full login notice (EULA) from the login server.
    /// </summary>
    public void RequestNotice()
    {
        if (State != ConnectionState.Login)
            return;

        Client.Send(new NoticeRequestArgs());
    }

    /// <summary>
    ///     Sends a refresh request (F5).
    /// </summary>
    public void RequestRefresh()
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(new RefreshRequestArgs());
    }

    /// <summary>
    ///     Sends a self profile request.
    /// </summary>
    public void RequestSelfProfile()
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(new SelfProfileRequestArgs());
    }

    /// <summary>
    ///     Requests the server table from the lobby.
    /// </summary>
    public void RequestServerTable()
    {
        if (State != ConnectionState.Lobby)
            return;

        Client.Send(
            new ServerTableRequestArgs
            {
                ServerTableRequestType = ServerTableRequestType.RequestTable
            });
    }

    /// <summary>
    ///     Sends a world list request.
    /// </summary>
    public void RequestWorldList()
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(new WorldListRequestArgs());
    }

    /// <summary>
    ///     Selects a server from the server table by ID, triggering a redirect.
    /// </summary>
    public void SelectServer(byte serverId)
    {
        if (State != ConnectionState.Lobby)
            return;

        Client.Send(
            new ServerTableRequestArgs
            {
                ServerTableRequestType = ServerTableRequestType.ServerId,
                ServerId = serverId
            });
    }

    /// <summary>
    ///     Adds a player to the ignore list.
    /// </summary>
    public void SendAddIgnore(string targetName)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new IgnoreArgs
            {
                IgnoreType = IgnoreType.AddUser,
                TargetName = targetName
            });
    }

    /// <summary>
    ///     Sends a begin chant packet with the number of cast lines.
    /// </summary>
    public void SendBeginChant(byte castLineCount)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new BeginChantArgs
            {
                CastLineCount = castLineCount
            });
    }

    /// <summary>
    ///     Sends a board/mail interaction (view board, read post, send mail, delete, etc.).
    /// </summary>
    public void SendBoardInteraction(
        BoardRequestType requestType,
        ushort boardId = 0,
        short postId = 0,
        short startPostId = 0,
        BoardControls? controls = null,
        string? to = null,
        string? subject = null,
        string? message = null)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new BoardInteractionArgs
            {
                BoardRequestType = requestType,
                BoardId = boardId,
                PostId = postId,
                StartPostId = startPostId,
                Controls = controls,
                To = to,
                Subject = subject,
                Message = message
            });
    }

    /// <summary>
    ///     Sends a chant line message.
    /// </summary>
    public void SendChant(string message)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new ChantArgs
            {
                ChantMessage = message
            });
    }

    /// <summary>
    ///     Sends a dialog interaction response (Next, Close, option select, text input).
    /// </summary>
    public void SendDialogResponse(
        EntityType entityType,
        uint entityId,
        ushort pursuitId,
        ushort dialogId,
        DialogArgsType argsType = DialogArgsType.None,
        byte? option = null,
        List<string>? args = null)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new DialogInteractionArgs
            {
                EntityType = entityType,
                EntityId = entityId,
                PursuitId = pursuitId,
                DialogId = dialogId,
                DialogArgsType = argsType,
                Option = option,
                Args = args
            });
    }

    /// <summary>
    ///     Sends the player's portrait and profile text to the server.
    /// </summary>
    public void SendEditableProfile(byte[] portraitData, string profileMessage)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new EditableProfileArgs
            {
                PortraitData = portraitData,
                ProfileMessage = profileMessage
            });
    }

    /// <summary>
    ///     Sends an emote request (body animation 9-44).
    /// </summary>
    public void SendEmote(BodyAnimation bodyAnimation)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new EmoteArgs
            {
                BodyAnimation = bodyAnimation
            });
    }

    /// <summary>
    ///     Sends an exchange interaction.
    /// </summary>
    public void SendExchangeInteraction(
        ExchangeRequestType type,
        uint otherId = 0,
        byte? sourceSlot = null,
        byte? itemCount = null,
        int? goldAmount = null)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new ExchangeInteractionArgs
            {
                ExchangeRequestType = type,
                OtherPlayerId = otherId,
                SourceSlot = sourceSlot,
                ItemCount = itemCount,
                GoldAmount = goldAmount
            });
    }

    /// <summary>
    ///     Sends a group invite or group management action.
    /// </summary>
    public void SendGroupInvite(ClientGroupSwitch action, string? targetName = null)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new GroupInviteArgs
            {
                ClientGroupSwitch = action,
                TargetName = targetName
            });
    }

    /// <summary>
    ///     Requests the current ignore list from the server.
    /// </summary>
    public void SendIgnoreRequest()
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new IgnoreArgs
            {
                IgnoreType = IgnoreType.Request
            });
    }

    /// <summary>
    ///     Sends a menu interaction response (pursuit selection).
    /// </summary>
    public void SendMenuResponse(
        EntityType entityType,
        uint entityId,
        ushort pursuitId,
        byte? slot = null,
        string[]? args = null)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new MenuInteractionArgs
            {
                EntityType = entityType,
                EntityId = entityId,
                PursuitId = pursuitId,
                Slot = slot,
                Args = args
            });
    }

    /// <summary>
    ///     Sends a metadata request to the server (checksums or specific file data).
    /// </summary>
    public void SendMetaDataRequest(MetaDataRequestType requestType, string? name = null)
        => Client.Send(
            new MetaDataRequestArgs
            {
                MetaDataRequestType = requestType,
                Name = name
            });

    /// <summary>
    ///     Sends a spacebar (assail) request.
    /// </summary>
    public void SendOptionToggle(UserOption option)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new OptionToggleArgs
            {
                UserOption = option
            });
    }

    /// <summary>
    ///     Sends a public chat message.
    /// </summary>
    public void SendPublicMessage(string message)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new PublicMessageArgs
            {
                Message = message,
                PublicMessageType = PublicMessageType.Normal
            });
    }

    /// <summary>
    ///     Removes a player from the ignore list.
    /// </summary>
    public void SendRemoveIgnore(string targetName)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new IgnoreArgs
            {
                IgnoreType = IgnoreType.RemoveUser,
                TargetName = targetName
            });
    }

    /// <summary>
    ///     Sends notepad text for an editable notepad slot.
    /// </summary>
    public void SendSetNotepad(byte slot, string message)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new SetNotepadArgs
            {
                Slot = slot,
                Message = message
            });
    }

    /// <summary>
    ///     Sends a shout message (! prefix).
    /// </summary>
    public void SendShout(string message)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new PublicMessageArgs
            {
                Message = message,
                PublicMessageType = PublicMessageType.Shout
            });
    }

    /// <summary>
    ///     Sends a social status change to the server.
    /// </summary>
    public void SendSocialStatus(SocialStatus status)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new SocialStatusArgs
            {
                SocialStatus = status
            });
    }

    /// <summary>
    ///     Sends a whisper to a specific player.
    /// </summary>
    public void SendWhisper(string targetName, string message)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new WhisperArgs
            {
                TargetName = targetName,
                Message = message
            });
    }

    public void Spacebar()
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(new SpacebarArgs());
    }

    /// <summary>
    ///     Fired when the connection state changes. Args: (oldState, newState).
    /// </summary>
    public event Action<ConnectionState, ConnectionState>? StateChanged;

    /// <summary>
    ///     Sends a swap slot request between two panel positions.
    /// </summary>
    public void SwapSlot(PanelType panelType, byte slot1, byte slot2)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new SwapSlotArgs
            {
                PanelType = panelType,
                Slot1 = slot1,
                Slot2 = slot2
            });
    }

    public void ToggleGroup()
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(new ToggleGroupArgs());
    }

    /// <summary>
    ///     Sends a turn request to face the specified direction.
    /// </summary>
    public void Turn(Direction direction)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new TurnArgs
            {
                Direction = direction
            });
    }

    /// <summary>
    ///     Sends an unequip request for the specified equipment slot.
    /// </summary>
    public void Unequip(EquipmentSlot slot)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new UnequipArgs
            {
                EquipmentSlot = slot
            });
    }

    /// <summary>
    ///     Sends an item use request (equip, consume).
    /// </summary>
    public void UseItem(byte slot)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new ItemUseArgs
            {
                SourceSlot = slot
            });
    }

    /// <summary>
    ///     Sends a skill use request.
    /// </summary>
    public void UseSkill(byte slot)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new SkillUseArgs
            {
                SourceSlot = slot
            });
    }

    /// <summary>
    ///     Sends a spell use request.
    /// </summary>
    public void UseSpell(byte slot, byte[]? argsData = null)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new SpellUseArgs
            {
                SourceSlot = slot,
                ArgsData = argsData ?? []
            });
    }

    /// <summary>
    ///     Sends a targeted spell use with entity ID and position packed as ArgsData.
    /// </summary>
    public void UseSpellOnTarget(
        byte slot,
        uint targetId,
        int targetX,
        int targetY)
    {
        var argsData = new byte[8];
        argsData[0] = (byte)(targetId >> 24);
        argsData[1] = (byte)(targetId >> 16);
        argsData[2] = (byte)(targetId >> 8);
        argsData[3] = (byte)targetId;
        argsData[4] = (byte)(targetX >> 8);
        argsData[5] = (byte)targetX;
        argsData[6] = (byte)(targetY >> 8);
        argsData[7] = (byte)targetY;

        UseSpell(slot, argsData);
    }

    /// <summary>
    ///     Sends a walk request in the specified direction.
    /// </summary>
    public void Walk(Direction direction)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new ClientWalkArgs
            {
                Direction = direction,
                StepCount = WalkStepCount++
            });
    }

    #region Private
    private ushort LobbyClientVersion;
    private bool PendingLobbyVersion;
    private ConnectionState PendingTargetState;
    private byte WalkStepCount;

    private void IndexHandlers()
    {
        // Lobby
        PacketHandlers[(byte)ServerOpCode.AcceptConnection] = HandleAcceptConnection;
        PacketHandlers[(byte)ServerOpCode.ConnectionInfo] = HandleConnectionInfo;
        PacketHandlers[(byte)ServerOpCode.ServerTableResponse] = HandleServerTableResponse;
        PacketHandlers[(byte)ServerOpCode.Redirect] = HandleRedirect;

        // Login
        PacketHandlers[(byte)ServerOpCode.LoginMessage] = HandleLoginMessage;
        PacketHandlers[(byte)ServerOpCode.LoginNotice] = HandleLoginNotice;
        PacketHandlers[(byte)ServerOpCode.LoginControl] = HandleLoginControl;

        // World entry
        PacketHandlers[(byte)ServerOpCode.UserId] = HandleUserId;
        PacketHandlers[(byte)ServerOpCode.MapInfo] = HandleMapInfo;
        PacketHandlers[(byte)ServerOpCode.MapData] = HandleMapData;
        PacketHandlers[(byte)ServerOpCode.MapLoadComplete] = HandleMapLoadComplete;
        PacketHandlers[(byte)ServerOpCode.MapChangeComplete] = HandleMapChangeComplete;
        PacketHandlers[(byte)ServerOpCode.Location] = HandleLocation;
        PacketHandlers[(byte)ServerOpCode.Attributes] = HandleAttributes;
        PacketHandlers[(byte)ServerOpCode.DisplayVisibleEntities] = HandleDisplayVisibleEntities;
        PacketHandlers[(byte)ServerOpCode.DisplayAisling] = HandleDisplayAisling;

        // World entities
        PacketHandlers[(byte)ServerOpCode.RemoveEntity] = HandleRemoveEntity;
        PacketHandlers[(byte)ServerOpCode.CreatureWalk] = HandleCreatureWalk;
        PacketHandlers[(byte)ServerOpCode.ClientWalkResponse] = HandleClientWalkResponse;
        PacketHandlers[(byte)ServerOpCode.CreatureTurn] = HandleCreatureTurn;

        // Chat / messages
        PacketHandlers[(byte)ServerOpCode.ServerMessage] = HandleServerMessage;
        PacketHandlers[(byte)ServerOpCode.DisplayPublicMessage] = HandleDisplayPublicMessage;

        // Inventory
        PacketHandlers[(byte)ServerOpCode.AddItemToPane] = HandleAddItemToPane;
        PacketHandlers[(byte)ServerOpCode.RemoveItemFromPane] = HandleRemoveItemFromPane;

        // Skills / spells
        PacketHandlers[(byte)ServerOpCode.AddSkillToPane] = HandleAddSkillToPane;
        PacketHandlers[(byte)ServerOpCode.RemoveSkillFromPane] = HandleRemoveSkillFromPane;
        PacketHandlers[(byte)ServerOpCode.AddSpellToPane] = HandleAddSpellToPane;
        PacketHandlers[(byte)ServerOpCode.RemoveSpellFromPane] = HandleRemoveSpellFromPane;

        // Equipment
        PacketHandlers[(byte)ServerOpCode.Equipment] = HandleEquipment;
        PacketHandlers[(byte)ServerOpCode.DisplayUnequip] = HandleDisplayUnequip;

        // Visual / audio
        PacketHandlers[(byte)ServerOpCode.HealthBar] = HandleHealthBar;
        PacketHandlers[(byte)ServerOpCode.Sound] = HandleSound;
        PacketHandlers[(byte)ServerOpCode.BodyAnimation] = HandleBodyAnimation;
        PacketHandlers[(byte)ServerOpCode.Animation] = HandleAnimation;
        PacketHandlers[(byte)ServerOpCode.Cooldown] = HandleCooldown;
        PacketHandlers[(byte)ServerOpCode.Effect] = HandleEffect;

        // World state
        PacketHandlers[(byte)ServerOpCode.LightLevel] = HandleLightLevel;
        PacketHandlers[(byte)ServerOpCode.Door] = HandleDoor;
        PacketHandlers[(byte)ServerOpCode.RefreshResponse] = HandleRefreshResponse;
        PacketHandlers[(byte)ServerOpCode.MapChangePending] = HandleMapChangePending;

        // NPC interaction
        PacketHandlers[(byte)ServerOpCode.DisplayMenu] = HandleDisplayMenu;
        PacketHandlers[(byte)ServerOpCode.DisplayDialog] = HandleDisplayDialog;
        PacketHandlers[(byte)ServerOpCode.DisplayBoard] = HandleDisplayBoard;
        PacketHandlers[(byte)ServerOpCode.DisplayExchange] = HandleDisplayExchange;
        PacketHandlers[(byte)ServerOpCode.DisplayGroupInvite] = HandleDisplayGroupInvite;

        // Profiles / lists
        PacketHandlers[(byte)ServerOpCode.EditableProfileRequest] = HandleEditableProfileRequest;
        PacketHandlers[(byte)ServerOpCode.SelfProfile] = HandleSelfProfile;
        PacketHandlers[(byte)ServerOpCode.OtherProfile] = HandleOtherProfile;
        PacketHandlers[(byte)ServerOpCode.WorldList] = HandleWorldList;
        PacketHandlers[(byte)ServerOpCode.WorldMap] = HandleWorldMap;

        // Notepads
        PacketHandlers[(byte)ServerOpCode.DisplayEditableNotepad] = HandleDisplayEditableNotepad;
        PacketHandlers[(byte)ServerOpCode.DisplayReadonlyNotepad] = HandleDisplayReadonlyNotepad;

        // Misc
        PacketHandlers[(byte)ServerOpCode.ExitResponse] = HandleExitResponse;
        PacketHandlers[(byte)ServerOpCode.ForceClientPacket] = HandleForceClientPacket;
        PacketHandlers[(byte)ServerOpCode.CancelCasting] = HandleCancelCasting;
        PacketHandlers[(byte)ServerOpCode.MetaData] = HandleMetaData;
    }

    private void HandlePacket(ServerPacket pkt)
    {
        var handler = PacketHandlers[pkt.OpCode];
        handler?.Invoke(pkt);
    }

    private void HandleAcceptConnection(ServerPacket _)
    {
        if (PendingLobbyVersion)
        {
            // Lobby handshake — send Version packet
            PendingLobbyVersion = false;
            State = PendingTargetState;

            Client.Send(
                new VersionArgs
                {
                    Version = LobbyClientVersion
                });
        }

        // Redirected connections send ClientRedirected in FollowRedirectAsync
        // immediately after connecting, without waiting for AcceptConnection.
    }

    private void HandleConnectionInfo(ServerPacket pkt)
    {
        var args = Client.Deserialize<ConnectionInfoArgs>(in pkt);

        // Lobby always uses empty keySaltSeed (Crypto falls back to "default")
        // Must explicitly pass keySaltSeed to avoid binding to the 2-arg constructor
        // Crypto(byte seed, string keySaltSeed) which generates a random key
        Client.Crypto = new Crypto(args.Seed, args.Key, null);

        // Crypto is now configured — safe to request the server table
        if (State == ConnectionState.Lobby)
            RequestServerTable();
    }

    private void HandleServerTableResponse(ServerPacket pkt)
    {
        var args = Client.Deserialize<ServerTableResponseArgs>(in pkt);
        var serverTableData = ServerTableData.Parse(args.ServerTable);
        OnServerTableReceived?.Invoke(serverTableData);
    }

    private void HandleRedirect(ServerPacket pkt)
    {
        var args = Client.Deserialize<RedirectArgs>(in pkt);

        // Determine target state based on current state
        var targetState = State switch
        {
            ConnectionState.Lobby => ConnectionState.Login,
            ConnectionState.Login => ConnectionState.World,
            _                     => ConnectionState.Login
        };

        PendingRedirect = new RedirectInfo(
            args.EndPoint,
            args.Seed,
            args.Key,
            args.Name,
            args.Id,
            targetState);

        // Begin teardown immediately — the redirect will be followed from the game loop
        // once the old connection is fully dead.
        Client.Disconnect();

        OnRedirectReceived?.Invoke(PendingRedirect.Value);
    }

    private void HandleLoginMessage(ServerPacket pkt)
    {
        var args = Client.Deserialize<LoginMessageArgs>(in pkt);
        OnLoginMessage?.Invoke(args);
    }

    private void HandleLoginNotice(ServerPacket pkt)
    {
        var args = Client.Deserialize<LoginNoticeArgs>(in pkt);
        OnLoginNotice?.Invoke(args);
    }

    private void HandleLoginControl(ServerPacket pkt)
    {
        var args = Client.Deserialize<LoginControlArgs>(in pkt);
        OnLoginControl?.Invoke(args);
    }

    private void HandleUserId(ServerPacket pkt)
    {
        var args = Client.Deserialize<UserIdArgs>(in pkt);
        AislingId = args.Id;
        OnUserId?.Invoke(args.Id);
        EntryState |= WorldEntryState.UserId;
        CheckWorldEntryComplete();
    }

    private void HandleMapInfo(ServerPacket pkt)
    {
        var args = Client.Deserialize<MapInfoArgs>(in pkt);
        MapInfo = args;
        EntryState |= WorldEntryState.MapInfo;
        OnMapInfo?.Invoke(args);
    }

    private void HandleMapData(ServerPacket pkt)
    {
        var args = Client.Deserialize<MapDataArgs>(in pkt);
        OnMapData?.Invoke(args);
    }

    private void HandleMapLoadComplete(ServerPacket _)
    {
        EntryState |= WorldEntryState.MapLoaded;
        OnMapLoadComplete?.Invoke();
        CheckWorldEntryComplete();
    }

    private void HandleMapChangeComplete(ServerPacket _)
    {
        // Map tile data is fully loaded — ideal time for a full collection while the loading screen is still up
        GC.Collect(
            2,
            GCCollectionMode.Aggressive,
            true,
            true);

        //GC.WaitForPendingFinalizers();

        EntryState |= WorldEntryState.MapChangeComplete;
        CheckWorldEntryComplete();
    }

    private void HandleLocation(ServerPacket pkt)
    {
        var args = Client.Deserialize<LocationArgs>(in pkt);
        PlayerX = args.X;
        PlayerY = args.Y;
        EntryState |= WorldEntryState.Location;
        OnLocationChanged?.Invoke(args.X, args.Y);
        CheckWorldEntryComplete();
    }

    private void HandleAttributes(ServerPacket pkt)
    {
        var args = Client.Deserialize<AttributesArgs>(in pkt);

        // Merge partial updates with previously stored attributes so consumers always get a complete picture
        if (Attributes is not null)
            args = MergeAttributes(Attributes, args);

        Attributes = args;
        EntryState |= WorldEntryState.Attributes;
        OnAttributes?.Invoke(args);
        CheckWorldEntryComplete();
    }

    private static AttributesArgs MergeAttributes(AttributesArgs previous, AttributesArgs incoming)
    {
        var flags = incoming.StatUpdateType;

        // Start from previous complete state, then overlay the incoming partial fields
        var merged = previous with
        {
            StatUpdateType = flags
        };

        if (flags.HasFlag(StatUpdateType.Primary))
            merged = merged with
            {
                Level = incoming.Level,
                Ability = incoming.Ability,
                MaximumHp = incoming.MaximumHp,
                MaximumMp = incoming.MaximumMp,
                Str = incoming.Str,
                Int = incoming.Int,
                Wis = incoming.Wis,
                Con = incoming.Con,
                Dex = incoming.Dex,
                UnspentPoints = incoming.UnspentPoints,
                MaxWeight = incoming.MaxWeight,
                CurrentWeight = incoming.CurrentWeight
            };

        if (flags.HasFlag(StatUpdateType.Vitality))
            merged = merged with
            {
                CurrentHp = incoming.CurrentHp,
                CurrentMp = incoming.CurrentMp
            };

        if (flags.HasFlag(StatUpdateType.ExpGold))
            merged = merged with
            {
                TotalExp = incoming.TotalExp,
                ToNextLevel = incoming.ToNextLevel,
                TotalAbility = incoming.TotalAbility,
                ToNextAbility = incoming.ToNextAbility,
                GamePoints = incoming.GamePoints,
                Gold = incoming.Gold
            };

        if (flags.HasFlag(StatUpdateType.Secondary))
            merged = merged with
            {
                Blind = incoming.Blind,
                HasUnreadMail = incoming.HasUnreadMail,
                OffenseElement = incoming.OffenseElement,
                DefenseElement = incoming.DefenseElement,
                MagicResistance = incoming.MagicResistance,
                Ac = incoming.Ac,
                Dmg = incoming.Dmg,
                Hit = incoming.Hit
            };

        if (flags.HasFlag(StatUpdateType.GameMasterA))
            merged = merged with
            {
                IsAdmin = incoming.IsAdmin
            };

        if (flags.HasFlag(StatUpdateType.GameMasterB))
            merged = merged with
            {
                IsSwimming = incoming.IsSwimming
            };

        return merged;
    }

    private void HandleDisplayVisibleEntities(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayVisibleEntitiesArgs>(in pkt);
        OnDisplayVisibleEntities?.Invoke(args);
    }

    private void HandleDisplayAisling(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayAislingArgs>(in pkt);
        OnDisplayAisling?.Invoke(args);
    }

    private void CheckWorldEntryComplete()
    {
        if (State != ConnectionState.World)
            return;

        if (!EntryState.HasFlag(WorldEntryState.AllRequired))
            return;

        // Clear the flag so we don't fire again until the next world entry
        EntryState = WorldEntryState.None;
        OnWorldEntryComplete?.Invoke();
    }

    private void HandleRemoveEntity(ServerPacket pkt)
    {
        var args = Client.Deserialize<RemoveEntityArgs>(in pkt);
        OnRemoveEntity?.Invoke(args.SourceId);
    }

    private void HandleCreatureWalk(ServerPacket pkt)
    {
        var args = Client.Deserialize<CreatureWalkArgs>(in pkt);

        OnCreatureWalk?.Invoke(
            args.SourceId,
            args.OldPoint.X,
            args.OldPoint.Y,
            args.Direction);
    }

    private void HandleClientWalkResponse(ServerPacket pkt)
    {
        var args = Client.Deserialize<ClientWalkResponseArgs>(in pkt);
        OnClientWalkResponse?.Invoke(args.Direction, args.OldPoint.X, args.OldPoint.Y);
    }

    private void HandleCreatureTurn(ServerPacket pkt)
    {
        var args = Client.Deserialize<CreatureTurnArgs>(in pkt);
        OnCreatureTurn?.Invoke(args.SourceId, args.Direction);
    }

    // --- Chat / Messages ---

    private void HandleServerMessage(ServerPacket pkt)
    {
        var args = Client.Deserialize<ServerMessageArgs>(in pkt);
        OnServerMessage?.Invoke(args);
    }

    private void HandleDisplayPublicMessage(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayPublicMessageArgs>(in pkt);
        OnDisplayPublicMessage?.Invoke(args);
    }

    // --- Inventory ---

    private void HandleAddItemToPane(ServerPacket pkt)
    {
        var args = Client.Deserialize<AddItemToPaneArgs>(in pkt);
        var displayName = args.Item is { Stackable: true, Count: > 0 } ? $"{args.Item.Name}[ {args.Item.Count} ]" : args.Item.Name;

        InventoryState[args.Item.Slot] = new InventorySlotInfo(
            args.Item.Sprite,
            displayName,
            args.Item.Stackable,
            args.Item.Count ?? 0);
        OnAddItemToPane?.Invoke(args);
    }

    private void HandleRemoveItemFromPane(ServerPacket pkt)
    {
        var args = Client.Deserialize<RemoveItemFromPaneArgs>(in pkt);
        InventoryState.Remove(args.Slot);
        OnRemoveItemFromPane?.Invoke(args);
    }

    // --- Skills / Spells ---

    private void HandleAddSkillToPane(ServerPacket pkt)
    {
        var args = Client.Deserialize<AddSkillToPaneArgs>(in pkt);
        SkillState[args.Skill.Slot] = (args.Skill.Sprite, args.Skill.PanelName);
        OnAddSkillToPane?.Invoke(args);
    }

    private void HandleRemoveSkillFromPane(ServerPacket pkt)
    {
        var args = Client.Deserialize<RemoveSkillFromPaneArgs>(in pkt);
        SkillState.Remove(args.Slot);
        OnRemoveSkillFromPane?.Invoke(args);
    }

    private void HandleAddSpellToPane(ServerPacket pkt)
    {
        var args = Client.Deserialize<AddSpellToPaneArgs>(in pkt);
        SpellState[args.Spell.Slot] = args.Spell;
        OnAddSpellToPane?.Invoke(args);
    }

    private void HandleRemoveSpellFromPane(ServerPacket pkt)
    {
        var args = Client.Deserialize<RemoveSpellFromPaneArgs>(in pkt);
        SpellState.Remove(args.Slot);
        OnRemoveSpellFromPane?.Invoke(args);
    }

    // --- Equipment ---

    private void HandleEquipment(ServerPacket pkt)
    {
        var args = Client.Deserialize<EquipmentArgs>(in pkt);

        EquipmentState[args.Slot] = new EquipmentInfo(
            args.Item.Sprite,
            args.Item.Color,
            args.Item.Name,
            args.Item.MaxDurability,
            args.Item.CurrentDurability);

        OnEquipment?.Invoke(args);
    }

    private void HandleDisplayUnequip(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayUnequipArgs>(in pkt);
        EquipmentState.Remove(args.EquipmentSlot);
        OnDisplayUnequip?.Invoke(args);
    }

    // --- Visual / Audio ---

    private void HandleHealthBar(ServerPacket pkt)
    {
        var args = Client.Deserialize<HealthBarArgs>(in pkt);
        OnHealthBar?.Invoke(args);
    }

    private void HandleSound(ServerPacket pkt)
    {
        var args = Client.Deserialize<SoundArgs>(in pkt);
        OnSound?.Invoke(args);
    }

    private void HandleBodyAnimation(ServerPacket pkt)
    {
        var args = Client.Deserialize<BodyAnimationArgs>(in pkt);
        OnBodyAnimation?.Invoke(args);
    }

    private void HandleAnimation(ServerPacket pkt)
    {
        var args = Client.Deserialize<AnimationArgs>(in pkt);
        OnAnimation?.Invoke(args);
    }

    private void HandleCooldown(ServerPacket pkt)
    {
        var args = Client.Deserialize<CooldownArgs>(in pkt);
        OnCooldown?.Invoke(args);
    }

    private void HandleEffect(ServerPacket pkt)
    {
        var args = Client.Deserialize<EffectArgs>(in pkt);
        OnEffect?.Invoke(args);
    }

    // --- World State ---

    private void HandleLightLevel(ServerPacket pkt)
    {
        var args = Client.Deserialize<LightLevelArgs>(in pkt);
        OnLightLevel?.Invoke(args);
    }

    private void HandleDoor(ServerPacket pkt)
    {
        var args = Client.Deserialize<DoorArgs>(in pkt);
        OnDoor?.Invoke(args);
    }

    private void HandleRefreshResponse(ServerPacket _) => OnRefreshResponse?.Invoke();

    private void HandleMapChangePending(ServerPacket _) => OnMapChangePending?.Invoke();

    // --- NPC Interaction ---

    private void HandleDisplayMenu(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayMenuArgs>(in pkt);
        OnDisplayMenu?.Invoke(args);
    }

    private void HandleDisplayDialog(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayDialogArgs>(in pkt);
        OnDisplayDialog?.Invoke(args);
    }

    private void HandleDisplayBoard(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayBoardArgs>(in pkt);
        OnDisplayBoard?.Invoke(args);
    }

    private void HandleDisplayExchange(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayExchangeArgs>(in pkt);
        OnDisplayExchange?.Invoke(args);
    }

    private void HandleDisplayGroupInvite(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayGroupInviteArgs>(in pkt);
        OnDisplayGroupInvite?.Invoke(args);
    }

    // --- Profiles / Lists ---

    private void HandleEditableProfileRequest(ServerPacket _) => OnEditableProfileRequest?.Invoke();

    /// <summary>
    ///     Fired when the server requests the player's portrait and profile text.
    /// </summary>
    public event Action? OnEditableProfileRequest;

    private void HandleSelfProfile(ServerPacket pkt)
    {
        var args = Client.Deserialize<SelfProfileArgs>(in pkt);
        OnSelfProfile?.Invoke(args);
    }

    private void HandleOtherProfile(ServerPacket pkt)
    {
        var args = Client.Deserialize<OtherProfileArgs>(in pkt);
        OnOtherProfile?.Invoke(args);
    }

    private void HandleWorldList(ServerPacket pkt)
    {
        var args = Client.Deserialize<WorldListArgs>(in pkt);
        OnWorldList?.Invoke(args);
    }

    private void HandleWorldMap(ServerPacket pkt)
    {
        var args = Client.Deserialize<WorldMapArgs>(in pkt);
        OnWorldMap?.Invoke(args);
    }

    // --- Notepads ---

    private void HandleDisplayEditableNotepad(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayEditableNotepadArgs>(in pkt);
        OnDisplayEditableNotepad?.Invoke(args);
    }

    private void HandleDisplayReadonlyNotepad(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayReadonlyNotepadArgs>(in pkt);
        OnDisplayReadonlyNotepad?.Invoke(args);
    }

    // --- Misc ---

    private void HandleExitResponse(ServerPacket pkt)
    {
        var args = Client.Deserialize<ExitResponseArgs>(in pkt);
        OnExitResponse?.Invoke(args);
    }

    private void HandleForceClientPacket(ServerPacket pkt)
    {
        var args = Client.Deserialize<ForceClientPacketArgs>(in pkt);

        var owner = MemoryPool<byte>.Shared.Rent(args.Data.Length);
        args.Data.CopyTo(owner.Memory.Span);

        var packet = new Packet((byte)args.ClientOpCode, owner, args.Data.Length);
        Client.Send(ref packet);

        OnForceClientPacket?.Invoke(args);
    }

    private void HandleCancelCasting(ServerPacket _) => OnCancelCasting?.Invoke();

    private void HandleMetaData(ServerPacket pkt)
    {
        var args = Client.Deserialize<MetaDataArgs>(in pkt);
        OnMetaData?.Invoke(args);
    }

    private void HandleDisconnected()
    {
        if (State != ConnectionState.Connecting)
            State = ConnectionState.Disconnected;
    }
    #endregion
}

/// <summary>
///     Buffered equipment slot data received from the server.
/// </summary>
public readonly record struct EquipmentInfo(
    ushort Sprite,
    DisplayColor Color,
    string Name,
    int MaxDurability,
    int CurrentDurability);

/// <summary>
///     Contains redirect information received from the server.
/// </summary>
public readonly record struct RedirectInfo(
    IPEndPoint EndPoint,
    byte Seed,
    string Key,
    string Name,
    uint Id,
    ConnectionState TargetState);

/// <summary>
///     Tracks which world entry packets have been received.
/// </summary>
[Flags]
public enum WorldEntryState : byte
{
    None = 0,
    UserId = 1 << 0,
    MapInfo = 1 << 1,
    MapLoaded = 1 << 2,
    MapChangeComplete = 1 << 3,
    Location = 1 << 4,
    Attributes = 1 << 5,

    AllRequired = UserId | MapInfo | MapLoaded | MapChangeComplete | Location | Attributes
}
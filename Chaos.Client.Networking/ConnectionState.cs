namespace Chaos.Client.Networking;

/// <summary>
///     Represents the current phase of the client's connection to the server.
/// </summary>
public enum ConnectionState
{
    /// <summary>
    ///     Not connected to any server.
    /// </summary>
    Disconnected,

    /// <summary>
    ///     TCP connection is being established.
    /// </summary>
    Connecting,

    /// <summary>
    ///     Connected to the lobby server, performing handshake (Version, ConnectionInfo, ServerTable).
    /// </summary>
    Lobby,

    /// <summary>
    ///     Connected to the login server, awaiting authentication.
    /// </summary>
    Login,

    /// <summary>
    ///     Connected to the world server, game is active.
    /// </summary>
    World
}
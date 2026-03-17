#region
using Chaos.Client.Data.Repositories;
#endregion

namespace Chaos.Client.Data;

public static class DataContext
{
    /// <summary>
    ///     The client version sent during the lobby handshake.
    /// </summary>
    public static ushort ClientVersion { get; private set; }

    public static CreatureSpriteRepository CreatureSprites { get; private set; } = null!;

    /// <summary>
    ///     The root path to the Dark Ages installation directory. Must be set before accessing any repositories or archives.
    /// </summary>
    public static string DataPath { get; private set; } = null!;

    public static EffectsRepository Effects { get; private set; } = null!;

    /// <summary>
    ///     The lobby server hostname or IP address.
    /// </summary>
    public static string LobbyHost { get; private set; } = null!;

    /// <summary>
    ///     The lobby server port.
    /// </summary>
    public static int LobbyPort { get; private set; }

    public static MapFileRepository MapsFiles { get; private set; } = null!;
    public static MetaFileRepository MetaFiles { get; private set; } = null!;
    public static PanelIconRepository PanelIcons { get; private set; } = null!;
    public static PanelItemRepository PanelItems { get; private set; } = null!;
    public static SoundRepository Sounds { get; private set; } = null!;
    public static TileRepository Tiles { get; private set; } = null!;
    public static UiComponentRepository UserControls { get; private set; } = null!;

    public static void Initialize(
        ushort clientVersion,
        string dataPath,
        string lobbyHost,
        int lobbyPort)
    {
        ClientVersion = clientVersion;
        DataPath = dataPath;
        LobbyHost = lobbyHost;
        LobbyPort = lobbyPort;

        CreatureSprites = new CreatureSpriteRepository();
        Effects = new EffectsRepository();
        MapsFiles = new MapFileRepository();
        MetaFiles = new MetaFileRepository();
        PanelIcons = new PanelIconRepository();
        PanelItems = new PanelItemRepository();
        Sounds = new SoundRepository();
        Tiles = new TileRepository();
        UserControls = new UiComponentRepository();
    }
}
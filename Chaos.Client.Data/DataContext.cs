#region
using Chaos.Client.Data.Repositories;
#endregion

namespace Chaos.Client.Data;

public static class DataContext
{
    public static AislingDrawDataRepository AislingDrawData { get; private set; } = null!;

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
    public static FontRepository Fonts { get; private set; } = null!;

    public static LightMaskRepository LightMasks { get; private set; } = null!;

    /// <summary>
    ///     The lobby server hostname or IP address.
    /// </summary>
    public static string LobbyHost { get; private set; } = null!;

    /// <summary>
    ///     The lobby server port.
    /// </summary>
    public static int LobbyPort { get; private set; }

    public static LocalPlayerSettingsRepository LocalPlayerSettings { get; private set; } = null!;

    public static MapFileRepository MapsFiles { get; private set; } = null!;
    public static MetaFileRepository MetaFiles { get; private set; } = null!;
    public static PanelSpriteRepository PanelSprites { get; private set; } = null!;
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

        LegendPalette.Initialize();

        AislingDrawData = new AislingDrawDataRepository();
        CreatureSprites = new CreatureSpriteRepository();
        Effects = new EffectsRepository();
        Fonts = new FontRepository();
        LightMasks = new LightMaskRepository();
        LocalPlayerSettings = new LocalPlayerSettingsRepository();
        MapsFiles = new MapFileRepository();
        MetaFiles = new MetaFileRepository();
        PanelSprites = new PanelSpriteRepository();
        Tiles = new TileRepository();
        UserControls = new UiComponentRepository();
    }
}
#region
using Chaos.Client.Data.Repositories;
#endregion

namespace Chaos.Client.Data;

public static class DataContext
{
    /// <summary>
    ///     The client version sent during the lobby handshake.
    /// </summary>
    public static ushort ClientVersion { get; set; } = 741;

    /// <summary>
    ///     The root path to the Dark Ages installation directory. Must be set before accessing any repositories or archives.
    /// </summary>
    public static string DataPath { get; set; } = @"C:\Users\Despe\Desktop\Unora\Unora"; //@"C:\Users\Despe\Desktop\Dark Ages";

    /// <summary>
    ///     The lobby server hostname or IP address.
    /// </summary>
    public static string LobbyHost { get; set; } = "127.0.0.1"; //"da0.kru.com";

    /// <summary>
    ///     The lobby server port.
    /// </summary>
    public static int LobbyPort { get; set; } = 4200; //2610;

    public static CreatureSpriteRepository CreatureSprites { get; } = new();
    public static EffectsRepository Effects { get; } = new();
    public static MapFileRepository MapsFiles { get; } = new();
    public static MetaFileRepository MetaFiles { get; } = new();
    public static PanelIconRepository PanelIcons { get; } = new();
    public static PanelItemRepository PanelItems { get; } = new();
    public static SoundRepository Sounds { get; } = new();
    public static TileRepository Tiles { get; } = new();
    public static UiComponentRepository UserControls { get; } = new();
}
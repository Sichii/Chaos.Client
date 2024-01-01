using Chaos.Client.Data.Repositories;

namespace Chaos.Client.Data;

public static class DataContext
{
    public static CreatureSpriteRepository CreatureSprites { get; } = new();
    public static EffectsRepository Effects { get; } = new();
    public static MapFileRepository MapsFiles { get; } = new();
    public static MetaFileRepository MetaFiles { get; } = new();
    public static PanelIconRepository PanelIcons { get; } = new();
    public static PanelItemRepository PanelItems { get; } = new();
    public static SoundRepository Sounds { get; } = new();
    public static TileRepository Tiles { get; } = new();
    public static UserControlRepository UserControls { get; } = new();
}
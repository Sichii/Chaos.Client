namespace Chaos.Client.Systems;

/// <summary>
///     Reads and writes the client settings file in the original DarkAges format. File is a line-delimited key-value
///     format: "Key : Value" or "Key: Value". Settings file is saved next to the executable as "DarkAges" (no extension).
/// </summary>
public sealed class ClientSettings
{
    private const string FILE_NAME = "Darkages.cfg";
    public bool AutoAcceptGroupInvites { get; set; } = true;
    public int ChattingMode { get; set; }
    public bool DoGroundAnimation { get; set; } = true;
    public int EnableProfileClick { get; set; }
    public bool GroupOpen { get; set; }
    public int MusicVolume { get; set; } = 5;
    public bool RecordNpcChat { get; set; } = true;
    public int ScrollLevel { get; set; }

    // Defaults match the original client
    public int SoundVolume { get; set; } = 5;
    public int Speed { get; set; } = 100;
    public bool UseShiftKeyForAltPanels { get; set; } = true;

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, FILE_NAME);

    /// <summary>
    ///     Loads settings from the DarkAges file. Returns defaults if the file doesn't exist.
    /// </summary>
    public static ClientSettings Load()
    {
        var settings = new ClientSettings();

        if (!File.Exists(FilePath))
            return settings;

        try
        {
            foreach (var line in File.ReadLines(FilePath))
            {
                var colonIndex = line.IndexOf(':');

                if (colonIndex < 0)
                    continue;

                var key = line[..colonIndex]
                    .Trim();

                var value = line[(colonIndex + 1)..]
                    .Trim();

                switch (key)
                {
                    case "Sound Volume":
                        if (int.TryParse(value, out var sv))
                            settings.SoundVolume = Math.Clamp(sv, 0, 10);

                        break;

                    case "Music Volume":
                        if (int.TryParse(value, out var mv))
                            settings.MusicVolume = Math.Clamp(mv, 0, 10);

                        break;

                    case "doGroundAnimation":
                        settings.DoGroundAnimation = value == "1";

                        break;

                    case "SkillSpellSelectByToggle":
                        settings.UseShiftKeyForAltPanels = value == "1";

                        break;

                    case "GroupAnswer":
                        settings.GroupOpen = value == "1";

                        break;

                    case "ScrollLevel":
                        if (int.TryParse(value, out var sl))
                            settings.ScrollLevel = sl;

                        break;

                    case "UserClickMode":
                        if (int.TryParse(value, out var ucm))
                            settings.EnableProfileClick = ucm;

                        break;

                    case "MonsterSayRecordMode":
                        settings.RecordNpcChat = value == "1";

                        break;

                    case "GroupObjectOption":
                        settings.AutoAcceptGroupInvites = value == "1";

                        break;

                    case "Chatting Mode":
                        if (int.TryParse(value, out var cm))
                            settings.ChattingMode = cm;

                        break;

                    case "Speed":
                        if (int.TryParse(value, out var spd))
                            settings.Speed = spd;

                        break;
                }
            }
        } catch
        {
            // Corrupted file — return defaults
        }

        return settings;
    }

    /// <summary>
    ///     Saves the current settings to the DarkAges file in the original format.
    /// </summary>
    public void Save()
    {
        try
        {
            using var writer = new StreamWriter(FilePath, false);
            writer.WriteLine("Version: 9728");
            writer.WriteLine("Port: 5");
            writer.WriteLine($"Speed: {Speed}");
            writer.WriteLine("KeyBoard: 0");
            writer.WriteLine("Tel: 1");
            writer.WriteLine("HanFont: 0");
            writer.WriteLine("EngFont: 0");
            writer.WriteLine("Tel1: \"Nexus\",\"1\"");
            writer.WriteLine("Tel2: \"Nexus\",\"2\"");
            writer.WriteLine("Tel3: \"Nexus\",\"3\"");
            writer.WriteLine("Tel4: \"Nexus\",\"4\"");
            writer.WriteLine($"Chatting Mode : {ChattingMode}");
            writer.WriteLine($"doGroundAnimation : {(DoGroundAnimation ? 1 : 0)}");
            writer.WriteLine($"Sound Volume : {SoundVolume}");
            writer.WriteLine($"Music Volume : {MusicVolume}");
            writer.WriteLine($"SkillSpellSelectByToggle : {(UseShiftKeyForAltPanels ? 1 : 0)}");
            writer.WriteLine($"GroupAnswer : {(GroupOpen ? 1 : 0)}");
            writer.WriteLine($"ScrollLevel : {ScrollLevel}");
            writer.WriteLine($"UserClickMode : {EnableProfileClick}");
            writer.WriteLine($"MonsterSayRecordMode : {(RecordNpcChat ? 1 : 0)}");
            writer.WriteLine($"GroupObjectOption : {(AutoAcceptGroupInvites ? 1 : 0)}");
        } catch
        {
            // Best effort — don't crash on save failure
        }
    }
}
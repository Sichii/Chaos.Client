#region
using Chaos.Client.Data.Abstractions;
#endregion

namespace Chaos.Client.Data.Repositories;

public sealed class SoundRepository : RepositoryBase
{
    private string ConstructKeyForEffectSound(int soundId) => $"EFFECTSOUND_{soundId}";

    /// <summary>
    ///     Returns an MP3 stream for a sound effect, or null if not found.
    /// </summary>
    public Stream? GetEffectSound(int soundId)
    {
        try
        {
            return GetOrCreate(ConstructKeyForEffectSound(soundId), () => LoadEffectSound(soundId));
        } catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Returns a file stream for a music track from the music directory, or null if not found.
    /// </summary>
    public Stream? GetMusic(int musicId)
    {
        var path = Path.Combine(DataContext.DataPath, "music", $"{musicId}.mus");

        if (!File.Exists(path))
            return null;

        return File.OpenRead(path);
    }

    private Stream LoadEffectSound(int soundId)
    {
        var buffer = DatArchives.Legend[$"{soundId}.mp3"]
                                .ToSpan();

        return new MemoryStream(buffer.ToArray());

        //We cant do this atm, need to fix ToStreamSegment to allow specifying "leaveOpen"
        //this method will allow us to not load the sound into memory, and instead read them from disk in parallel if needed
        //var freshArchive = DatArchives.Load(nameof(DatArchives.Legend));

        //return freshArchive[$"{soundId}.mp3"].ToStreamSegment(false);
    }
}
using Chaos.Client.Common.Abstractions;

namespace Chaos.Client.Data.Repositories;

public class SoundRepository : RepositoryBase
{
    private string ConstructKeyForEffectSound(int soundId) => $"EFFECTSOUND_{soundId}";
    private string ConstructKeyForMusic(int musicId) => $"MUSIC_{musicId}";

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

    private Stream LoadMusic(int musicId)
        =>
            /* Unsure where music files are atm
            var buffer = DatArchives.Legend[$"{musicId}.mp3"]
                                    .ToSpan();

            return new MemoryStream(buffer.ToArray());
            */
            new MemoryStream();

    //We cant do this atm, need to fix ToStreamSegment to allow specifying "leaveOpen"
    //this method will allow us to not load the sound into memory, and instead read them from disk in parallel if needed
    //var freshArchive = DatArchives.Load(nameof(DatArchives.Legend));
    //return freshArchive[$"{musicId}.mp3"].ToStreamSegment(false);
}
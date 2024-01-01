using Chaos.Client.Common.Abstractions;
using Chaos.Client.Data.Utilities;
using Chaos.Common.Definitions;
using Chaos.Geometry.Abstractions.Definitions;
using DALib.Drawing;
using DALib.Utility;
using Microsoft.Extensions.Caching.Memory;

namespace Chaos.Client.Data.Repositories;

public class AislingBodyAnimationRepository : RepositoryBase
{
    private readonly PaletteLookup Body2Palettes = PaletteLookup.FromArchive("palm", DatArchives.Khanpal);
    private readonly PaletteLookup BodyPalettes = PaletteLookup.FromArchive("palb", DatArchives.Khanpal);

    /// <inheritdoc />
    protected override void ConfigureEntry(ICacheEntry entry) => entry.SetPriority(CacheItemPriority.NeverRemove);

    public Palettized<EpfFile>? GetBodyAnimation(
        BodySprite bodySprite,
        BodyAnimation animation,
        Direction direction,
        BodyColor color)
    {
        if (bodySprite == BodySprite.None)
            return null;

        var gender = Helpers.DetermineGender(bodySprite);

        return default;
    }
}
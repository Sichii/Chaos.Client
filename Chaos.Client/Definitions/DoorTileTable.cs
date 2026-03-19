#region
using System.Collections.Frozen;
#endregion

namespace Chaos.Client.Definitions;

/// <summary>
///     Hardcoded door tile mapping extracted from DarkAges.exe (table at 0x0068b8b0). Maps closed foreground tile IDs to
///     their open counterparts. 66 pairs, bidirectional lookup.
/// </summary>
public static class DoorTileTable
{
    // Bidirectional: ClosedToOpen for opening, OpenToClosed for closing
    private static readonly FrozenDictionary<int, int> ClosedToOpen;
    private static readonly FrozenDictionary<int, int> OpenToClosed;

    static DoorTileTable()
    {
        var pairs = new (int Closed, int Open)[]
        {
            (1994, 1997),
            (2000, 2003),
            (2163, 4519),
            (2164, 4520),
            (2165, 4521),
            (2196, 4532),
            (2197, 4533),
            (2198, 4534),
            (2227, 4527),
            (2228, 4528),
            (2229, 4529),
            (2260, 4540),
            (2261, 4541),
            (2262, 4542),
            (2291, 4523),
            (2292, 4524),
            (2293, 4525),
            (2328, 4536),
            (2329, 4537),
            (2330, 4538),
            (2436, 2432),
            (2461, 2465),
            (2673, 2680),
            (2674, 2681),
            (2675, 2682),
            (2687, 2694),
            (2688, 2695),
            (2689, 2696),
            (2714, 2721),
            (2715, 2722),
            (2727, 2734),
            (2728, 2735),
            (2761, 2768),
            (2762, 2769),
            (2776, 2783),
            (2777, 2784),
            (2850, 2857),
            (2851, 2858),
            (2852, 2859),
            (2874, 2881),
            (2875, 2882),
            (2876, 2883),
            (2897, 2903),
            (2898, 2904),
            (2929, 2923),
            (2930, 2924),
            (2945, 2951),
            (2946, 2952),
            (2971, 2977),
            (2972, 2978),
            (2993, 2999),
            (2994, 3000),
            (3019, 3025),
            (3020, 3026),
            (3058, 3066),
            (3059, 3067),
            (3090, 3098),
            (3091, 3099),
            (3118, 3126),
            (3119, 3127),
            (3150, 3158),
            (3159, 3151),
            (3178, 3186),
            (3179, 3187),
            (3210, 3218),
            (3211, 3219)
        };

        var closedToOpen = new Dictionary<int, int>(pairs.Length);
        var openToClosed = new Dictionary<int, int>(pairs.Length);

        foreach ((var closed, var open) in pairs)
        {
            closedToOpen[closed] = open;
            openToClosed[open] = closed;
        }

        ClosedToOpen = closedToOpen.ToFrozenDictionary();
        OpenToClosed = openToClosed.ToFrozenDictionary();
    }

    /// <summary>
    ///     Gets the closed tile ID for an open tile. Returns null if the tile is not a recognized door.
    /// </summary>
    public static int? GetClosedTileId(int openTileId) => OpenToClosed.TryGetValue(openTileId, out var closed) ? closed : null;

    /// <summary>
    ///     Gets the open tile ID for a closed tile. Returns null if the tile is not a recognized door.
    /// </summary>
    public static int? GetOpenTileId(int closedTileId) => ClosedToOpen.TryGetValue(closedTileId, out var open) ? open : null;
}
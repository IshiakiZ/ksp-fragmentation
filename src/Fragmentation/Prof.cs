namespace Fragmentation
{
    /// <summary>
    /// For the development build: the time this mod spends in each of its parts, by name, for Tests.Clock to read frame by frame.
    /// The calls to Add are left out of the release build altogether (Conditional), and reading the clock costs some nanoseconds.
    /// (The parts overlap: a piece's mesh is made within the making of pieces, which is within a part going up or breaking.)
    /// </summary>
    internal static class Prof
    {
        public const int Watch = 0, Blows = 1, Slide = 2, Decide = 3, Leaks = 4, Break = 5, Pieces = 6, Skin = 7, Bits = 8, Marks = 9, Spawn = 10, FirstSkin = 11,
                         PieceMesh = 12, PieceMake = 13, Events = 14, Explode = 15, BlowOff = 16, Frames = 17, Sever = 18, Shape = 19, Measure = 20, Mass = 21,
                         Crush = 22, Crater = 23, HitSound = 24, Dies = 25, Hide = 26, Show = 27, Feel = 28, Thud = 29, Prepared = 30;
        public static readonly string[] Names = { "watch", "blows", "slide", "decide", "leaks", "break", "pieces", "skin", "bits", "marks", "spawn", "first skins",
                                                  "piece meshes", "piece making", "crash events", "explosions", "blowing off", "frames", "severing", "colliders", "measuring", "mass",
                                                  "crushing", "craters", "hit sounds", "the game's parting", "hiding", "skins shown", "ground feel", "thuds", "skins drawn ahead" };
        public static readonly long[] Ticks = new long[31];

        public static long Now => System.Diagnostics.Stopwatch.GetTimestamp();

        [System.Diagnostics.Conditional("DEV")]
        public static void Add(int k, long since) { Ticks[k] += System.Diagnostics.Stopwatch.GetTimestamp() - since; }
    }
}

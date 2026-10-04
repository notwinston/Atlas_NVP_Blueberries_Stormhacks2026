namespace Atlas.Blades.Playground.Shell
{
    /// <summary>
    /// The ten beats of the Geometry Playground run (Master Plan section 10.1.2). The value is the beat number
    /// of the table, and the number the director logs in <c>pg.beat</c>.
    /// </summary>
    public enum PlaygroundBeat : byte
    {
        None = 0,
        Scan = 1,
        HomeAppears = 2,
        HeadsetOn = 3,
        Centre = 4,
        Dial = 5,
        Walk345 = 6,
        FreeCurvedWalk = 7,
        Compare = 8,
        ExhibitMenu = 9,
        ReturnHome = 10
    }

    /// <summary>Names and counts of the beats. Every method returns a constant: no allocation.</summary>
    public static class PlaygroundBeats
    {
        public const int Count = 10;

        static readonly string[] s_names =
        {
            "none", "scan", "home", "headset", "centre", "dial", "walk345", "free", "compare", "menu", "return"
        };

        /// <summary>The short name written to the event log: "scan", "home", "headset", "centre", "dial",
        /// "walk345", "free", "compare", "menu" or "return". "none" for any other value.</summary>
        public static string Name(PlaygroundBeat beat)
        {
            int index = (int)beat;
            return index >= 0 && index <= Count ? s_names[index] : s_names[0];
        }
    }

    /// <summary>
    /// One beat's exit condition, as the director sees it. The director calls <see cref="Enter"/> when the beat
    /// starts, ticks the beat's own inputs, and advances when <see cref="Complete"/> turns true or when the
    /// operator's <c>SceneCmd</c> asks for it (Master Plan section 10.1.2).
    /// </summary>
    public interface IBeatStep
    {
        /// <summary>The beat this step is the exit condition of.</summary>
        PlaygroundBeat Beat { get; }
        /// <summary>The beat starts: forget what an earlier visit left behind.</summary>
        void Enter();
        /// <summary>The exit condition holds.</summary>
        bool Complete { get; }
    }
}

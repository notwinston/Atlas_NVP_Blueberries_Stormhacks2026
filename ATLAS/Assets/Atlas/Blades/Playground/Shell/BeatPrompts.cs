namespace Atlas.Blades.Playground
{
    /// <summary>
    /// The tutorial line of each of the ten beats (Master Plan section 10.1.2). Beats are numbered 1 to 10 as in
    /// the plan's table. A flat space is never called curved (rule of section 10.1.4), and every line names the
    /// action the walker has: confirm with A, dwell on the chip, or click.
    /// </summary>
    public static class BeatPrompts
    {
        public const int BeatCount = 10;

        static readonly string[] Lines =
        {
            "Sweep the phone slowly over the floor, then confirm the outline.",          // 1 Scan
            "This is your room, drawn one to one. Confirm when it looks right.",         // 2 Home appears
            "Put the phone in the headset and stand still.",                             // 3 Headset on
            "Walk to the glowing disc on the floor.",                                    // 4 Centre
            "Stand still and turn the dial to five rooms per corner.",                   // 5 Dial
            "Follow the beacons round the loop and count the corners.",                  // 6 3-4-5 walk
            "Walk freely. Open the mode chip when you are ready.",                       // 7 Free Curved Walk
            "Same walk, other mode. Open the menu when you are ready.",                  // 8 Compare
            "Look at a sign to choose an exhibit, or confirm that you are done.",        // 9 Exhibit menu
            "Walking home. Take the headset off when your room is back."                 // 10 Return Home
        };

        /// <summary>The line of a beat (1 to 10), or null for a number outside the table.</summary>
        public static string TextFor(int beat)
        {
            return beat >= 1 && beat <= BeatCount ? Lines[beat - 1] : null;
        }
    }
}

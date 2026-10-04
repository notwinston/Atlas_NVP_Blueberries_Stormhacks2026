using System;
using System.Globalization;

namespace Atlas.Blades.FieldFit.BlindSpotFinder
{
    /// <summary>Every user-facing string of B06/B07 (G13: no clinical claim; river lines are RiverText.DefaultLines).</summary>
    public static class BlindSpotStrings
    {
        public const string Intro = "Blind-Spot Finder\nKeep your eye on the cross.\nPress A or click when you see a dot.\nPress A or click to start.";
        public const string NotFound = "No blind spot found.\nPress A or click to try again.";
        public const string RiverPrompt = "Keep your eye on the cross.\nPress A or click to finish.";
        public const string NoRig = "Stereo rig not found. Load Atlas_Boot.";

        public static string Result(in BlindSpotResult r)
        {
            var ci = CultureInfo.InvariantCulture;
            double az = r.CentreAzRad * 180.0 / Math.PI, el = r.CentreElRad * 180.0 / Math.PI, rad = r.RadiusRad * 180.0 / Math.PI;
            return "Your blind spot: " + Math.Abs(az).ToString("0.0", ci) + "° to the " + (az >= 0 ? "right" : "left")
                 + ", " + Math.Abs(el).ToString("0.0", ci) + "° " + (el >= 0 ? "up" : "down")
                 + ", radius " + rad.ToString("0.0", ci) + "°.\nFound in " + r.TimeToFindS.ToString("0.0", ci)
                 + " s. Measured in this headset, for teaching only.\nPress A or click for the river text.";
        }
    }
}

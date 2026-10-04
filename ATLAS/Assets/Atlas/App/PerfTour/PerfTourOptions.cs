using System;
using System.Globalization;

namespace Atlas.App
{
    /// <summary>Where the head comes from during a run. The value is the index of <c>perf.pose</c>.</summary>
    public enum PerfPoseChoice : byte
    {
        /// <summary>The run decides: see <see cref="PerfTourOptions.ResolvePose"/>.</summary>
        Auto = 0,
        /// <summary>The platform's own head: the tracked camera on a phone.</summary>
        Tracked = 1,
        /// <summary>The newest recorded walk.</summary>
        Replay = 2,
        /// <summary>The built-in lap of <see cref="SyntheticPoseTrack"/>.</summary>
        Synthetic = 3,
    }

    /// <summary>
    /// What the command line asks of the performance runs. Engine-free.
    /// <code>-atlasPerf tour|soak|soakNoDepth|ladder   -atlasPerfVisit 30   -atlasPerfSoakMinutes 15
    /// -atlasPerfInterval 60   -atlasPerfPose auto|tracked|replay|synthetic   -atlasPerfQuit</code>
    /// </summary>
    public sealed class PerfTourOptions
    {
        public static readonly string[] RunNames = { "off", "tour", "soak", "soakNoDepth", "ladder" };
        public static readonly string[] PoseNames = { "auto", "tracked", "replay", "synthetic" };

        public PerfRunKind Run = PerfRunKind.None;
        /// <summary>Seconds measured per space; 0 when not given.</summary>
        public int VisitSeconds;
        /// <summary>Minutes of the soak; 0 when not given.</summary>
        public int SoakMinutes;
        /// <summary>Seconds between two changes of space in the soak; 0 when not given.</summary>
        public int IntervalSeconds;
        /// <summary>-1 when not given.</summary>
        public int Pose = -1;
        /// <summary>Quit the app when the run has ended: for a run started by a script.</summary>
        public bool Quit;

        public static PerfTourOptions Parse(string[] args)
        {
            var options = new PerfTourOptions();
            if (args == null) return options;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string value = i + 1 < args.Length ? args[i + 1] : null;
                if (Is(arg, "-atlasPerf") && value != null)
                {
                    int run = IndexOf(RunNames, value);
                    if (run > 0) options.Run = (PerfRunKind)run;
                    i++;
                }
                else if (Is(arg, "-atlasPerfVisit") && value != null) { options.VisitSeconds = Whole(value); i++; }
                else if (Is(arg, "-atlasPerfSoakMinutes") && value != null) { options.SoakMinutes = Whole(value); i++; }
                else if (Is(arg, "-atlasPerfInterval") && value != null) { options.IntervalSeconds = Whole(value); i++; }
                else if (Is(arg, "-atlasPerfPose") && value != null) { options.Pose = IndexOf(PoseNames, value); i++; }
                else if (Is(arg, "-atlasPerfQuit")) options.Quit = true;
            }
            return options;
        }

        /// <summary>
        /// The pose source a run uses. The per-space run walks the built-in lap, so two runs compare. The soak
        /// follows the walker in the headset where there is one to track. With depth off nobody may wear the
        /// headset (Master Plan section 8.6: depth is never switched off while someone walks), so the tracked
        /// head is refused and a recorded walk, or the built-in lap, drives the run.
        /// </summary>
        /// <param name="hasTracking">True where the platform tracks a head (a phone, XR Simulation).</param>
        /// <param name="hasRecording">True when a recorded walk can be replayed.</param>
        public static PerfPoseChoice ResolvePose(PerfRunKind run, PerfPoseChoice asked, bool hasTracking, bool hasRecording)
        {
            PerfPoseChoice pose = asked;
            if (pose == PerfPoseChoice.Auto)
            {
                if (run == PerfRunKind.Soak) pose = hasTracking ? PerfPoseChoice.Tracked : PerfPoseChoice.Synthetic;
                else if (run == PerfRunKind.SoakDepthOff) pose = PerfPoseChoice.Replay;
                else pose = PerfPoseChoice.Synthetic;
            }
            if (run == PerfRunKind.SoakDepthOff && pose == PerfPoseChoice.Tracked) pose = PerfPoseChoice.Replay;
            if (pose == PerfPoseChoice.Replay && !hasRecording) pose = PerfPoseChoice.Synthetic;
            return pose;
        }

        static int Whole(string text)
        {
            int value;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0 ? value : 0;
        }

        static int IndexOf(string[] names, string value)
        {
            for (int i = 0; i < names.Length; i++)
                if (Is(names[i], value)) return i;
            return -1;
        }

        static bool Is(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}

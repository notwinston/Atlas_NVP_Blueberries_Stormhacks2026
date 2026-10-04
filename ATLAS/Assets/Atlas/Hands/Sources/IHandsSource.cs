using System;

namespace Atlas.Hands
{
    /// <summary>
    /// Where hand detections come from (documents/ATLAS_Hands_Design.md section 4.5): the plugin on the phone,
    /// a recorded log in tests, mouse and keys in the Editor, nothing anywhere else. Main thread only; no call
    /// ever waits for the detector.
    /// </summary>
    public interface IHandsSource
    {
        /// <summary>False where there is nothing to detect with: the Station, a phone without LiDAR.</summary>
        bool Available { get; }
        /// <summary>Short name for logs: "vision", "replay", "editor", "none".</summary>
        string Name { get; }
        /// <summary>Detections per second: 30, 15 or 5. 0 idles the detector. Starts it when it was stopped.</summary>
        void SetRate(int hz);
        /// <summary>
        /// The newest result, once: true when a result newer than the last one returned was copied into
        /// detection. time is the app clock of the frame. Returns at once either way.
        /// </summary>
        bool TryRead(double time, ref HandsDetection detection);
        /// <summary>0 while healthy; otherwise the last error of the detector (negative, section 4.2).</summary>
        int Fault { get; }
        void Stop();
    }

    /// <summary>No hands: Available is false and TryRead never has anything.</summary>
    public sealed class NullHandsSource : IHandsSource
    {
        public bool Available { get { return false; } }
        public string Name { get { return "none"; } }
        public int Fault { get { return 0; } }
        public void SetRate(int hz) { }
        public bool TryRead(double time, ref HandsDetection detection) { return false; }
        public void Stop() { }
    }

    /// <summary>
    /// The slot a platform assembly fills before the scene loads, as Atlas.Scan.ARKit does for its bridges:
    /// Atlas.Hands.ARKit registers the phone's source here from a method marked
    /// RuntimeInitializeOnLoadMethod(BeforeSceneLoad). Nothing references that assembly.
    /// </summary>
    public static class HandsPlatform
    {
        static readonly NullHandsSource s_null = new NullHandsSource();
        static Func<IntPtr, IHandsSource> s_factory;

        /// <summary>The factory takes XRSessionSubsystem.nativePtr of the running AR session (IntPtr.Zero when there is none).</summary>
        public static void Register(Func<IntPtr, IHandsSource> factory) { s_factory = factory; }

        public static bool HasPlatformSource { get { return s_factory != null; } }

        /// <summary>The platform's source for this session, or the null source when no platform registered or it declines.</summary>
        public static IHandsSource Create(IntPtr nativeSession)
        {
            Func<IntPtr, IHandsSource> factory = s_factory;
            IHandsSource source = factory != null ? factory(nativeSession) : null;
            return source ?? s_null;
        }

        /// <summary>For tests.</summary>
        public static void Clear() { s_factory = null; }
    }
}

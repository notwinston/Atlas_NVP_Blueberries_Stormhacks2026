using System;
using Atlas.World;

namespace Atlas.Scan
{
    /// <summary>
    /// What scan route 4 needs from a provider-specific assembly: a RoomPlan capture of its own, read as the
    /// walls file of WallBridgeJson. Atlas.Scan names no ARKit type (contract README F1); Atlas.Scan.ARKit
    /// implements this over Assets/Plugins/iOS/AtlasRoomPlan.swift and registers itself before the first
    /// scene loads.
    /// </summary>
    public interface IWallBridge
    {
        /// <summary>"RoomPlan".</summary>
        string Name { get; }

        /// <summary>True where RoomPlan can capture (an iPhone with LiDAR). False in the Editor and in XR Simulation.</summary>
        bool Available { get; }

        /// <summary>
        /// Starts the capture on the running AR session. nativeSession is XRSessionSubsystem.nativePtr of
        /// that session. False when it cannot start.
        /// </summary>
        bool Start(IntPtr nativeSession);

        /// <summary>The walls, doors and windows seen so far as a walls file, or "" when there are none yet.</summary>
        string TakeJson();

        /// <summary>Ends the capture, leaves the AR session running and has its configuration applied again.</summary>
        void Stop();
    }

    /// <summary>The registered wall bridge, or a neutral stand-in.</summary>
    public static class WallBridge
    {
        sealed class Neutral : IWallBridge
        {
            public string Name { get { return "none"; } }
            public bool Available { get { return false; } }
            public bool Start(IntPtr nativeSession) { return false; }
            public string TakeJson() { return ""; }
            public void Stop() { }
        }

        static readonly IWallBridge s_neutral = new Neutral();
        static IWallBridge s_current;

        /// <summary>
        /// Route 4 is Extended and runs only when this is set. Off until the device check has shown that a
        /// second RoomPlan capture on the AR session leaves the furniture boxes and the depth image alone
        /// (AR Foundation open point D7).
        /// </summary>
        public static bool Enabled;

        /// <summary>Never null. With nothing registered: not available, no capture.</summary>
        public static IWallBridge Current { get { return s_current ?? s_neutral; } }

        /// <summary>Called by the provider assembly from a BeforeSceneLoad method. Null removes the registration.</summary>
        public static void Register(IWallBridge bridge)
        {
            s_current = bridge;
        }
    }

    /// <summary>
    /// One scan's use of the wall bridge, for the scan session: started with the furniture capture, ended
    /// with it, and merged into the room the walker confirms. Where there is no bridge every call does
    /// nothing and the room keeps the walls of its outline.
    /// </summary>
    public sealed class WallBridgeCapture
    {
        IWallBridge _running;
        string _json = "";

        /// <summary>A capture is running.</summary>
        public bool Running { get { return _running != null; } }

        /// <summary>
        /// The capture on the rig's AR session. With no rig or no ARKit session none starts, and the last
        /// capture is forgotten all the same: every scan calls this once.
        /// </summary>
        public bool Begin(ArSessionRig rig)
        {
            bool arKit = rig != null && rig.HasAr && rig.Backend == XrBackend.ARKit && rig.Session != null && rig.Session.subsystem != null;
            return Begin(arKit ? rig.Session.subsystem.nativePtr : IntPtr.Zero);
        }

        /// <summary>
        /// Starts a capture and forgets the last one. False when route 4 is not enabled, the bridge is not
        /// available here, there is no session or the capture does not start.
        /// </summary>
        public bool Begin(IntPtr nativeSession)
        {
            End();
            _json = "";
            IWallBridge bridge = WallBridge.Current;
            if (!WallBridge.Enabled || !bridge.Available || nativeSession == IntPtr.Zero) return false;
            if (!bridge.Start(nativeSession)) return false;
            _running = bridge;
            return true;
        }

        /// <summary>Ends the capture and keeps what it saw for Apply. Does nothing when none runs.</summary>
        public void End()
        {
            IWallBridge bridge = _running;
            if (bridge == null) return;
            _running = null;
            _json = bridge.TakeJson() ?? "";
            bridge.Stop();
        }

        /// <summary>
        /// Merges what the last capture saw into a room (WallBridgeMerge) and forgets it. The room is
        /// unchanged, and the report's Tried false, when there was no capture.
        /// </summary>
        public WallBridgeReport Apply(RoomModel room)
        {
            End();
            string json = _json;
            _json = "";
            return WallBridgeMerge.Merge(room, json);
        }
    }
}

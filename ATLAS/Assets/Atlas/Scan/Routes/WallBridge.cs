using System;
using Atlas.World;

namespace Atlas.Scan
{
    /// <summary>
    /// The one RoomPlan session of a scan (standalone-phone plan D2): a RoomCaptureSession on the running AR
    /// session, read as an atlas.room/1 file (RoomPlanJson) that holds walls, doors, windows, openings, the
    /// furniture objects with their attributes, and RoomPlan's coaching instruction. Atlas.Scan names no ARKit
    /// type (contract README F1); Atlas.Scan.ARKit implements this over Assets/Plugins/iOS/AtlasRoomPlan.swift and
    /// registers itself before the first scene loads.
    /// </summary>
    public interface IRoomPlanCapture
    {
        /// <summary>True where RoomPlan can capture (an iPhone with LiDAR). False in the Editor and in XR Simulation.</summary>
        bool Available { get; }

        /// <summary>
        /// Starts the capture on the running AR session. nativeSession is XRSessionSubsystem.nativePtr of
        /// that session. False when it cannot start.
        /// </summary>
        bool Start(IntPtr nativeSession);

        /// <summary>Everything seen so far as an atlas.room/1 file, or "" when nothing has been seen yet.</summary>
        string TakeJson();

        /// <summary>Ends the capture, leaves the AR session running and has its configuration applied again.</summary>
        void Stop();
    }

    /// <summary>The registered RoomPlan capture, or a neutral stand-in.</summary>
    public static class RoomPlanPlugin
    {
        sealed class Neutral : IRoomPlanCapture
        {
            public bool Available { get { return false; } }
            public bool Start(IntPtr nativeSession) { return false; }
            public string TakeJson() { return ""; }
            public void Stop() { }
        }

        static readonly IRoomPlanCapture s_neutral = new Neutral();
        static IRoomPlanCapture s_current;

        /// <summary>Never null. With nothing registered: not available, no capture.</summary>
        public static IRoomPlanCapture Current { get { return s_current ?? s_neutral; } }

        /// <summary>Called by the provider assembly from a BeforeSceneLoad method. Null removes the registration.</summary>
        public static void Register(IRoomPlanCapture capture)
        {
            s_current = capture;
        }

        /// <summary>
        /// RoomPlan's coaching instruction (RoomPlanCapture.Instruction) as the line shown to the walker. RoomPlan
        /// gives one instruction at a time; those that have a line are, most important first: turnOnLight,
        /// slowDown, moveAwayFromWall, moveCloseToWall. normal, lowTexture (no line for it), "" and anything
        /// unknown give None.
        /// </summary>
        public static ScanHint Hint(string instruction)
        {
            switch (instruction)
            {
                case "turnOnLight": return ScanHint.TurnOnLight;
                case "slowDown": return ScanHint.SlowDown;
                case "moveAwayFromWall": return ScanHint.MoveAwayFromWall;
                case "moveCloseToWall": return ScanHint.MoveCloseToWall;
                default: return ScanHint.None;
            }
        }
    }

    /// <summary>
    /// Route 4's switch, the parameter scan.wallBridge (standalone-phone plan D13): RoomPlan's walls, doors and
    /// windows from the scan's RoomPlan session are merged into the confirmed room (WallBridgeMerge). Off, the room
    /// keeps the walls of its outline. The RoomPlan session itself runs either way when boxes are on; this only
    /// decides whether its walls are used.
    /// </summary>
    public static class WallBridge
    {
        /// <summary>On by default (D13). Read when a confirmed room is finalised.</summary>
        public static bool Enabled = true;

        /// <summary>Sets Enabled from the parameter. The RoomPlan plugin is not asked: availability decides whether a capture runs, not this.</summary>
        public static void Apply(bool parameterOn)
        {
            Enabled = parameterOn;
        }
    }

    /// <summary>
    /// One scan's RoomPlan session, for the scan session: started with the scan features, read while the walker
    /// sweeps (objects for route 3, the instruction for the hint line), ended when the scan's features go off,
    /// and its walls merged into the room the walker confirms (route 4). Where there is no plugin every call does
    /// nothing, Latest stays empty and the room keeps the walls of its outline. Main thread only.
    /// </summary>
    public sealed class RoomPlanSession
    {
        IRoomPlanCapture _running;
        string _json = "";
        RoomPlanCapture _latest = new RoomPlanCapture();
        bool _readable;

        /// <summary>A capture is running.</summary>
        public bool Running { get { return _running != null; } }

        /// <summary>
        /// What the capture saw at the last Refresh (or End), in Unity's AR session frame. Never null: empty before
        /// the first file, after Begin and after the walls were applied. A file that could not be read leaves the
        /// one before it here.
        /// </summary>
        public RoomPlanCapture Latest { get { return _latest; } }

        /// <summary>The text of the newest file taken, "" when none.</summary>
        public string Json { get { return _json; } }

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
        /// Starts a capture and forgets the last one. False when the plugin is not available here, there is no
        /// session or the capture does not start.
        /// </summary>
        public bool Begin(IntPtr nativeSession)
        {
            End();
            Forget();
            IRoomPlanCapture plugin = RoomPlanPlugin.Current;
            if (nativeSession == IntPtr.Zero || !plugin.Available) return false;
            if (!plugin.Start(nativeSession)) return false;
            _running = plugin;
            return true;
        }

        /// <summary>
        /// Takes the newest file from the running capture and reads it when it changed. Returns Latest. Does
        /// nothing when no capture runs.
        /// </summary>
        public RoomPlanCapture Refresh()
        {
            if (_running != null) Take(_running);
            return _latest;
        }

        /// <summary>Ends the capture and keeps what it saw last for ApplyWalls. Does nothing when none runs.</summary>
        public void End()
        {
            IRoomPlanCapture plugin = _running;
            if (plugin == null) return;
            Take(plugin);
            _running = null;
            plugin.Stop();
        }

        /// <summary>
        /// Ends a running capture and merges the walls, doors and windows it saw into a room (WallBridgeMerge),
        /// when the switch scan.wallBridge is on (WallBridge.Enabled); then forgets the capture either way: what
        /// was seen belongs to that one room. The room is unchanged, and the report's Tried false, when the switch
        /// is off or nothing was captured; Error is set when the last file could not be read.
        /// </summary>
        public WallBridgeReport ApplyWalls(RoomModel room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            End();
            var report = new WallBridgeReport();
            if (WallBridge.Enabled && _json.Length > 0)
            {
                if (_readable) report = WallBridgeMerge.Merge(room, _latest);
                else report = new WallBridgeReport { Tried = true, Error = "not an atlas.room/1 file" };
            }
            Forget();
            return report;
        }

        void Take(IRoomPlanCapture plugin)
        {
            string json = plugin.TakeJson() ?? "";
            if (json.Length == 0 || string.Equals(json, _json, StringComparison.Ordinal)) return;
            _json = json;
            RoomPlanCapture capture;
            _readable = RoomPlanJson.TryRead(json, out capture);
            if (_readable) _latest = capture;
        }

        void Forget()
        {
            _json = "";
            _latest = new RoomPlanCapture();
            _readable = false;
        }
    }
}

using System;
using System.Collections.Generic;
using Atlas.Blades.Playground.Shell;
using Atlas.Kernel;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Blades.Playground.Tests.ShellFlow
{
    /// <summary>Stand-ins for the ports and the room content of the shell. None touches an engine object.</summary>
    static class ShellWalkFakes
    {
        public sealed class FakeView : IRoomObjectView
        {
            public RoomObject Obj;
            public MeshState State = MeshState.Placeholder;
            public int ObjectId { get { return Obj.Id; } }
            public RoomObject Data { get { return Obj; } }
            public MeshState Shown { get { return State; } }
            public UnityEngine.Transform Transform { get { return null; } }
            public UnityEngine.Renderer Renderer { get { return null; } }
            public UnityEngine.Material Material { get { return null; } }
            public int TriangleCount { get { return 12; } }
        }

        public sealed class FakeContent : IRoomContent
        {
            public readonly Dictionary<int, FakeView> Views = new Dictionary<int, FakeView>();
            RoomModel m_Room;
            public bool HideViews;

            public RoomModel Room { get { return m_Room; } }
            public bool IsBuilt { get { return m_Room != null; } }

            public void Build(RoomModel room)
            {
                m_Room = room;
                Views.Clear();
                foreach (RoomObject o in room.Objects) Views[o.Id] = new FakeView { Obj = o };
                if (Built != null) Built(room);
            }

            public void Clear() { m_Room = null; Views.Clear(); }
            public UnityEngine.Transform Root { get { return null; } }
            public WallMode Walls { get { return WallMode.Full; } }
            public void SetWallMode(WallMode mode, float seconds = 0.5f) { }
            public float KerbHeight { get; set; }
            public bool FloorVisible { get; set; }
            public bool ObjectsVisible { get; set; }
            public void ResetPresentation() { }
            public UnityEngine.Renderer FloorRenderer { get { return null; } }
            public UnityEngine.Renderer WallRenderer { get { return null; } }
            public UnityEngine.Material ShellMaterial { get { return null; } }
            public int ShellTriangleCount { get { return 0; } }
            public int ObjectCount { get { return m_Room != null ? m_Room.Objects.Length : 0; } }
            public IRoomObjectView GetObject(int index) { return Views[m_Room.Objects[index].Id]; }

            public bool TryGetObject(int objectId, out IRoomObjectView view)
            {
                FakeView v;
                bool ok = !HideViews && Views.TryGetValue(objectId, out v);
                view = ok ? Views[objectId] : null;
                return ok;
            }

            public bool SetObjectState(int objectId, MeshState state)
            {
                if (!Views.ContainsKey(objectId)) return false;
                Views[objectId].State = state;
                if (ObjectChanged != null) ObjectChanged(objectId);
                return true;
            }

            public bool SetGeneratedMesh(int objectId, UnityEngine.Mesh mesh, string assetHash, float fadeSeconds = 0.3f) { return false; }
            public FurnitureLevel FurnitureLevel { get; set; }
            public bool SetGeneratedLightMesh(int objectId, UnityEngine.Mesh mesh, string assetHashLight) { return false; }
            public bool SetBuiltInMesh(int objectId, UnityEngine.Mesh sharedMesh, UnityEngine.Vector3 scale, float fadeSeconds = 0.3f) { return false; }
            public event Action<RoomModel> Built;
            public event Action<int> ObjectChanged;
        }

        public sealed class FakeDial : IDialPort
        {
            public int Q { get; private set; } = 4;
            public int Calls;
            public bool Set(int q) { Calls++; if (q == Q) return false; Q = q; return true; }
            public bool Next() { Calls++; return Q < 6 && SetQuiet(Q + 1); }
            public bool Prev() { Calls++; return Q > 3 && SetQuiet(Q - 1); }
            public bool Cycle() { Calls++; return SetQuiet(Q == 6 ? 3 : Q + 1); }
            bool SetQuiet(int q) { Q = q; return true; }
        }

        /// <summary>A walk port that replays a script of events and numbers, one per Update.</summary>
        public sealed class FakeWalk : IWalkPort
        {
            public struct Step { public WalkEvent Event; public double Distance; public double Turn; }

            public readonly Queue<Step> Script = new Queue<Step>();
            public int ArmCalls;
            public int ArmedQ;
            public double ArmedKappa, ArmedScale, ArmedSide;
            public bool StillSeen;

            public bool Armed { get; private set; }
            public bool Started { get; private set; }
            public bool Closed { get; private set; }
            public int Q { get; private set; }
            public int Corners { get; private set; }
            public double DistanceToFlagM { get; private set; } = double.NaN;
            public double WorldTurnDeg { get; private set; }
            public int NextBeacon { get { return !Armed || Closed ? -1 : (Started ? Corners + 1 : 0); } }
            public double NextBeaconDistanceM { get; set; } = double.NaN;
            public double NextBeaconBearingRad { get; set; } = double.NaN;

            public void Arm(int q, double kappa, double lengthScaleM, double sideM)
            {
                ArmCalls++;
                ArmedQ = q; ArmedKappa = kappa; ArmedScale = lengthScaleM; ArmedSide = sideM;
                Q = q; Armed = true; Started = false; Closed = false; Corners = 0;
                DistanceToFlagM = double.NaN; WorldTurnDeg = 0;
            }

            public void Disarm() { Armed = false; Started = false; Closed = false; Corners = 0; }

            public WalkEvent Update(bool still)
            {
                StillSeen = still;
                if (!Armed || Script.Count == 0) return WalkEvent.None;
                Step s = Script.Dequeue();
                DistanceToFlagM = s.Distance;
                WorldTurnDeg = s.Turn;
                switch (s.Event)
                {
                    case WalkEvent.Started: Started = true; break;
                    case WalkEvent.Corner: Corners++; break;
                    case WalkEvent.Closed: Corners++; Closed = true; break;
                }
                return s.Event;
            }

            /// <summary>Queues a full loop: started, q - 1 corners, closed, with the closure and the turn.</summary>
            public void QueueLoop(int q, double closureM, double turnDeg)
            {
                Script.Enqueue(new Step { Event = WalkEvent.Started, Distance = 0.1 });
                for (int k = 1; k < q; k++) Script.Enqueue(new Step { Event = WalkEvent.Corner, Distance = 1.0 * k, Turn = 0 });
                Script.Enqueue(new Step { Event = WalkEvent.Closed, Distance = closureM, Turn = turnDeg });
            }
        }

        public sealed class FakeMode : IModePort
        {
            public CurvedMode Mode { get; private set; } = CurvedMode.CurvedWalk;
            public TileId Tile { get; set; } = new TileId(1, 2, 3, 4, 5, 6);
            public bool ChangeTileOnSwitch;
            public bool Request(CurvedMode mode)
            {
                if (mode == Mode) return false;
                Mode = mode;
                if (ChangeTileOnSwitch) Tile = TileId.Origin;
                return true;
            }
        }

        public sealed class FakeBeacon : IHomeBeaconPort
        {
            public bool Enabled { get; set; }
            public double ShownDistanceM { get; set; }
            public double KernelDistanceM { get; set; }
        }

        public sealed class FakeMap : IMiniMapPort
        {
            public bool Visible { get; set; }
        }

        public sealed class RecordingLog : IEventLog
        {
            public readonly List<string> Events = new List<string>();
            public readonly List<string> Json = new List<string>();
            public void Log(string ev) { Events.Add(ev); Json.Add("{}"); }
            public void Log(string ev, string key, double value) { Events.Add(ev); Json.Add(key); }
            public void Log(string ev, string key, string value) { Events.Add(ev); Json.Add(key); }
            public void Log(string ev, string key1, double value1, string key2, double value2) { Events.Add(ev); Json.Add(key1); }
            public void LogJson(string ev, string dataJson) { Events.Add(ev); Json.Add(dataJson); }
            public int Count(string ev) { int n = 0; foreach (string e in Events) if (e == ev) n++; return n; }
        }

        public static HeadPose Pose(double time, float x, float z, TrackingState tracking = TrackingState.Normal)
        {
            return new HeadPose(time, new float3(x, 1.6f, z), quaternion.identity, tracking);
        }

        /// <summary>Feeds a head standing at (x, z) for the given seconds at 60 Hz, starting at t0. Returns the end time.</summary>
        public static double Stand(HeadStillness still, double t0, double seconds, float x = 0f, float z = 0f)
        {
            double t = t0;
            for (; t <= t0 + seconds + 1e-9; t += 1.0 / 60.0) still.Update(Pose(t, x, z));
            return t;
        }

        /// <summary>Feeds a head walking along +x at the given speed for the given seconds at 60 Hz.</summary>
        public static double Walk(HeadStillness still, double t0, double seconds, float speed, float x0 = 0f)
        {
            double t = t0;
            for (; t <= t0 + seconds + 1e-9; t += 1.0 / 60.0) still.Update(Pose(t, x0 + speed * (float)(t - t0), 0f));
            return t;
        }
    }
}

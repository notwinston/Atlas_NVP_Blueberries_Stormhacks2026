using System;
using System.Collections.Generic;
using System.IO;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Rendering.Room;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells.Tests
{
    // Test support: lane C's fixture rooms (World/Resources/AtlasRooms) and the recorded walks (recordings/), read engine-free.
    static class Fixtures
    {
        public static string ProjectDir =>
            Environment.GetEnvironmentVariable(RoomFixtures.ProjectDirVariable) ?? Directory.GetCurrentDirectory();

        public static RoomModel Room(string name) => RoomFixtures.LoadFromProject(name);

        public static PoseLog Walk(string name)
        {
            using (var reader = new StreamReader(Path.Combine(ProjectDir, "..", "recordings", name + ".poselog")))
                return PoseLog.Read(reader);
        }

        public static string ProjectText(string assetPath) => File.ReadAllText(Path.Combine(ProjectDir, assetPath));

        // A room from a free rectangle alone: walls `margin` outside it, the derived values from the room rules.
        public static RoomModel Rectangle(float wf, float df, float margin = 0.3f, params RoomObject[] objects)
        {
            float x = 0.5f * wf + margin, z = 0.5f * df + margin;
            var outline = new[] { new float2(-x, -z), new float2(x, -z), new float2(x, z), new float2(-x, z) };
            var room = new RoomModel { Id = "test", Outline = outline, Walls = new RoomWall[4], FreeRect = new FreeRect(wf, df), Objects = objects };
            for (int k = 0; k < 4; k++) room.Walls[k] = new RoomWall { A = outline[k], B = outline[(k + 1) % 4], Height = 2.4f };
            room.ApplyLoopFit();
            return room;
        }

        public static RoomObject Obj(int id, float px, float pz, float sx, float sy, float sz, float yaw, int triangles = 12) => new RoomObject
        { Id = id, Category = ObjectCategory.Other, Base = new float3(px, 0, pz), Size = new float3(sx, sy, sz), Yaw = yaw, Mesh = MeshState.Placeholder, TriTarget = triangles };
    }

    // What S3 asks of the room content, recorded; no engine object. The shell's triangle counts are the real shell builder's.
    sealed class FakeRoomContent : IRoomContent
    {
        sealed class View : IRoomObjectView
        {
            public RoomObject Object;
            public int ObjectId => Object.Id;
            public RoomObject Data => Object;
            public MeshState Shown => Object.Mesh;
            public UnityEngine.Transform Transform => null;
            public UnityEngine.Renderer Renderer => null;
            public UnityEngine.Material Material => null;
            // The real room content's own rule: 5,000 for a stand-in, the box for a placeholder, the triangle target for a mesh.
            public int TriangleCount => RoomObjectMeshes.Triangles(Object);
        }

        public readonly List<string> Calls = new List<string>();
        readonly List<View> views = new List<View>();
        RoomModel room;
        WallMode walls = WallMode.Kerbs;
        bool floor = true, objects;
        int floorTriangles, wallTriangles;

        public FakeRoomContent(RoomModel room = null) { if (room != null) Build(room); }

        public RoomModel Room => room;
        public bool IsBuilt => room != null;
        public void Build(RoomModel model)
        {
            room = model; views.Clear();
            foreach (RoomObject o in model.Objects) views.Add(new View { Object = o });
            var data = new AtlasMeshBuilder();
            RoomShellBuilder.Floor(model, data);
            floorTriangles = data.TriangleCount;
            RoomShellBuilder.Walls(model, floorTriangles, data, out _);
            wallTriangles = data.TriangleCount - floorTriangles;
            Calls.Add("Build");
            Built?.Invoke(model);
        }
        public void Clear() { room = null; views.Clear(); Calls.Add("Clear"); }
        public UnityEngine.Transform Root => null;
        public WallMode Walls => walls;
        public float LastSeconds = -1f;
        public void SetWallMode(WallMode mode, float seconds = 0.5f) { walls = mode; LastSeconds = seconds; Calls.Add("SetWallMode:" + mode); }
        public float KerbHeight { get; set; }
        public bool FloorVisible { get => floor; set { floor = value; Calls.Add("FloorVisible:" + value); } }
        public bool ObjectsVisible { get => objects; set { objects = value; Calls.Add("ObjectsVisible:" + value); } }
        public void ResetPresentation() { walls = WallMode.Full; floor = true; objects = true; Calls.Add("ResetPresentation"); }
        public UnityEngine.Renderer FloorRenderer => null;
        public UnityEngine.Renderer WallRenderer => null;
        public UnityEngine.Material ShellMaterial => null;
        public int ShellTriangleCount => (floor ? floorTriangles : 0) + (walls == WallMode.Full ? wallTriangles : 0);
        public int ShellDrawCalls => (floor ? 1 : 0) + (walls == WallMode.Hidden ? 0 : 1);
        public int ObjectCount => views.Count;
        public IRoomObjectView GetObject(int index) => views[index];
        public bool TryGetObject(int objectId, out IRoomObjectView view)
        {
            view = views.Find(v => v.ObjectId == objectId);
            return view != null;
        }
        public bool SetObjectState(int objectId, MeshState state) { Calls.Add("SetObjectState"); return false; }
        // The asset client's swap: the state and the triangle count change, the object and its place do not.
        public bool SetGeneratedMesh(int objectId, UnityEngine.Mesh mesh, string assetHash, float fadeSeconds = 0.3f)
            => Swap(objectId, MeshState.Ready, assetHash);
        public FurnitureLevel FurnitureLevel { get; set; }
        public bool SetGeneratedLightMesh(int objectId, UnityEngine.Mesh mesh, string assetHashLight) => false;
        public bool SetBuiltInMesh(int objectId, UnityEngine.Mesh sharedMesh, UnityEngine.Vector3 scale, float fadeSeconds = 0.3f)
            => Swap(objectId, MeshState.BuiltIn, "");
        bool Swap(int objectId, MeshState state, string hash)
        {
            View v = views.Find(x => x.ObjectId == objectId);
            if (v == null) return false;
            v.Object.Mesh = state; v.Object.AssetHash = hash;
            Calls.Add("Swap:" + objectId);
            ObjectChanged?.Invoke(objectId);
            return true;
        }
        public event Action<RoomModel> Built;
        public event Action<int> ObjectChanged;
    }

    // Test rig: the engine-free core on a room, with the room driver on a recording room content, walked as the frame loop
    // walks it (one head sample per frame, step 5 then step 6).
    sealed class Rig
    {
        public readonly RoomModel Room;
        public readonly ParamRegistry Registry = new ParamRegistry();
        public readonly AlignedCellsCore Core = new AlignedCellsCore();
        public readonly FakeRoomContent Content;
        public readonly AlignedCellsRoomView RoomView;
        public double X, Z;
        public CellGrid Grid => Core.Grid;

        public Rig(RoomModel room, int2? startCell = null, WalkerPose? start = null)
        {
            Room = room;
            Core.Configure(room, Registry);
            Content = new FakeRoomContent(room);
            RoomView = new AlignedCellsRoomView(Content, Core.Grid);
            RoomView.Enter();
            double2 c = Grid.CellCentre(startCell ?? new int2(0, 0));
            X = c.x; Z = c.y;
            Core.Enter(start ?? WalkerPose.Start, new double2(X, Z));
            RoomView.Apply(Core.Instances);
        }

        public void Step(bool rejected = false)
        {
            if (Core.Tick(new double2(X, Z), rejected) || RoomView.Dirty) RoomView.Apply(Core.Instances);
        }

        // Straight line in 0.05 m steps, ending exactly at (x, z).
        public void MoveTo(double x, double z, Action perFrame = null)
        {
            double dx = x - X, dz = z - Z, len = Math.Sqrt(dx * dx + dz * dz);
            int n = Math.Max(1, (int)Math.Ceiling(len / 0.05));
            double x0 = X, z0 = Z;
            for (int i = 1; i <= n; i++) { X = x0 + dx * i / n; Z = z0 + dz * i / n; Step(); perFrame?.Invoke(); }
        }

        public void GoCell(int cx, int cz, Action perFrame = null)
        {
            double2 c = Grid.CellCentre(new int2(cx, cz));
            MoveTo(c.x, c.y, perFrame);
        }
    }
}

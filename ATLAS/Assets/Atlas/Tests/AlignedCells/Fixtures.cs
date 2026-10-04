using System.IO;
using Atlas.World;
using UnityEngine;

namespace Atlas.Spaces.AlignedCells.Tests
{
    // Test support: lane C's RoomModel recordings (Master Plan App. C.8; placeholders on laneB/placeholders).
    static class Fixtures
    {
        public static RoomModel Room(string name) =>
            RoomModel.Load(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "recordings", name + ".json")));
    }
}

namespace Atlas.Spaces.AlignedCells.Tests
{
    using System;
    using Atlas.Kernel;
    using Atlas.Link;
    using Unity.Mathematics;

    // Test rig: an AlignedCellsModule on a parent object with a Home double, driven directly (no SpaceLab.Tick, PH-4).
    sealed class Rig : IDisposable
    {
        public readonly GameObject Parent = new GameObject("S3Parent");
        public readonly Material Template = new Material(Shader.Find("Hidden/Atlas/Tests/AlignedCellsGates"));
        public readonly RoomModel Room;
        public TestHomeShell Shell;
        public bool HomeAvailable = true;
        public readonly AlignedCellsModule Module;
        public double X, Z;
        long frame;
        public CellGrid Grid => Module.Space.Grid;

        public Rig(RoomModel room, TestHomeShell.Mode mode = TestHomeShell.Mode.Functional, int2? startCell = null, bool enter = true)
        {
            Room = room;
            Shell = new TestHomeShell(room, mode, 3000);
            Module = new AlignedCellsModule(new TileGraph(5), Parent.transform, Template, () => HomeAvailable ? Shell : null);
            Module.Configure(room, new ParamRegistry());
            if (!enter) return;
            double2 c = Grid.CellCentre(startCell ?? new int2(0, 0));
            X = c.x; Z = c.y;
            Module.Enter(new WalkerPose { Tile = TileId.Zero, Local = Iso2.Identity });
            Step();
        }

        public void Step()
        {
            var f = new FrameContext(frame, frame / 60.0, 1 / 60f, new float3((float)X, 1.6f, (float)Z), quaternion.identity, true, false);
            frame++;
            Module.Tick(f); Module.Upload(f);
        }

        // Straight line in 0.05 m steps (Tick and Upload per step), ending exactly at (x, z).
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

        public void Dispose()
        {
            Module.Dispose(); Shell.Destroy();
            UnityEngine.Object.DestroyImmediate(Template); UnityEngine.Object.DestroyImmediate(Parent);
        }
    }

    static class RoomEdit
    {
        public static RoomModel Clone(RoomModel r) => RoomModel.FromJson(r.ToJson());

        public static RoomObject Obj(int id, float px, float pz, float sx, float sy, float sz, float yaw) => new RoomObject
        { id = id, category = "test", position = new Vector3(px, 0, pz), size = new Vector3(sx, sy, sz), yaw = yaw, mesh = "placeholder", hash = "" };
    }
}

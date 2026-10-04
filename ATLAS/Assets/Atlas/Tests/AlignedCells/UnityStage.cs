#if !ATLAS_HEADLESS
using System;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Rendering.Room;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.AlignedCells.Tests
{
    // The real component on the real services, as SpaceLab drives it: a material pool, the room content built from a
    // fixture room, the module made by its registered factory. Editor only.
    sealed class UnityStage : IDisposable
    {
        public readonly GameObject RoomRoot = new GameObject("Atlas Room (test)");
        public readonly MaterialPool Pool = MaterialPool.CreateDefault(128);
        public readonly RoomContent Content;
        public readonly ParamRegistry Registry = new ParamRegistry();
        public readonly AlignedCellsSpace Space;
        public readonly RoomModel Room;
        public double X, Z;
        int frame;

        public Transform Root => Space.transform;
        public CellGrid Grid => Space.Core.Grid;

        public UnityStage(RoomModel room, bool buildRoom = true)
        {
            AtlasServices.Clear();
            Room = room;
            Content = new RoomContent(RoomRoot.transform, Pool) { Instant = true };
            if (buildRoom) Content.Build(room);
            AtlasServices.Register(Pool);
            AtlasServices.Register<IRoomContent>(Content);
            Space = (AlignedCellsSpace)AlignedCellsSpace.Create();
            Space.Configure(room, Registry);
        }

        public void Enter(in WalkerPose start, int2 cell)
        {
            double2 c = Grid.CellCentre(cell);
            X = c.x; Z = c.y;
            Space.Enter(in start);
            Step();
        }

        public void Step(bool rejected = false)
        {
            var f = new FrameContext
            {
                FrameIndex = frame, Time = frame / 60.0, DeltaTime = 1 / 60f,
                HeadPosition = new float3((float)X, 1.6f, (float)Z), HeadRotation = quaternion.identity,
                RawHeadPosition = new float3((float)X, 1.6f, (float)Z), RawHeadRotation = quaternion.identity,
                Tracking = TrackingState.Normal, Kernel = new KernelStep { Rejected = rejected, LastEdge = -1 },
            };
            frame++;
            Space.Tick(in f); Space.Upload(in f);
        }

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
            if (Space != null)
            {
                Space.Exit();
                UnityEngine.Object.DestroyImmediate(Space.gameObject);
            }
            Content.Dispose();
            UnityEngine.Object.DestroyImmediate(RoomRoot);
            Pool.Dispose();
            AtlasServices.Clear();
        }
    }
}
#endif

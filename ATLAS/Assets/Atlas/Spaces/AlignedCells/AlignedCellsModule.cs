using System;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.AlignedCells
{
    // Master Plan §6.5 ISpaceModule; §6.3 steps 5 and 6 (PH-3 IUploadStage). [P1-B12 D4] The adapter P1-B10 deferred.
    public sealed class AlignedCellsModule : ISpaceModule, IUploadStage, IDisposable
    {
        readonly TileGraph graph;
        readonly Material template;
        readonly Func<IHomeShell> home;
        WalkerPose start;
        bool configured, pendingEnter, dirty;

        public AlignedCellsModule(TileGraph graph, Transform parent, Material template, Func<IHomeShell> home)
        {
            this.graph = graph; this.template = template; this.home = home;
            var go = new GameObject("AlignedCells") { hideFlags = HideFlags.DontSave };
            Root = go.transform;
            Root.SetParent(parent, false);
            Space = new AlignedCellsSpace(graph);
        }

        public SpaceId Id => SpaceId.S3;
        public bool Registered => true;                       // §4.9
        public Transform Root { get; }
        public AlignedCellsSpace Space { get; }
        public AlignedCellsView View { get; private set; }
        public AlignedCellsRoomView RoomView { get; private set; }
        public int Crossings { get; private set; }            // cell edges crossed since Enter

        // §4.7 grid rule via CellGrid. A free rectangle with g < 0.4 m makes CellGrid throw ArgumentOutOfRangeException; documented, not caught.
        // registry unused: P1-B12 adds no S3 parameter.
        public void Configure(RoomModel room, ParamRegistry registry)
        {
            DisposeViews();
            Space.Configure(new FreeRectangle(room.freeRect.Wf, room.freeRect.Df));
            View = new AlignedCellsView(Root, template, Space.Grid);
            RoomView = null;
            configured = true; dirty = true;
        }

        // §4.8: same tile id. TurnFromCurvedWalk assumes the anchor axes equal the world axes (B10 review, X06 open point).
        public void Enter(in WalkerPose start)
        {
            if (!configured) throw new InvalidOperationException("AlignedCellsModule.Enter before Configure");
            this.start = start; pendingEnter = true; Crossings = 0;
            Root.gameObject.SetActive(true);
        }

        public void Tick(in FrameContext frame)
        {
            if (!configured) return;
            // PH-2 / A2: Unity world axes = RoomModel anchor frame.
            var head = new double2(frame.HeadPosition.x, frame.HeadPosition.z);
            if (pendingEnter)
            {
                Space.Enter(start, head);
                pendingEnter = false; dirty = true;
            }
            else
            {
                // [P1-B12 D12] mirrors AlignedCellsSpace.Tick (Walk then SightLines.Build) so the crossing count is available.
                int n = Space.Walker.Walk(head);
                if (n > 0)
                {
                    SightLines.Build(Space.Grid, graph, Space.Walker.Cell, Space.Walker.Frame, Space.Instances);
                    Crossings += n; dirty = true;
                }
            }
            if (RoomView == null)
            {
                var h = home();
                if (h != null) { RoomView = new AlignedCellsRoomView(Root, template, h, Space.Grid); dirty = true; }   // [P1-B12 D6]
            }
        }

        // [P1-B12 D11] re-uploads when the root moves: gate positions are world-space.
        public void Upload(in FrameContext frame)
        {
            if (!configured || Space.Walker == null) return;
            if (!dirty && !Root.hasChanged) return;
            View.Apply(Space.Instances);
            RoomView?.Apply(Space.Instances);
            Root.hasChanged = false; dirty = false;
        }

        public void Exit()
        {
            Space.Exit();
            Root.gameObject.SetActive(false);
        }

        // App. B.1; [P1-B12 D5]
        public void WriteState(ref StateMessage state)
        {
            if (!configured || Space.Walker == null) return;
            state.SpaceId = 3;
            TileId t = Space.Walker.Frame.Tile;
            if (state.TileId != null && state.TileId.Length >= 6)
            { state.TileId[0] = t.Xu; state.TileId[1] = t.Xv; state.TileId[2] = t.Yu; state.TileId[3] = t.Yv; state.TileId[4] = t.Tu; state.TileId[5] = t.Tv; }
            int2 c = Space.Walker.Cell;
            state.Sheet = (short)(c.y * Space.Grid.CountX + c.x);
            if (state.Xform != null && state.Xform.Length >= 9)
            {
                for (int i = 0; i < 9; i++) state.Xform[i] = 0;
                state.Xform[0] = state.Xform[4] = state.Xform[8] = 1;
            }
        }

        void DisposeViews()
        {
            RoomView?.Dispose(); RoomView = null;
            View?.Dispose(); View = null;
        }

        public void Dispose()
        {
            DisposeViews();
            if (Root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(Root.gameObject); else UnityEngine.Object.DestroyImmediate(Root.gameObject);
            }
        }
    }
}

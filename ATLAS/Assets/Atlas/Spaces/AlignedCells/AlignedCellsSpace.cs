using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.AlignedCells
{
    /// <summary>
    /// S3 Aligned Cells, Mode 2 (Master Plan sections 4.7 to 4.9, tasks P1-B10 and P1-B12): the free floor cut into
    /// flat square cells, each showing one tile of the {4,5} tiling; which tile depends on the path walked.
    /// Registered: the root is the identity of the room frame, the walls are the real walls and the furniture
    /// stands where it really is, drawn once by the shared room content.
    /// </summary>
    /// <remarks>
    /// A thin adapter. The logic is in <see cref="AlignedCellsCore"/>, the cell instances in
    /// <see cref="AlignedCellsView"/>, the room in <see cref="AlignedCellsRoomView"/>. The frame loop's kernel
    /// stays flat and registered here; entered from Curved Walk, SpaceLab hands over Mode 1's pose and the tile
    /// id is kept (section 4.8). The duck maze (S3 duck maze design, 2026-10-04): the view draws the core's maze
    /// (walls, off tiles, the duck from the kit), redrawn whenever the maze changes, and a head-locked
    /// <see cref="MazeHud"/> shows the run.
    /// </remarks>
    public sealed class AlignedCellsSpace : MonoBehaviour, ISpaceModule, IUploadStep, ISpaceStateReader
    {
        readonly AlignedCellsCore _core = new AlignedCellsCore();
        AlignedCellsView _view;
        AlignedCellsRoomView _roomView;
        MazeHud _hud;                                          // made at the first Configure, kept across reconfigures
        DuckMaze _shownMaze;                                   // the maze of the last Apply: a new one (a new run, P7, a reconfigure) redraws

        MaterialPool _pool;
        IRoomContent _room;
        ISpaceHost _host;
        IEventLog _events;
        RoomModel _roomModel;                                  // kept for the reconfigure when cells.maze changes while entered
        ParamRegistry _registry;

        WalkerPose _start;
        bool _entered, _pendingEnter, _dirty;

        /// <summary>The factory given to SpaceRegistry: a new object named like the scene.</summary>
        public static ISpaceModule Create()
        {
            return new GameObject(SpaceIds.SceneName(SpaceId.AlignedCells)).AddComponent<AlignedCellsSpace>();
        }

        /// <summary>The parameters of group "cells". Safe to call again.</summary>
        public static void DeclareParams(ParamRegistry registry)
        {
            AlignedCellsCore.DeclareParams(registry);
        }

        public SpaceId Id { get { return SpaceId.AlignedCells; } }
        public bool Registered { get { return true; } }

        /// <summary>The logic behind this component.</summary>
        public AlignedCellsCore Core { get { return _core; } }
        /// <summary>The cell instances, or null when there is no material pool.</summary>
        public AlignedCellsView View { get { return _view; } }
        /// <summary>The driver of the shared room content, or null until the room is built.</summary>
        public AlignedCellsRoomView RoomView { get { return _roomView; } }
        /// <summary>The maze read-out (no object without a stereo rig or TextMeshPro fonts), or null before Configure.</summary>
        public MazeHud Hud { get { return _hud; } }

        public void Configure(RoomModel room, ParamRegistry registry)
        {
            ReleaseViews();
            _roomModel = room;
            _registry = registry;
            _core.Configure(room, registry);
            AtlasServices.TryGet(out _room);
            AtlasServices.TryGet(out _host);
            AtlasServices.TryGet(out _events);
            _core.Events = _events;                            // cells.cross and cells.postLoop (Master Plan §10.1.3)
            if (_hud == null)
            {
                // The read-out hangs under the head-locked HUD root, as Curved Walk's; no rig (the Station, a test): no object.
                AtlasServices.TryGet(out IStereoRig rig);
                _hud = new MazeHud(rig != null ? rig.Hud : null);
            }
            if (!AtlasServices.TryGet(out _pool)) return;     // no look in this build: nothing to draw with
            // The duck modelled in Blender (kit piece "duck"; one AtlasKit for the whole app, read as Glued reads it), once per
            // Configure and only when a maze can be drawn. No kit, or a refused piece: the view draws the procedural duck.
            KitSet kit = _core.MazeMode ? KitSet.Load(ReadKitResource) : null;
            _view = new AlignedCellsView(transform, _pool, _core.Grid, kit);
            _dirty = true;
        }

        public void Enter(in WalkerPose start)
        {
            // Registered: the module's root is the room frame itself (contract README F4).
            Transform root = transform;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            _start = start;
            _entered = true;
            TakeRoom();
            // The walker stands in the cell under the head. The loop's last frame gives the head now; without a
            // loop (a test, the Station) the first Tick brings it.
            if (AtlasServices.TryGet(out IFrameLoop loop)) Place(loop.Current.HeadPosition);
            else _pendingEnter = true;
            gameObject.SetActive(true);
        }

        public void ReadState(in StateMessage state)
        {
            _core.ReadState(in state);
        }

        public void Tick(in FrameContext frame)
        {
            if (!_entered) return;
            var head = new double2(frame.HeadPosition.x, frame.HeadPosition.z);
            if (_pendingEnter) Place(frame.HeadPosition);
            if (_core.Tick(head, frame.Kernel.Rejected, frame.Time)) _dirty = true;   // also true when a new maze run began
            if (_core.NeedsReconfigure) Reconfigure(frame.HeadPosition);
            if (_hud != null) ShowRun(_hud, _core);
            if (_roomView == null) TakeRoom();                // the room content was built after Enter
            if (_core.PollModeSwitch() && (_host == null || !_host.RequestSwitch(SpaceId.CurvedWalk))) _core.RefuseModeSwitch();
        }

        /// <summary>
        /// Step 6. Re-uploads when the instances, the maze, the room content or the root changed: gate posts are world-space.
        /// Then, every frame a duck is drawn, it turns and bobs (Apply places the ducks, Animate moves them: in that order).
        /// </summary>
        public void Upload(in FrameContext frame)
        {
            if (!_entered || !_core.Entered) return;
            DuckMaze maze = ShownMaze(_core);
            // The core's Tick reports a new run; this also catches a maze changed by any other path (a Station's P7, a reconfigure).
            if (!ReferenceEquals(maze, _shownMaze)) _dirty = true;
            bool roomDirty = _roomView != null && _roomView.Dirty;
            if (_dirty || roomDirty || transform.hasChanged)
            {
                if (_view != null) _view.Apply(_core.Instances, maze);
                if (_roomView != null) _roomView.Apply(_core.Instances);
                _shownMaze = maze;
                transform.hasChanged = false;
                _dirty = false;
            }
            if (_view != null && _view.DuckViewCount > 0) _view.Animate(frame.Time, _core.MazeFound, _core.MazeFoundTime);
        }

        /// <summary>The maze the cells show: the core's in maze mode, none otherwise (classic S3, a Station copy without one).</summary>
        internal static DuckMaze ShownMaze(AlignedCellsCore core)
        {
            return core.MazeMode ? core.Maze : null;
        }

        /// <summary>
        /// The read-out follows the run, every frame: while searching the live time (the core's MazeElapsed runs until the
        /// find and is frozen there), then the toast with the steps until the next run; hidden without a maze. MazeHud
        /// rebuilds its text only when what it shows changes, so this allocates nothing per frame.
        /// </summary>
        internal static void ShowRun(MazeHud hud, AlignedCellsCore core)
        {
            if (ShownMaze(core) != null) hud.Show(core.MazeElapsed, core.MazeSteps, core.MazeWallsCrossed, core.MazeFound);
            else hud.Hide();
        }

        public void Exit()
        {
            _entered = false;
            _pendingEnter = false;
            if (_hud != null) _hud.Hide();
            if (_roomView != null) _roomView.Exit();
            _roomView = null;
            _core.Exit();
            ReleaseViews();
        }

        public void WriteState(ref StateMessage state)
        {
            _core.WriteState(ref state);
        }

        void OnDestroy()
        {
            // Exit has returned the materials already; this only covers a root destroyed without Exit.
            if (_roomView != null) _roomView.Exit();
            _roomView = null;
            ReleaseViews();
            if (_hud != null) _hud.Dispose();
            _hud = null;
        }

        void Place(float3 head)
        {
            _pendingEnter = false;
            _core.Enter(in _start, new double2(head.x, head.z));
            _dirty = true;
            if (_events != null)
                _events.Log("cells.enter", "cells", _core.Grid.CountX * _core.Grid.CountZ, "fromMode1", _core.EnteredFromCurvedWalk ? 1 : 0);
        }

        // cells.maze changed while entered (duck maze design §2): the grid changes, so the space is configured again and
        // re-entered with the head of this frame, as a fresh entry is (cells.enter is logged again).
        void Reconfigure(float3 head)
        {
            if (_roomView != null) _roomView.Exit();
            _roomView = null;
            Configure(_roomModel, _registry);
            Enter(in _start);
            if (_pendingEnter) Place(head);
        }

        void TakeRoom()
        {
            if (_room == null || !_room.IsBuilt || !_core.Configured) return;
            _roomView = new AlignedCellsRoomView(_room, _core.Grid);
            _roomView.Enter();
            _dirty = true;
        }

        void ReleaseViews()
        {
            if (_view != null) _view.Dispose();
            _view = null;
            _shownMaze = null;
        }

        // GluedSpace's reader: the kit's files are TextAssets in Resources/AtlasKit (Spaces/Glued/Resources/AtlasKit).
        static byte[] ReadKitResource(string name)
        {
            var asset = Resources.Load<TextAsset>(KitSet.ResourceFolder + "/" + name);
            if (asset == null) return null;
            byte[] bytes = asset.bytes;
            Resources.UnloadAsset(asset);
            return bytes;
        }
    }
}

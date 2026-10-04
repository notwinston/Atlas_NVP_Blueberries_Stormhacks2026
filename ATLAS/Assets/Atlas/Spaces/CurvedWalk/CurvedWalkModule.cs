// P1-B09 / P1-B11: S2 as an ISpaceModule (Master Plan §6.5) driven by the frame loop (§6.3 steps 4, 5, 6); no Update().
using System;
using Atlas.Input;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.CurvedWalk
{
    public sealed class CurvedWalkModule : ISpaceModule, IKernelStage, IUploadStage
    {
        readonly CurvedWalkSpace space = new CurvedWalkSpace();
        GameObject root;
        TileRenderer tiles;
        TileGraph tilesGraph;
        double tilesL;
        CurvedRoomView roomView;
        LapMarkerView markers;
        RoomModel room;

        public CurvedWalkModule() { space.SpaceRequested += id => SpaceRequested?.Invoke(id); }

        public SpaceId Id => SpaceId.S2;
        public bool Registered => false;   // MP §4.9: S2 starts at the room's registration but is not a registered space

        public CurvedWalkSpace Space => space;
        public Material SharedMaterial { get; set; }   // Atlas/AtlasSurface, set by the host (the scene component)
        public IHomeShell Home { get; set; }           // SpaceLab.Home
        public IActionSource Actions { get; set; }     // lane C's action map
        public event Action<SpaceId> SpaceRequested;   // the host wires this to SpaceLab.Request
        public TileRenderer Tiles => tiles;
        public CurvedRoomView RoomView => roomView;
        public LapMarkerView Markers => markers;

        public void Configure(RoomModel roomModel, ParamRegistry registry)
        {
            room = roomModel;
            space.Configure(roomModel, registry);
        }

        public void Enter(in WalkerPose start)
        {
            if (SharedMaterial == null) throw new InvalidOperationException("CurvedWalkModule.SharedMaterial is not set");
            space.Enter();
            // [B09 D18, A2] the module owns its view objects; the root sits at the identity transform: world = anchor frame
            root = new GameObject("S2_CurvedWalk") { hideFlags = HideFlags.DontSave };
            tiles = root.AddComponent<TileRenderer>();
            ConfigureTiles(space.Resolve(5).LengthScaleM);
            if (Home != null) roomView = new CurvedRoomView(root.transform, SharedMaterial, Home, room.walls);
            markers = new LapMarkerView(root.transform, SharedMaterial);
            markers.Visible = false;
        }

        void ConfigureTiles(double l)
        {
            tiles.Configure(space.Graph, l, SharedMaterial);   // B06 S1 (the generator's content is not drawn by B06, deviation)
            tilesGraph = space.Graph; tilesL = l;
        }

        // Step 4: the kernel step, then B06's draw-rule selection with the pose and the (ramping) L.
        public void KernelStep(in FrameContext f)
        {
            space.KernelStep(f);
            if (tiles == null) return;
            if (!ReferenceEquals(tilesGraph, space.Graph)) ConfigureTiles(space.Rig.Curvature != 0 ? space.Rig.LengthScale : space.Resolve(space.SelectedQ).LengthScaleM);
            if (space.Rig.Curvature != 0)
            {
                double l = space.Rig.LengthScale;
                if (l != tilesL) { tiles.SetLengthScale(l); tilesL = l; }   // L changes every frame in a ramp; the shells are rebuilt (S2)
                tiles.CullRho = space.CullRho;
                tiles.KernelStep(space.Rig.Pose, f);
            }
        }

        public void Tick(in FrameContext f) => space.Tick(f, Actions);

        // Step 6
        public void Upload(in FrameContext f)
        {
            if (tiles == null) return;
            var rig = space.Rig;
            double kappa = rig.Kappa, l = kappa == 0 ? 1.0 : rig.LengthScale;
            bool curved = rig.Curvature != 0;
            if (tiles.gameObject.activeSelf != curved) tiles.gameObject.SetActive(curved);   // S3: hidden at K = 0 (OnDisable restores the fog)
            if (curved) tiles.Upload(f);
            Iso2 u = rig.Pose.Local;
            if (roomView != null)
            {
                bool draw = space.TileZeroFound;
                if (draw && curved)
                    draw = Geometry2.Distance(u.M.c2, space.TileZeroInCurrent.M.c2, kappa) <= space.CullRho + space.RoomRadiusM / l;   // [B09 D12]
                roomView.Upload(kappa, l, u, space.TileZeroInCurrent, draw);
            }
            markers.Visible = space.Walk.Armed;
            markers.Upload(space.Walk, space.TileZeroInCurrent, u, kappa, l, (Vector3)f.HeadPosition);
        }

        public void Exit()
        {
            space.Exit();
            markers?.Dispose(); markers = null;
            roomView?.Dispose(); roomView = null;
            if (root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(root); else UnityEngine.Object.DestroyImmediate(root);
            }
            root = null; tiles = null; tilesGraph = null;
        }

        public void WriteState(ref StateMessage state) => space.WriteState(ref state);
    }
}

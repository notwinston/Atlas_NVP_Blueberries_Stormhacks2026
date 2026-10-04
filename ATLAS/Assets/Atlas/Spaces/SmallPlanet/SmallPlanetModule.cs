// P1-B09: S4 Small Planet (q = 3) as an ISpaceModule (Master Plan §6.5, §3.2, §10.1.3). Driven by the frame loop; no Update().
// [B09 D2] There is no ramp in S4: the entry is a step change behind the switch fade, requested only while still.
// [B09 D17] No room geometry here: S4 shows the planet, the pole beacon and the lap markers.
using System;
using Atlas.Input;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.SmallPlanet
{
    public sealed class SmallPlanetModule : ISpaceModule, IKernelStage, IUploadStage
    {
        public const double StillSpeed = 0.1;   // m/s, MP §4.6 (3D head speed)

        readonly SmallPlanetSpace space = new SmallPlanetSpace();
        readonly LapWalk walk = new LapWalk();
        readonly CurvatureDial dial = new CurvatureDial(3);
        ParamRegistry registry;
        ushort qId;
        double radius, sideM;
        bool standing, armed, requestedS2, hasPrev, entering;
        double3 prevPos; double prevTime;
        LapEvent lastEvent;
        Iso2 f0 = new Iso2(double3x3.identity, 1);
        GameObject root;
        SmallPlanetView view;
        LapMarkerView markers;

        public SpaceId Id => SpaceId.S4;
        public bool Registered => false;   // MP §4.9
        public SmallPlanetSpace Space => space;
        public LapWalk Walk => walk;
        public double RadiusM => radius;
        public int SelectedQ { get; private set; } = 3;
        public bool LastStill { get; private set; }
        public Material SharedMaterial { get; set; }
        public IActionSource Actions { get; set; }
        public LapMarkerView Markers => markers;
        public event Action<SpaceId> SpaceRequested;   // the host wires this to SpaceLab.Request

        public void Configure(RoomModel room, ParamRegistry reg)
        {
            registry = reg;
            qId = CurvedDialParams.RegisterQ(reg);
            ushort strongId = CurvedDialParams.RegisterStrong(reg);
            bool strong = CurvedDialParams.Strong(reg, strongId);
            var c = room.curvature;
            radius = CurvatureDial.Target(3, c.L5, c.L6, c.R3, strong).LengthScaleM;
            sideM = room.lapSquare.s;
            standing = room.standing;
            space.Configure(radius);
        }

        public void Enter(in WalkerPose start)
        {
            if (SharedMaterial == null) throw new InvalidOperationException("SmallPlanetModule.SharedMaterial is not set");
            registry.Set(qId, 3);   // the dial reads 3 while we are here
            dial.Set(3); SelectedQ = 3;
            entering = true; requestedS2 = false; hasPrev = false; armed = false; LastStill = false; lastEvent = LapEvent.None;
            walk.Disarm();
            root = new GameObject("S4_SmallPlanet") { hideFlags = HideFlags.DontSave };
            view = root.AddComponent<SmallPlanetView>();
            view.Build(space, SharedMaterial);
            markers = new LapMarkerView(root.transform, SharedMaterial);
            markers.Visible = false;
        }

        public void KernelStep(in FrameContext f)
        {
            double3 pos = (double3)f.HeadPosition;
            LastStill = f.TrackingNormal && hasPrev && f.Time > prevTime && math.length(pos - prevPos) / (f.Time - prevTime) <= StillSpeed;
            prevPos = pos; prevTime = f.Time; hasPrev = true;
            if (entering)
            {
                space.Enter(SmallPlanetSpace.StartPose(new double2(pos.x, pos.z), radius));
                entering = false;
            }
            space.Tick(new PlanetHead(pos, f.TrackingNormal));
            f0 = CubeTiling.Chain(space.Walker.Face, 0);
            lastEvent = LapEvent.None;
            if (!standing)
            {
                if (!armed) { walk.Arm(3, +1, radius, sideM, TileZeroPlacement.LapFlag(sideM, +1, radius)); armed = true; }
                lastEvent = walk.Update(f0, space.Walker.U, LastStill);
            }
            if (SelectedQ != 3 && LastStill && !requestedS2) { requestedS2 = true; SpaceRequested?.Invoke(SpaceId.S2); }
        }

        public void Tick(in FrameContext f)
        {
            dial.Set(Math.Min(CurvatureDial.MaxQ, Math.Max(CurvatureDial.MinQ, (int)Math.Round(registry.Get(qId)))));
            var actions = Actions;
            if (actions != null && LastStill)
            {
                if (actions.Triggered(AtlasAction.DialPrev) && dial.Prev()) registry.Set(qId, dial.Q);
                if (actions.Triggered(AtlasAction.DialNext) && dial.Next()) registry.Set(qId, dial.Q);
            }
            SelectedQ = dial.Q;
            WalkEvents.Emit(walk, lastEvent, "S4");
            lastEvent = LapEvent.None;
        }

        public void Upload(in FrameContext f)
        {
            if (view == null) return;
            var head = (Vector3)f.HeadPosition;
            view.Upload(space, head);
            markers.Visible = walk.Armed;
            markers.Upload(walk, f0, space.Walker.U, 1.0, radius, head);
        }

        public void Exit()
        {
            space.Exit();
            walk.Disarm(); armed = false;
            markers?.Dispose(); markers = null;
            view = null;
            if (root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(root); else UnityEngine.Object.DestroyImmediate(root);
            }
            root = null;
        }

        // kernel.md §4.5: TileId[0] = face, other words 0; Xform = U row-major; P[0] = R
        public void WriteState(ref StateMessage st)
        {
            st.SpaceId = (byte)SpaceId.S4;
            for (int i = 0; i < 6; i++) st.TileId[i] = 0;
            st.TileId[0] = space.Walker.Face;
            double3x3 m = space.Walker.U.M;
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) st.Xform[3 * r + c] = m[c][r];
            st.P[0] = (float)radius;
        }
    }
}

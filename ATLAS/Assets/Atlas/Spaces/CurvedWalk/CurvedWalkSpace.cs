// P1-B09 / P1-B11: S2 Curved Walk, plain logic (the B08/B10 seam pattern: CurvedWalkModule adapts it to ISpaceModule).
// Master Plan §3.2 (dial), §4.6 (holonomy rig, registration), §10.1.3 / §10.1.4 (dial, 3-4-5 walk, ramp only while standing).
using System;
using System.Collections.Generic;
using System.Globalization;
using Atlas.Input;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.CurvedWalk
{
    public sealed class CurvedWalkSpace
    {
        public const uint WorldSeed = 1u;   // [B09 D14]
        public const string ParamQ = CurvedDialParams.ParamQ, ParamStrong = CurvedDialParams.ParamStrong;
        public const string ParamCullRho = "curved.cullRho";
        public const double DefaultCullRho = 3.0;   // MP §8.5

        RoomModel room;
        ParamRegistry registry;
        ushort qId, strongId;
        double s, l5, l6, r3;
        bool strong, entered, pending, hadCurvature, f0Valid, s4Requested;
        TileGraph graph5, graph6;
        readonly CurvatureDial dial = new CurvatureDial(5);
        readonly List<VisibleTile> visible = new List<VisibleTile>(2048);
        readonly List<double2> checkPoints = new List<double2>();
        LapEvent lastEvent;
        Iso2 f0 = Iso2.Identity;

        public event Action<SpaceId> SpaceRequested;   // the host wires this to SpaceLab.Request

        public HolonomyRig Rig { get; private set; }
        public TileGraph Graph { get; private set; }
        public WorldGenerator World { get; private set; }          // q = 5 world; null while Graph.Q == 6
        public RoomFootprint Footprint { get; private set; }
        public int SelectedQ { get; private set; } = 5;
        public DialTarget Target => Resolve(SelectedQ);
        public bool Standing { get; private set; }
        public LapWalk Walk { get; } = new LapWalk();
        public Iso2 TileZeroInCurrent => f0;
        public bool TileZeroFound { get; private set; }
        public bool LastStill { get; private set; }
        public double LastRegistrationErrorM { get; private set; }
        public int Reregistrations { get; private set; }
        public double RoomRadiusM { get; private set; }
        public double SideM => s;
        public bool Entered => entered;
        public bool RegistrationPending => pending;
        public RoomModel Room => room;
        public ParamRegistry Registry => registry;

        /// <summary>curved.cullRho when registered, else 3.0 (MP §8.5).</summary>
        public double CullRho => registry != null && registry.TryGetId(ParamCullRho, out ushort id) ? registry.Get(id) : DefaultCullRho;

        public DialTarget Resolve(int q) => CurvatureDial.Target(q, l5, l6, r3, strong);

        public void Configure(RoomModel roomModel, ParamRegistry reg)
        {
            room = roomModel ?? throw new ArgumentNullException(nameof(roomModel));
            registry = reg ?? throw new ArgumentNullException(nameof(reg));
            s = room.lapSquare.s; l5 = room.curvature.L5; l6 = room.curvature.L6; r3 = room.curvature.R3;
            Standing = room.standing;
            qId = CurvedDialParams.RegisterQ(registry);
            strongId = CurvedDialParams.RegisterStrong(registry);
            strong = CurvedDialParams.Strong(registry, strongId);
            double l5p = Resolve(5).LengthScaleM;
            Footprint = RoomFootprint.FromRoomModel(room, l5p);
            World = new WorldGenerator(WorldSeed, Footprint);   // the world uses the target L, never the ramping L
            graph5 = World.Graph;
            graph6 = null;
            RoomRadiusM = ComputeRoomRadius(room, Footprint);
        }

        static double ComputeRoomRadius(RoomModel room, RoomFootprint fp)
        {
            double r = 0;
            if (room.walls != null)
                foreach (var w in room.walls)
                {
                    r = math.max(r, math.length(new double2(w.a.x, w.a.z)));
                    r = math.max(r, math.length(new double2(w.b.x, w.b.z)));
                }
            if (room.objects != null)
                foreach (var o in room.objects)
                {
                    double c = Math.Cos(o.yaw), sn = Math.Sin(o.yaw);   // Unity yaw: +z turns towards +x
                    for (int i = 0; i < 4; i++)
                    {
                        double lx = (i & 1) == 0 ? -o.size.x / 2 : o.size.x / 2, lz = (i & 2) == 0 ? -o.size.z / 2 : o.size.z / 2;
                        double2 p = new double2(o.position.x + lx * c + lz * sn, o.position.z - lx * sn + lz * c);
                        r = math.max(r, math.length(p));
                    }
                }
            if (r == 0) foreach (var p in fp.Outline) r = math.max(r, math.length(p));
            return r;
        }

        public void Enter()
        {
            if (room == null) throw new InvalidOperationException("Enter before Configure");
            int q = ClampQ(registry.Get(qId));
            if (q == 3) { registry.Set(qId, 5); q = 5; }   // S2 has no q = 3: the dial hands that to S4 (D2)
            dial.Set(q); SelectedQ = q;
            Graph = graph5; World = World ?? new WorldGenerator(WorldSeed, Footprint);
            Rig = new HolonomyRig(Graph);
            pending = true; hadCurvature = false; f0Valid = false; s4Requested = false;
            f0 = Iso2.Identity; TileZeroFound = false; LastStill = false;
            LastRegistrationErrorM = 0; Reregistrations = 0; lastEvent = LapEvent.None;
            Walk.Disarm();
            entered = true;
        }

        static int ClampQ(float v) => Math.Min(CurvatureDial.MaxQ, Math.Max(CurvatureDial.MinQ, (int)Math.Round(v)));

        public void Exit()
        {
            entered = false; pending = false; hadCurvature = false;
            Walk.Disarm();
            TileZeroFound = false; lastEvent = LapEvent.None;
        }

        /// <summary>B11: Return to Home re-registers (MP §4.6). Behind the host's fade: the one curvature change not gated by stillness.</summary>
        public void ReturnHome()
        {
            pending = true;
            registry.Set(qId, 4);
            dial.Set(4); SelectedQ = 4;
            Walk.Disarm();
            s4Requested = false;
        }

        void Register(double2 head)
        {
            Rig.Reset(new WalkerPose { Tile = TileId.Zero, Local = TileZeroPlacement.Registered(head, 0, 1) }, 0, 1);
            f0Valid = false;
        }

        /// <summary>MP §6.3 step 4.</summary>
        public void KernelStep(in FrameContext f)
        {
            if (!entered) return;
            double2 head = new double2(f.HeadPosition.x, f.HeadPosition.z);
            if (pending) { Register(head); pending = false; hadCurvature = false; }
            HolonomyRigResult res = Rig.Step(f.Frame, new HeadSample(f.Frame, f.Time, (double3)f.HeadPosition, f.TrackingNormal));
            LastStill = res.Still;
            if (res.Crossings > 0) f0Valid = false;

            Control(head);
            UpdateTileZero();
            ArmAndUpdateWalk();
        }

        // Step 4, curvature control (brief §4): the rig ramps only while still; the dial only decides the target.
        void Control(double2 head)
        {
            int q = SelectedQ;
            bool landed = Rig.Curvature == 0 && Rig.TargetCurvature == 0;
            if (landed)
            {
                if (hadCurvature)
                {
                    // (i) landing snap [D3]: error measured before re-registering (pitfall 11)
                    LastRegistrationErrorM = RegistrationError(head);
                    Register(head);
                    Reregistrations++;
                    hadCurvature = false;
                }
                if (q == 3)
                {
                    if (LastStill && !s4Requested) { s4Requested = true; SpaceRequested?.Invoke(SpaceId.S4); }   // (ii)
                }
                else if ((q == 5 || q == 6) && Graph.Q != q)
                {
                    // (iii) the tiling is swapped only at K = 0 [D3]
                    if (q == 6) { graph6 = graph6 ?? new TileGraph(6); Graph = graph6; World = null; }
                    else { Graph = graph5; World = World ?? new WorldGenerator(WorldSeed, Footprint); }
                    Rig = new HolonomyRig(Graph);
                    Register(head);
                }
                else if (q == 5 || q == 6)
                {
                    Rig.SetCurvatureTarget(-1, Resolve(q).LengthScaleM);   // (iv)
                }
            }
            else
            {
                bool wantCurved = (q == 5 || q == 6) && Graph.Q == q;
                double want = wantCurved ? -1.0 / (Resolve(q).LengthScaleM * Resolve(q).LengthScaleM) : 0.0;
                if (want != Rig.TargetCurvature) Rig.SetCurvatureTarget(wantCurved ? -1 : 0, wantCurved ? Resolve(q).LengthScaleM : 1.0);
            }
            if (Rig.Curvature != 0) hadCurvature = true;
        }

        double RegistrationError(double2 head)
        {
            if (!Rig.Pose.Tile.Equals(TileId.Zero)) return double.PositiveInfinity;
            Iso2 uInv = Rig.Pose.Local.Inverse();
            double worst = 0;
            if (room.walls != null)
                foreach (var w in room.walls)
                {
                    worst = math.max(worst, Err(uInv, head, new double2(w.a.x, w.a.z)));
                    worst = math.max(worst, Err(uInv, head, new double2(w.b.x, w.b.z)));
                }
            if (room.objects != null)
                foreach (var o in room.objects) worst = math.max(worst, Err(uInv, head, new double2(o.position.x, o.position.z)));
            return worst;
        }

        static double Err(in Iso2 uInv, double2 head, double2 x)
        {
            double3 v = math.mul(uInv.M, new double3(x, 1));
            return math.length(head + v.xy - x);
        }

        void UpdateTileZero()
        {
            if (Rig.Curvature == 0)
            {
                f0 = Iso2.Identity; TileZeroFound = Rig.Pose.Tile.Equals(TileId.Zero); f0Valid = false;
                return;
            }
            if (f0Valid) return;
            if (Rig.Pose.Tile.Equals(TileId.Zero)) { f0 = new Iso2(double3x3.identity, -1); TileZeroFound = true; f0Valid = true; return; }
            double l = Rig.LengthScale;
            Graph.VisibleSet(Rig.Pose.Tile, 3.5 + RoomRadiusM / l + 0.05, visible);   // reused list, no allocation once warm
            TileZeroFound = false;
            for (int i = 0; i < visible.Count; i++)
                if (visible[i].Id.Equals(TileId.Zero)) { f0 = visible[i].F; TileZeroFound = true; break; }
            f0Valid = true;
        }

        void ArmAndUpdateWalk()
        {
            int q = SelectedQ;
            bool settled = Rig.Curvature == Rig.TargetCurvature;
            bool cond = !Standing && settled && ((q == 4 && Rig.Curvature == 0) || ((q == 5 || q == 6) && Graph.Q == q && Rig.Curvature != 0));
            lastEvent = LapEvent.None;
            if (cond && !Walk.Armed)
            {
                double kappa = Rig.Kappa, l = kappa == 0 ? 1.0 : Rig.LengthScale;
                Walk.Arm(q, kappa, l, s, TileZeroPlacement.LapFlag(s, kappa, l));
            }
            else if (!cond && Walk.Armed) Walk.Disarm();
            if (Walk.Armed && TileZeroFound) lastEvent = Walk.Update(f0, Rig.Pose.Local, LastStill);
        }

        /// <summary>MP §6.3 step 5: dial input (locked while moving, MP §10.1.4) and the walk.* events (MP §10.1.3, §11.2).</summary>
        public void Tick(in FrameContext f, IActionSource actionsOrNull)
        {
            if (!entered) return;
            dial.Set(ClampQ(registry.Get(qId)));
            if (actionsOrNull != null && LastStill)
            {
                if (actionsOrNull.Triggered(AtlasAction.DialPrev) && dial.Prev()) registry.Set(qId, dial.Q);
                if (actionsOrNull.Triggered(AtlasAction.DialNext) && dial.Next()) registry.Set(qId, dial.Q);
            }
            SelectedQ = dial.Q;
            EmitWalkEvents(Walk, lastEvent, "S2");
            lastEvent = LapEvent.None;
        }

        /// <summary>Shared with S4: the three event names of MP §10.1.3.</summary>
        public static void EmitWalkEvents(LapWalk walk, LapEvent ev, string scene)
        {
            if (ev != LapEvent.Corner && ev != LapEvent.Closed) return;
            var c = CultureInfo.InvariantCulture;
            string flag = walk.DistanceToFlagM.ToString("F3", c), turn = walk.WorldTurnDeg.ToString("F2", c);
            EventLog.Emit(scene, "walk.corner", "{\"q\":" + walk.Q + ",\"corner\":" + walk.Corners + ",\"flag_m\":" + flag + ",\"turn_deg\":" + turn + "}");
            if (walk.Corners % 4 == 0)
                EventLog.Emit(scene, "walk.lap", "{\"q\":" + walk.Q + ",\"laps\":" + (walk.Corners / 4) + ",\"flag_m\":" + flag + ",\"turn_deg\":" + turn + "}");
            if (ev == LapEvent.Closed)
                EventLog.Emit(scene, "walk.closed", "{\"q\":" + walk.Q + ",\"corners\":" + walk.Corners + ",\"closure_m\":" + flag + ",\"turn_deg\":" + turn + "}");
        }

        /// <summary>kernel.md §4.5 / MP App. B.1: SpaceId 2, tile id words, Xform = Local.M row-major, P[0] = L when curved else 0, P[1..2] = rho - 1, rho.</summary>
        public void WriteState(ref StateMessage st)
        {
            if (Rig == null) return;
            st.SpaceId = (byte)SpaceId.S2;
            TileId t = Rig.Pose.Tile;
            st.TileId[0] = t.Xu; st.TileId[1] = t.Xv; st.TileId[2] = t.Yu; st.TileId[3] = t.Yv; st.TileId[4] = t.Tu; st.TileId[5] = t.Tv;
            double3x3 m = Rig.Pose.Local.M;
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) st.Xform[3 * r + c] = m[c][r];
            st.P[0] = Rig.Curvature != 0 ? (float)Rig.LengthScale : 0f;
            double rho = CullRho;
            st.P[1] = (float)(rho - 1); st.P[2] = (float)rho;
        }
    }
}

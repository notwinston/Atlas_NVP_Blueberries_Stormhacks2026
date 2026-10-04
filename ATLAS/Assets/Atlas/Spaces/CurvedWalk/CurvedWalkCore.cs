// P1-B09 / P1-B11: S2 Curved Walk, plain logic (CurvedWalkSpace adapts it to the engine). It drives the frame loop's one
// walker kernel through IDialKernel and keeps no rig of its own: the kernel is stepped at step 4, this runs at step 5.
// Master Plan §3.2 (dial), §4.6 (holonomy rig, registration), §10.1.3 / §10.1.4 (dial, 3-4-5 walk, ramp only while standing).
using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.CurvedWalk
{
    /// <summary>What the walker asked for in one frame: the real input actions and their gaze twins, already merged.</summary>
    public struct CurvedWalkInput
    {
        /// <summary>AtlasAction.DialLeft / DialRight fired this frame.</summary>
        public bool DialLeft, DialRight;
        /// <summary>AtlasAction.Primary fired this frame: Mode 1 to Mode 2.</summary>
        public bool ModeToggle;
    }

    public sealed class CurvedWalkCore
    {
        public const uint WorldSeed = 1u;   // [B09 D14]
        public const int GeneratorVersion = 1;
        public const string ParamQ = CurvedDialParams.ParamQ, ParamStrong = CurvedDialParams.ParamStrong;
        /// <summary>The kill switch of Master Plan §12.4, declared by the app: 0 = Mode 1 Curved Walk, 1 = Mode 2 Aligned Cells.</summary>
        public const string ParamMode = "curved.mode";
        /// <summary>State.p[3] is the dial snap, p[4] the signed curvature of this frame in 1/m².</summary>
        public const int StateQ = 3, StateCurvature = 4;

        RoomModel room;
        ParamRegistry registry;
        ParamEntry qEntry, strongEntry, modeEntry;
        IDialKernel kernel;
        IEventLog log;
        double s;
        bool strong, entered, pending, hadCurvature, f0Valid, s4Requested;
        int modeAtEnter;
        readonly CurvatureDial dial = new CurvatureDial(5);
        readonly List<VisibleTile> visible = new List<VisibleTile>(2048);
        LapEvent lastEvent;
        Iso2 f0 = Iso2.Identity;
        // The Station's copy (ISpaceStateReader): the phone's State sets the kernel and the dial; nothing is driven here.
        bool remote;
        int remoteQ;
        TileId remoteTile;

        /// <summary>ISpaceHost.RequestSwitch, set by the module. Null: requests go nowhere.</summary>
        public Func<SpaceId, bool> RequestSwitch { get; set; }
        /// <summary>ISpaceHost.BumpWorldVersion, set by the module: q, L or the generated world changed.</summary>
        public Action WorldChanged { get; set; }

        public IDialKernel Kernel => kernel;
        public WorldGenerator World { get; private set; }          // the {4,5} world; null while the kernel's q is 6
        public RoomFootprint Footprint { get; private set; }
        /// <summary>WorldGenerator.WorldHash(6) of the world in force as 16 hexadecimal digits; empty at q = 6.</summary>
        public string ContentHash { get; private set; } = "";
        /// <summary>Counts the tiling swaps and world rebuilds, so the views know when to configure again.</summary>
        public int WorldRevision { get; private set; }
        public int SelectedQ { get; private set; } = 5;
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
        public bool Remote => remote;
        public bool RegistrationPending => pending;
        public RoomModel Room => room;
        public ParamRegistry Registry => registry;

        /// <summary>R (q = 3), 1 (q = 4, metres) or L (q = 5, 6): the room's loop-fit value after the comfort floor unless strong (MP §4.4).</summary>
        public double LengthScale(int q) => q == 4 ? 1.0 : room.Curvature.ForDial(q, strong);

        /// <summary>The dial's two parameters, in the app's manifest from boot. Safe to call again.</summary>
        public static void DeclareParams(ParamRegistry registry)
        {
            CurvedDialParams.RegisterQ(registry);
            CurvedDialParams.RegisterStrong(registry);
        }

        public void Configure(RoomModel roomModel, ParamRegistry reg, IEventLog eventLog = null)
        {
            room = roomModel ?? throw new ArgumentNullException(nameof(roomModel));
            registry = reg ?? throw new ArgumentNullException(nameof(reg));
            log = eventLog;
            s = room.LapSide;
            Standing = room.Standing;
            qEntry = CurvedDialParams.RegisterQ(registry);
            strongEntry = CurvedDialParams.RegisterStrong(registry);
            modeEntry = registry.Find(ParamMode);   // the app's; absent in a build without it
            strong = CurvedDialParams.Strong(strongEntry);
            BuildWorld();
        }

        // The world uses the target L, never the ramping L.
        void BuildWorld()
        {
            Footprint = RoomFootprint.FromRoomModel(room, LengthScale(5));
            World = new WorldGenerator(WorldSeed, Footprint);
            ContentHash = World.WorldHash(6).ToString("x16");
            RoomRadiusM = ComputeRoomRadius(room, Footprint);
            WorldRevision++;
        }

        static double ComputeRoomRadius(RoomModel room, RoomFootprint fp)
        {
            double r = 0;
            foreach (var w in room.Walls)
            {
                r = math.max(r, math.length((double2)w.A));
                r = math.max(r, math.length((double2)w.B));
            }
            foreach (var o in room.Objects)
            {
                double c = Math.Cos(o.Yaw), sn = Math.Sin(o.Yaw);   // Unity yaw: +z turns towards +x
                for (int i = 0; i < 4; i++)
                {
                    double lx = (i & 1) == 0 ? -o.Size.x / 2 : o.Size.x / 2, lz = (i & 2) == 0 ? -o.Size.z / 2 : o.Size.z / 2;
                    double2 p = new double2(o.Base.x + lx * c + lz * sn, o.Base.z - lx * sn + lz * c);
                    r = math.max(r, math.length(p));
                }
            }
            if (r == 0) foreach (var p in fp.Outline) r = math.max(r, math.length(p));
            return r;
        }

        /// <summary>
        /// Puts the app's kernel on the dial's tiling. The walker is registered to the room at K = 0 by Register, or by the
        /// first Tick when the head is not known yet; the ramp to the dial's curvature then runs while the walker stands.
        /// </summary>
        public void Enter(IDialKernel dialKernel)
        {
            if (room == null) throw new InvalidOperationException("Enter before Configure");
            kernel = dialKernel ?? throw new ArgumentNullException(nameof(dialKernel));
            int q = ClampQ(qEntry.Int);
            if (q == 3) { qEntry.Set(5); q = 5; }   // S2 has no q = 3: the dial hands that to S4 (D2)
            dial.Set(q); SelectedQ = q;
            ConfigureKernel(q == 6 ? 6 : 5);
            kernel.ResetToFloorFlat(double2.zero);   // K = 0 from the first moment; Register moves the walker under the head
            pending = true; hadCurvature = false; f0Valid = false; s4Requested = false;
            f0 = Iso2.Identity; TileZeroFound = false; LastStill = false;
            LastRegistrationErrorM = 0; Reregistrations = 0; lastEvent = LapEvent.None;
            modeAtEnter = modeEntry != null ? modeEntry.Int : 0;
            Walk.Disarm();
            remote = false;
            entered = true;
        }

        static int ClampQ(int v) => Math.Min(CurvatureDial.MaxQ, Math.Max(CurvatureDial.MinQ, v));

        // The tiling in force is the kernel's: {4,5} with the generated world, or {4,6} with bare tiles.
        void ConfigureKernel(int q)
        {
            kernel.Configure(KernelConfig.Curved(q, LengthScale(q)));
            if (q == 6) { World = null; ContentHash = ""; WorldRevision++; }
            else if (World == null || Footprint.L != LengthScale(5)) BuildWorld();
            else WorldRevision++;
        }

        public void Exit()
        {
            entered = false; pending = false; hadCurvature = false; remote = false;
            Walk.Disarm();
            TileZeroFound = false; lastEvent = LapEvent.None;
            kernel = null;   // SpaceLab puts it back to flat
        }

        /// <summary>Tile zero, K = 0, the frame a translation by the head's floor point: the room is where it really is (MP §4.6).</summary>
        public void Register(double2 head)
        {
            if (!entered) return;
            kernel.ResetToFloorFlat(head);
            pending = false; hadCurvature = false; f0Valid = false;
        }

        /// <summary>
        /// MP §6.3 step 5, after the frame loop stepped the kernel with this frame's head: curvature control, tile zero, the
        /// walk, then the dial input (locked while moving, MP §10.1.4) and the walk.* events (MP §10.1.3, §11.2).
        /// </summary>
        public void Tick(in FrameContext f, in CurvedWalkInput input)
        {
            if (!entered) return;
            if (remote) { Follow(); return; }
            double2 head = new double2(f.HeadPosition.x, f.HeadPosition.z);
            if (pending) Register(head);
            LastStill = kernel.Still;
            if (kernel.LastStep.Crossings > 0) f0Valid = false;

            Control(head);
            UpdateTileZero();
            ArmAndUpdateWalk();

            dial.Set(ClampQ(qEntry.Int));
            if (LastStill)
            {
                if (input.DialLeft && dial.Prev()) qEntry.Set(dial.Q);
                if (input.DialRight && dial.Next()) qEntry.Set(dial.Q);
            }
            SelectedQ = dial.Q;
            ModeSwitch(input.ModeToggle);
            WalkReadout.Emit(log, Walk, lastEvent);
            lastEvent = LapEvent.None;
        }

        /// <summary>The Station's side of WriteState. The Spectator's loop has put the kernel on the State's tiling, curvature
        /// and pose before this; from here on this copy reads the kernel and drives nothing. A State of another space is ignored.</summary>
        public void ReadState(in StateMessage st)
        {
            if (!entered || st.SpaceId != (byte)SpaceId.CurvedWalk) return;
            float q = st.GetParam(StateQ);
            remoteQ = q >= CurvatureDial.MinQ && q <= CurvatureDial.MaxQ ? (int)q : SelectedQ;
            remote = true;
            pending = false;
        }

        // Step 5 on the Station: no curvature control, no dial input, no mode switch and no walk.* events. The world follows
        // the kernel's tiling as Control would have swapped it, at K = 0 for a comfort-floor change; the walker never stands
        // as far as the Station can tell, so its lap does not advance.
        void Follow()
        {
            dial.Set(remoteQ); SelectedQ = remoteQ;
            LastStill = false;
            WalkerPose pose = kernel.Pose;
            if (!pose.Tile.Equals(remoteTile)) { remoteTile = pose.Tile; f0Valid = false; }
            if (kernel.CurvatureNow == 0) strong = CurvedDialParams.Strong(strongEntry);
            if (kernel.Q == 6)
            {
                if (World != null) { World = null; ContentHash = ""; WorldRevision++; }
            }
            else if (kernel.Q == 5 && (World == null || Footprint.L != LengthScale(5))) BuildWorld();
            UpdateTileZero();
            ArmAndUpdateWalk();
            lastEvent = LapEvent.None;
        }

        // Mode 1 to Mode 2 (MP §4.8): the walker's button, or the operator's kill switch moving to Mode 2. SpaceLab keeps the
        // tile id and the frame when the walker stands in the {4,5} world.
        void ModeSwitch(bool toggled)
        {
            int mode = modeEntry != null ? modeEntry.Int : 0;
            bool asked = mode != modeAtEnter && mode == 1;
            modeAtEnter = mode;
            if ((asked || (toggled && LastStill)) && RequestSwitch != null) RequestSwitch(SpaceId.AlignedCells);
        }

        // Curvature control (brief §4): the kernel ramps only while still; the dial only decides the target.
        void Control(double2 head)
        {
            int q = SelectedQ;
            bool hyperbolic = q == 5 || q == 6;
            // A comfort-floor change gives another L and another footprint: taken up like a tiling swap, at K = 0.
            bool stale = hyperbolic && (kernel.Q != q || CurvedDialParams.Strong(strongEntry) != strong);
            bool landed = kernel.CurvatureNow == 0 && kernel.CurvatureTarget == 0;
            if (landed)
            {
                if (hadCurvature)
                {
                    // (i) landing snap [D3]: error measured before re-registering (pitfall 11)
                    LastRegistrationErrorM = RegistrationError(head);
                    kernel.ResetToFloorFlat(head);
                    f0Valid = false;
                    Reregistrations++;
                    hadCurvature = false;
                }
                if (q == 3)
                {
                    if (LastStill && !s4Requested) { s4Requested = true; RequestSwitch?.Invoke(SpaceId.SmallPlanet); }   // (ii)
                }
                else if (stale)
                {
                    // (iii) the tiling is swapped only at K = 0 [D3]
                    strong = CurvedDialParams.Strong(strongEntry);
                    ConfigureKernel(q);
                    kernel.ResetToFloorFlat(head);
                    f0Valid = false;
                    WorldChanged?.Invoke();
                }
                else if (hyperbolic)
                {
                    kernel.SetCurvatureTarget(Curvature.Hyperbolic, LengthScale(q));   // (iv)
                }
            }
            else if (hyperbolic && !stale) kernel.SetCurvatureTarget(Curvature.Hyperbolic, LengthScale(q));
            else kernel.SetCurvatureTarget(Curvature.Flat, 0);
            if (q != 3) s4Requested = false;
            if (kernel.CurvatureNow != 0) hadCurvature = true;
        }

        double RegistrationError(double2 head)
        {
            WalkerPose pose = kernel.Pose;
            if (!pose.Tile.Equals(TileId.Zero)) return double.PositiveInfinity;
            Iso2 uInv = pose.Local.Inverse();
            double worst = 0;
            foreach (var w in room.Walls)
            {
                worst = math.max(worst, Err(uInv, head, w.A));
                worst = math.max(worst, Err(uInv, head, w.B));
            }
            foreach (var o in room.Objects) worst = math.max(worst, Err(uInv, head, new double2(o.Base.x, o.Base.z)));
            return worst;
        }

        static double Err(in Iso2 uInv, double2 head, double2 x)
        {
            double3 v = math.mul(uInv.M, new double3(x, 1));
            return math.length(head + v.xy - x);
        }

        void UpdateTileZero()
        {
            WalkerPose pose = kernel.Pose;
            if (kernel.CurvatureNow == 0)
            {
                f0 = Iso2.Identity; TileZeroFound = pose.Tile.Equals(TileId.Zero); f0Valid = false;
                return;
            }
            if (f0Valid) return;
            if (pose.Tile.Equals(TileId.Zero)) { f0 = new Iso2(double3x3.identity, -1); TileZeroFound = true; f0Valid = true; return; }
            double l = kernel.MetresPerUnit;
            kernel.Graph.VisibleSet(pose.Tile, 3.5 + RoomRadiusM / l + 0.05, visible);   // reused list, no allocation once warm
            TileZeroFound = false;
            for (int i = 0; i < visible.Count; i++)
                if (visible[i].Id.Equals(TileId.Zero)) { f0 = visible[i].F; TileZeroFound = true; break; }
            f0Valid = true;
        }

        void ArmAndUpdateWalk()
        {
            int q = SelectedQ;
            double k = kernel.CurvatureNow;
            bool settled = !kernel.Ramping;
            bool cond = !Standing && settled && ((q == 4 && k == 0) || ((q == 5 || q == 6) && kernel.Q == q && k != 0));
            lastEvent = LapEvent.None;
            if (cond && !Walk.Armed)
            {
                double kappa = kernel.Kappa, l = kappa == 0 ? 1.0 : kernel.MetresPerUnit;
                Walk.Arm(q, kappa, l, s, TileZeroPlacement.LapFlag(s, kappa, l));
            }
            else if (!cond && Walk.Armed) Walk.Disarm();
            if (Walk.Armed && TileZeroFound) lastEvent = Walk.Update(f0, kernel.Pose.Local, LastStill);
        }

        /// <summary>
        /// The frame loop has already written the tile id, Local, p[0] = metres per unit and p[1..2] = rho - 1, rho from the
        /// shared kernel (kernel.md §4.5). The space adds what the kernel does not say: the dial snap and the signed curvature.
        /// </summary>
        public void WriteState(ref StateMessage st)
        {
            if (kernel == null) return;
            st.SetParam(StateQ, SelectedQ);
            st.SetParam(StateCurvature, (float)kernel.CurvatureNow);
        }
    }
}

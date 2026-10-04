using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    /// <summary>The kernel of every dial position the frame loop can step: the HolonomyRig on a {4,5} or {4,6}
    /// TileGraph (kappa = -1), the SphereWalker on the six faces of the cube (q = 3, kappa = +1), and the
    /// FlatWalkerKernel for q = 4. Engine-free; Step allocates only when the graph meets tiles it has not held before.</summary>
    /// <remarks>
    /// Freshly constructed it is a FlatWalkerKernel in every respect. Configure with KernelConfig.Curved(q, L)
    /// puts the rig (q = 5, 6) or the sphere walker (q = 3) behind the same interface (Master Plan sections 4.6
    /// and 6.3 step 4). It is the app's one walker: a curved space configures it on Enter, drives the dial through
    /// IDialKernel, and SpaceLab puts it back to flat after Exit.
    /// </remarks>
    public sealed class HolonomyWalkerKernel : IDialKernel
    {
        private const int CandidateCapacity = 1024;
        private const int MaxResetCrossings = 64;

        private readonly FlatWalkerKernel _flat = new FlatWalkerKernel();
        private readonly List<VisibleTile> _candidates = new List<VisibleTile>(CandidateCapacity);
        private readonly CubeTileGraph _cube = new CubeTileGraph();
        private readonly SphereWalker _sphere = new SphereWalker();
        private TileGraph _graph5, _graph6;
        private TileGraph _graph;       // null unless q = 5 or 6
        private HolonomyRig _rig;       // null unless q = 5 or 6
        private int _q = 4;
        private double _configuredL = KernelConstants.FlatMetresPerUnit;    // L (or R) of the last curved Configure
        private double _cullRho = KernelConstants.DefaultCullRho;
        private KernelStep _lastStep;
        private long _stepCount;
        private long _frame;
        private double _time;
        private double2 _previous;
        private bool _hasPrevious;
        private bool _candidatesStale;
        private bool _hasStepped;       // a Step since the last placement: the next one has a displacement and a duration
        private bool _still;
        private bool _curvatureChanged;

        public HolonomyWalkerKernel()
        {
            ClearLastStep();
        }

        /// <summary>The rig while q is 5 or 6; null at q = 3 and 4. For tests: a space drives it through IDialKernel.</summary>
        public HolonomyRig Rig { get { return _rig; } }

        public int Q { get { return _q; } }

        public ITileGraph Graph
        {
            get
            {
                if (_rig != null) return _graph;
                return _q == 3 ? (ITileGraph)_cube : _flat.Graph;
            }
        }

        public double Kappa
        {
            get
            {
                if (_rig != null) return _rig.Kappa;
                return _q == 3 ? Curvature.Spherical : _flat.Kappa;
            }
        }

        public double MetresPerUnit
        {
            get
            {
                if (_rig == null) return _q == 3 ? _configuredL : _flat.MetresPerUnit;
                // Mid-ramp the rig may stand at kappa = 0, where its length unit is the metre.
                return _rig.Kappa == Curvature.Flat ? KernelConstants.FlatMetresPerUnit : _rig.LengthScale;
            }
        }

        public double ConfiguredMetresPerUnit { get { return _q == 4 ? _flat.MetresPerUnit : _configuredL; } }

        public double CurvatureNow
        {
            get
            {
                if (_rig != null) return _rig.Curvature;
                return _q == 3 ? 1.0 / (_configuredL * _configuredL) : 0.0;
            }
        }

        public double CurvatureTarget { get { return _rig != null ? _rig.TargetCurvature : CurvatureNow; } }
        public bool Ramping { get { return _rig != null && _rig.Curvature != _rig.TargetCurvature; } }
        public bool Still { get { return _still; } }
        public bool CurvatureChanged { get { return _curvatureChanged; } }

        public double CullRho
        {
            get { return _cullRho; }
            set
            {
                _flat.CullRho = value;      // validates
                _cullRho = value;
                _candidatesStale = true;
            }
        }

        public WalkerPose Pose
        {
            get
            {
                if (_rig != null) return _rig.Pose;
                return _q == 3 ? _sphere.Pose : _flat.Pose;
            }
        }

        public List<VisibleTile> Candidates { get { return _q == 4 ? _flat.Candidates : _candidates; } }
        public KernelStep LastStep { get { return _q == 4 ? _flat.LastStep : _lastStep; } }
        public long StepCount { get { return _stepCount; } }

        public void Configure(in KernelConfig config)
        {
            // OfQ rejects a q that is no tiling at all.
            double kappa = Curvature.OfQ(config.Q);
            if (kappa == Curvature.Flat)
            {
                _flat.Configure(in config);     // refuses a bad config before any state changes
                _rig = null;
                _graph = null;
                _q = 4;
                ClearDial();
                return;
            }
            if (!(config.MetresPerUnit > 0.0) || double.IsInfinity(config.MetresPerUnit))
            {
                throw new ArgumentOutOfRangeException(nameof(config), config.MetresPerUnit,
                    "Atlas.Kernel: MetresPerUnit must be finite and positive.");
            }

            if (kappa == Curvature.Spherical)
            {
                _rig = null;
                _graph = null;
                _q = 3;
                _configuredL = config.MetresPerUnit;
                // The start of the sphere is the centre of face 0, whose id is six zeros (Appendix B.1).
                WalkerPose start;
                start.Tile = CubeTiling.FaceId(0);
                start.Local = Iso2.Identity;
                PlaceOnSphere(start);
                return;
            }

            // A TileGraph never evicts, so one per q is kept across dial changes.
            TileGraph graph;
            if (config.Q == 5) graph = _graph5 ?? (_graph5 = new TileGraph(5));
            else graph = _graph6 ?? (_graph6 = new TileGraph(6));

            _graph = graph;
            _rig = new HolonomyRig(graph);
            _q = config.Q;
            _configuredL = config.MetresPerUnit;
            Place(WalkerPose.Start);
        }

        public void Reset(in WalkerPose start)
        {
            if (_rig != null) Place(start);
            else if (_q == 3) PlaceOnSphere(start);
            else
            {
                _flat.Reset(in start);
                ClearDial();
            }
        }

        public void ResetToFloor(double2 floorMetres)
        {
            if (_q == 4)
            {
                _flat.ResetToFloor(floorMetres);
                ClearDial();
                return;
            }
            if (!IsFinite(floorMetres))
            {
                throw new ArgumentOutOfRangeException(nameof(floorMetres), "Atlas.Kernel: the floor point must be finite.");
            }

            WalkerPose pose;
            if (_q == 3)
            {
                // Face 0 is the room's tile: its centre on the RoomModel anchor, unturned (Master Plan section 4.6).
                pose.Tile = CubeTiling.FaceId(0);
                pose.Local = TileZeroPlacement.Registered(floorMetres, Curvature.Spherical, _configuredL);
                PlaceOnSphere(pose);
                _sphere.SetPrevious(floorMetres);
                return;
            }

            // Standing flat with no ramp asked for, the walker is registered flat again: a recentre must not
            // snap the room into the curved world.
            if (_rig.Curvature == 0.0 && _rig.TargetCurvature == 0.0)
            {
                ResetToFloorFlat(floorMetres);
                return;
            }

            pose.Tile = TileId.Zero;
            pose.Local = TileZeroPlacement.Registered(floorMetres, Curvature.Hyperbolic, _configuredL);
            Place(pose);
            SetRigPrevious(floorMetres);
        }

        public void ResetToFloorFlat(double2 floorMetres)
        {
            if (_rig == null)
            {
                ResetToFloor(floorMetres);
                return;
            }
            if (!IsFinite(floorMetres))
            {
                throw new ArgumentOutOfRangeException(nameof(floorMetres), "Atlas.Kernel: the floor point must be finite.");
            }

            // At K = 0 the rig's unit is the metre and no edge is crossed: tile zero's chart is the room frame.
            WalkerPose pose;
            pose.Tile = TileId.Zero;
            pose.Local = TileZeroPlacement.Registered(floorMetres, Curvature.Flat, KernelConstants.FlatMetresPerUnit);
            _rig.Reset(in pose, Curvature.Flat, KernelConstants.FlatMetresPerUnit);
            ClearLastStep();
            ClearDial();
            RebuildCandidates();
            SetRigPrevious(floorMetres);
        }

        public bool SetCurvatureTarget(double kappa, double metresPerUnit)
        {
            if (kappa != Curvature.Flat && kappa != Curvature.Hyperbolic)
            {
                throw new ArgumentOutOfRangeException(nameof(kappa), kappa,
                    "Atlas.Kernel: the ramp runs between kappa = 0 and kappa = -1.");
            }
            if (kappa != Curvature.Flat && (!(metresPerUnit > 0.0) || double.IsInfinity(metresPerUnit)))
            {
                throw new ArgumentOutOfRangeException(nameof(metresPerUnit), metresPerUnit,
                    "Atlas.Kernel: MetresPerUnit must be finite and positive.");
            }
            if (_rig == null) return false;

            double target = kappa == Curvature.Flat ? 0.0 : -1.0 / (metresPerUnit * metresPerUnit);
            if (kappa != Curvature.Flat) _configuredL = metresPerUnit;
            // The same target again must not start the four seconds afresh.
            if (target != _rig.TargetCurvature)
                _rig.SetCurvatureTarget(kappa, kappa == Curvature.Flat ? KernelConstants.FlatMetresPerUnit : metresPerUnit);
            return true;
        }

        public bool Reaim(double alpha)
        {
            if (_rig != null) _rig.Reaim(alpha);
            else if (_q == 3) _sphere.Reaim(alpha);
            else return false;
            return true;
        }

        /// <summary>Step without a clock: the displacement is integrated, a curvature ramp does not advance.</summary>
        public KernelStep Step(double2 headFloorMetres, bool trackingNormal)
        {
            return Step(headFloorMetres, trackingNormal, _time, false);
        }

        /// <summary>Step 4 with the frame's time in seconds, which the rig needs to tell standing from walking
        /// (the curvature ramp only runs while the walker stands, Master Plan section 4.6).</summary>
        public KernelStep Step(double2 headFloorMetres, bool trackingNormal, double timeSeconds)
        {
            return Step(headFloorMetres, trackingNormal, timeSeconds, true);
        }

        private KernelStep Step(double2 headFloorMetres, bool trackingNormal, double timeSeconds, bool timed)
        {
            _stepCount++;
            double dt = timeSeconds - _time;
            _time = timeSeconds;
            _curvatureChanged = false;

            KernelStep result;
            if (_rig == null)
            {
                if (_q == 4) result = _flat.Step(headFloorMetres, trackingNormal);
                else
                {
                    _sphere.Step(headFloorMetres, trackingNormal);
                    result.Rejected = _sphere.LastRejected;
                    result.Crossings = _sphere.LastCrossings;
                    result.LastEdge = _sphere.LastEdge;
                    result.AppliedMetres = _sphere.LastApplied;
                    if (result.Crossings > 0 || _candidatesStale) RebuildCandidates();
                    _lastStep = result;
                }
                // Standing, as the rig judges it: a step that was integrated, slower than the still speed.
                _still = timed && _hasStepped && trackingNormal && !result.Rejected && dt > 0.0
                    && math.length(result.AppliedMetres) / dt <= HolonomyRig.StillSpeed;
                _hasStepped = true;
                return result;
            }

            _frame++;
            HolonomyRigResult r;
            if (!IsFinite(headFloorMetres))
            {
                // A broken sample is handed to the rig as a stale one: its previous head position is left as it was.
                r = _rig.Step(_frame, new HeadSample(_frame - 1, timeSeconds, double3.zero, false));
                result.Rejected = true;
            }
            else
            {
                double2 d = _hasPrevious ? headFloorMetres - _previous : double2.zero;
                _previous = headFloorMetres;
                _hasPrevious = true;
                result.Rejected = !trackingNormal || math.length(d) > HolonomyRig.JumpLimit;
                r = _rig.Step(_frame, new HeadSample(_frame, timeSeconds,
                    new double3(headFloorMetres.x, 0.0, headFloorMetres.y), trackingNormal));
            }

            result.Crossings = r.Crossings;
            result.LastEdge = r.LastEdge;
            result.AppliedMetres = r.Applied;
            _still = timed && r.Still;
            _curvatureChanged = r.CurvatureChanged;
            _hasStepped = true;

            if (r.Crossings > 0 || _candidatesStale) RebuildCandidates();

            _lastStep = result;
            return result;
        }

        /// <summary>Hands the pose to the rig at kappa = -1 and the configured L, re-rooted into the tile it lies in.
        /// A curvature ramp in progress is ended: the rig stands at the configured curvature again.</summary>
        private void Place(WalkerPose start)
        {
            // A flat-tagged rotation is an isometry of every geometry (kernel.md section 4.2), so the tag is
            // set here and the group residual is what decides.
            if (start.Tile.IsNone || start.Local.Kappa == Curvature.Spherical
                || !(new Iso2(start.Local.M, Curvature.Hyperbolic).Defect() < 1e-6))
            {
                throw new ArgumentException("Atlas.Kernel: the start frame is not a hyperbolic isometry.", nameof(start));
            }

            WalkerPose pose;
            pose.Tile = start.Tile;
            pose.Local = new Iso2(start.Local.M, Curvature.Hyperbolic);
            // Throws ArgumentException for an id that is no tile of the graph.
            _graph.Neighbour(pose.Tile, 0);
            for (int guard = 0; guard < MaxResetCrossings; guard++)
            {
                if (!_graph.Reroot(ref pose)) break;
            }

            _rig.Reset(in pose, Curvature.Hyperbolic, _configuredL);
            _hasPrevious = false;
            _previous = double2.zero;
            ClearLastStep();
            ClearDial();
            RebuildCandidates();
        }

        /// <summary>Hands the pose to the sphere walker at radius R, which re-roots it into the face it lies in.</summary>
        private void PlaceOnSphere(WalkerPose start)
        {
            if (CubeTiling.FaceOf(start.Tile) < 0)
            {
                throw new ArgumentException("Atlas.Kernel: the start tile is not a face of the q = 3 tiling.", nameof(start));
            }
            if (start.Local.Kappa == Curvature.Hyperbolic
                || !(new Iso2(start.Local.M, Curvature.Spherical).Defect() < 1e-6))
            {
                throw new ArgumentException("Atlas.Kernel: the start frame is not a spherical isometry.", nameof(start));
            }

            _sphere.Reset(in start, _configuredL);
            _hasPrevious = false;
            _previous = double2.zero;
            ClearLastStep();
            ClearDial();
            RebuildCandidates();
        }

        /// <summary>The point becomes the rig's previous head position: its first sample integrates nothing.</summary>
        private void SetRigPrevious(double2 floorMetres)
        {
            _frame++;
            _rig.Step(_frame, new HeadSample(_frame, _time, new double3(floorMetres.x, 0.0, floorMetres.y), true));
            _previous = floorMetres;
            _hasPrevious = true;
        }

        private static bool IsFinite(double2 v)
        {
            // x - x is zero for every finite x and NaN for NaN and the infinities.
            return (v.x - v.x) == 0.0 && (v.y - v.y) == 0.0;
        }

        private void ClearLastStep()
        {
            _lastStep = default;
            _lastStep.LastEdge = -1;
        }

        private void ClearDial()
        {
            _still = false;
            _curvatureChanged = false;
            _hasStepped = false;
        }

        private void RebuildCandidates()
        {
            if (_rig == null)
            {
                // q = 3: all six faces, whatever rho is.
                _cube.VisibleSet(_sphere.Pose.Tile, _cullRho, _candidates);
                _candidatesStale = false;
                return;
            }
            // As FlatWalkerKernel: two circumradii and the crossing band round rho (kernel.md section 4.6).
            double radius = _cullRho + 2.0 * _graph.CircumRadius + KernelConstants.CrossHysteresis;
            _graph.VisibleSet(_rig.Pose.Tile, radius, _candidates);
            _candidatesStale = false;
        }
    }
}

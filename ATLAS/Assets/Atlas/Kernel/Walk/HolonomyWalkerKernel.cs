using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    /// <summary>The kernel of every dial position the frame loop can step: the HolonomyRig on a {4,5} or {4,6}
    /// TileGraph (kappa = -1), and the FlatWalkerKernel for q = 4. Engine-free; Step allocates only when the graph meets tiles it has not held before.</summary>
    /// <remarks>
    /// Freshly constructed it is a FlatWalkerKernel in every respect. Configure with KernelConfig.Curved(5 or 6, L)
    /// puts the rig behind the same interface (Master Plan sections 4.6 and 6.3 step 4). q = 3 is not carried:
    /// the six-face sphere has its own walker in Spaces/SmallPlanet.
    /// </remarks>
    public sealed class HolonomyWalkerKernel : IWalkerKernel
    {
        private const int CandidateCapacity = 1024;
        private const int MaxResetCrossings = 64;

        private readonly FlatWalkerKernel _flat = new FlatWalkerKernel();
        private readonly List<VisibleTile> _candidates = new List<VisibleTile>(CandidateCapacity);
        private TileGraph _graph5, _graph6;
        private TileGraph _graph;       // null while q = 4
        private HolonomyRig _rig;       // null while q = 4
        private double _configuredL;    // L of the last curved Configure
        private double _cullRho = KernelConstants.DefaultCullRho;
        private KernelStep _lastStep;
        private long _stepCount;
        private long _frame;
        private double _time;
        private double2 _previous;
        private bool _hasPrevious;
        private bool _candidatesStale;

        public HolonomyWalkerKernel()
        {
            ClearLastStep();
        }

        /// <summary>The rig while q is 5 or 6, for the curvature ramp and Reaim; null at q = 4.</summary>
        public HolonomyRig Rig { get { return _rig; } }

        public ITileGraph Graph { get { return _rig != null ? _graph : _flat.Graph; } }
        public double Kappa { get { return _rig != null ? _rig.Kappa : _flat.Kappa; } }

        public double MetresPerUnit
        {
            get
            {
                if (_rig == null) return _flat.MetresPerUnit;
                // Mid-ramp the rig may stand at kappa = 0, where its length unit is the metre.
                return _rig.Kappa == Curvature.Flat ? KernelConstants.FlatMetresPerUnit : _rig.LengthScale;
            }
        }

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

        public WalkerPose Pose { get { return _rig != null ? _rig.Pose : _flat.Pose; } }
        public List<VisibleTile> Candidates { get { return _rig != null ? _candidates : _flat.Candidates; } }
        public KernelStep LastStep { get { return _rig != null ? _lastStep : _flat.LastStep; } }
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
                return;
            }
            if (kappa != Curvature.Hyperbolic)
            {
                throw new NotSupportedException(
                    "Atlas.Kernel: q = " + config.Q.ToString(CultureInfo.InvariantCulture) + " is not walked by the holonomy rig. " +
                    "HolonomyWalkerKernel carries q = 4, 5 and 6; the {4,3} sphere is walked over CubeTiling.");
            }
            if (!(config.MetresPerUnit > 0.0) || double.IsInfinity(config.MetresPerUnit))
            {
                throw new ArgumentOutOfRangeException(nameof(config), config.MetresPerUnit,
                    "Atlas.Kernel: MetresPerUnit must be finite and positive.");
            }

            // A TileGraph never evicts, so one per q is kept across dial changes.
            TileGraph graph;
            if (config.Q == 5) graph = _graph5 ?? (_graph5 = new TileGraph(5));
            else graph = _graph6 ?? (_graph6 = new TileGraph(6));

            _graph = graph;
            _rig = new HolonomyRig(graph);
            _configuredL = config.MetresPerUnit;
            Place(WalkerPose.Start);
        }

        public void Reset(in WalkerPose start)
        {
            if (_rig == null)
            {
                _flat.Reset(in start);
                return;
            }
            Place(start);
        }

        public void ResetToFloor(double2 floorMetres)
        {
            if (_rig == null)
            {
                _flat.ResetToFloor(floorMetres);
                return;
            }
            if (!IsFinite(floorMetres))
            {
                throw new ArgumentOutOfRangeException(nameof(floorMetres), "Atlas.Kernel: the floor point must be finite.");
            }

            WalkerPose pose;
            pose.Tile = TileId.Zero;
            pose.Local = TileZeroPlacement.Registered(floorMetres, Curvature.Hyperbolic, _configuredL);
            Place(pose);

            // The point becomes the rig's previous head position: its first sample integrates nothing.
            _frame++;
            _rig.Step(_frame, new HeadSample(_frame, _time, new double3(floorMetres.x, 0.0, floorMetres.y), true));
            _previous = floorMetres;
            _hasPrevious = true;
        }

        /// <summary>Step without a clock: the displacement is integrated, a curvature ramp does not advance.</summary>
        public KernelStep Step(double2 headFloorMetres, bool trackingNormal)
        {
            return Step(headFloorMetres, trackingNormal, _time);
        }

        /// <summary>Step 4 with the frame's time in seconds, which the rig needs to tell standing from walking
        /// (the curvature ramp only runs while the walker stands, Master Plan section 4.6).</summary>
        public KernelStep Step(double2 headFloorMetres, bool trackingNormal, double timeSeconds)
        {
            _stepCount++;
            if (_rig == null) return _flat.Step(headFloorMetres, trackingNormal);

            _frame++;
            _time = timeSeconds;

            KernelStep result;
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
            RebuildCandidates();
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

        private void RebuildCandidates()
        {
            // As FlatWalkerKernel: two circumradii and the crossing band round rho (kernel.md section 4.6).
            double radius = _cullRho + 2.0 * _graph.CircumRadius + KernelConstants.CrossHysteresis;
            _graph.VisibleSet(_rig.Pose.Tile, radius, _candidates);
            _candidatesStale = false;
        }
    }
}

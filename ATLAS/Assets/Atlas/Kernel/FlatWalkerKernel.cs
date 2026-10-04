using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    /// <summary>The kappa = 0 kernel on a FlatTileGraph. Engine-free; allocates nothing in Step.</summary>
    /// <remarks>
    /// The walker is held as a tile and a small frame inside it (Master Plan section 4.6). Each frame the
    /// floor displacement of the head is composed on the right of that frame, and the frame is re-rooted
    /// when the walker leaves the tile, so its entries stay below one tile side however far the walk goes.
    /// </remarks>
    public sealed class FlatWalkerKernel : IWalkerKernel
    {
        private const int CandidateCapacity = 1024;
        // A cell index beyond this cannot be held exactly in a double, let alone walked to.
        private const double MaxCellIndex = 1e15;

        private readonly List<VisibleTile> _candidates = new List<VisibleTile>(CandidateCapacity);
        private FlatTileGraph _graph;
        private double _metresPerUnit;
        private double _cullRho = KernelConstants.DefaultCullRho;
        private WalkerPose _pose;
        private KernelStep _lastStep;
        private long _stepCount;
        private double2 _previous;
        private bool _hasPrevious;
        private bool _candidatesStale;

        /// <summary>Configure(KernelConfig.Flat(KernelConstants.DefaultFlatTileSideMetres)); CullRho = DefaultCullRho;
        /// Candidates pre-sized to 1,024.</summary>
        public FlatWalkerKernel()
        {
            Configure(KernelConfig.Flat(KernelConstants.DefaultFlatTileSideMetres));
        }

        public ITileGraph Graph { get { return _graph; } }
        public double Kappa { get { return Curvature.Flat; } }
        public double MetresPerUnit { get { return _metresPerUnit; } }

        public double CullRho
        {
            get { return _cullRho; }
            set
            {
                if (!(value >= 0.0) || double.IsInfinity(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value,
                        "Atlas.Kernel: rho must be finite and not negative.");
                }
                _cullRho = value;
                _candidatesStale = true;
            }
        }

        public WalkerPose Pose { get { return _pose; } }
        public List<VisibleTile> Candidates { get { return _candidates; } }
        public KernelStep LastStep { get { return _lastStep; } }
        public long StepCount { get { return _stepCount; } }

        public void Configure(in KernelConfig config)
        {
            // OfQ rejects a q that is no tiling at all; the curved tilings are then refused by name.
            if (Curvature.OfQ(config.Q) != Curvature.Flat)
            {
                throw new NotSupportedException(Curvature.NotAvailable(
                    "q = " + config.Q.ToString(CultureInfo.InvariantCulture), "P1-B04"));
            }
            if (!(config.MetresPerUnit > 0.0) || double.IsInfinity(config.MetresPerUnit))
            {
                throw new ArgumentOutOfRangeException(nameof(config), config.MetresPerUnit,
                    "Atlas.Kernel: MetresPerUnit must be finite and positive.");
            }
            if (!(config.TileSideMetres > 0.0) || double.IsInfinity(config.TileSideMetres))
            {
                throw new ArgumentOutOfRangeException(nameof(config), config.TileSideMetres,
                    "Atlas.Kernel: TileSideMetres must be finite and positive.");
            }

            // The graph constructor rejects a ratio that underflows or overflows, before any state changes.
            FlatTileGraph graph = new FlatTileGraph(config.TileSideMetres / config.MetresPerUnit);
            _graph = graph;
            _metresPerUnit = config.MetresPerUnit;
            Reset(WalkerPose.Start);
        }

        public void Reset(in WalkerPose start)
        {
            long i, j;
            if (!FlatTileGraph.TryGetCell(start.Tile, out i, out j))
            {
                throw new ArgumentException("Atlas.Kernel: the start tile is not a tile of the current graph.", nameof(start));
            }
            if (start.Local.Kappa != Kappa || !start.IsValid)
            {
                throw new ArgumentException("Atlas.Kernel: the start frame is not a flat isometry.", nameof(start));
            }

            double a = _graph.CentreSpacing;
            Iso2 local = start.Local;

            // Bring the walker next to its tile in one move, so a far-off pose costs no loop.
            double ti = RoundHalfTowardsZero(local.M02 / a);
            double tj = RoundHalfTowardsZero(local.M12 / a);
            if (!(Math.Abs(ti) <= MaxCellIndex) || !(Math.Abs(tj) <= MaxCellIndex))
            {
                throw new ArgumentException("Atlas.Kernel: the start frame lies too far from its tile.", nameof(start));
            }
            if (ti != 0.0 || tj != 0.0)
            {
                local = Iso2.Boost(new double2(-a * ti, -a * tj), Curvature.Flat) * local;
                i += (long)ti;
                j += (long)tj;
            }

            // Then settle rounding with the same edge test as Step, without the hysteresis band.
            TileId tile = FlatTileGraph.Cell(i, j);
            for (int guard = 0; guard < 8; guard++)
            {
                int k = EdgePassed(local, a, 0.0);
                if (k < 0) break;
                local = _graph.EdgeStep(tile, k).Inverse() * local;
                tile = _graph.Neighbour(tile, k);
            }

            _pose.Tile = tile;
            _pose.Local = local;
            _hasPrevious = false;
            _previous = double2.zero;
            ClearLastStep();
            RebuildCandidates();
        }

        public void ResetToFloor(double2 floorMetres)
        {
            if (!IsFinite(floorMetres))
            {
                throw new ArgumentOutOfRangeException(nameof(floorMetres), "Atlas.Kernel: the floor point must be finite.");
            }

            double a = _graph.CentreSpacing;
            double ux = floorMetres.x / _metresPerUnit;
            double uz = floorMetres.y / _metresPerUnit;
            // A tie goes to the higher index.
            double fi = Math.Floor(ux / a + 0.5);
            double fj = Math.Floor(uz / a + 0.5);
            if (!(Math.Abs(fi) <= MaxCellIndex) || !(Math.Abs(fj) <= MaxCellIndex))
            {
                throw new ArgumentOutOfRangeException(nameof(floorMetres), "Atlas.Kernel: the floor point is out of range.");
            }

            _pose.Tile = FlatTileGraph.Cell((long)fi, (long)fj);
            _pose.Local = Iso2.Boost(new double2(ux - a * fi, uz - a * fj), Curvature.Flat);
            _previous = floorMetres;
            _hasPrevious = true;
            ClearLastStep();
            RebuildCandidates();
        }

        public KernelStep Step(double2 headFloorMetres, bool trackingNormal)
        {
            _stepCount++;

            double2 d = double2.zero;
            bool rejected;
            if (!IsFinite(headFloorMetres))
            {
                // A broken sample: the previous head position is left as it was.
                rejected = true;
            }
            else
            {
                if (_hasPrevious) d = headFloorMetres - _previous;
                // The previous position moves on every call, so a jump is rejected once and movement made
                // while tracking was lost is never integrated afterwards.
                _previous = headFloorMetres;
                _hasPrevious = true;
                double limit = KernelConstants.MaxStepMetres;
                rejected = !trackingNormal || !(d.x * d.x + d.y * d.y <= limit * limit);
                if (rejected) d = double2.zero;
            }

            KernelStep result;
            result.Rejected = rejected;
            result.Crossings = 0;
            result.LastEdge = -1;
            result.AppliedMetres = d;

            // A rejected step leaves the pose bit for bit as it was.
            if (!rejected)
            {
                double a = _graph.CentreSpacing;
                Iso2 local = (_pose.Local * Iso2.Boost(d / _metresPerUnit, Curvature.Flat)).Orthonormalised();
                TileId tile = _pose.Tile;

                while (result.Crossings < KernelConstants.MaxCrossingsPerStep)
                {
                    int k = EdgePassed(local, a, KernelConstants.CrossHysteresis);
                    if (k < 0) break;
                    local = _graph.EdgeStep(tile, k).Inverse() * local;
                    tile = _graph.Neighbour(tile, k);
                    result.Crossings++;
                    result.LastEdge = k;
                }

                _pose.Local = local;
                _pose.Tile = tile;
            }

            if (result.Crossings > 0 || _candidatesStale) RebuildCandidates();

            _lastStep = result;
            return result;
        }

        /// <summary>The edge the walker is furthest past, or -1 when no edge is passed by more than the band.
        /// On a tie the lowest edge index.</summary>
        private static int EdgePassed(in Iso2 local, double a, double band)
        {
            double half = 0.5 * a;
            double px = local.M02;
            double pz = local.M12;

            int k = 0;
            double worst = px - half;
            double e = pz - half;
            if (e > worst) { worst = e; k = 1; }
            e = -px - half;
            if (e > worst) { worst = e; k = 2; }
            e = -pz - half;
            if (e > worst) { worst = e; k = 3; }
            return worst > band ? k : -1;
        }

        /// <summary>Whole tiles between the tile centre and the point; a point exactly on an edge stays.</summary>
        private static double RoundHalfTowardsZero(double tiles)
        {
            return tiles >= 0.0 ? Math.Ceiling(tiles - 0.5) : Math.Floor(tiles + 0.5);
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
            // The walker can stand a circumradius from the centre of its tile and still see a tile whose
            // nearest corner, not its centre, is within rho; hence two circumradii and the crossing band.
            double radius = _cullRho + 2.0 * _graph.CircumRadius + KernelConstants.CrossHysteresis;
            _graph.VisibleSet(_pose.Tile, radius, _candidates);
            _candidatesStale = false;
        }
    }
}

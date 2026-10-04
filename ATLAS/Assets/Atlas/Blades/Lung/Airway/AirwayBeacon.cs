using System;
using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.Blades.Lung
{
    public readonly struct BeaconStep
    {
        public readonly bool Arrived;
        public readonly AirwayTile Next;
        public readonly int TreeEdge; // tree-frame edge of the current tile (R3 §D3: 0 back, 1 right, 2 straight, 3 left); -1 if Arrived

        public BeaconStep(bool arrived, AirwayTile next, int treeEdge) { Arrived = arrived; Next = next; TreeEdge = treeEdge; }
    }

    // Beacon (MP §10.2.3 Aids): "Arrow to the target. Next step = prefix comparison of addresses (exact). Shown after 8 s of hesitation,
    // except at the Two Compasses fork." Driven once per frame by the airway module (G6); no Update.
    public sealed class AirwayBeacon
    {
        public const double HesitationSeconds = 8.0;          // MP §10.2.3 Aids
        public const double ArrowAheadM = 0.6, ArrowHeightM = 0.01; // D-G4-12

        readonly AirwayTree tree;
        string target = "";
        bool compassActive;
        bool started;
        double entered, now;

        public AirwayBeacon(AirwayTree tree)
        {
            this.tree = tree ?? throw new ArgumentNullException(nameof(tree));
            Reset(AirwayRoute.Full(tree));
        }

        public AirwayTile Current { get; private set; }
        public BeaconStep Step { get; private set; }
        public int ExitCanonicalEdge { get; private set; } = -1;
        public bool AtCompassFork { get; private set; }
        public bool Visible => started && !Step.Arrived && (AtCompassFork || now - entered >= HesitationSeconds);
        public bool RescueShown => started && !Step.Arrived && !AtCompassFork && now - entered >= HesitationSeconds; // D-G4-7

        // Next tile on the tree path to the target, by ordinal prefix comparison of addresses (exact; R3 §D3).
        public static BeaconStep NextStep(AirwayTile current, string targetAddress)
        {
            string cur = current.Address;
            bool onPath = targetAddress.StartsWith(cur, StringComparison.Ordinal);
            if (current.Kind == AirwayTileKind.Fork)
            {
                if (cur == targetAddress) return new BeaconStep(true, default, -1);
                if (onPath)
                {
                    char c = targetAddress[cur.Length];
                    return new BeaconStep(false, AirwayTile.Bend(cur + c), c == 'L' ? RuleS.LeftEdge : RuleS.RightEdge);
                }
                return new BeaconStep(false, AirwayTile.Bend(cur), RuleS.BackEdge);
            }
            if (onPath) return new BeaconStep(false, AirwayTile.Fork(cur), RuleS.StraightEdge);
            return new BeaconStep(false, AirwayTile.Fork(AirwayAddress.Parent(cur)), RuleS.BackEdge);
        }

        // Clears the timer; the compass is active only if the compass fork lies on this route (D-G4-8).
        public void Reset(AirwayRoute route)
        {
            target = route.TargetAddress;
            compassActive = route.HasTwoCompasses(tree);
            started = false; entered = 0; now = 0;
            Current = AirwayTile.Fork(""); Step = NextStep(Current, target); ExitCanonicalEdge = -1; AtCompassFork = false;
        }

        // Once per frame (H-2: after tree.SetCurrent). Hesitation = continuous time on the current tree tile (D-G4-6).
        public void Tick(AirwayTile current, double timeSeconds)
        {
            if (!started || !current.Equals(Current))
            {
                entered = timeSeconds; started = true;
                Current = current;
                Step = NextStep(current, target);
                ExitCanonicalEdge = Step.Arrived ? -1 : tree.FrameOf(current).CanonicalEdge(Step.TreeEdge);
                AtCompassFork = compassActive && current.Kind == AirwayTileKind.Fork && current.Address == tree.CompassForkAddress;
            }
            now = timeSeconds;
        }

        // Unit (x, z) direction in the walker frame toward the neighbour across a canonical edge (D-G4-5): the neighbour's centre
        // p_k = (sqrt(phi) cos(k pi/2), sqrt(phi) sin(k pi/2), phi) (kernel.md §5.5), moved into the walker frame and taken through Log.
        public static double2 ArrowDirection(in WalkerPose pose, int canonicalEdge)
        {
            if (canonicalEdge < 0 || canonicalEdge > 3) throw new ArgumentOutOfRangeException(nameof(canonicalEdge));
            const double phi = 1.618033988749895, sqrtPhi = 1.272019649514069;
            double a = canonicalEdge * Math.PI / 2;
            var p = new double3(sqrtPhi * Math.Round(Math.Cos(a)), sqrtPhi * Math.Round(Math.Sin(a)), phi);
            double3 q = math.mul(pose.Local.Inverse().M, p);
            return math.normalize(Geometry2.Log(q, -1));
        }
    }
}

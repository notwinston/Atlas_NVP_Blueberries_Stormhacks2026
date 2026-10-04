using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.Blades.Lung
{
    // Two Compasses (MP §10.2.3 Aids): at the designed fork the Euclidean compass ("3D direction to the target from pos_mm") and the
    // beacon (prefix comparison) point to different children. The fork comes from tree.json (compass_fork.address), never hard-coded.
    public static class TwoCompasses
    {
        // Distal end of the target node: PosMm + LenMm * Dir.
        public static double3 TargetPointMm(AirwayTree t, string target)
        {
            var n = t.Node(target);
            return n.PosMm + n.LenMm * n.Dir;
        }

        // F = PosMm of child fork+'L' (the fork point); score_c = dot(Dir of fork+c, T - F); the larger score wins (PH-35 generator rule).
        public static char EuclideanChild(AirwayTree t, string fork, string target)
        {
            double3 f = t.Node(fork + "L").PosMm, d = TargetPointMm(t, target) - f;
            return math.dot(t.Node(fork + "L").Dir, d) >= math.dot(t.Node(fork + "R").Dir, d) ? 'L' : 'R';
        }

        // Requires fork to be a proper prefix of target.
        public static char BeaconChild(string fork, string target) => target[fork.Length];
    }
}

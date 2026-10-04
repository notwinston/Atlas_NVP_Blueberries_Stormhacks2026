// Beacon and route cost (Master Plan 10.2.3 "Aids and tasks"; work package P2-B06).
//   Beacon      "Arrow to the target. Next step = prefix comparison of addresses (exact). Shown after 8 s of
//               hesitation, except at the Two Compasses fork"
//   Route cost  "Insertion depth (sum of len_mm); sharp turns (branch angle > 45 degrees, our definition); narrowest
//               lumen against scope outer diameter 3.5 / 4.2 / 4.0 mm (Spec 11.3.1). In the symmetric model the lumen
//               falls below 4.2 mm at generation 7 (derived)"
// Time is passed in; nothing here reads a clock. No allocation.
using System;

namespace Atlas.Airway
{
    public enum BeaconStep : byte
    {
        /// <summary>The walker is at the target.</summary>
        Arrived = 0,
        /// <summary>Take the left child.</summary>
        Left = 1,
        /// <summary>Take the right child.</summary>
        Right = 2,
        /// <summary>The walker is off the route (or past the target): go back to the parent.</summary>
        Back = 3,
    }

    public static class Beacon
    {
        /// <summary>Seconds of hesitation at a fork after which the beacon is shown.</summary>
        public const double HesitationSeconds = 8.0;

        /// <summary>
        /// The next step from the branch the walker is in towards the target, by prefix comparison only:
        /// equal -> Arrived; walker a proper prefix of target -> the target's next letter; otherwise -> Back.
        /// </summary>
        public static BeaconStep NextStep(AirwayAddress walker, AirwayAddress target)
        {
            if (walker.Length > target.Length) return BeaconStep.Back;
            int shift = target.Length - walker.Length;
            if ((target.Bits >> shift) != walker.Bits) return BeaconStep.Back;
            if (shift == 0) return BeaconStep.Arrived;
            return ((target.Bits >> (shift - 1)) & 1u) != 0 ? BeaconStep.Right : BeaconStep.Left;
        }

        /// <summary>The address after taking the step (unchanged on Arrived).</summary>
        public static AirwayAddress Apply(AirwayAddress walker, BeaconStep step)
        {
            switch (step)
            {
                case BeaconStep.Left: return walker.Child(false);
                case BeaconStep.Right: return walker.Child(true);
                case BeaconStep.Back: return walker.Parent;
                default: return walker;
            }
        }

        /// <summary>Generations still to walk: back to the last common branch, then down to the target.</summary>
        public static int GenerationsToTarget(AirwayAddress walker, AirwayAddress target)
        {
            int k = AirwayAddress.CommonPrefixLength(walker, target);
            return (walker.Length - k) + (target.Length - k);
        }

        /// <summary>The beacon rule: shown once the walker has stood at the fork for 8 s, never at the Two Compasses fork.</summary>
        public static bool Shown(double secondsAtFork, bool isTwoCompassesFork)
        {
            return !isTwoCompassesFork && secondsAtFork >= HesitationSeconds;
        }

        /// <summary>True when the chosen child is the beacon's step at this fork. False for any child when the fork is
        /// off the route (the beacon says Back) and for "no choice".</summary>
        public static bool IsCorrect(AirwayAddress fork, AirwayChoice choice, AirwayAddress target)
        {
            BeaconStep s = NextStep(fork, target);
            return (s == BeaconStep.Left && choice == AirwayChoice.Left) || (s == BeaconStep.Right && choice == AirwayChoice.Right);
        }

        /// <summary>
        /// The fork event for a choice made secondsAtFork after the walker entered the fork chamber.
        /// beacon_shown follows <see cref="Shown"/> at that time.
        /// </summary>
        public static ForkEvent Fork(AirwayAddress fork, AirwayChoice choice, AirwayAddress target, double secondsAtFork, bool isTwoCompassesFork)
        {
            if (choice == AirwayChoice.None) throw new ArgumentException("a fork event needs the chosen letter");
            return new ForkEvent(fork, choice, IsCorrect(fork, choice, target), Shown(secondsAtFork, isTwoCompassesFork), secondsAtFork);
        }
    }

    /// <summary>The route cost read-out along one route from the trachea.</summary>
    public readonly struct RouteCost
    {
        /// <summary>Branch angle above which a turn counts as sharp ("our definition" in the plan).</summary>
        public const double SharpTurnDeg = 45.0;
        /// <summary>The three scope outer diameters of the plan's read-out, in the plan's order (Spec 11.3.1).</summary>
        public const double ScopeOdA = 3.5, ScopeOdB = 4.2, ScopeOdC = 4.0;

        /// <summary>Sum of len_mm of every branch on the route, the trachea (120 mm) and the last branch included.</summary>
        public readonly double InsertionDepthMm;
        /// <summary>Branches on the route that leave their parent at more than 45 degrees.</summary>
        public readonly int SharpTurns;
        /// <summary>Smallest lumen diameter (2 * radius_mm) on the route, and the generation where it is.</summary>
        public readonly double NarrowestLumenMm;
        public readonly int NarrowestGen;
        /// <summary>Branches summed (generation of the last one + 1).</summary>
        public readonly int Branches;

        public RouteCost(double insertionDepthMm, int sharpTurns, double narrowestLumenMm, int narrowestGen, int branches)
        {
            InsertionDepthMm = insertionDepthMm; SharpTurns = sharpTurns; NarrowestLumenMm = narrowestLumenMm;
            NarrowestGen = narrowestGen; Branches = branches;
        }

        /// <summary>True when the narrowest lumen on the route is at least the scope's outer diameter.</summary>
        public bool Passes(double scopeOuterDiameterMm) { return NarrowestLumenMm >= scopeOuterDiameterMm; }

        /// <summary>The cost of the trachea alone: the start of every route.</summary>
        public static RouteCost Start(in AirwayNode trachea)
        {
            return new RouteCost(trachea.LenMm, 0, trachea.DiameterMm, trachea.Gen, 1);
        }

        /// <summary>The cost after walking one more branch (child, which left parent).</summary>
        public RouteCost Add(in AirwayNode parent, in AirwayNode child)
        {
            bool narrower = child.DiameterMm < NarrowestLumenMm;
            return new RouteCost(InsertionDepthMm + child.LenMm,
                SharpTurns + (BranchAngleDeg(parent.Dir, child.Dir) > SharpTurnDeg ? 1 : 0),
                narrower ? child.DiameterMm : NarrowestLumenMm, narrower ? child.Gen : NarrowestGen, Branches + 1);
        }

        /// <summary>Angle in degrees between a branch's direction and its parent's (unit vectors).</summary>
        public static double BranchAngleDeg(in Vec3d parentDir, in Vec3d childDir)
        {
            double c = Vec3d.Dot(parentDir, childDir);
            if (c > 1.0) c = 1.0; else if (c < -1.0) c = -1.0;
            return Math.Acos(c) * (180.0 / Math.PI);
        }

        /// <summary>The cost of the route from the trachea to target, computed from the rule.</summary>
        public static RouteCost Compute(AirwayResolver resolver, AirwayAddress target)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            AirwayNode parent = resolver.Node(AirwayAddress.Trachea);
            RouteCost cost = Start(parent);
            for (int k = 1; k <= target.Length; k++)
            {
                AirwayNode child = resolver.Node(target.Prefix(k));
                cost = cost.Add(parent, child);
                parent = child;
            }
            return cost;
        }

        /// <summary>First generation on the route whose lumen diameter is below the scope's outer diameter, or -1.</summary>
        public static int FirstGenerationBelow(AirwayResolver resolver, AirwayAddress target, double scopeOuterDiameterMm)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            for (int k = 0; k <= target.Length; k++)
                if (resolver.Node(target.Prefix(k)).DiameterMm < scopeOuterDiameterMm) return k;
            return -1;
        }

        /// <summary>
        /// The symmetric model (every ratio 2^(-1/3), no jitter): first generation whose diameter 18 * 2^(-g/3) is below
        /// the scope's outer diameter. 7 for 4.2 mm, the plan's derived number.
        /// </summary>
        public static int SymmetricFirstGenerationBelow(double scopeOuterDiameterMm)
        {
            double d = AirwayResolver.TracheaDiameterMm;
            for (int g = 0; g <= AirwayAddress.MaxLength; g++)
            {
                if (d < scopeOuterDiameterMm) return g;
                d *= AirwayResolver.DiameterRatio;
            }
            return -1;
        }
    }
}

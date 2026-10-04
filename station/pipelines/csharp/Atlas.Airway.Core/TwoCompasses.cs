// Two Compasses (Master Plan 10.2.3; work package P2-B08) and the greedy study (docs/case_contract.md 10.3, 10.4).
// Port of airway.py: compass_scores, compass_choice, compass_margin, compass_wrong, greedy_target, greedy_study.
//   Euclidean compass  t = pos_mm(target) - pos_mm(fork); the child with the larger dot(dir(child), t); a tie picks none
//   Beacon             the target's next letter after the fork's address
//   Margin             (dot(dir(off-route child), t) - dot(dir(on-route child), t)) / |t|; positive exactly when the
//                      compass points down the wrong child
using System;
using System.Globalization;

namespace Atlas.Airway
{
    /// <summary>Both compasses at one fork on the way to one target.</summary>
    public readonly struct CompassReading
    {
        public readonly AirwayAddress Fork;
        /// <summary>The Euclidean compass's child; None on an exact tie.</summary>
        public readonly AirwayChoice Compass;
        /// <summary>The beacon's child (the next letter of the target's address).</summary>
        public readonly AirwayChoice Beacon;
        public readonly double Margin;

        public CompassReading(AirwayAddress fork, AirwayChoice compass, AirwayChoice beacon, double margin)
        {
            Fork = fork; Compass = compass; Beacon = beacon; Margin = margin;
        }

        /// <summary>True at a Two Compasses fork: the compass picks the child the beacon does not. A tie is not one.</summary>
        public bool Disagree { get { return Compass != AirwayChoice.None && Compass != Beacon; } }
    }

    public static class TwoCompasses
    {
        /// <summary>Dot products of the two children's directions with the straight line from the fork to the target
        /// (not normalised: only their order matters).</summary>
        public static void Scores(in Vec3d forkPos, in Vec3d targetPos, in Vec3d dirL, in Vec3d dirR, out double scoreL, out double scoreR)
        {
            Vec3d t = new Vec3d(targetPos.X - forkPos.X, targetPos.Y - forkPos.Y, targetPos.Z - forkPos.Z);
            scoreL = Vec3d.Dot(dirL, t);
            scoreR = Vec3d.Dot(dirR, t);
        }

        /// <summary>The Euclidean compass: the child whose direction is closer to the straight line to the target.</summary>
        public static AirwayChoice Compass(in Vec3d forkPos, in Vec3d targetPos, in Vec3d dirL, in Vec3d dirR)
        {
            double sl, sr;
            Scores(forkPos, targetPos, dirL, dirR, out sl, out sr);
            return sl > sr ? AirwayChoice.Left : sr > sl ? AirwayChoice.Right : AirwayChoice.None;
        }

        /// <summary>The beacon at a fork on the route: the target's next letter. Throws when fork is not a proper prefix of target.</summary>
        public static AirwayChoice BeaconChoice(AirwayAddress fork, AirwayAddress target)
        {
            if (!fork.IsProperPrefixOf(target)) throw new ArgumentException("the fork is not a proper prefix of the target");
            return target.LetterAt(fork.Length);
        }

        /// <summary>Both compasses and the margin, from positions and directions (from the resolver or from tree.json).</summary>
        public static CompassReading Read(AirwayAddress fork, AirwayAddress target, in Vec3d forkPos, in Vec3d targetPos, in Vec3d dirL, in Vec3d dirR)
        {
            AirwayChoice beacon = BeaconChoice(fork, target);
            double sl, sr;
            Scores(forkPos, targetPos, dirL, dirR, out sl, out sr);
            AirwayChoice compass = sl > sr ? AirwayChoice.Left : sr > sl ? AirwayChoice.Right : AirwayChoice.None;
            double on = beacon == AirwayChoice.Left ? sl : sr, off = beacon == AirwayChoice.Left ? sr : sl;
            Vec3d t = new Vec3d(targetPos.X - forkPos.X, targetPos.Y - forkPos.Y, targetPos.Z - forkPos.Z);
            return new CompassReading(fork, compass, beacon, (off - on) / Math.Sqrt(Vec3d.Dot(t, t)));
        }

        /// <summary>Both compasses at a fork of the route to target, from the rule (double precision, no file).</summary>
        public static CompassReading At(AirwayResolver resolver, AirwayAddress fork, AirwayAddress target)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            AirwayNode f = resolver.Node(fork);
            AirwayNode l = resolver.Child(f, false), r = resolver.Child(f, true);
            AirwayNode t = resolver.Node(target);
            return Read(fork, target, f.PosMm, t.PosMm, l.Dir, r.Dir);
        }

        /// <summary>The same from tree.json values alone (quantised to 1e-6); every node involved must be in the file.</summary>
        public static CompassReading At(AirwayTreeFile tree, AirwayAddress fork, AirwayAddress target)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            AirwayNode f = tree.Node(fork), t = tree.Node(target), l = tree.Node(fork.Child(false)), r = tree.Node(fork.Child(true));
            return Read(fork, target, f.PosMm, t.PosMm, l.Dir, r.Dir);
        }

        /// <summary>
        /// The compass_choice event of a walker who took <paramref name="taken"/> at a fork where the reading was r.
        /// </summary>
        public static CompassChoiceEvent Choice(in CompassReading r, AirwayChoice taken, double secondsAtFork)
        {
            if (taken == AirwayChoice.None) throw new ArgumentException("a compass_choice event needs the chosen letter");
            CompassFollowed followed;
            if (taken == r.Beacon) followed = taken == r.Compass ? CompassFollowed.Both : CompassFollowed.Beacon;
            else followed = taken == r.Compass ? CompassFollowed.Compass : CompassFollowed.Neither;
            return new CompassChoiceEvent(r.Fork, followed, taken == r.Beacon, taken, r.Compass, r.Beacon, r.Margin, secondsAtFork);
        }
    }

    /// <summary>Result of the greedy study: the counts of greedy.json.</summary>
    public sealed class GreedyResult
    {
        public long TreeSeed, Seed;
        public int NTargets, NDistinctTargets, TargetGen;
        public int Forks, WrongForks, RoutesWithWrongFork;
        public int[] ForksByGen, WrongByGen;
        public double WrongShare { get { return (double)WrongForks / Forks; } }
    }

    /// <summary>The offline study of rule atlas.greedy/1, re-run from the rule. Not a per-frame path (it allocates its result).</summary>
    public static class GreedyStudy
    {
        public const string Rule = "atlas.greedy/1";
        public const int DefaultTargets = 1000;
        public const int DefaultTargetGen = 16;

        /// <summary>Target i of the study: the first gen bits, most significant first, of
        /// SHA-256("atlas.greedy/1|seed|i"), 0 = L and 1 = R.</summary>
        public static AirwayAddress Target(long studySeed, int i, int gen = DefaultTargetGen)
        {
            return Target(new Sha256(), new byte[64], studySeed, i, gen);
        }

        static AirwayAddress Target(Sha256 sha, byte[] buf, long studySeed, int i, int gen)
        {
            if (gen < 0 || gen > AirwayAddress.MaxLength) throw new ArgumentOutOfRangeException(nameof(gen));
            string s = Rule + "|" + studySeed.ToString(CultureInfo.InvariantCulture) + "|" + i.ToString(CultureInfo.InvariantCulture);
            for (int k = 0; k < s.Length; k++) buf[k] = (byte)s[k];
            sha.Compute(buf, s.Length);
            uint bits = 0;
            for (int j = 0; j < gen; j++)
                bits = (bits << 1) | (uint)((sha.DigestByte(j >> 3) >> (7 - (j & 7))) & 1);
            return new AirwayAddress(bits, gen);
        }

        /// <summary>For every fork on the route to each target: does the Euclidean compass pick the child off the route?</summary>
        public static GreedyResult Run(long treeSeed, long studySeed, int nTargets = DefaultTargets, int gen = DefaultTargetGen)
        {
            if (nTargets < 1) throw new ArgumentOutOfRangeException(nameof(nTargets));
            if (gen < 1 || gen > AirwayAddress.MaxLength) throw new ArgumentOutOfRangeException(nameof(gen));
            var resolver = new AirwayResolver(treeSeed);
            var sha = new Sha256();
            var buf = new byte[64];
            var res = new GreedyResult
            {
                TreeSeed = treeSeed, Seed = studySeed, NTargets = nTargets, TargetGen = gen,
                ForksByGen = new int[gen], WrongByGen = new int[gen],
            };
            var seen = new System.Collections.Generic.HashSet<uint>();
            for (int i = 0; i < nTargets; i++)
            {
                AirwayAddress target = Target(sha, buf, studySeed, i, gen);
                seen.Add(target.Bits);
                AirwayNode t = resolver.Node(target); // fills the path: every fork below is a cached prefix
                bool any = false;
                for (int k = 0; k < gen; k++)
                {
                    AirwayAddress fork = target.Prefix(k);
                    AirwayNode f = resolver.Node(fork);
                    bool right = target.IsRight(k);
                    AirwayNode on = resolver.Node(target.Prefix(k + 1));
                    AirwayNode off = resolver.Child(f, !right);
                    CompassReading r = right
                        ? TwoCompasses.Read(fork, target, f.PosMm, t.PosMm, off.Dir, on.Dir)
                        : TwoCompasses.Read(fork, target, f.PosMm, t.PosMm, on.Dir, off.Dir);
                    res.ForksByGen[k]++;
                    if (r.Disagree) { res.WrongByGen[k]++; any = true; }
                }
                if (any) res.RoutesWithWrongFork++;
            }
            for (int k = 0; k < gen; k++) { res.Forks += res.ForksByGen[k]; res.WrongForks += res.WrongByGen[k]; }
            res.NDistinctTargets = seen.Count;
            return res;
        }
    }
}

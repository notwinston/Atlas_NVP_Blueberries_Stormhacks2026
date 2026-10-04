// Dependency-free tests of Atlas.Airway.Core (no test framework, so the library stays movable into Unity and the build
// needs no third-party packages). Exit code 0 = all passed and nothing failed; a section that could not run says "NOT RUN".
//
//   Atlas.Airway.Core.Tests --fixture <dir with tree.json and greedy.json> --ref <resolver reference file> [--ref ...]
//   env ATLAS_CASES_DIR  the cases directory holding lung101/airway/ (the same checks on the built package)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Atlas.Airway;
using Atlas.Cases;

namespace Atlas.Airway.Tests
{
    public static class Program
    {
        static int passed, failed;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static void Check(bool ok, string name)
        {
            if (ok) passed++;
            else { failed++; Console.Error.WriteLine("FAIL " + name); }
        }

        static void Throws<T>(Action a, string name) where T : Exception
        {
            try { a(); Check(false, name + " (no exception)"); }
            catch (T) { Check(true, name); }
        }

        static ulong rng = 0x9E3779B97F4A7C15UL;
        static ulong NextU64() // splitmix64
        {
            rng += 0x9E3779B97F4A7C15UL;
            ulong z = rng;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
        static AirwayAddress RandomAddress(int gen) { return new AirwayAddress((uint)(NextU64() & ((1UL << gen) - 1UL)), gen); }
        static AirwayAddress RandomAddress(int lo, int hi) { return RandomAddress(lo + (int)(NextU64() % (ulong)(hi - lo + 1))); }

        static string E(double v) { return v.ToString("E3", Inv); }

        public static int Main(string[] args)
        {
            string fixture = null;
            var refs = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--fixture" && i + 1 < args.Length) fixture = args[++i];
                else if (args[i] == "--ref" && i + 1 < args.Length) refs.Add(args[++i]);
                else { Console.Error.WriteLine("unknown argument " + args[i]); return 2; }
            }

            TestSha256();
            TestAddress();
            TestStepsAndWindow();
            TestResolverBasics();
            foreach (string f in refs) TestReference(f);
            TestBeacon();
            TestRouteCostSymmetric();
            TestEvents();
            TestNoAllocation();
            if (fixture != null)
            {
                TestTree(fixture, "fixture");
                TestRejections(fixture);
            }
            else Console.WriteLine("NOT RUN: tree.json / greedy.json tests on the committed copy (no --fixture argument)");
            TestRealCase(fixture);

            Console.WriteLine("PASSED " + passed + " FAILED " + failed);
            return failed == 0 ? 0 : 1;
        }

        // ============================================================ SHA-256
        static void TestSha256()
        {
            var sha = new Sha256();
            byte[] abc = Encoding.ASCII.GetBytes("abc");
            sha.Compute(abc, 3);
            Check(sha.Word64(0) == 0xba7816bf8f01cfeaUL && sha.Word64(3) == 0xb410ff61f20015adUL, "SHA-256 of \"abc\" (FIPS 180-4 vector)");
            int bad = 0;
            var data = new byte[300];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)NextU64();
            using (var sys = System.Security.Cryptography.SHA256.Create())
            {
                for (int len = 0; len <= 300; len++)
                {
                    byte[] want = sys.ComputeHash(data, 0, len);
                    sha.Compute(data, len);
                    for (int i = 0; i < 32; i++) if (sha.DigestByte(i) != want[i]) { bad++; break; }
                }
            }
            Check(bad == 0, "own SHA-256 equals System.Security.Cryptography on every length 0..300 (" + bad + " differ)");
        }

        // ============================================================ addresses
        static void TestAddress()
        {
            AirwayAddress t = AirwayAddress.Trachea;
            Check(t.Length == 0 && t.IsTrachea && t.ToString() == "" && t.NodeIndex == 0 && t == default(AirwayAddress), "trachea is the empty address");
            AirwayAddress a = AirwayAddress.Parse("RRRL");
            Check(a.Length == 4 && a.Gen == 4 && a.Bits == 14 && a.NodeIndex == 15 + 14 && a.ToString() == "RRRL", "RRRL parses to bits 1110, node index 29");
            Check(a.IsRight(0) && a.IsRight(2) && !a.IsRight(3) && a.LetterAt(3) == AirwayChoice.Left && a.LastLetter == AirwayChoice.Left, "letters of RRRL");
            Check(a.Parent.ToString() == "RRR" && a.Sibling.ToString() == "RRRR" && a.Child(true).ToString() == "RRRLR" && a.Child(AirwayChoice.Left).ToString() == "RRRLL", "parent, sibling, children");
            Check(a.Prefix(0) == t && a.Prefix(2).ToString() == "RR" && a.Prefix(4) == a, "prefixes");
            Check(t.IsPrefixOf(a) && t.IsProperPrefixOf(a) && a.IsPrefixOf(a) && !a.IsProperPrefixOf(a) && AirwayAddress.Parse("RR").IsProperPrefixOf(a)
                && !AirwayAddress.Parse("RL").IsPrefixOf(a) && !a.IsPrefixOf(AirwayAddress.Parse("RR")), "prefix relations");
            Check(AirwayAddress.CommonPrefixLength(AirwayAddress.Parse("RRLL"), a) == 2 && AirwayAddress.CommonPrefixLength(a, AirwayAddress.Parse("L")) == 0
                && AirwayAddress.CommonPrefixLength(a, AirwayAddress.Parse("RRRLRR")) == 4 && AirwayAddress.CommonPrefixLength(t, a) == 0, "common prefix length");
            AirwayAddress x;
            Check(!AirwayAddress.TryParse("RXL", out x) && !AirwayAddress.TryParse(null, out x) && !AirwayAddress.TryParse("lr", out x)
                && !AirwayAddress.TryParse(new string('L', 24), out x) && AirwayAddress.TryParse(new string('R', 23), out x) && x.Length == 23, "rejects other letters and more than 23 letters");
            Throws<ArgumentException>(() => AirwayAddress.Parse("L R"), "Parse throws on a bad address");
            Throws<InvalidOperationException>(() => { var p = AirwayAddress.Trachea.Parent; }, "the trachea has no parent");
            Throws<InvalidOperationException>(() => AirwayAddress.Parse(new string('L', 23)).Child(false), "no child below 23 letters");
            Throws<ArgumentException>(() => new AirwayAddress(4, 2), "bits beyond the length rejected");

            // against a string implementation, 20,000 random pairs
            int bad = 0;
            for (int i = 0; i < 20000; i++)
            {
                AirwayAddress p = RandomAddress(0, 23), q = (i & 1) == 0 ? RandomAddress(0, 23) : p.Prefix((int)(NextU64() % (ulong)(p.Length + 1)));
                string ps = p.ToString(), qs = q.ToString();
                int k = 0;
                while (k < ps.Length && k < qs.Length && ps[k] == qs[k]) k++;
                if (AirwayAddress.CommonPrefixLength(p, q) != k) bad++;
                if (q.IsPrefixOf(p) != ps.StartsWith(qs, StringComparison.Ordinal)) bad++;
                if (AirwayAddress.Parse(ps) != p) bad++;
                if (p.Length > 0 && (p.Parent.ToString() != ps.Substring(0, ps.Length - 1))) bad++;
            }
            Check(bad == 0, "bit operations equal the string operations on 20,000 random address pairs (" + bad + " differ)");
        }

        // ============================================================ Rule S steps, lazy window (P2-B01)
        struct CodeStepper : IAirwayStepper<ulong>
        {
            public ulong Apply(ulong state, AirwayStep step) { return state * 3UL + (ulong)step + 1UL; }
        }

        static void TestStepsAndWindow()
        {
            AirwayAddress a = AirwayAddress.Parse("RL");
            Span<AirwayStep> steps = stackalloc AirwayStep[2 * AirwayAddress.MaxLength];
            int n = AirwaySteps.Fill(a, steps);
            Check(n == 4 && steps[0] == AirwayStep.TurnRight && steps[1] == AirwayStep.Straight && steps[2] == AirwayStep.TurnLeft && steps[3] == AirwayStep.Straight,
                "RL is turn right, straight, turn left, straight (one turn tile plus one straight tile per generation)");
            Check(AirwaySteps.Count(AirwayAddress.Trachea) == 0 && AirwaySteps.Count(AirwayAddress.Parse("RRRL")) == 8, "the four-fork route is 8 tile steps");
            Check(AirwaySteps.Count(RandomAddress(AirwaySteps.WalkableGen)) == 32, "16 generations are 32 tile steps");
            Check(AirwaySteps.Count(RandomAddress(23)) == 46, "23 generations are 46 tile steps");
            bool edges = true;
            for (int b = 0; b < 4; b++)
            {
                int l = AirwaySteps.ExitEdge(b, AirwayStep.TurnLeft), s = AirwaySteps.ExitEdge(b, AirwayStep.Straight), r = AirwaySteps.ExitEdge(b, AirwayStep.TurnRight);
                edges &= l == (b + 3) % 4 && s == (b + 2) % 4 && r == (b + 1) % 4 && l != b && s != b && r != b && l != s && s != r && l != r;
            }
            Check(edges, "exit edges: left (b + 3) mod 4, straight (b + 2) mod 4, right (b + 1) mod 4, never the entry edge");

            // Plan row P2-B01: "32,767 nodes to depth 14 map to distinct ids; window <= 60 nodes".
            // Here: the 32,767 addresses, their node indices and their step sequences are distinct. The tile ids are Phase 1's.
            const int Depth = 14;
            int count = 0, maxWindow = 0, windowBad = 0;
            var seenIndex = new bool[(1 << (Depth + 1)) - 1];
            var seenSteps = new HashSet<ulong>();
            Span<AirwayAddress> window = stackalloc AirwayAddress[AirwayWindow.MaxNodes];
            for (int g = 0; g <= Depth; g++)
            {
                for (uint b = 0; b < (1u << g); b++)
                {
                    var x = new AirwayAddress(b, g);
                    count++;
                    if (!seenIndex[x.NodeIndex]) seenIndex[x.NodeIndex] = true; else windowBad += 1000000;
                    seenSteps.Add(AirwaySteps.Fold(x, 0UL, new CodeStepper()));
                    int w = AirwayWindow.Fill(x, AirwaySteps.WalkableGen, window);
                    if (w > maxWindow) maxWindow = w;
                    int anc = Math.Min(2, g), below = Math.Min(3, AirwaySteps.WalkableGen - g);
                    if (w != anc + (1 << (below + 1)) - 1) windowBad++;
                    for (int i = 0; i < anc; i++) if (window[i] != x.Prefix(g - anc + i)) windowBad++;
                    if (window[anc] != x) windowBad++;
                    for (int i = anc + 1; i < w; i++)
                    {
                        if (!x.IsProperPrefixOf(window[i]) || window[i].Length - g > 3) windowBad++;
                        if (window[i].NodeIndex <= window[i - 1].NodeIndex && i > anc + 1) windowBad++; // strictly increasing = distinct
                    }
                }
            }
            Check(count == 32767, "32,767 nodes to depth 14 (got " + count + ")");
            Check(seenSteps.Count == 32767, "32,767 distinct step sequences (got " + seenSteps.Count + ")");
            Check(maxWindow <= AirwayWindow.PlanBoundNodes && maxWindow == AirwayWindow.MaxNodes && AirwayWindow.MaxNodes == 17, "window <= 60 nodes (largest " + maxWindow + ")");
            Check(windowBad == 0, "every window is the 2 ancestors, the node and its descendants to depth 3, all distinct (" + windowBad + " faults)");
            Check(AirwayWindow.Fill(AirwayAddress.Trachea, 16, window) == 15 && AirwayWindow.Fill(RandomAddress(16), 16, window) == 3
                && AirwayWindow.Fill(RandomAddress(15), 16, window) == 5, "the window stops at the walkable depth");
            Console.WriteLine("P2-B01 addresses: " + count + " nodes to depth 14, " + seenSteps.Count + " distinct step sequences, largest window "
                + maxWindow + " nodes (plan bound 60), " + (2 * maxWindow) + " tiles (plan bound 120)");
            Console.WriteLine("NOT RUN: P2-B01 \"32,767 nodes to depth 14 map to distinct ids\" on tile ids: needs the {4,5} ITileGraph of Phase 1 (P1-B04), in the Editor");
        }

        // ============================================================ resolver (rule atlas.airway/2)
        static void TestResolverBasics()
        {
            var r = new AirwayResolver(101);
            AirwayNode t = r.Node(AirwayAddress.Trachea);
            Check(t.RadiusMm == 9.0 && t.LenMm == 120.0 && t.PosMm.X == 0.0 && t.PosMm.Y == 0.0 && t.PosMm.Z == -120.0
                && t.Dir.Z == -1.0 && t.Normal.X == 1.0 && t.Gen == 0, "trachea: radius 9, length 120, end (0, 0, -120), dir (0, 0, -1), normal (1, 0, 0)");
            Check(AirwayNode.Source == "model", "every node is \"model\"");
            AirwayNode l = r.Node("L"), rr = r.Node("R");
            Check(l.Dir.X > 0.0 && rr.Dir.X < 0.0 && Math.Abs(l.Dir.Y) < 1e-15, "at the first fork L goes to +x (left)");
            Check(Math.Abs(AirwayResolver.DiameterRatio - 0.7937005259840998) < 1e-15, "diameter ratio 2^(-1/3)");
            Check(r.PlaneRotationDeg(AirwayAddress.Trachea) == 90.0, "plane rotation is exactly 90 degrees at the trachea's fork");

            int bad = 0, ratioBad = 0, angleBad = 0, cacheBad = 0;
            double worstUnit = 0, worstPlane = 0, worstAngle = 0, minRatio = 9, maxRatio = 0, maxAngle = 0;
            var fresh = new AirwayResolver(101);
            for (int i = 0; i < 4000; i++)
            {
                AirwayAddress a = RandomAddress(1, 23);
                AirwayNode n = r.Node(a), p = r.Node(a.Parent), s = r.Node(a.Sibling);
                // same result from a resolver that has never seen another address (the path cache changes nothing)
                AirwayNode f = new AirwayResolver(101).Node(a);
                if (!BitEqual(n, f)) cacheBad++;
                if (i % 7 == 0 && !BitEqual(fresh.Node(a), n)) cacheBad++;
                worstUnit = Math.Max(worstUnit, Math.Abs(Math.Sqrt(Vec3d.Dot(n.Dir, n.Dir)) - 1.0));
                worstUnit = Math.Max(worstUnit, Math.Abs(Math.Sqrt(Vec3d.Dot(n.Normal, n.Normal)) - 1.0));
                if (n.PosMm.X != p.PosMm.X + n.LenMm * n.Dir.X || n.PosMm.Z != p.PosMm.Z + n.LenMm * n.Dir.Z) bad++;
                if (n.Normal.X != s.Normal.X || n.Normal.Y != s.Normal.Y || n.Normal.Z != s.Normal.Z) bad++;
                // both children lie in one plane with the parent's axis, one on each side
                Vec3d c = Vec3d.Cross(n.Dir, s.Dir);
                worstPlane = Math.Max(worstPlane, Math.Abs(Vec3d.Dot(c, p.Dir)));
                Vec3d side = Vec3d.Cross(n.Normal, p.Dir);
                if (Vec3d.Dot(n.Dir, side) * Vec3d.Dot(s.Dir, side) >= 0.0) bad++;
                double q = n.RadiusMm / p.RadiusMm;
                minRatio = Math.Min(minRatio, q); maxRatio = Math.Max(maxRatio, q);
                if (Math.Abs(q - 0.794) > 0.08) ratioBad++;
                if (Math.Abs(q - r.DiameterRatioOf(a)) > 1e-15) ratioBad++;
                double ld = n.LenMm / n.DiameterMm;
                if (ld < 2.0 - 1e-12 || ld > 4.0 + 1e-12) ratioBad++;
                double rule = r.BranchAngleDeg(a), geo = RouteCost.BranchAngleDeg(p.Dir, n.Dir);
                worstAngle = Math.Max(worstAngle, Math.Abs(rule - geo));
                maxAngle = Math.Max(maxAngle, rule);
                if (!(rule < 60.0)) angleBad++;
                double rho = r.PlaneRotationDeg(a);
                if (rho < 0.0 || rho > 180.0) angleBad++;
            }
            Check(cacheBad == 0, "the node depends on (seed, address) only: a fresh resolver gives the same bits, 4,000 addresses up to length 23 (" + cacheBad + " differ)");
            Check(bad == 0, "pos = pos(parent) + len * dir; siblings share the branching plane and leave on opposite sides (" + bad + " faults)");
            Check(worstUnit < 1e-15 * 4 && worstPlane < 1e-14, "dir and normal are unit vectors, children coplanar with the parent's axis (worst " + E(worstUnit) + ", " + E(worstPlane) + ")");
            Check(ratioBad == 0, "diameter ratio within 0.794 +/- 0.08 and length / diameter in 2..4 (range " + minRatio.ToString("F6", Inv) + " .. " + maxRatio.ToString("F6", Inv) + ")");
            Check(angleBad == 0 && worstAngle < 1e-6, "branching angle below 60 degrees, equal to the angle between the directions (largest " + maxAngle.ToString("F2", Inv) + " deg, worst difference " + E(worstAngle) + " deg)");
            Check(!BitEqual(new AirwayResolver(102).Node("LRLR"), r.Node("LRLR")), "another seed gives another tree");
            Throws<ArgumentException>(() => r.Node("LXR"), "a bad address is rejected");
            Throws<ArgumentException>(() => r.Child(new AirwayNode(AirwayAddress.Trachea, 9, 120, new Vec3d(0, 0, -120), new Vec3d(0, 0, -1), default(Vec3d)), false),
                "a node without its normal (read from the file) cannot be a parent in the rule");
        }

        static bool BitEqual(double a, double b) { return BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b); }
        static bool BitEqual(in Vec3d a, in Vec3d b) { return BitEqual(a.X, b.X) && BitEqual(a.Y, b.Y) && BitEqual(a.Z, b.Z); }
        static bool BitEqual(in AirwayNode a, in AirwayNode b)
        {
            return a.Address == b.Address && BitEqual(a.RadiusMm, b.RadiusMm) && BitEqual(a.LenMm, b.LenMm) && BitEqual(a.PosMm, b.PosMm)
                && BitEqual(a.Dir, b.Dir) && BitEqual(a.Normal, b.Normal);
        }

        // ============================================================ C# resolver == Python resolver
        const double RefTolerance = 1e-9; // mm for radius, len, pos; absolute for the unit vectors

        static double Hex(string s) { return BitConverter.Int64BitsToDouble(long.Parse(s, NumberStyles.HexNumber, Inv)); }

        static void TestReference(string file)
        {
            string name = Path.GetFileName(file);
            if (!File.Exists(file)) { Check(false, "resolver reference file exists: " + file); return; }
            AirwayResolver resolver = null;
            int n = 0, bitEqualNodes = 0, values = 0, bitEqualValues = 0, minGen = 99, maxGen = 0;
            double worst = 0;
            string worstAt = "";
            var got = new double[11];
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] f = line.Split(' ');
                if (f[0] == "seed") { resolver = new AirwayResolver(long.Parse(f[1], Inv)); continue; }
                if (resolver == null || f.Length != 12) { Check(false, name + ": malformed line"); return; }
                AirwayAddress a = AirwayAddress.Parse(f[0]);
                AirwayNode node = resolver.Node(a);
                got[0] = node.RadiusMm; got[1] = node.LenMm;
                got[2] = node.PosMm.X; got[3] = node.PosMm.Y; got[4] = node.PosMm.Z;
                got[5] = node.Dir.X; got[6] = node.Dir.Y; got[7] = node.Dir.Z;
                got[8] = node.Normal.X; got[9] = node.Normal.Y; got[10] = node.Normal.Z;
                bool all = true;
                for (int k = 0; k < 11; k++)
                {
                    double want = Hex(f[k + 1]);
                    values++;
                    if (BitEqual(got[k], want)) bitEqualValues++; else all = false;
                    double d = Math.Abs(got[k] - want);
                    if (!(d <= worst)) { worst = d; worstAt = f[0] + " value " + k; }
                }
                if (all) bitEqualNodes++;
                n++;
                minGen = Math.Min(minGen, a.Length); maxGen = Math.Max(maxGen, a.Length);
            }
            Check(n > 0, name + ": has addresses");
            Check(worst <= RefTolerance, name + ": C# resolver equals the Python resolver within 1e-9 (worst " + E(worst) + (worst > 0 ? " at " + worstAt : "") + ")");
            Console.WriteLine("REF " + name + ": " + n + " addresses of generations " + minGen + " to " + maxGen + ", " + bitEqualNodes + " nodes bit-equal, "
                + bitEqualValues + "/" + values + " doubles bit-equal, worst difference " + E(worst) + " against the Python resolver");
        }

        // ============================================================ beacon (P2-B06)
        static BeaconStep RefStep(string walker, string target) // the prefix comparison written on strings
        {
            if (walker == target) return BeaconStep.Arrived;
            if (target.StartsWith(walker, StringComparison.Ordinal)) return target[walker.Length] == 'R' ? BeaconStep.Right : BeaconStep.Left;
            return BeaconStep.Back;
        }

        static void TestBeacon()
        {
            // Plan row P2-B06: "Beacon step equals the address-prefix step on 1,000 random targets".
            // The 1,000 targets are those of the greedy study (seed 101, generation 16). For each: the walker at every
            // branch of the route (17 positions), at 16 branches off the route, and one walk from a random branch to the target.
            int targets = 0, onRoute = 0, offRoute = 0, mismatch = 0, walkBad = 0, walkSteps = 0;
            for (int i = 0; i < GreedyStudy.DefaultTargets; i++)
            {
                AirwayAddress target = GreedyStudy.Target(101, i);
                string ts = target.ToString();
                targets++;
                for (int k = 0; k <= target.Length; k++)
                {
                    AirwayAddress w = target.Prefix(k);
                    BeaconStep s = Beacon.NextStep(w, target);
                    if (s != RefStep(ts.Substring(0, k), ts)) mismatch++;
                    if (k < target.Length && (s == BeaconStep.Right) != target.IsRight(k)) mismatch++;
                    onRoute++;
                }
                for (int j = 0; j < 16; j++)
                {
                    AirwayAddress w = RandomAddress(1, 23);
                    if (Beacon.NextStep(w, target) != RefStep(w.ToString(), ts)) mismatch++;
                    if (!w.IsPrefixOf(target)) offRoute++; else onRoute++;
                }
                // follow the beacon from a random branch: back up to the route, then down, in exactly GenerationsToTarget steps
                AirwayAddress walker = RandomAddress(0, 23);
                int budget = Beacon.GenerationsToTarget(walker, target), steps = 0;
                bool descended = false, order = true;
                while (steps <= 64)
                {
                    BeaconStep s = Beacon.NextStep(walker, target);
                    if (s == BeaconStep.Arrived) break;
                    if (s == BeaconStep.Back) { if (descended) order = false; } else descended = true;
                    walker = Beacon.Apply(walker, s);
                    steps++;
                }
                if (walker != target || steps != budget || !order) walkBad++;
                walkSteps += steps;
            }
            Check(targets == 1000, "1,000 random targets");
            Check(mismatch == 0, "beacon step equals the address-prefix step on 1,000 random targets (" + mismatch + " mismatches)");
            Check(walkBad == 0, "following the beacon from any branch backs up to the route and reaches the target in the fewest steps (" + walkBad + " failures)");
            Console.WriteLine("BEACON: " + targets + " targets, " + onRoute + " walker positions on the route and " + offRoute + " off it, "
                + mismatch + " mismatches against the address-prefix step; " + walkSteps + " steps walked in 1,000 walks from random branches, " + walkBad + " failures");

            AirwayAddress tg = AirwayAddress.Parse("RRRL");
            Check(Beacon.NextStep(AirwayAddress.Trachea, tg) == BeaconStep.Right && Beacon.NextStep(AirwayAddress.Parse("RRR"), tg) == BeaconStep.Left
                && Beacon.NextStep(tg, tg) == BeaconStep.Arrived && Beacon.NextStep(AirwayAddress.Parse("L"), tg) == BeaconStep.Back
                && Beacon.NextStep(AirwayAddress.Parse("RRRLL"), tg) == BeaconStep.Back && Beacon.NextStep(AirwayAddress.Parse("RRL"), tg) == BeaconStep.Back,
                "next step on the route, at the target, off the route and past the target");
            Check(Beacon.GenerationsToTarget(AirwayAddress.Parse("LL"), tg) == 6 && Beacon.GenerationsToTarget(tg, tg) == 0, "generations to the target");

            // the 8 s rule, as a function of the time passed in
            Check(Beacon.HesitationSeconds == 8.0, "hesitation is 8 s");
            Check(!Beacon.Shown(0.0, false) && !Beacon.Shown(7.999, false) && Beacon.Shown(8.0, false) && Beacon.Shown(60.0, false), "beacon shown after 8 s of hesitation");
            Check(!Beacon.Shown(8.0, true) && !Beacon.Shown(1e6, true) && !Beacon.Shown(0.0, true), "beacon never shown at the Two Compasses fork");

            ForkEvent e = Beacon.Fork(AirwayAddress.Parse("RR"), AirwayChoice.Right, tg, 3.25, false);
            Check(e.Address.ToString() == "RR" && e.Choice == AirwayChoice.Right && e.Correct && !e.BeaconShown && e.TSeconds == 3.25, "fork event of a quick, correct choice");
            e = Beacon.Fork(AirwayAddress.Parse("RR"), AirwayChoice.Left, tg, 9.5, false);
            Check(!e.Correct && e.BeaconShown, "fork event of a late, wrong choice: beacon was shown");
            e = Beacon.Fork(AirwayAddress.Trachea, AirwayChoice.Right, tg, 30.0, true);
            Check(e.Correct && !e.BeaconShown, "fork event at the Two Compasses fork: beacon not shown however long");
            e = Beacon.Fork(AirwayAddress.Parse("RL"), AirwayChoice.Left, tg, 1.0, false);
            Check(!e.Correct, "no child of a fork off the route is correct");
            Throws<ArgumentException>(() => Beacon.Fork(AirwayAddress.Trachea, AirwayChoice.None, tg, 1.0, false), "a fork event needs a letter");
        }

        // ============================================================ route cost (P2-B06)
        static void TestRouteCostSymmetric()
        {
            // "In the symmetric model the lumen falls below 4.2 mm at generation 7 (derived)"
            int g42 = RouteCost.SymmetricFirstGenerationBelow(RouteCost.ScopeOdB);
            Check(g42 == 7, "symmetric model: the lumen falls below 4.2 mm at generation 7 (got " + g42 + ")");
            double d6 = 18.0 * Math.Pow(2.0, -6.0 / 3.0), d7 = 18.0 * Math.Pow(2.0, -7.0 / 3.0);
            Check(d6 >= 4.2 && d7 < 4.2, "18 * 2^(-g/3): " + d6.ToString("F4", Inv) + " mm at generation 6, " + d7.ToString("F4", Inv) + " mm at generation 7");
            Check(RouteCost.ScopeOdA == 3.5 && RouteCost.ScopeOdB == 4.2 && RouteCost.ScopeOdC == 4.0 && RouteCost.SharpTurnDeg == 45.0, "scope outer diameters 3.5 / 4.2 / 4.0 mm, sharp turn above 45 degrees");
            Console.WriteLine("ROUTECOST symmetric model: lumen first below 4.2 mm at generation " + g42 + " (" + d7.ToString("F4", Inv) + " mm), below 3.5 mm at generation "
                + RouteCost.SymmetricFirstGenerationBelow(3.5) + ", below 4.0 mm at generation " + RouteCost.SymmetricFirstGenerationBelow(4.0));

            Check(Math.Abs(RouteCost.BranchAngleDeg(new Vec3d(0, 0, -1), new Vec3d(1, 0, 0)) - 90.0) < 1e-12
                && RouteCost.BranchAngleDeg(new Vec3d(0, 0, -1), new Vec3d(0, 0, -1)) == 0.0, "branch angle from two directions");

            // against a plain loop over the nodes, 500 random routes
            var r = new AirwayResolver(101);
            int bad = 0;
            for (int i = 0; i < 500; i++)
            {
                AirwayAddress t = RandomAddress(0, 23);
                RouteCost c = RouteCost.Compute(r, t);
                double depth = 0, narrow = double.PositiveInfinity; int sharp = 0, ngen = -1, first42 = -1;
                for (int k = 0; k <= t.Length; k++)
                {
                    AirwayNode n = r.Node(t.Prefix(k));
                    depth += n.LenMm;
                    if (n.DiameterMm < narrow) { narrow = n.DiameterMm; ngen = k; }
                    if (k > 0 && r.BranchAngleDeg(t.Prefix(k)) > 45.0) sharp++;
                    if (first42 < 0 && n.DiameterMm < 4.2) first42 = k;
                }
                if (!BitEqual(c.InsertionDepthMm, depth) || c.SharpTurns != sharp || c.NarrowestLumenMm != narrow || c.NarrowestGen != ngen || c.Branches != t.Length + 1) bad++;
                if (RouteCost.FirstGenerationBelow(r, t, 4.2) != first42 || c.Passes(4.2) != (first42 < 0)) bad++;
            }
            Check(bad == 0, "route cost equals a plain sum over the route's nodes on 500 random routes (" + bad + " differ)");
        }

        // ============================================================ event payloads
        static void TestEvents()
        {
            Check(AirwayEvents.ForkName == "fork" && AirwayEvents.CompassChoiceName == "compass_choice", "event names as the plan writes them");
            string j = new ForkEvent(AirwayAddress.Parse("RR"), AirwayChoice.Right, true, false, 3.254).ToJson();
            Check(j == "{\"address\":\"RR\",\"choice\":\"R\",\"correct\":true,\"beacon_shown\":false,\"t_s\":3.25}", "fork JSON: " + j);
            var o = CanonicalJson.AsObject(CanonicalJson.Parse(j), "fork");
            Check(o.Count == 5 && o.ContainsKey("address") && o.ContainsKey("choice") && o.ContainsKey("correct") && o.ContainsKey("beacon_shown") && o.ContainsKey("t_s"),
                "fork payload has exactly {address, choice, correct, beacon_shown, t_s}");
            j = new ForkEvent(AirwayAddress.Trachea, AirwayChoice.Left, false, true, 12.0).ToJson();
            Check(j == "{\"address\":\"\",\"choice\":\"L\",\"correct\":false,\"beacon_shown\":true,\"t_s\":12.00}", "fork JSON at the trachea: " + j);

            var reading = new CompassReading(AirwayAddress.Trachea, AirwayChoice.Left, AirwayChoice.Right, 0.1420471);
            Check(reading.Disagree, "compass L, beacon R disagree");
            CompassChoiceEvent c = TwoCompasses.Choice(reading, AirwayChoice.Right, 6.5);
            Check(c.Choice == CompassFollowed.Beacon && c.Correct, "taking the beacon's child is \"beacon\", correct");
            j = c.ToJson();
            Check(j == "{\"address\":\"\",\"choice\":\"beacon\",\"correct\":true,\"taken\":\"R\",\"compass\":\"L\",\"beacon\":\"R\",\"margin\":0.142047,\"t_s\":6.50}", "compass_choice JSON: " + j);
            c = TwoCompasses.Choice(reading, AirwayChoice.Left, 2.0);
            Check(c.Choice == CompassFollowed.Compass && !c.Correct && c.ToJson().Contains("\"choice\":\"compass\",\"correct\":false"), "taking the compass's child is \"compass\", not correct");
            var agree = new CompassReading(AirwayAddress.Parse("R"), AirwayChoice.Right, AirwayChoice.Right, -0.5);
            Check(!agree.Disagree && TwoCompasses.Choice(agree, AirwayChoice.Right, 1).Choice == CompassFollowed.Both
                && TwoCompasses.Choice(agree, AirwayChoice.Left, 1).Choice == CompassFollowed.Neither, "where the compasses agree: both or neither");
            var tie = new CompassReading(AirwayAddress.Parse("R"), AirwayChoice.None, AirwayChoice.Right, 0.0);
            Check(!tie.Disagree && TwoCompasses.Choice(tie, AirwayChoice.Left, 1).ToJson().Contains("\"compass\":\"\""), "an exact tie is not a disagreement");
            Throws<ArgumentException>(() => TwoCompasses.Choice(reading, AirwayChoice.None, 1.0), "a compass_choice event needs a letter");
            Throws<ArgumentException>(() => new ForkEvent(AirwayAddress.Trachea, AirwayChoice.Left, true, false, double.NaN).ToJson(), "NaN time rejected");

            // compass on hand-made geometry
            Vec3d fork = new Vec3d(0, 0, 0), dl = new Vec3d(1, 0, 0), dr = new Vec3d(0, 1, 0);
            Check(TwoCompasses.Compass(fork, new Vec3d(5, 1, 0), dl, dr) == AirwayChoice.Left && TwoCompasses.Compass(fork, new Vec3d(1, 5, 0), dl, dr) == AirwayChoice.Right
                && TwoCompasses.Compass(fork, new Vec3d(3, 3, 9), dl, dr) == AirwayChoice.None, "Euclidean compass picks the child closer to the straight line; a tie picks none");
            CompassReading m = TwoCompasses.Read(AirwayAddress.Trachea, AirwayAddress.Parse("R"), fork, new Vec3d(3, 4, 0), dl, dr);
            Check(m.Beacon == AirwayChoice.Right && m.Compass == AirwayChoice.Right && Math.Abs(m.Margin - (0.6 - 0.8)) < 1e-15, "margin = cos(off) - cos(on) = 0.6 - 0.8");
            Throws<ArgumentException>(() => TwoCompasses.BeaconChoice(AirwayAddress.Parse("L"), AirwayAddress.Parse("R")), "beacon choice needs a fork on the route");
        }

        // ============================================================ allocation and time
        static void TestNoAllocation()
        {
            var r = new AirwayResolver(101);
            AirwayAddress target = AirwayAddress.Parse("RRRL");
            Span<AirwayAddress> window = stackalloc AirwayAddress[AirwayWindow.MaxNodes];
            Span<AirwayStep> steps = stackalloc AirwayStep[2 * AirwayAddress.MaxLength];
            double sink = 0;
            for (int warm = 0; warm < 2; warm++)
            {
                const int N = 20000;
                rng = 12345;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < N; i++)
                {
                    AirwayAddress a = RandomAddress(16);
                    AirwayNode n = r.Node(a);
                    sink += n.PosMm.X + n.LenMm;
                }
                sw.Stop();
                long mid = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < N; i++)
                {
                    AirwayAddress a = RandomAddress(0, 16);
                    BeaconStep s = Beacon.NextStep(a, target);
                    sink += (int)s + Beacon.GenerationsToTarget(a, target) + (Beacon.Shown(0.001 * i, (i & 1) == 0) ? 1 : 0);
                    ForkEvent e = Beacon.Fork(a, AirwayChoice.Left, target, 0.001 * i, false);
                    sink += e.Correct ? 1 : 0;
                    CompassReading c = TwoCompasses.At(r, target.Prefix(i & 3), target);
                    sink += c.Margin + TwoCompasses.Choice(c, AirwayChoice.Right, 1.0).Margin;
                    sink += RouteCost.Compute(r, a.Length > 8 ? a.Prefix(8) : a).InsertionDepthMm;
                    sink += AirwayWindow.Fill(a, 16, window) + AirwaySteps.Fill(a, steps) + (int)(AirwaySteps.Fold(a, 0UL, new CodeStepper()) % 3UL);
                }
                long after = GC.GetAllocatedBytesForCurrentThread();
                if (warm == 1)
                {
                    Check(mid - before == 0, "no allocation in " + N + " lazy resolutions of generation-16 addresses (" + (mid - before) + " bytes)");
                    Check(after - mid == 0, "no allocation in " + N + " beacon + fork event + compass + route cost + window + steps calls (" + (after - mid) + " bytes)");
                    Console.WriteLine("ALLOC resolve: " + (mid - before) + " bytes over " + N + " random generation-16 addresses; "
                        + (sw.Elapsed.TotalMilliseconds * 1000.0 / N).ToString("F1", Inv) + " us per address on this machine (16 rule steps, 3 SHA-256 each; a walk recomputes 1 step)");
                    Console.WriteLine("ALLOC navigation: " + (after - mid) + " bytes over " + N + " iterations (beacon, fork event, compasses, route cost, window, steps)" + (sink == 0 ? " " : ""));
                }
            }
        }

        // ============================================================ tree.json and greedy.json
        static string Q(double v) { return CanonicalJson.FormatFloat(v); }

        static void TestTree(string dir, string name)
        {
            string treePath = Path.Combine(dir, "tree.json"), greedyPath = Path.Combine(dir, "greedy.json");
            if (!File.Exists(treePath) || !File.Exists(greedyPath)) { Check(false, name + ": tree.json and greedy.json exist in " + dir); return; }
            AirwayTreeFile tree = AirwayTreeFile.Parse(File.ReadAllBytes(treePath));
            GreedyFile greedy = GreedyFile.Parse(File.ReadAllBytes(greedyPath));
            Check(tree.Label == "Model-generated airway" && greedy.Label == "Model-generated airway", name + ": label is \"Model-generated airway\"");
            Check(tree.AddressableGen == 16 && tree.MaxAddressLen == 23, name + ": addressable to generation 16, addresses up to 23 letters");
            Check(tree.Nodes.Length == (1 << (tree.MaterialisedGen + 1)) - 1, name + ": every generation 0.." + tree.MaterialisedGen + " is in the file");

            // ---- P2-B01: resolver == tree.json on every materialised node
            var resolver = new AirwayResolver(tree.Seed);
            double worstMm = 0, worstDir = 0;
            int values = 0, quantEqual = 0;
            string worstAt = "";
            int total = tree.Nodes.Length + tree.TargetRouteNodes.Length;
            for (int i = 0; i < total; i++)
            {
                AirwayNode f = i < tree.Nodes.Length ? tree.Nodes[i] : tree.TargetRouteNodes[i - tree.Nodes.Length];
                AirwayNode n = resolver.Node(f.Address);
                for (int k = 0; k < 8; k++)
                {
                    double got = k == 0 ? n.RadiusMm : k == 1 ? n.LenMm : k < 5 ? n.PosMm[k - 2] : n.Dir[k - 5];
                    double want = k == 0 ? f.RadiusMm : k == 1 ? f.LenMm : k < 5 ? f.PosMm[k - 2] : f.Dir[k - 5];
                    double d = Math.Abs(got - want);
                    if (k < 5) { if (d > worstMm) { worstMm = d; worstAt = f.Address + " value " + k; } }
                    else if (d > worstDir) worstDir = d;
                    values++;
                    if (Q(got) == Q(want)) quantEqual++;
                }
            }
            const double FileTol = 5.0e-7 + 1e-12; // half of the file's 1e-6 resolution, plus the rounding of the product v * 1e6
            Check(worstMm <= FileTol && worstDir <= FileTol, name + ": C# resolver within half a file step (5e-7) of tree.json on every node (worst " + E(worstMm) + " mm, " + E(worstDir) + " dir)");
            Check(quantEqual == values, name + ": every value of every node equals tree.json after the file's 1e-6 quantisation (" + quantEqual + "/" + values + ")");
            Console.WriteLine("TREE " + name + ": " + total + " nodes (generations 0.." + tree.MaterialisedGen + "), " + quantEqual + "/" + values
                + " values equal tree.json after the file's 1e-6 quantisation; worst |C# - file| " + E(worstMm) + " mm (" + worstAt + "), " + E(worstDir) + " in dir; the file's resolution is 1e-6");

            // ---- P2-B08: compass against beacon on the four-fork route
            Check(tree.FourForks.Length == 4, name + ": the route has 4 forks");
            Check(tree.HasCompassFork, name + ": the file names a Two Compasses fork");
            if (!tree.HasCompassFork) return;
            int differ = 0, agree = 0, fileDiffer = 0;
            var sb = new StringBuilder();
            bool beaconRule = true;
            for (int i = 0; i < tree.FourForks.Length; i++)
            {
                AirwayAddress fork = tree.FourForks[i];
                Check(fork == tree.Target.Prefix(i), name + ": fork " + i + " of the route is a prefix of the target");
                CompassReading rule = TwoCompasses.At(resolver, fork, tree.Target), file = TwoCompasses.At(tree, fork, tree.Target);
                bool designed = tree.IsTwoCompassesFork(fork);
                Check(rule.Disagree == designed, name + ": at fork \"" + fork + "\" compass " + AirwayAddress.Letter(rule.Compass) + ", beacon " + AirwayAddress.Letter(rule.Beacon)
                    + (designed ? " must differ (the designed fork)" : " must agree"));
                Check(file.Compass == rule.Compass && file.Beacon == rule.Beacon && Math.Abs(file.Margin - rule.Margin) < 5e-6, name + ": the file's values give the same compass at fork \"" + fork + "\"");
                Check((rule.Margin > 0) == rule.Disagree, name + ": the margin is positive exactly where the compasses disagree");
                Check(rule.Beacon == (Beacon.NextStep(fork, tree.Target) == BeaconStep.Right ? AirwayChoice.Right : AirwayChoice.Left), name + ": the beacon of the compass reading is the beacon step");
                if (rule.Disagree) differ++; else agree++;
                if (file.Disagree) fileDiffer++;
                beaconRule &= Beacon.Shown(8.0, tree.IsTwoCompassesFork(fork)) == !designed && !Beacon.Shown(7.9, tree.IsTwoCompassesFork(fork));
                sb.Append(i == 0 ? "" : "; ").Append('"').Append(fork.ToString()).Append("\" compass ").Append(AirwayAddress.Letter(rule.Compass)).Append(" beacon ")
                  .Append(AirwayAddress.Letter(rule.Beacon)).Append(" margin ").Append(Q(rule.Margin));
            }
            Check(differ == 1 && agree == 3 && fileDiffer == 1, name + ": exactly 1 Two Compasses fork on the 4-fork route (" + differ + " differ, " + agree + " agree)");
            Check(tree.CompassWrongForks.Length == 1 && tree.CompassWrongForks[0] == tree.CompassFork, name + ": compass_wrong_forks is the designed fork alone");
            Check(beaconRule, name + ": after 8 s the beacon is shown at the three other forks and not at the Two Compasses fork");
            CompassReading at = TwoCompasses.At(resolver, tree.CompassFork, tree.Target);
            Check(Q(at.Margin) == Q(tree.CompassForkMargin), name + ": margin at the designed fork " + Q(at.Margin) + " equals tree.json " + Q(tree.CompassForkMargin));
            Console.WriteLine("COMPASS " + name + ": target \"" + tree.Target + "\", designed fork \"" + tree.CompassFork + "\": compass " + AirwayAddress.Letter(at.Compass)
                + ", beacon " + AirwayAddress.Letter(at.Beacon) + ", margin " + Q(at.Margin) + " (tree.json " + Q(tree.CompassForkMargin) + "); "
                + differ + " fork differs, " + agree + " agree: " + sb);

            // ---- the target choice, re-derived from the rule
            var cands = new List<AirwayCandidate>();
            for (uint b = 0; b < (1u << tree.TargetGen); b++)
            {
                var t = new AirwayAddress(b, tree.TargetGen);
                int wrong = 0; CompassReading w = default(CompassReading);
                for (int k = 0; k < 4; k++)
                {
                    CompassReading c = TwoCompasses.At(resolver, t.Prefix(k), t);
                    if (c.Disagree) { wrong++; w = c; }
                }
                if (wrong == 1) cands.Add(new AirwayCandidate(t, w.Fork, w.Margin));
            }
            bool same = cands.Count == tree.Candidates.Length;
            int best = -1;
            for (int i = 0; same && i < cands.Count; i++)
            {
                same &= cands[i].Target == tree.Candidates[i].Target && cands[i].CompassFork == tree.Candidates[i].CompassFork && Q(cands[i].Margin) == Q(tree.Candidates[i].Margin);
                if (best < 0 || cands[i].Margin > cands[best].Margin) best = i;
            }
            Check(same, name + ": the " + tree.Candidates.Length + " candidates of tree.json and their margins are reproduced from the rule (" + cands.Count + " found)");
            Check(best >= 0 && cands[best].Target == tree.Target && cands[best].CompassFork == tree.CompassFork, name + ": the target is the candidate with the largest margin");
            var ms = new StringBuilder();
            foreach (AirwayCandidate c in cands) ms.Append(ms.Length == 0 ? "" : ", ").Append(c.Target.ToString()).Append(' ').Append(Q(c.Margin));
            Console.WriteLine("MARGINS " + name + ": " + cands.Count + " candidates reproduced, margins equal tree.json: " + ms);

            // ---- routes and route cost
            Check(tree.FourForksEnd == tree.Target.Prefix(4) && tree.OneForkEnd == tree.Target && tree.OneFork == tree.Target.Parent, name + ": four-fork and one-fork routes end as the contract says");
            Check(AirwaySteps.Count(tree.FourForksEnd) == 8 && AirwaySteps.Count(tree.OneForkEnd) - AirwaySteps.Count(tree.OneFork) == 2, name + ": 8 tile steps on the four-fork route, 2 on the one-fork route");
            RouteCost cost = RouteCost.Compute(resolver, tree.Target);
            double depthFile = 0, narrowFile = double.PositiveInfinity;
            for (int k = 0; k <= tree.Target.Length; k++)
            {
                AirwayNode f = tree.Node(tree.Target.Prefix(k));
                depthFile += f.LenMm; narrowFile = Math.Min(narrowFile, 2.0 * f.RadiusMm);
            }
            Check(Math.Abs(cost.InsertionDepthMm - depthFile) <= (tree.Target.Length + 1) * 5e-7 + 1e-9 && Math.Abs(cost.NarrowestLumenMm - narrowFile) <= 1.01e-6,
                name + ": insertion depth is the sum of len_mm of the file's nodes on the route");
            Console.WriteLine("ROUTECOST " + name + ": route to \"" + tree.Target + "\": insertion depth " + cost.InsertionDepthMm.ToString("F3", Inv) + " mm (file sum " + depthFile.ToString("F3", Inv)
                + "), " + cost.SharpTurns + " sharp turns (> 45 deg), narrowest lumen " + cost.NarrowestLumenMm.ToString("F3", Inv) + " mm at generation " + cost.NarrowestGen
                + "; passes 3.5 / 4.2 / 4.0 mm scope: " + cost.Passes(3.5) + " / " + cost.Passes(4.2) + " / " + cost.Passes(4.0));

            // ---- greedy study re-run from the rule
            GreedyResult g = GreedyStudy.Run(greedy.TreeSeed, greedy.Seed, greedy.NTargets, greedy.TargetGen);
            Check(greedy.TreeSeed == tree.Seed && greedy.TreeRule == AirwayResolver.Rule, name + ": greedy.json studies this tree");
            Check(greedy.NTargets == 1000 && greedy.TargetGen == 16, name + ": the study has 1,000 targets of generation 16");
            Check(g.WrongForks == greedy.WrongForks, name + ": greedy study wrong forks " + g.WrongForks + " equal greedy.json " + greedy.WrongForks);
            Check(g.Forks == greedy.Forks && g.RoutesWithWrongFork == greedy.RoutesWithWrongFork && g.NDistinctTargets == greedy.NDistinctTargets,
                name + ": forks, routes with a wrong fork and distinct targets equal greedy.json (" + g.Forks + ", " + g.RoutesWithWrongFork + ", " + g.NDistinctTargets + ")");
            int genBad = 0;
            for (int k = 0; k < greedy.TargetGen; k++) if (g.ForksByGen[k] != greedy.ForksByGen[k] || g.WrongByGen[k] != greedy.WrongByGen[k]) genBad++;
            Check(genBad == 0, name + ": wrong forks by generation equal greedy.json at all " + greedy.TargetGen + " generations (" + genBad + " differ)");
            Check(Q(g.WrongShare) == Q(greedy.WrongShare), name + ": wrong share " + Q(g.WrongShare) + " equals greedy.json " + Q(greedy.WrongShare));
            Check(g.WrongByGen[greedy.TargetGen - 1] == 0, name + ": the compass is never wrong at the last fork before the target");
            Console.WriteLine("GREEDY " + name + ": " + g.WrongForks + " wrong forks of " + g.Forks + " over " + g.NTargets + " targets (greedy.json " + greedy.WrongForks + " of " + greedy.Forks
                + "), share " + Q(g.WrongShare) + " (" + Q(greedy.WrongShare) + "), routes with a wrong fork " + g.RoutesWithWrongFork + " (" + greedy.RoutesWithWrongFork + "), by generation "
                + (greedy.TargetGen - genBad) + "/" + greedy.TargetGen + " equal");
        }

        static void TestRejections(string dir)
        {
            string text = File.ReadAllText(Path.Combine(dir, "tree.json"));
            Throws<ContractException>(() => AirwayTreeFile.Parse(text.Replace("\"schema\":\"atlas.tree/2\"", "\"schema\":\"atlas.tree/1\"")), "another schema is rejected");
            Throws<ContractException>(() => AirwayTreeFile.Parse(text.Replace("\"rule\":\"atlas.airway/2\"", "\"rule\":\"atlas.airway/1\"")), "another rule is rejected");
            int at = text.IndexOf("\"source\":\"model\"", StringComparison.Ordinal);
            Throws<ContractException>(() => AirwayTreeFile.Parse(text.Substring(0, at) + "\"source\":\"ct\"" + text.Substring(at + 16)), "a node that is not \"model\" is rejected");
            Throws<ContractException>(() => AirwayTreeFile.Parse(text.Replace("\"label\":\"Model-generated airway\"", "\"label\":\"Patient airway\"")), "another label is rejected");
            Throws<ContractException>(() => AirwayTreeFile.Parse(text.Replace("\"address\":\"LR\",", "\"address\":\"RL\",")), "nodes out of breadth-first order are rejected");
            Throws<ContractException>(() => AirwayTreeFile.Parse(text.Replace("\"materialised_gen\":10", "\"materialised_gen\":9")), "a node count that does not match materialised_gen is rejected");
            AirwayTreeFile none = AirwayTreeFile.Parse(text.Replace("\"compass_fork\":{\"address\":\"\",\"margin\":0.142047}", "\"compass_fork\":{\"address\":null,\"margin\":null}"));
            Check(!none.HasCompassFork && !none.IsTwoCompassesFork(AirwayAddress.Trachea), "a file without a Two Compasses fork reads as having none");
            AirwayTreeFile tree = AirwayTreeFile.Parse(text);
            Throws<ContractException>(() => tree.Node(AirwayAddress.Parse(new string('L', tree.MaterialisedGen + 1))), "a node below materialised_gen is not in the file");
            string gtext = File.ReadAllText(Path.Combine(dir, "greedy.json"));
            Throws<ContractException>(() => GreedyFile.Parse(Encoding.UTF8.GetBytes(gtext.Replace("atlas.greedy/1\",\"seed", "atlas.greedy/2\",\"seed"))), "another greedy schema is rejected");
        }

        // ============================================================ real package
        static void TestRealCase(string fixture)
        {
            string cases = Environment.GetEnvironmentVariable("ATLAS_CASES_DIR");
            string dir = string.IsNullOrEmpty(cases) ? null : Path.Combine(cases, "lung101", "airway");
            if (dir == null || !File.Exists(Path.Combine(dir, "tree.json")))
            {
                Console.WriteLine("NOT RUN: real-case acceptance (P2-B01, P2-B06, P2-B08 on the built lung101 package): set ATLAS_CASES_DIR to the cases directory holding lung101/airway/tree.json");
                return;
            }
            TestTree(dir, "lung101");
            if (fixture != null)
            {
                bool same = BytesEqual(Path.Combine(dir, "tree.json"), Path.Combine(fixture, "tree.json")) && BytesEqual(Path.Combine(dir, "greedy.json"), Path.Combine(fixture, "greedy.json"));
                Check(same, "the committed copies of tree.json and greedy.json are byte-equal to the built package");
                Console.WriteLine("REAL-CASE lung101: committed tree.json and greedy.json " + (same ? "are byte-equal to" : "DIFFER from") + " the built package");
            }
        }

        static bool BytesEqual(string a, string b)
        {
            byte[] x = File.ReadAllBytes(a), y = File.ReadAllBytes(b);
            if (x.Length != y.Length) return false;
            for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
            return true;
        }
    }
}

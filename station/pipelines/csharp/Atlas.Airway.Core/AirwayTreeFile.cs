// Readers of airway/tree.json (schema atlas.tree/2) and airway/greedy.json (schema atlas.greedy/1),
// docs/case_contract.md sections 10.3 and 10.4. Use the generic parser of Atlas.Cases.CanonicalJson.
// The file holds generations 0..materialised_gen quantised to 1e-6; deeper nodes come from AirwayResolver.
using System.Collections.Generic;
using Atlas.Cases;

namespace Atlas.Airway
{
    /// <summary>One entry of target_choice.candidates.</summary>
    public readonly struct AirwayCandidate
    {
        public readonly AirwayAddress Target, CompassFork;
        public readonly double Margin;
        public AirwayCandidate(AirwayAddress target, AirwayAddress compassFork, double margin)
        {
            Target = target; CompassFork = compassFork; Margin = margin;
        }
    }

    public sealed class AirwayTreeFile
    {
        public const string SchemaName = "atlas.tree/2";
        public const string LabelText = "Model-generated airway";

        public string Label, Frame;
        public long Seed;
        public int AddressableGen, MaxAddressLen, MaterialisedGen;
        /// <summary>Generations 0..MaterialisedGen, breadth-first, L before R: the node of address a is Nodes[a.NodeIndex].
        /// Normal is (0, 0, 0): the file does not store it.</summary>
        public AirwayNode[] Nodes;
        /// <summary>Present only when the target is deeper than MaterialisedGen; otherwise empty.</summary>
        public AirwayNode[] TargetRouteNodes;
        public AirwayAddress Target;
        /// <summary>False when the file names no Two Compasses fork (compass_fork.address is null).</summary>
        public bool HasCompassFork;
        public AirwayAddress CompassFork;
        public double CompassForkMargin;
        public int TargetGen;
        public AirwayCandidate[] Candidates;
        public bool SeedSelected;
        public AirwayAddress[] FourForks;
        public AirwayAddress FourForksEnd;
        public AirwayAddress[] CompassWrongForks;
        public AirwayAddress OneFork, OneForkEnd;

        public static AirwayTreeFile Parse(byte[] utf8)
        {
            return FromObject(CanonicalJson.AsObject(CanonicalJson.Parse(utf8), "tree.json"));
        }

        public static AirwayTreeFile Parse(string text)
        {
            return FromObject(CanonicalJson.AsObject(CanonicalJson.Parse(text), "tree.json"));
        }

        /// <summary>True when the fork is the one Two Compasses fork the file names (the beacon is not shown there).</summary>
        public bool IsTwoCompassesFork(AirwayAddress fork) { return HasCompassFork && fork == CompassFork; }

        public bool TryGetNode(AirwayAddress address, out AirwayNode node)
        {
            if (address.Length <= MaterialisedGen) { node = Nodes[address.NodeIndex]; return true; }
            for (int i = 0; i < TargetRouteNodes.Length; i++)
                if (TargetRouteNodes[i].Address == address) { node = TargetRouteNodes[i]; return true; }
            node = default(AirwayNode);
            return false;
        }

        /// <summary>The node as the file stores it; throws ContractException for an address the file does not hold.</summary>
        public AirwayNode Node(AirwayAddress address)
        {
            AirwayNode n;
            if (!TryGetNode(address, out n))
                throw new ContractException("tree.json: no node " + address + " (the file holds generations 0.." + MaterialisedGen + "; resolve deeper nodes from the rule)");
            return n;
        }

        static AirwayTreeFile FromObject(Dictionary<string, object> o)
        {
            const string W = "tree.json";
            string schema = CanonicalJson.AsString(CanonicalJson.Need(o, "schema", W), "schema");
            if (schema != SchemaName) throw new ContractException("tree.json: schema is " + schema + ", expected " + SchemaName);
            string rule = CanonicalJson.AsString(CanonicalJson.Need(o, "rule", W), "rule");
            if (rule != AirwayResolver.Rule) throw new ContractException("tree.json: rule is " + rule + ", this reader implements " + AirwayResolver.Rule);

            var t = new AirwayTreeFile();
            t.Label = CanonicalJson.AsString(CanonicalJson.Need(o, "label", W), "label");
            if (t.Label != LabelText) throw new ContractException("tree.json: label is not \"" + LabelText + "\"");
            t.Frame = o.ContainsKey("frame") ? CanonicalJson.AsString(o["frame"], "frame") : "";
            t.Seed = CanonicalJson.AsLong(CanonicalJson.Need(o, "seed", W), "seed");
            t.AddressableGen = (int)CanonicalJson.AsLong(CanonicalJson.Need(o, "addressable_gen", W), "addressable_gen");
            t.MaxAddressLen = (int)CanonicalJson.AsLong(CanonicalJson.Need(o, "max_address_len", W), "max_address_len");
            if (t.MaxAddressLen != AirwayAddress.MaxLength)
                throw new ContractException("tree.json: max_address_len is " + t.MaxAddressLen + ", this reader holds addresses of up to " + AirwayAddress.MaxLength);
            long mg = CanonicalJson.AsLong(CanonicalJson.Need(o, "materialised_gen", W), "materialised_gen");
            if (mg < 0 || mg > 20) throw new ContractException("tree.json: materialised_gen out of range");
            t.MaterialisedGen = (int)mg;

            var nodes = CanonicalJson.AsArray(CanonicalJson.Need(o, "nodes", W), "nodes");
            int want = (1 << (t.MaterialisedGen + 1)) - 1;
            if (nodes.Count != want) throw new ContractException("tree.json: nodes has " + nodes.Count + " entries, generations 0.." + t.MaterialisedGen + " are " + want);
            t.Nodes = new AirwayNode[want];
            for (int i = 0; i < want; i++)
            {
                t.Nodes[i] = ReadNode(nodes[i], "nodes[" + i + "]");
                if (t.Nodes[i].Address.NodeIndex != i) throw new ContractException("tree.json: nodes[" + i + "] is not in breadth-first order (address " + t.Nodes[i].Address + ")");
            }
            if (o.ContainsKey("target_route_nodes"))
            {
                var extra = CanonicalJson.AsArray(o["target_route_nodes"], "target_route_nodes");
                t.TargetRouteNodes = new AirwayNode[extra.Count];
                for (int i = 0; i < extra.Count; i++) t.TargetRouteNodes[i] = ReadNode(extra[i], "target_route_nodes[" + i + "]");
            }
            else t.TargetRouteNodes = new AirwayNode[0];

            var target = CanonicalJson.AsObject(CanonicalJson.Need(o, "target", W), "target");
            t.Target = Address(CanonicalJson.Need(target, "address", "target"), "target.address");

            var cf = CanonicalJson.AsObject(CanonicalJson.Need(o, "compass_fork", W), "compass_fork");
            object cfa = CanonicalJson.Need(cf, "address", "compass_fork"), cfm = CanonicalJson.Need(cf, "margin", "compass_fork");
            if ((cfa == null) != (cfm == null)) throw new ContractException("tree.json: compass_fork address and margin must both be null or both be set");
            t.HasCompassFork = cfa != null;
            if (t.HasCompassFork)
            {
                t.CompassFork = Address(cfa, "compass_fork.address");
                t.CompassForkMargin = CanonicalJson.AsDouble(cfm, "compass_fork.margin");
                if (!t.CompassFork.IsProperPrefixOf(t.Target)) throw new ContractException("tree.json: compass_fork is not on the route to the target");
            }

            var tc = CanonicalJson.AsObject(CanonicalJson.Need(o, "target_choice", W), "target_choice");
            t.TargetGen = (int)CanonicalJson.AsLong(CanonicalJson.Need(tc, "target_gen", "target_choice"), "target_choice.target_gen");
            object ss = CanonicalJson.Need(tc, "seed_selected", "target_choice");
            if (!(ss is bool)) throw new ContractException("tree.json: target_choice.seed_selected must be a boolean");
            t.SeedSelected = (bool)ss;
            var cands = CanonicalJson.AsArray(CanonicalJson.Need(tc, "candidates", "target_choice"), "target_choice.candidates");
            t.Candidates = new AirwayCandidate[cands.Count];
            for (int i = 0; i < cands.Count; i++)
            {
                var c = CanonicalJson.AsObject(cands[i], "candidate");
                t.Candidates[i] = new AirwayCandidate(Address(CanonicalJson.Need(c, "target", "candidate"), "candidate.target"),
                    Address(CanonicalJson.Need(c, "compass_fork", "candidate"), "candidate.compass_fork"),
                    CanonicalJson.AsDouble(CanonicalJson.Need(c, "margin", "candidate"), "candidate.margin"));
            }

            var routes = CanonicalJson.AsObject(CanonicalJson.Need(o, "routes", W), "routes");
            var four = CanonicalJson.AsObject(CanonicalJson.Need(routes, "four_forks", "routes"), "routes.four_forks");
            t.FourForks = Addresses(CanonicalJson.Need(four, "forks", "four_forks"), "four_forks.forks");
            t.FourForksEnd = Address(CanonicalJson.Need(four, "end", "four_forks"), "four_forks.end");
            t.CompassWrongForks = Addresses(CanonicalJson.Need(four, "compass_wrong_forks", "four_forks"), "four_forks.compass_wrong_forks");
            var one = CanonicalJson.AsObject(CanonicalJson.Need(routes, "one_fork", "routes"), "routes.one_fork");
            var oneForks = Addresses(CanonicalJson.Need(one, "forks", "one_fork"), "one_fork.forks");
            if (oneForks.Length != 1) throw new ContractException("tree.json: routes.one_fork.forks must hold one fork");
            t.OneFork = oneForks[0];
            t.OneForkEnd = Address(CanonicalJson.Need(one, "end", "one_fork"), "one_fork.end");
            return t;
        }

        static AirwayNode ReadNode(object v, string what)
        {
            var n = CanonicalJson.AsObject(v, what);
            if (n.ContainsKey("parent_iso") || n.ContainsKey("R_m")) throw new ContractException("tree.json: " + what + " has a key of the retired layout");
            string source = CanonicalJson.AsString(CanonicalJson.Need(n, "source", what), what + ".source");
            if (source != AirwayNode.Source) throw new ContractException("tree.json: " + what + ".source is " + source + "; every node is \"" + AirwayNode.Source + "\"");
            AirwayAddress a = Address(CanonicalJson.Need(n, "address", what), what + ".address");
            if (CanonicalJson.AsLong(CanonicalJson.Need(n, "gen", what), what + ".gen") != a.Length)
                throw new ContractException("tree.json: " + what + ".gen is not the length of its address");
            double[] p = CanonicalJson.AsDoubles(CanonicalJson.Need(n, "pos_mm", what), 3, what + ".pos_mm");
            double[] d = CanonicalJson.AsDoubles(CanonicalJson.Need(n, "dir", what), 3, what + ".dir");
            return new AirwayNode(a,
                CanonicalJson.AsDouble(CanonicalJson.Need(n, "radius_mm", what), what + ".radius_mm"),
                CanonicalJson.AsDouble(CanonicalJson.Need(n, "len_mm", what), what + ".len_mm"),
                new Vec3d(p[0], p[1], p[2]), new Vec3d(d[0], d[1], d[2]), default(Vec3d));
        }

        static AirwayAddress Address(object v, string what)
        {
            AirwayAddress a;
            if (!AirwayAddress.TryParse(CanonicalJson.AsString(v, what), out a))
                throw new ContractException("tree.json: " + what + " is not an address over {L, R} of at most " + AirwayAddress.MaxLength + " letters");
            return a;
        }

        static AirwayAddress[] Addresses(object v, string what)
        {
            var a = CanonicalJson.AsArray(v, what);
            var r = new AirwayAddress[a.Count];
            for (int i = 0; i < r.Length; i++) r[i] = Address(a[i], what);
            return r;
        }
    }

    /// <summary>The counts of airway/greedy.json. wrong_share is the only number of the study that is quoted.</summary>
    public sealed class GreedyFile
    {
        public const string SchemaName = "atlas.greedy/1";

        public string Label, TreeRule;
        public long TreeSeed, Seed;
        public int NTargets, NDistinctTargets, TargetGen, Forks, WrongForks, RoutesWithWrongFork;
        public double WrongShare;
        public int[] ForksByGen, WrongByGen;

        public static GreedyFile Parse(byte[] utf8)
        {
            const string W = "greedy.json";
            var o = CanonicalJson.AsObject(CanonicalJson.Parse(utf8), W);
            string schema = CanonicalJson.AsString(CanonicalJson.Need(o, "schema", W), "schema");
            if (schema != SchemaName) throw new ContractException("greedy.json: schema is " + schema + ", expected " + SchemaName);
            var g = new GreedyFile();
            g.Label = CanonicalJson.AsString(CanonicalJson.Need(o, "label", W), "label");
            g.TreeRule = CanonicalJson.AsString(CanonicalJson.Need(o, "tree_rule", W), "tree_rule");
            g.TreeSeed = CanonicalJson.AsLong(CanonicalJson.Need(o, "tree_seed", W), "tree_seed");
            g.Seed = CanonicalJson.AsLong(CanonicalJson.Need(o, "seed", W), "seed");
            var method = CanonicalJson.AsObject(CanonicalJson.Need(o, "method", W), "method");
            string rule = CanonicalJson.AsString(CanonicalJson.Need(method, "rule", "method"), "method.rule");
            if (rule != GreedyStudy.Rule) throw new ContractException("greedy.json: method.rule is " + rule + ", expected " + GreedyStudy.Rule);
            g.NTargets = (int)CanonicalJson.AsLong(CanonicalJson.Need(o, "n_targets", W), "n_targets");
            g.NDistinctTargets = (int)CanonicalJson.AsLong(CanonicalJson.Need(o, "n_distinct_targets", W), "n_distinct_targets");
            g.TargetGen = (int)CanonicalJson.AsLong(CanonicalJson.Need(o, "target_gen", W), "target_gen");
            g.Forks = (int)CanonicalJson.AsLong(CanonicalJson.Need(o, "forks", W), "forks");
            g.WrongForks = (int)CanonicalJson.AsLong(CanonicalJson.Need(o, "wrong_forks", W), "wrong_forks");
            g.RoutesWithWrongFork = (int)CanonicalJson.AsLong(CanonicalJson.Need(o, "routes_with_wrong_fork", W), "routes_with_wrong_fork");
            g.WrongShare = CanonicalJson.AsDouble(CanonicalJson.Need(o, "wrong_share", W), "wrong_share");
            var by = CanonicalJson.AsArray(CanonicalJson.Need(o, "by_generation", W), "by_generation");
            if (by.Count != g.TargetGen) throw new ContractException("greedy.json: by_generation does not have target_gen entries");
            g.ForksByGen = new int[by.Count];
            g.WrongByGen = new int[by.Count];
            for (int i = 0; i < by.Count; i++)
            {
                var b = CanonicalJson.AsObject(by[i], "by_generation");
                if (CanonicalJson.AsLong(CanonicalJson.Need(b, "gen", "by_generation"), "gen") != i) throw new ContractException("greedy.json: by_generation is not in order");
                g.ForksByGen[i] = (int)CanonicalJson.AsLong(CanonicalJson.Need(b, "forks", "by_generation"), "forks");
                g.WrongByGen[i] = (int)CanonicalJson.AsLong(CanonicalJson.Need(b, "wrong_forks", "by_generation"), "wrong_forks");
            }
            return g;
        }
    }
}

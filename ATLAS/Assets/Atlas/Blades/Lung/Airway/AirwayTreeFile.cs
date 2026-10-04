using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Atlas.Blades.Lung
{
    // Reader for airway/tree.json, schema atlas.tree/2 (Master Plan §10.2.4; PH-30 / R8: lane B writes the reader). Field names are
    // the file's, because JsonUtility needs them verbatim.
    [Serializable]
    public sealed class AirwayTreeFile
    {
        public const string SchemaId = "atlas.tree/2";

        public string schema;
        public AirwayTreeFileNode[] nodes;
        public AirwayAddressRef target;
        public AirwayAddressRef compass_fork;

        internal bool Validated;

        public static AirwayTreeFile Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            AirwayTreeFile f;
            try { f = JsonUtility.FromJson<AirwayTreeFile>(json); }
            catch (ArgumentException e) { throw new FormatException("tree.json is not valid JSON: " + e.Message); }
            if (f == null) throw new FormatException("tree.json is empty");
            f.Validate();
            return f;
        }

        public static AirwayTreeFile Load(string caseRoot)
        {
            var text = File.ReadAllText(Path.Combine(caseRoot, "airway", "tree.json"));
            if (text.StartsWith("version https://git-lfs", StringComparison.Ordinal))
                throw new InvalidDataException("tree.json is a Git LFS pointer; run git lfs pull");
            return Parse(text);
        }

        // Rules of the brief: schema, non-empty, addresses, gen, duplicates, root and parents, vector lengths, positive len and radius,
        // target and compass fork present, compass fork a proper prefix of the target (MP §10.2.3 "the beacon points down the right one").
        public void Validate()
        {
            if (schema != SchemaId) throw new FormatException($"schema must be {SchemaId}, got '{schema}'");
            if (nodes == null || nodes.Length == 0) throw new FormatException("nodes is empty");
            var seen = new HashSet<string>(nodes.Length, StringComparer.Ordinal);
            foreach (var n in nodes)
            {
                if (n == null || n.address == null || !AirwayAddress.IsValid(n.address))
                    throw new FormatException("invalid address '" + n?.address + "' (letters L and R, at most 23)");
                if (n.gen != n.address.Length) throw new FormatException($"gen {n.gen} != address length for '{n.address}'");
                if (!seen.Add(n.address)) throw new FormatException("duplicate address '" + n.address + "'");
                if (n.pos_mm == null || n.pos_mm.Length != 3) throw new FormatException("pos_mm needs 3 numbers at '" + n.address + "'");
                if (n.dir == null || n.dir.Length != 3) throw new FormatException("dir needs 3 numbers at '" + n.address + "'");
                if (!(n.len_mm > 0)) throw new FormatException("len_mm must be > 0 at '" + n.address + "'");
                if (!(n.radius_mm > 0)) throw new FormatException("radius_mm must be > 0 at '" + n.address + "'");
            }
            if (!seen.Contains("")) throw new FormatException("the root node \"\" is missing");
            foreach (var n in nodes)
                if (n.address.Length > 0 && !seen.Contains(n.address.Substring(0, n.address.Length - 1)))
                    throw new FormatException("parent of '" + n.address + "' is missing");
            if (target == null || target.address == null || !seen.Contains(target.address)) throw new FormatException("target.address is not a node");
            if (compass_fork == null || compass_fork.address == null || !seen.Contains(compass_fork.address))
                throw new FormatException("compass_fork.address is not a node");
            if (!AirwayAddress.IsProperPrefixOf(compass_fork.address, target.address))
                throw new FormatException("compass_fork.address must be a proper prefix of target.address");
            Validated = true;
        }
    }

    [Serializable]
    public sealed class AirwayTreeFileNode
    {
        public string address; public int gen; public double len_mm; public double radius_mm;
        public double[] pos_mm; public double[] dir; public string source;
    }

    [Serializable] public sealed class AirwayAddressRef { public string address; }
}

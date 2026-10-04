using System;
using System.Collections.Generic;
using System.IO;
using Atlas.Link;

namespace Atlas.Blades.Lung
{
    // Reader for airway/tree.json, schema atlas.tree/2 (Master Plan §10.2.4; PH-30 / R8: lane B writes the reader). Field names are
    // the file's. It reads through the link's AtlasJson, which is engine-free.
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
            if (!AtlasJson.TryParse(json, out JsonValue j, out string error)) throw new FormatException("tree.json is not valid JSON: " + error);
            return FromJson(j);
        }

        static AirwayTreeFile FromJson(JsonValue j)
        {
            if (!j.IsObject) throw new FormatException("tree.json is empty");
            var f = new AirwayTreeFile { schema = j.GetString("schema") };
            JsonValue nodes = j["nodes"];
            if (nodes.IsArray)
            {
                f.nodes = new AirwayTreeFileNode[nodes.Count];
                for (int i = 0; i < f.nodes.Length; i++)
                {
                    JsonValue n = nodes[i];
                    f.nodes[i] = !n.IsObject ? null : new AirwayTreeFileNode
                    {
                        address = n.GetString("address"), gen = n.GetInt("gen"), len_mm = n.GetDouble("len_mm"), radius_mm = n.GetDouble("radius_mm"),
                        pos_mm = Vector(n["pos_mm"]), dir = Vector(n["dir"]), source = n.GetString("source"),
                    };
                }
            }
            if (j["target"].IsObject) f.target = new AirwayAddressRef { address = j["target"].GetString("address") };
            if (j["compass_fork"].IsObject) f.compass_fork = new AirwayAddressRef { address = j["compass_fork"].GetString("address") };
            f.Validate();
            return f;
        }

        static double[] Vector(JsonValue a)
        {
            if (!a.IsArray) return null;
            var v = new double[a.Count];
            for (int i = 0; i < v.Length; i++) v[i] = a[i].AsDouble();
            return v;
        }

        public static AirwayTreeFile Load(string caseRoot)
        {
            var bytes = File.ReadAllBytes(Path.Combine(caseRoot, "airway", "tree.json"));
            if (bytes.Length > 23 && System.Text.Encoding.ASCII.GetString(bytes, 0, 23) == "version https://git-lfs")
                throw new InvalidDataException("tree.json is a Git LFS pointer; run git lfs pull");
            if (!AtlasJson.TryParseUtf8(bytes, 0, bytes.Length, false, out JsonValue j, out string error))
                throw new FormatException("tree.json is not valid JSON: " + error);
            return FromJson(j);
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

    public sealed class AirwayTreeFileNode
    {
        public string address; public int gen; public double len_mm; public double radius_mm;
        public double[] pos_mm; public double[] dir; public string source;
    }

    public sealed class AirwayAddressRef { public string address; }
}

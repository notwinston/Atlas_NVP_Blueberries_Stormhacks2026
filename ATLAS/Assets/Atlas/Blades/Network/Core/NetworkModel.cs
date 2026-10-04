using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Atlas.Link;
using Unity.Mathematics;

namespace Atlas.Blades.Network.Core
{
    public enum RoutingRule : byte { Hyperbolic = 0, Flat = 1 }      // net.rule option order

    public readonly struct NetworkStudy
    {
        public readonly int Pairs;
        public readonly double HyperbolicSuccess, HyperbolicStretch;  // stretch NaN when the file has null
        public readonly double FlatSuccess, FlatStretch;

        public NetworkStudy(int pairs, double hyperbolicSuccess, double hyperbolicStretch, double flatSuccess, double flatStretch)
        {
            Pairs = pairs;
            HyperbolicSuccess = hyperbolicSuccess; HyperbolicStretch = hyperbolicStretch;
            FlatSuccess = flatSuccess; FlatStretch = flatStretch;
        }
    }

    // An atlas.network/1 file (docs/contracts/network.md section 2) with its distances precomputed. Engine-free.
    public sealed class NetworkModel
    {
        public const string Schema = "atlas.network/1";
        public static readonly int[] BundledSeeds = { 7, 83 };

        public static string FileName(int seed) { return "network_" + seed.ToString(CultureInfo.InvariantCulture) + ".json"; }

        readonly double[] _r, _theta;
        readonly int[] _degree;
        readonly string[] _label;
        readonly int[] _edgeA, _edgeB;
        readonly int[] _nbStart, _nb;      // CSR adjacency, ascending ids
        readonly double[] _dist, _flat;    // N x N
        readonly double3[] _hyper;

        public int N { get; }
        public double Kbar { get; }
        public double Gamma { get; }
        public double T { get; }
        public int Seed { get; }
        public double R { get; }
        public int NodeCount { get { return N; } }
        public int EdgeCount { get { return _edgeA.Length; } }
        public NetworkStudy Study { get; }

        public static NetworkModel Load(string path) { return Parse(File.ReadAllText(path)); }

        public static NetworkModel Parse(string json)
        {
            JsonValue root;
            string error;
            if (json == null || !AtlasJson.TryParse(json, out root, out error))
                throw new FormatException("network file: not valid JSON");
            if (!root.IsObject) throw new FormatException("network file: root is not an object");
            if (root.GetString("schema") != Schema) throw new FormatException("schema: expected " + Schema);
            JsonValue model = root["model"];
            if (!model.IsObject) throw new FormatException("model: missing");
            return new NetworkModel(root, model);
        }

        static double Num(JsonValue v, string field)
        {
            if (v == null || !v.IsNumber) throw new FormatException(field + ": not a number");
            return v.AsDouble();
        }

        static int Int(JsonValue v, string field)
        {
            if (v == null || !v.IsInteger) throw new FormatException(field + ": not an integer");
            return v.AsInt();
        }

        NetworkModel(JsonValue root, JsonValue model)
        {
            N = Int(model["n"], "model.n");
            if (N < 1) throw new FormatException("model.n: below 1");
            Kbar = Num(model["kbar"], "model.kbar");
            Gamma = Num(model["gamma"], "model.gamma");
            T = Num(model["T"], "model.T");
            Seed = Int(model["seed"], "model.seed");
            R = Num(model["R"], "model.R");

            JsonValue nodes = root["nodes"];
            if (!nodes.IsArray || nodes.Count != N) throw new FormatException("nodes: expected " + N + " entries");
            _r = new double[N]; _theta = new double[N]; _degree = new int[N]; _label = new string[N];
            for (int i = 0; i < N; i++)
            {
                JsonValue nd = nodes[i];
                string p = "nodes[" + i + "]";
                if (!nd.IsObject) throw new FormatException(p + ": not an object");
                if (Int(nd["id"], p + ".id") != i) throw new FormatException(p + ".id: expected " + i);
                _r[i] = Num(nd["r"], p + ".r");
                _theta[i] = Num(nd["theta"], p + ".theta");
                _degree[i] = Int(nd["degree"], p + ".degree");
                _label[i] = nd["label"].AsString("AS-" + i.ToString(CultureInfo.InvariantCulture));
            }

            JsonValue edges = root["edges"];
            if (!edges.IsArray) throw new FormatException("edges: not an array");
            int m = edges.Count;
            _edgeA = new int[m]; _edgeB = new int[m];
            int[] count = new int[N + 1];
            for (int e = 0; e < m; e++)
            {
                JsonValue ed = edges[e];
                string p = "edges[" + e + "]";
                if (!ed.IsArray || ed.Count != 2) throw new FormatException(p + ": expected [i, j]");
                int a = Int(ed[0], p + "[0]"), b = Int(ed[1], p + "[1]");
                if (a < 0 || b <= a || b >= N) throw new FormatException(p + ": expected 0 <= i < j < n");
                _edgeA[e] = a; _edgeB[e] = b;
                count[a + 1]++; count[b + 1]++;
            }
            _nbStart = new int[N + 1];
            for (int i = 0; i < N; i++) _nbStart[i + 1] = _nbStart[i] + count[i + 1];
            _nb = new int[2 * m];
            int[] fill = new int[N];
            for (int e = 0; e < m; e++)
            {
                int a = _edgeA[e], b = _edgeB[e];
                _nb[_nbStart[a] + fill[a]++] = b;
                _nb[_nbStart[b] + fill[b]++] = a;
            }
            for (int i = 0; i < N; i++) Array.Sort(_nb, _nbStart[i], _nbStart[i + 1] - _nbStart[i]);

            JsonValue study = root["study"];
            if (!study.IsObject) throw new FormatException("study: missing");
            Study = new NetworkStudy(Int(study["pairs"], "study.pairs"),
                Num(study["hyperbolic"]["success"], "study.hyperbolic.success"), Stretch(study["hyperbolic"]["stretch"], "study.hyperbolic.stretch"),
                Num(study["flat_picture"]["success"], "study.flat_picture.success"), Stretch(study["flat_picture"]["stretch"], "study.flat_picture.stretch"));

            _dist = new double[N * N]; _flat = new double[N * N];
            for (int i = 0; i < N; i++)
                for (int j = 0; j < N; j++)
                {
                    _dist[i * N + j] = DistanceOf(_r[i], _theta[i], _r[j], _theta[j]);
                    _flat[i * N + j] = FlatDistanceOf(_r[i], _theta[i], _r[j], _theta[j]);
                }
            _hyper = new double3[N];
            for (int i = 0; i < N; i++)
                _hyper[i] = new double3(Math.Sinh(_r[i]) * Math.Cos(_theta[i]), Math.Sinh(_r[i]) * Math.Sin(_theta[i]), Math.Cosh(_r[i]));
        }

        static double Stretch(JsonValue v, string field)
        {
            if (v == null || v.IsNull) return double.NaN;
            return Num(v, field);
        }

        public double Radius(int node) { return _r[node]; }
        public double Theta(int node) { return _theta[node]; }
        public int Degree(int node) { return _degree[node]; }
        public string Label(int node) { return _label[node]; }
        public int EdgeA(int edge) { return _edgeA[edge]; }
        public int EdgeB(int edge) { return _edgeB[edge]; }
        public int NeighbourCount(int node) { return _nbStart[node + 1] - _nbStart[node]; }
        public int Neighbour(int node, int k) { return _nb[_nbStart[node] + k]; }

        public double Distance(int a, int b) { return _dist[a * N + b]; }
        public double FlatDistance(int a, int b) { return _flat[a * N + b]; }
        public double3 HyperboloidPoint(int node) { return _hyper[node]; }

        // eq. 5, in the reference's order of operations (hypernet.distance_matrix).
        public static double DistanceOf(double ra, double ta, double rb, double tb)
        {
            double dth = Math.PI - Math.Abs(Math.PI - Math.Abs(ta - tb));
            double c = Math.Cosh(ra) * Math.Cosh(rb) - Math.Sinh(ra) * Math.Sinh(rb) * Math.Cos(dth);
            return Math.Acosh(Math.Max(c, 1.0));
        }

        public static double FlatDistanceOf(double ra, double ta, double rb, double tb)
        {
            double pa = Math.Tanh(ra / 2), pb = Math.Tanh(rb / 2);
            double dx = pa * Math.Cos(ta) - pb * Math.Cos(tb);
            double dy = pa * Math.Sin(ta) - pb * Math.Sin(tb);
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}

using System;
using Atlas.Link;
using Unity.Mathematics;

namespace Atlas.Telemetry
{
    /// <summary>
    /// The network disc of NET-04: the nodes of an <c>atlas.network/1</c> file as a Poincare disc in the
    /// Spectator's map panel, the same picture as the Console's for the same seed. Engine-free. The file is read
    /// once with <see cref="AtlasJson"/> into preallocated arrays; <see cref="Draw"/> allocates nothing.
    /// Atlas.Telemetry cannot reference Atlas.Blades.Network (rule F1), so the reader and the projection are
    /// repeated here and checked against the same golden file.
    /// </summary>
    public sealed class NetworkDisc
    {
        public const string Schema = "atlas.network/1";

        readonly double[] _r, _theta;
        readonly int[] _edgeA, _edgeB;
        readonly double[] _px, _py;             // the disc point of every node, for the current centre
        double2 _centre;

        NetworkDisc(int n, int edges)
        {
            _r = new double[n];
            _theta = new double[n];
            _px = new double[n];
            _py = new double[n];
            _edgeA = new int[edges];
            _edgeB = new int[edges];
        }

        public int NodeCount { get { return _r.Length; } }
        public int EdgeCount { get { return _edgeA.Length; } }
        public int Seed { get; private set; }

        /// <summary>Parses an <c>atlas.network/1</c> file; FormatException names the bad field.</summary>
        public static NetworkDisc Parse(string json)
        {
            JsonValue root;
            string error;
            if (!AtlasJson.TryParse(json, out root, out error)) throw new FormatException("network file: " + error);
            if (root.GetString("schema") != Schema) throw new FormatException("network file: schema is not " + Schema);
            JsonValue nodes = root["nodes"], edges = root["edges"];
            if (!nodes.IsArray) throw new FormatException("network file: nodes is not an array");
            if (!edges.IsArray) throw new FormatException("network file: edges is not an array");
            int n = nodes.Count, m = edges.Count;
            var disc = new NetworkDisc(n, m);
            disc.Seed = root["model"].GetInt("seed");
            for (int i = 0; i < n; i++)
            {
                JsonValue node = nodes[i];
                if (!node["r"].IsNumber) throw new FormatException("network file: nodes[" + i + "].r");
                if (!node["theta"].IsNumber) throw new FormatException("network file: nodes[" + i + "].theta");
                disc._r[i] = node.GetDouble("r");
                disc._theta[i] = node.GetDouble("theta");
            }
            for (int e = 0; e < m; e++)
            {
                JsonValue edge = edges[e];
                int a = edge[0].AsInt(-1), b = edge[1].AsInt(-1);
                if (a < 0 || b < 0 || a >= n || b >= n) throw new FormatException("network file: edges[" + e + "]");
                disc._edgeA[e] = a;
                disc._edgeB[e] = b;
            }
            disc.Recentre(new double2(0.0, 0.0));
            return disc;
        }

        /// <summary>The Poincare point of (r, theta): tanh(r / 2) (cos theta, sin theta).</summary>
        public static double2 Project(double r, double theta)
        {
            double rho = Math.Tanh(0.5 * r);
            return new double2(rho * Math.Cos(theta), rho * Math.Sin(theta));
        }

        /// <summary>(z - a) / (1 - conj(a) z), complex arithmetic. The view is always this of the original points.</summary>
        public static double2 Mobius(double2 z, double2 a)
        {
            double nr = z.x - a.x, ni = z.y - a.y;
            double dr = 1.0 - (a.x * z.x + a.y * z.y), di = -(a.x * z.y - a.y * z.x);
            double den = dr * dr + di * di;
            return new double2((nr * dr + ni * di) / den, (ni * dr - nr * di) / den);
        }

        /// <summary>
        /// Screen mapping of the disc (contract section 6.1) for a square of size pixels, origin top-left, y down:
        /// scale = (size / 2) / 1.03, px = size / 2 + scale x, py = size / 2 - scale y.
        /// </summary>
        public static void ToScreen(double x, double y, double size, out double px, out double py)
        {
            double scale = (size / 2.0) / FlatTwinDisc.ViewHalfExtent;
            px = size / 2.0 + scale * x;
            py = size / 2.0 - scale * y;
        }

        /// <summary>Re-centres the picture on a disc point (|a| &lt; 1). Allocates nothing.</summary>
        public void Recentre(double2 centre)
        {
            _centre = centre;
            bool origin = centre.x == 0.0 && centre.y == 0.0;
            for (int i = 0; i < _r.Length; i++)
            {
                double2 p = Project(_r[i], _theta[i]);
                if (!origin) p = Mobius(p, centre);
                _px[i] = p.x;
                _py[i] = p.y;
            }
        }

        /// <summary>Re-centres on a node's own disc point (the picture as the phone's re-centre shows it).</summary>
        public void RecentreOnNode(int node)
        {
            if ((uint)node < (uint)_r.Length) Recentre(Project(_r[node], _theta[node]));
        }

        public double2 Centre { get { return _centre; } }

        /// <summary>The disc point of a node for the current centre.</summary>
        public double2 Point(int node) { return new double2(_px[node], _py[node]); }

        /// <summary>The disc point of a node on the screen, for a square of size pixels.</summary>
        public void NodeToScreen(int node, double size, out double px, out double py)
        {
            ToScreen(_px[node], _py[node], size, out px, out py);
        }

        /// <summary>
        /// Clears the twin and draws the disc: the edges as lines in Grid, the nodes as dots in Cell. Map x is the
        /// disc's x, map z its y. Allocates nothing once the twin's list has grown.
        /// </summary>
        public void Draw(FlatTwin twin)
        {
            if (twin == null) throw new ArgumentNullException("twin");
            twin.Clear();
            for (int e = 0; e < _edgeA.Length; e++)
            {
                int a = _edgeA[e], b = _edgeB[e];
                twin.Line((float)_px[a], (float)_py[a], (float)_px[b], (float)_py[b], FlatTwinInk.Grid);
            }
            float radius = FlatTwinBuilder.DotShare * FlatTwinDisc.ViewHalfExtent;
            for (int i = 0; i < _px.Length; i++)
                twin.Dot((float)_px[i], (float)_py[i], radius, FlatTwinInk.Cell);
            twin.SetView(0f, 0f, FlatTwinDisc.ViewHalfExtent, true);
        }
    }
}

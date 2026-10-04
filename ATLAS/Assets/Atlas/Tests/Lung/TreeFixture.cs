using System.Diagnostics;
using Atlas.Kernel;

namespace Atlas.Blades.Lung.Tests
{
    // Loads the 20 MB illustrative tree.json once for all Lung tests (PH-30; never per frame).
    static class TreeFixture
    {
        static AirwayTreeFile file; static AirwayTree tree;
        public static double LoadMs, BuildMs;

        public static AirwayTreeFile File { get { Ensure(); return file; } }
        public static AirwayTree Tree { get { Ensure(); return tree; } }

        static void Ensure()
        {
            if (tree != null) return;
            var root = LungTestEnv.CaseRoot("illustrative");
            var sw = Stopwatch.StartNew();
            file = AirwayTreeFile.Load(root);
            LoadMs = sw.Elapsed.TotalMilliseconds; sw.Restart();
            tree = new AirwayTree(file, TileFrame.Zero);
            BuildMs = sw.Elapsed.TotalMilliseconds;
        }

        public static string Json(string nodes, string target = "\"\"", string compass = "\"\"", string schema = "atlas.tree/2") =>
            "{\"schema\":\"" + schema + "\",\"nodes\":[" + nodes + "],\"target\":{\"address\":" + target + "},\"compass_fork\":{\"address\":" + compass + "}}";

        public static string Node(string address, int gen, double len = 10, double r = 5, string pos = "[0,0,0]", string dir = "[0,0,-1]", string source = "model") =>
            "{\"address\":\"" + address + "\",\"gen\":" + gen + ",\"len_mm\":" + len.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",\"radius_mm\":" + r.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"pos_mm\":" + pos + ",\"dir\":" + dir + ",\"source\":\"" + source + "\"}";
    }
}

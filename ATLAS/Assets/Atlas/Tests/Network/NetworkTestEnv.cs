using System;
using System.IO;
using Atlas.Blades.Network.Core;
using Atlas.Link;

namespace Atlas.Blades.Network.Tests
{
    // Where the tests find repository files (contract section 7, NET-05).
    static class NetworkTestEnv
    {
        public static string ProjectDir => Environment.GetEnvironmentVariable("ATLAS_PROJECT_DIR") ?? Directory.GetCurrentDirectory();
        public static string FixtureDir => Path.GetFullPath(Path.Combine(ProjectDir, "..", "station", "pipelines", "tests", "fixtures", "network"));
        public static string BundledDir => Path.Combine(ProjectDir, "Assets", "StreamingAssets", "networks");

        public static JsonValue Fixture(string name) { return AtlasJson.Parse(File.ReadAllText(Path.Combine(FixtureDir, name))); }
        public static NetworkModel Model(int seed) { return NetworkModel.Load(Path.Combine(FixtureDir, NetworkModel.FileName(seed))); }
    }
}

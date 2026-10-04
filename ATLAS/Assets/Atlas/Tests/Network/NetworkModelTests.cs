using System;
using System.IO;
using Atlas.Blades.Network.Core;
using Atlas.Kernel;
using NUnit.Framework;

namespace Atlas.Blades.Network.Tests
{
    public class NetworkModelTests
    {
        [Test]
        public void Parse_FixtureCounts()
        {
            NetworkModel a = NetworkEnvModel(7), b = NetworkEnvModel(83);
            Assert.AreEqual(200, a.NodeCount); Assert.AreEqual(200, b.NodeCount);
            Assert.AreEqual(580, a.EdgeCount); Assert.AreEqual(604, b.EdgeCount);
            Assert.AreEqual(10.504399538733493, a.R, 0);
            Assert.AreEqual(1.0, a.Study.HyperbolicSuccess, 0);
            Assert.AreEqual(2000, a.Study.Pairs);
            for (int i = 0; i < a.NodeCount; i++) Assert.AreEqual(a.Degree(i), a.NeighbourCount(i));
            Assert.AreEqual("AS-3", a.Label(3));
        }

        [Test]
        public void Parse_BundledEqualsFixture()
        {
            foreach (int seed in NetworkModel.BundledSeeds)
                Assert.AreEqual(File.ReadAllText(Path.Combine(NetworkTestEnv.FixtureDir, NetworkModel.FileName(seed))),
                    File.ReadAllText(Path.Combine(NetworkTestEnv.BundledDir, NetworkModel.FileName(seed))));
        }

        [Test]
        public void Parse_BadFieldThrowsFormatExceptionNamingIt()
        {
            var ex = Assert.Throws<FormatException>(() => NetworkModel.Parse("{\"schema\":\"atlas.network/1\",\"model\":{\"n\":\"x\"}}"));
            StringAssert.Contains("model.n", ex.Message);
        }

        [Test]
        public void Distances_EqualPython_1e9()
        {
            foreach (int seed in NetworkModel.BundledSeeds)
            {
                NetworkModel m = NetworkEnvModel(seed);
                var pairs = NetworkTestEnv.Fixture("golden_distances_" + seed + ".json")["pairs"];
                Assert.AreEqual(500, pairs.Count);
                for (int k = 0; k < pairs.Count; k++)
                {
                    int i = pairs[k][0].AsInt(), j = pairs[k][1].AsInt();
                    double d = pairs[k][2].AsDouble();
                    Assert.AreEqual(d, m.Distance(i, j), 1e-9, "seed " + seed + " pair " + i + "," + j);
                    Assert.AreEqual(m.Distance(j, i), m.Distance(i, j), 1e-12);
                }
            }
        }

        [Test]
        public void HyperboloidPoint_DistanceEqualsGeometry2_1e9()
        {
            foreach (int seed in NetworkModel.BundledSeeds)
            {
                NetworkModel m = NetworkEnvModel(seed);
                var pairs = NetworkTestEnv.Fixture("golden_distances_" + seed + ".json")["pairs"];
                for (int k = 0; k < pairs.Count; k++)
                {
                    int i = pairs[k][0].AsInt(), j = pairs[k][1].AsInt();
                    double g = Geometry2.Distance(m.HyperboloidPoint(i), m.HyperboloidPoint(j), -1);
                    Assert.AreEqual(pairs[k][2].AsDouble(), g, 1e-9, "seed " + seed + " pair " + i + "," + j);
                }
            }
        }

        static NetworkModel NetworkEnvModel(int seed) { return NetworkTestEnv.Model(seed); }
    }
}

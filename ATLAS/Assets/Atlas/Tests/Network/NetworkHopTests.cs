using Atlas.Blades.Network.Core;
using Atlas.Blades.Network.Walk;
using Atlas.Kernel;
using Atlas.World;
using NUnit.Framework;

namespace Atlas.Blades.Network.Tests
{
    public class NetworkHopTests
    {
        [Test]
        public void Hop_ReRootsWithinHalfSecond_WithFade()
        {
            var hop = new NetworkHop();
            Assert.IsTrue(hop.Begin(3, 9, 10.0));
            Assert.IsFalse(hop.Begin(3, 4, 10.0), "one hop at a time");
            double rerootAt = -1, peak = 0, last = 0;
            bool faded = false;
            for (double t = 10.0; t <= 10.6; t += 1.0 / 60)
            {
                if (hop.Tick(t)) rerootAt = t - 10.0;
                if (hop.Level > peak) peak = hop.Level;
                // no pop: the level moves by no more than one frame of the 0.25 s ramps
                Assert.LessOrEqual(System.Math.Abs(hop.Level - last), 1.0 / 60 / 0.25 + 1e-9);
                last = hop.Level;
                if (hop.Level > 0 && hop.Level < 1) faded = true;
            }
            Assert.IsTrue(rerootAt >= 0 && rerootAt <= 0.5, "re-root at " + rerootAt);
            Assert.AreEqual(1.0, peak, 1e-12);
            Assert.IsTrue(faded);
            Assert.IsFalse(hop.Active);
            Assert.AreEqual(0.0, hop.Level);
        }

        [Test]
        public void Module_Hop_ReRootsKernelAtNeighbour_AndLogsOnce()
        {
            NetworkModel model = NetworkTestEnv.Model(7);
            var module = new NetworkWingModule(model, new TileGraph(5));
            int resets = 0; WalkerPose last = default; int logged = 0, from = -1, to = -1;
            module.ResetWalker = p => { resets++; last = p; };
            module.FadeSink = null;                     // AtlasLook needs the engine
            module.HopLogged = (f, t, d) => { logged++; from = f; to = t; Assert.AreEqual(model.Distance(f, t), d, 1e-12); };
            module.Enter(WalkerPose.Start);
            int start = module.Node;
            int target = model.Neighbour(start, 0);
            Assert.IsTrue(module.RequestHop(target, 5.0));
            var frame = new FrameContext();
            for (double t = 5.0; t <= 5.6; t += 1.0 / 60) { frame.Time = t; module.Tick(in frame); }
            Assert.AreEqual(target, module.Node);
            Assert.AreEqual(2, resets);                 // Enter + the hop
            Assert.AreEqual(1, logged);
            Assert.AreEqual(start, from); Assert.AreEqual(target, to);
            Assert.AreEqual(module.Beacon.Placement.Tile(target), last.Tile);
        }

        [Test]
        public void Module_Hop_AppliesWorldFade_1Idle_0AtReRoot_1After_AndAfterExit()
        {
            NetworkModel model = NetworkTestEnv.Model(7);
            var module = new NetworkWingModule(model, new TileGraph(5));
            module.ResetWalker = null;
            float applied = 1f /* AtlasLook's default: visible */, atReRoot = -1f; bool sawReRoot = false;
            int node0 = module.Node, calls = 0;
            module.FadeSink = f => { applied = f; calls++; };
            module.Enter(WalkerPose.Start);
            var frame = new FrameContext();
            frame.Time = 4.0; module.Tick(in frame);
            Assert.AreEqual(1f, applied, "idle: world visible");
            Assert.AreEqual(0, calls, "idle applies nothing per frame");
            Assert.IsTrue(module.RequestHop(model.Neighbour(node0, 0), 5.0));
            for (double t = 5.0; t <= 5.6; t += 1.0 / 60)
            {
                frame.Time = t; module.Tick(in frame);
                if (!sawReRoot && module.Node != node0) { sawReRoot = true; atReRoot = applied; }
            }
            Assert.IsTrue(sawReRoot);
            Assert.AreEqual(0f, atReRoot, 1e-6f, "black at the re-root");
            Assert.AreEqual(1f, applied, 1e-6f, "visible after the hop");
            module.Exit();
            Assert.AreEqual(1f, applied, "visible after Exit");
        }
    }
}

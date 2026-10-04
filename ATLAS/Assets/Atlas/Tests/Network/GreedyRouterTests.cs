using Atlas.Blades.Network.Core;
using Atlas.Link;
using NUnit.Framework;

namespace Atlas.Blades.Network.Tests
{
    public class GreedyRouterTests
    {
        [Test]
        public void Routes_EqualPython_1000Pairs_BothRules()
        {
            foreach (int seed in NetworkModel.BundledSeeds)
            {
                var router = new GreedyRouter(NetworkTestEnv.Model(seed));
                var routes = NetworkTestEnv.Fixture("golden_routes_" + seed + ".json")["routes"];
                Assert.AreEqual(1000, routes.Count);
                var path = new int[GreedyRouter.Limit];
                for (int k = 0; k < routes.Count; k++)
                {
                    JsonValue r = routes[k];
                    int src = r["src"].AsInt(), dst = r["dst"].AsInt();
                    Assert.AreEqual(r["shortest"].AsInt(), router.ShortestHops(src, dst));
                    Check(router, r["hyperbolic"], src, dst, RoutingRule.Hyperbolic, path, seed + " #" + k);
                    Check(router, r["flat"], src, dst, RoutingRule.Flat, path, seed + " #" + k);
                }
            }
        }

        static void Check(GreedyRouter router, JsonValue expected, int src, int dst, RoutingRule rule, int[] path, string what)
        {
            int count = router.Route(src, dst, rule, path, out bool success);
            JsonValue p = expected["path"];
            Assert.AreEqual(p.Count, count, what + " " + rule + " length");
            for (int i = 0; i < count; i++) Assert.AreEqual(p[i].AsInt(), path[i], what + " " + rule + " step " + i);
            Assert.AreEqual(expected["success"].AsBool(), success, what + " " + rule + " success");
            Assert.AreEqual(expected["hops"].AsInt(), count - 1, what + " " + rule + " hops");
        }

        [Test]
        public void Giant_Seed7Is179_Seed83Is194()
        {
            Assert.AreEqual(179, new GreedyRouter(NetworkTestEnv.Model(7)).GiantCount);
            Assert.AreEqual(194, new GreedyRouter(NetworkTestEnv.Model(83)).GiantCount);
        }

        [Test]
        public void Stretch_IsHopsOverShortest() { Assert.AreEqual(1.5, GreedyRouter.Stretch(3, 2), 0); }

        [Test]
        public void Route_AllocatesNothing()
        {
            var router = new GreedyRouter(NetworkTestEnv.Model(7));
            var path = new int[GreedyRouter.Limit];
            int a = router.GiantNode(0), b = router.GiantNode(router.GiantCount - 1);
            router.Route(a, b, RoutingRule.Hyperbolic, path, out _); router.ShortestHops(a, b);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++)
            {
                router.Route(a, b, RoutingRule.Hyperbolic, path, out _);
                router.Route(b, a, RoutingRule.Flat, path, out _);
                router.ShortestHops(a, b);
            }
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated);
        }
    }
}

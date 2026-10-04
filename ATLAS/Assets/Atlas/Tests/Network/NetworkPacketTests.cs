using System;
using System.Collections.Generic;
using Atlas.Blades.Network.Core;
using Atlas.Link;
using Atlas.Telemetry;
using Atlas.World;
using NUnit.Framework;

namespace Atlas.Blades.Network.Tests
{
    // NET-07: the packet run and its net.packet events.
    public class NetworkPacketTests
    {
        // A real EventLog (manual clock, no files) published as the session's IEventLog, with every event collected.
        sealed class EventCapture : IDisposable
        {
            public readonly List<EventMessage> Events = new List<EventMessage>();
            readonly EventLog m_Log;
            readonly IEventLog m_Previous;

            public EventCapture()
            {
                m_Log = new EventLog(new ManualClock(), null);
                m_Previous = AtlasServices.Replace<IEventLog>(m_Log);
                m_Log.Logged += Events.Add;
            }

            public void Dispose()
            {
                m_Log.Logged -= Events.Add;
                if (m_Previous != null) AtlasServices.Replace(m_Previous); else AtlasServices.Unregister<IEventLog>(m_Log);
            }
        }

        [Test]
        public void TenPackets_LoggedWithCoreHopsAndStretch()
        {
            NetworkModel model = NetworkTestEnv.Model(7);
            var router = new GreedyRouter(model);
            var packet = new NetworkPacket(router, new NetworkCaption());
            var path = new int[GreedyRouter.Limit + 1];
            using (var capture = new EventCapture())
            {
                var expected = new List<(int src, int dst, RoutingRule rule, int hops, int shortest, bool ok)>();
                for (int i = 0; i < 10; i++)
                {
                    int src = router.GiantNode((i * 7) % router.GiantCount);
                    int dst = router.GiantNode((i * 13 + 5) % router.GiantCount);
                    if (dst == src) dst = router.GiantNode((i * 13 + 6) % router.GiantCount);
                    RoutingRule rule = i % 2 == 0 ? RoutingRule.Hyperbolic : RoutingRule.Flat;
                    int count = router.Route(src, dst, rule, path, out bool ok);
                    expected.Add((src, dst, rule, count - 1, router.ShortestHops(src, dst), ok));
                    Assert.IsTrue(packet.Start(src, dst, rule));
                    Assert.AreEqual(count - 1, packet.Hops);
                    Assert.AreEqual(ok, packet.Success);
                }
                Assert.AreEqual(10, capture.Events.Count, "one net.packet per packet");
                for (int i = 0; i < 10; i++)
                {
                    EventMessage ev = capture.Events[i];
                    var e = expected[i];
                    Assert.AreEqual("net", ev.Scene);
                    Assert.AreEqual("net.packet", ev.Ev);
                    JsonValue d = ev.Data;
                    Assert.AreEqual(e.src, (int)d["src"].AsDouble());
                    Assert.AreEqual(e.dst, (int)d["dst"].AsDouble());
                    Assert.AreEqual(NetworkEvents.RuleName(e.rule), d["rule"].AsString());
                    Assert.AreEqual(e.hops, (int)d["hops"].AsDouble());
                    Assert.AreEqual(e.shortest, (int)d["shortest"].AsDouble());
                    Assert.AreEqual(e.ok, d["success"].AsBool());
                    if (e.ok) Assert.AreEqual(GreedyRouter.Stretch(e.hops, e.shortest), d["stretch"].AsDouble(), 1e-12);
                    else Assert.IsTrue(d["stretch"].IsNull, "a dropped packet has no stretch");
                }
            }
        }

        [Test]
        public void Packet_LightsHopByHop_AndCaptionNamesHopsAndStretch()
        {
            NetworkModel model = NetworkTestEnv.Model(7);
            var router = new GreedyRouter(model);
            var caption = new NetworkCaption();
            var packet = new NetworkPacket(router, caption);
            int src = router.GiantNode(0), dst = router.GiantNode(router.GiantCount - 1);
            Assert.IsTrue(packet.Start(src, dst, RoutingRule.Hyperbolic));
            StringAssert.Contains("hyperbolic", caption.Text);
            StringAssert.Contains(packet.Hops == 1 ? "1 hop" : packet.Hops + " hops", caption.Text);
            int last = packet.ShownCount, steps = 0;
            while (packet.Running && steps++ < 1000)
            {
                if (packet.Advance(NetworkPacket.HopSeconds)) { Assert.AreEqual(last + 1, packet.ShownCount); last = packet.ShownCount; }
            }
            Assert.AreEqual(packet.PathCount, packet.ShownCount);
            Assert.IsFalse(packet.Running);
            Assert.IsFalse(packet.Start(-1, 0, RoutingRule.Flat));
        }
    }
}

using System;
using Atlas.Blades.Network.Core;

namespace Atlas.Blades.Network
{
    /// <summary>
    /// One packet run (section 4 V2): routes with <see cref="GreedyRouter"/>, lights the route hop by hop and fills the
    /// caption chip. Engine-free; time is passed in; nothing allocates after construction. The result (hops, shortest,
    /// success) is the core's, unchanged. The <c>net.packet</c> event is written when the run starts, because the route is
    /// known then; a run replaced half way still logs its packet.
    /// </summary>
    public sealed class NetworkPacket
    {
        /// <summary>Seconds per lit hop (presentation only; the same as the hop fade of section 4 V3).</summary>
        public const float HopSeconds = 0.25f;

        readonly GreedyRouter m_Router;
        readonly int[] m_Path = new int[GreedyRouter.Limit + 1];
        readonly NetworkCaption m_Caption;
        float m_Elapsed;
        int m_Shown;

        public NetworkPacket(GreedyRouter router, NetworkCaption caption)
        {
            if (router == null) throw new ArgumentNullException(nameof(router));
            m_Router = router;
            m_Caption = caption ?? new NetworkCaption();
        }

        public NetworkCaption Caption { get { return m_Caption; } }
        public int Src { get; private set; }
        public int Dst { get; private set; }
        public RoutingRule Rule { get; private set; }
        /// <summary>Nodes on the route, source included (hops + 1).</summary>
        public int PathCount { get; private set; }
        public int Hops { get { return PathCount - 1; } }
        public int Shortest { get; private set; }
        public bool Success { get; private set; }
        public bool Running { get; private set; }
        public int Runs { get; private set; }
        /// <summary>stretch = hops / shortest when delivered, else NaN.</summary>
        public double Stretch { get { return Success && Shortest > 0 ? GreedyRouter.Stretch(Hops, Shortest) : double.NaN; } }
        /// <summary>The route; the first <see cref="ShownCount"/> nodes are lit.</summary>
        public int[] Path { get { return m_Path; } }
        /// <summary>How many route nodes are lit now (2 or more once the first hop shows).</summary>
        public int ShownCount { get { return m_Shown; } }

        /// <summary>Routes a packet, logs net.packet and starts the hop-by-hop reveal. False for ids out of range.</summary>
        public bool Start(int src, int dst, RoutingRule rule)
        {
            int n = m_Router.Model.NodeCount;
            if (src < 0 || src >= n || dst < 0 || dst >= n) return false;
            Src = src; Dst = dst; Rule = rule;
            bool ok;
            PathCount = m_Router.Route(src, dst, rule, m_Path, out ok);
            Success = ok;
            Shortest = m_Router.ShortestHops(src, dst);
            m_Elapsed = 0f;
            m_Shown = 1;
            Running = PathCount > 1;
            Runs++;
            NetworkEvents.Packet(src, dst, rule, Hops, Shortest, Success);
            m_Caption.Show(rule, Hops, Shortest, Success);
            return true;
        }

        /// <summary>Moves time on. True when one more hop is lit (the highlight must be set again).</summary>
        public bool Advance(float dt)
        {
            if (!Running) return false;
            m_Elapsed += dt > 0f ? dt : 0f;
            int shown = 1 + (int)(m_Elapsed / HopSeconds);
            if (shown > PathCount) shown = PathCount;
            if (shown == m_Shown) return false;
            m_Shown = shown;
            if (shown >= PathCount) Running = false;
            return true;
        }

        /// <summary>Lights the whole route at once.</summary>
        public void Finish()
        {
            if (m_Shown != PathCount) m_Shown = PathCount;
            Running = false;
        }
    }
}

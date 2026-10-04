using System.Globalization;
using System.Text;
using Atlas.Blades.Network.Core;
using Atlas.Link;
using Atlas.Telemetry;
using Atlas.World;

namespace Atlas.Blades.Network
{
    /// <summary>
    /// The network's events (contract section 6.4), all in scene "net". Written to the session's <see cref="IEventLog"/>;
    /// with none published the event is skipped, as <c>LungEvents</c> does. Numbers use the invariant culture.
    /// </summary>
    public static class NetworkEvents
    {
        public const string Scene = "net";

        /// <summary>net.recentre: each re-centre, when it starts (<paramref name="r"/> is the node's radius in the file).</summary>
        public static void Recentre(int node, double r, int degree)
        {
            Emit("net.recentre", RecentreJson(node, r, degree));
        }

        /// <summary>net.packet: each delivered or dropped packet. Stretch is hops / shortest when delivered, else null.</summary>
        public static void Packet(int src, int dst, RoutingRule rule, int hops, int shortest, bool success)
        {
            Emit("net.packet", PacketJson(src, dst, rule, hops, shortest, success));
        }

        /// <summary>net.hop (V3): each hop, with the hyperbolic distance of the edge.</summary>
        public static void Hop(int from, int to, double distance)
        {
            Emit("net.hop", HopJson(from, to, distance));
        }

        public static string RecentreJson(int node, double r, int degree)
        {
            return "{\"node\":" + Int(node) + ",\"r\":" + Num(r) + ",\"degree\":" + Int(degree) + "}";
        }

        public static string PacketJson(int src, int dst, RoutingRule rule, int hops, int shortest, bool success)
        {
            string stretch = success && shortest > 0 ? Num(GreedyRouter.Stretch(hops, shortest)) : "null";
            return new StringBuilder(112)
                .Append("{\"src\":").Append(Int(src))
                .Append(",\"dst\":").Append(Int(dst))
                .Append(",\"rule\":\"").Append(RuleName(rule)).Append('"')
                .Append(",\"hops\":").Append(Int(hops))
                .Append(",\"shortest\":").Append(Int(shortest))
                .Append(",\"stretch\":").Append(stretch)
                .Append(",\"success\":").Append(success ? "true" : "false")
                .Append('}').ToString();
        }

        public static string HopJson(int from, int to, double distance)
        {
            return "{\"from\":" + Int(from) + ",\"to\":" + Int(to) + ",\"distance\":" + Num(distance) + "}";
        }

        public static string RuleName(RoutingRule rule) { return rule == RoutingRule.Hyperbolic ? "hyperbolic" : "flat"; }

        static void Emit(string ev, string dataJson)
        {
            if (AtlasServices.TryGet<IEventLog>(out var log)) log.LogJson(Scene, ev, dataJson);
        }

        static string Int(int v) { return v.ToString(CultureInfo.InvariantCulture); }

        // JSON has no NaN or infinity; "R" round-trips a double.
        static string Num(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "null";
            return v.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}

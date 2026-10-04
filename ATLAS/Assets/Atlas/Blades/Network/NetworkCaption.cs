using System.Globalization;
using Atlas.Blades.Network.Core;

namespace Atlas.Blades.Network
{
    /// <summary>
    /// The caption chip's content (section 4 V2): the hop count and the stretch of the last packet. Engine-free; it holds
    /// the text and a revision that moves when the text changes, so a view copies the text only then.
    /// </summary>
    public sealed class NetworkCaption
    {
        string m_Text = "";

        public string Text { get { return m_Text; } }
        public int Revision { get; private set; }

        public void Clear() { Set(""); }

        /// <summary>A running or finished packet: "hyperbolic: 5 hops, stretch 1.25", or "flat: stuck after 3 hops".</summary>
        public void Show(RoutingRule rule, int hops, int shortest, bool success)
        {
            string name = NetworkEvents.RuleName(rule);
            if (!success) { Set(name + ": stuck after " + hops.ToString(CultureInfo.InvariantCulture) + (hops == 1 ? " hop" : " hops")); return; }
            string h = hops.ToString(CultureInfo.InvariantCulture) + (hops == 1 ? " hop" : " hops");
            string s = shortest > 0 ? GreedyRouter.Stretch(hops, shortest).ToString("0.00", CultureInfo.InvariantCulture) : "-";
            Set(name + ": " + h + ", stretch " + s);
        }

        void Set(string text)
        {
            if (text == m_Text) return;
            m_Text = text;
            Revision++;
        }
    }
}

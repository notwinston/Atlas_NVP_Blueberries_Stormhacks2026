using Atlas.Blades.Network.Core;
using Unity.Mathematics;

namespace Atlas.Blades.Network
{
    /// <summary>
    /// The re-centre animation of the floor map (section 4 V2): the whole map glides so that a node comes to the centre,
    /// a Mobius map eased by <see cref="PoincareMap.EaseCentre"/> over <see cref="RecentreSeconds"/>. Engine-free; time is
    /// passed in; nothing allocates.
    /// </summary>
    public sealed class NetworkFocus
    {
        public const float DefaultRecentreSeconds = 0.6f;

        readonly NetworkModel m_Model;
        double2 m_From, m_To;
        float m_Elapsed;

        public NetworkFocus(NetworkModel model)
        {
            m_Model = model;
            FocusNode = -1;
        }

        /// <summary>net.recentreSeconds; the view's field is copied here.</summary>
        public float RecentreSeconds = DefaultRecentreSeconds;

        /// <summary>The Mobius centre now, in the disc picture of the file (the point that is mapped to the middle).</summary>
        public double2 Centre { get; private set; }
        /// <summary>The node the map is centred on, or -1 for the file's own picture.</summary>
        public int FocusNode { get; private set; }
        public bool Active { get; private set; }

        /// <summary>Starts gliding to a node. False when it is out of range or already the centre (an animation in progress is retargeted).</summary>
        public bool Begin(int node)
        {
            if (node < 0 || node >= m_Model.NodeCount) return false;
            if (node == FocusNode && !Active) return false;
            m_From = Centre;
            m_To = PoincareMap.Project(m_Model.Radius(node), m_Model.Theta(node));
            FocusNode = node;
            m_Elapsed = 0f;
            Active = true;
            return true;
        }

        /// <summary>Back to the file's own picture, at once.</summary>
        public void Reset()
        {
            Centre = double2.zero;
            FocusNode = -1;
            Active = false;
            m_Elapsed = 0f;
        }

        /// <summary>Moves time on by dt seconds. True when the centre changed (the last call of an animation included).</summary>
        public bool Advance(float dt)
        {
            if (!Active) return false;
            m_Elapsed += dt > 0f ? dt : 0f;
            float t = RecentreSeconds > 0f ? m_Elapsed / RecentreSeconds : 1f;
            if (t >= 1f)
            {
                Centre = m_To;
                Active = false;
            }
            else
            {
                Centre = PoincareMap.EaseCentre(m_From, m_To, t);
            }
            return true;
        }
    }
}

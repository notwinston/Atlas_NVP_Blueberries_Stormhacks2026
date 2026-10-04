using System;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Spaces.Portal;
using Atlas.World;
using UnityEngine;

namespace Atlas.Blades.Playground.Exhibits
{
    /// <summary>
    /// The Impossible Rooms exhibit of the Geometry Playground (Master Plan tasks P2-C15 and P2-C16): the
    /// tasks and the fallback prompt over the layouts of <see cref="PortalSpace"/> (S5), and the room-number
    /// signs of the loop corridor. It adds no geometry of its own to the space, only what the walker is asked
    /// to do and, as the walker reads it, the numbers on the walls. The space logs <c>portal.cross</c> and
    /// <c>rooms.loop</c> itself, so the exhibit logs nothing of its own. The logic is
    /// <see cref="RoomsExhibitCore"/>; this is the adapter.
    /// </summary>
    public sealed class RoomsExhibit : MonoBehaviour
    {
        RoomsExhibitCore m_Core;
        PortalSpace m_Space;
        ParamRegistry m_Registry;
        RoomNumberSigns m_Signs;
        PortalRenderer m_Portals;
        PortalLayout m_Layout;
        Action<int, int> m_OnRoomChanged;
        // The layout last put into rooms.layout. The exhibit moves the parameter only when its own wish changes, so a
        // value the operator sets in between is not overwritten every frame.
        int m_Applied = -1;

        /// <summary>The logic behind this component. Null until Bind.</summary>
        public RoomsExhibitCore Core { get { return m_Core; } }
        /// <summary>The caption chip of the exhibit.</summary>
        public string Caption { get { return RoomsText.Caption; } }
        /// <summary>What the walker is asked to do now; empty before Bind. Raises <see cref="PromptChanged"/>.</summary>
        public string Prompt { get { return m_Core != null ? m_Core.Prompt : string.Empty; } }
        public event Action<string> PromptChanged;

        /// <summary>
        /// Wires the exhibit to a configured, entered Impossible Rooms space. <paramref name="registry"/> is the
        /// registry the space declared rooms.layout in.
        /// </summary>
        public void Bind(PortalSpace space, RoomModel room, ParamRegistry registry)
        {
            if (space == null) throw new ArgumentNullException(nameof(space));
            if (room == null) throw new ArgumentNullException(nameof(room));
            Unbind();
            m_Space = space;
            m_Registry = registry;
            m_Applied = -1;
            m_Core = new RoomsExhibitCore(room);
            m_Core.PromptChanged += OnPromptChanged;
            if (m_Signs == null) m_Signs = gameObject.AddComponent<RoomNumberSigns>();
            m_OnRoomChanged = OnRoomChanged;
            m_Portals = space.Portals;
            if (m_Portals != null) m_Portals.RoomChanged += m_OnRoomChanged;
            // The exhibit starts at the booth, so the space is asked for it before anything is walked.
            ApplyWantedLayout();
            SyncLayout();
        }

        public void Unbind()
        {
            if (m_Portals != null && m_OnRoomChanged != null) m_Portals.RoomChanged -= m_OnRoomChanged;
            m_Portals = null;
            if (m_Core != null) m_Core.PromptChanged -= OnPromptChanged;
            if (m_Signs != null) m_Signs.Release();
            m_Core = null;
            m_Space = null;
            m_Registry = null;
            m_Layout = null;
        }

        /// <summary>Starts the exhibit again, at the booth.</summary>
        public void Restart()
        {
            if (m_Core == null) return;
            m_Core.Begin();
            m_Applied = -1;
            ApplyWantedLayout();
        }

        /// <summary>Once a frame, after the space's own Tick: follows the layout the space has built.</summary>
        public void Tick()
        {
            if (m_Core == null) return;
            SyncLayout();
        }

        void OnDestroy()
        {
            Unbind();
        }

        // The space regenerates its rooms when rooms.layout changes: the signs and the exhibit follow it.
        void SyncLayout()
        {
            PortalLayout layout = m_Space != null ? m_Space.Core.Layout : null;
            if (layout == null || ReferenceEquals(layout, m_Layout)) return;
            // The space registers the new rooms in the same Tick that builds the layout.
            if (m_Portals == null || m_Portals.RoomCount != layout.RoomCount) return;
            m_Layout = layout;
            m_Core.OnLayoutBuilt(layout);
            m_Signs.Rebuild(layout, m_Portals);
            ApplyWantedLayout();
        }

        void OnRoomChanged(int from, int to)
        {
            if (m_Core == null) return;
            m_Core.OnRoomChanged(from, to);
            ApplyWantedLayout();
        }

        // Moves rooms.layout to what the exhibit wants. The space rebuilds at its next Tick.
        void ApplyWantedLayout()
        {
            if (m_Registry == null || m_Core == null) return;
            int wanted = m_Core.WantedLayout == PortalLayoutKind.Loop ? 1 : 0;
            if (wanted == m_Applied) return;
            ParamEntry layout = m_Registry.Find(PortalSpaceCore.LayoutParam);
            if (layout == null) return;
            m_Applied = wanted;
            if (layout.Int != wanted) layout.Set(wanted);
        }

        void OnPromptChanged(string text)
        {
            Action<string> changed = PromptChanged;
            if (changed != null) changed(text);
        }
    }
}

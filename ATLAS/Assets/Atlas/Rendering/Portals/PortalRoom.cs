using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Rendering
{
    /// <summary>
    /// One room of the portal renderer (Master Plan D16): the renderers under its roots, the pooled
    /// materials they use, and where the room is drawn this frame. A room is authored in the room frame,
    /// so it never moves; only its layer changes.
    /// </summary>
    public sealed class PortalRoom
    {
        readonly List<Transform> m_Roots = new List<Transform>(2);
        readonly List<Renderer> m_Renderers = new List<Renderer>(32);
        readonly List<Material> m_Materials = new List<Material>(32);
        static readonly List<Renderer> s_Found = new List<Renderer>(64);

        int m_Layer = PortalStencilTable.WorldLayer;
        float4 m_Plane = new float4(0f, 0f, 0f, 1f);
        bool m_On = true;

        internal PortalRoom(int id, Transform root, Bounds boundsWorld)
        {
            Id = id;
            m_Roots.Add(root);
            BoundsWorld = boundsWorld;
            BoundsMin = boundsWorld.min;
            BoundsMax = boundsWorld.max;
            Refresh();
        }

        /// <summary>0, 1, ... in the order the rooms were added.</summary>
        public int Id { get; }
        /// <summary>The root the room was registered with.</summary>
        public Transform Root { get { return m_Roots[0]; } }
        /// <summary>The room's bounds in world space, which is the room frame.</summary>
        public Bounds BoundsWorld { get; }
        public float3 BoundsMin { get; }
        public float3 BoundsMax { get; }
        /// <summary>The layer its renderers are on: Default, or a room layer.</summary>
        public int Layer { get { return m_Layer; } }
        /// <summary>The stencil reference it is drawn with: 0 on Default, 1 .. 15 on a room layer.</summary>
        public int Reference { get { return PortalStencilTable.ReferenceOfLayer(m_Layer); } }
        /// <summary>False while the room is switched off.</summary>
        public bool IsOn { get { return m_On; } }
        /// <summary>True from the frame the room is the walker's or is shown through a doorway, until it is switched off.</summary>
        public bool WasShown { get; internal set; }
        public int RendererCount { get { return m_Renderers.Count; } }
        public int MaterialCount { get { return m_Materials.Count; } }

        internal void AddRoot(Transform root)
        {
            if (root == null || m_Roots.Contains(root)) return;
            m_Roots.Add(root);
            Refresh();
        }

        /// <summary>Reads the renderers and their materials again, and puts the new ones where the room is.</summary>
        internal void Refresh()
        {
            m_Renderers.Clear();
            m_Materials.Clear();
            for (int r = 0; r < m_Roots.Count; r++)
            {
                Transform root = m_Roots[r];
                if (root == null) continue;
                s_Found.Clear();
                root.GetComponentsInChildren(true, s_Found);
                for (int k = 0; k < s_Found.Count; k++)
                {
                    Renderer renderer = s_Found[k];
                    m_Renderers.Add(renderer);
                    Material material = renderer.sharedMaterial;
                    if (material != null && !m_Materials.Contains(material)) m_Materials.Add(material);
                }
            }
            s_Found.Clear();
            Apply();
        }

        internal void SetLayer(int layer)
        {
            if (layer == m_Layer) return;
            m_Layer = layer;
            for (int k = 0; k < m_Renderers.Count; k++)
            {
                Renderer renderer = m_Renderers[k];
                if (renderer != null) renderer.gameObject.layer = layer;
            }
        }

        /// <summary>The clip plane of every material of the room; (0, 0, 0, 1) clips nothing.</summary>
        internal void SetPlane(float4 plane)
        {
            if (math.all(plane == m_Plane)) return;
            m_Plane = plane;
            for (int k = 0; k < m_Materials.Count; k++)
            {
                Material material = m_Materials[k];
                if (material != null) MaterialPool.SetPortalPlane(material, plane);
            }
        }

        internal void SetOn(bool on)
        {
            if (on == m_On) return;
            m_On = on;
            for (int r = 0; r < m_Roots.Count; r++)
            {
                Transform root = m_Roots[r];
                if (root != null) root.gameObject.SetActive(on);
            }
        }

        // After a refresh: renderers and materials that are new take the room's layer and plane.
        void Apply()
        {
            for (int k = 0; k < m_Renderers.Count; k++)
            {
                Renderer renderer = m_Renderers[k];
                if (renderer != null && renderer.gameObject.layer != m_Layer) renderer.gameObject.layer = m_Layer;
            }
            for (int k = 0; k < m_Materials.Count; k++) MaterialPool.SetPortalPlane(m_Materials[k], m_Plane);
        }
    }
}

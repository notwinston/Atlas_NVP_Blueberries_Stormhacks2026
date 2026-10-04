using System;
using Atlas.Blades.Network.Core;
using Atlas.Rendering;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Blades.Network
{
    /// <summary>
    /// V2 floor map (section 4 V2; contract NET-06): the thin MonoBehaviour over <see cref="NetworkFloorMesher"/>. Three
    /// meshes (nodes, edges, highlight), one pooled material each, so three draws per eye. It joins the frame loop through
    /// <see cref="IFrameLoop.AddUpload"/> and uploads a mesh only on a frame that rebuilt it. Built in code, hidden.
    /// </summary>
    public sealed class NetworkFloorView : MonoBehaviour, IUploadStep
    {
        // The plain fields NetworkParams (NET-07) writes; no view reads the registry (contract 6.3).
        public float RecentreSeconds = NetworkFocus.DefaultRecentreSeconds;
        public int MaxGazeNodes = 64;
        public RoutingRule Rule = RoutingRule.Hyperbolic;
        /// <summary>net.file: 0 = seed 7, 1 = seed 83.</summary>
        public int File;

        MaterialPool m_Pool;
        Mesh m_NodesMesh, m_EdgesMesh, m_HighlightMesh;
        Material m_NodesMaterial, m_EdgesMaterial, m_HighlightMaterial;
        MeshRenderer m_NodesRenderer, m_EdgesRenderer, m_HighlightRenderer;
        bool m_Visible;

        public NetworkFloorMesher Mesher { get; private set; }
        public NetworkFocus Focus { get; private set; }
        public MeshRenderer[] Renderers { get; private set; }
        public Material[] Materials { get; private set; }
        public Mesh[] Meshes { get; private set; }

        /// <summary>Builds the view in code, hidden, under no parent (the room frame is the world frame).</summary>
        public static NetworkFloorView Create(NetworkModel model, MaterialPool pool)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            var go = new GameObject("Network Floor");
            var view = go.AddComponent<NetworkFloorView>();
            view.Build(model, pool);
            return view;
        }

        void Build(NetworkModel model, MaterialPool pool)
        {
            m_Pool = pool;
            Mesher = new NetworkFloorMesher(model);
            Focus = new NetworkFocus(model);

            m_NodesMesh = AtlasMesh.Create(Mesher.Nodes, "Network Nodes");
            m_EdgesMesh = AtlasMesh.Create(Mesher.Edges, "Network Edges");
            m_HighlightMesh = AtlasMesh.Create(Mesher.Highlight, "Network Highlight");

            m_NodesMaterial = Rent(AtlasPalette.Text);
            m_EdgesMaterial = Rent(AtlasPalette.Text);
            MaterialPool.SetLineColor(m_EdgesMaterial, AtlasPalette.Linear(AtlasPalette.GridLine));
            m_HighlightMaterial = Rent(AtlasPalette.Text);

            m_NodesRenderer = AddRenderer("Nodes", m_NodesMesh, m_NodesMaterial);
            m_EdgesRenderer = AddRenderer("Edges", m_EdgesMesh, m_EdgesMaterial);
            m_HighlightRenderer = AddRenderer("Highlight", m_HighlightMesh, m_HighlightMaterial);
            Renderers = new[] { m_NodesRenderer, m_EdgesRenderer, m_HighlightRenderer };
            Materials = new[] { m_NodesMaterial, m_EdgesMaterial, m_HighlightMaterial };
            Meshes = new[] { m_NodesMesh, m_EdgesMesh, m_HighlightMesh };
            Visible = false;
        }

        Material Rent(int tintIndex)
        {
            Material m = m_Pool.Rent();
            MaterialPool.SetTintIndex(m, tintIndex);
            return m;
        }

        MeshRenderer AddRenderer(string name, Mesh mesh, Material material)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = child.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            // The look has no shadows, probes or motion vectors (contract README F7).
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return r;
        }

        /// <summary>Shown or hidden; NET-08's exhibit drives it. A hidden view does no work in the frame loop.</summary>
        public bool Visible
        {
            get { return m_Visible; }
            set
            {
                m_Visible = value;
                if (Renderers == null) return;
                for (int i = 0; i < Renderers.Length; i++) Renderers[i].enabled = value;
            }
        }

        /// <summary>Section 6.2: the disc sits at the room origin on the floor, its radius from the free rectangle.</summary>
        public void Place(RoomModel room)
        {
            float radius = room == null ? NetworkFloorMesher.DiscRadiusMetres : NetworkFloorMesher.DiscRadius(room.FreeRect);
            float s = radius / NetworkFloorMesher.DiscRadiusMetres;
            transform.localPosition = Vector3.zero;
            transform.localScale = new Vector3(s, 1f, s);
        }

        /// <summary>A node's post base in the room frame, now.</summary>
        public float3 RoomPosition(int node)
        {
            float3 p = Mesher.NodePosition(node);
            Vector3 s = transform.localScale;
            return new float3(p.x * s.x, 0f, p.z * s.z);
        }

        /// <summary>Starts the glide that brings a node to the centre.</summary>
        public bool Recentre(int node) { return Focus.Begin(node); }

        public void SetHighlight(int[] path, int count) { Mesher.SetHighlight(path, count); }

        public void Attach(IFrameLoop loop)
        {
            if (loop == null) throw new ArgumentNullException(nameof(loop));
            loop.AddUpload(this);
        }

        public void Upload(in FrameContext frame)
        {
            if (!m_Visible) return;
            Focus.RecentreSeconds = RecentreSeconds;
            if (!Mesher.Step(Focus, frame.DeltaTime)) return;
            if (Mesher.GeometryRebuilt)
            {
                AtlasMesh.Apply(Mesher.Nodes, m_NodesMesh);
                AtlasMesh.Apply(Mesher.Edges, m_EdgesMesh);
            }
            if (Mesher.HighlightRebuilt) AtlasMesh.Apply(Mesher.Highlight, m_HighlightMesh);
        }

        void OnDestroy()
        {
            if (m_Pool != null)
            {
                if (m_NodesMaterial != null) m_Pool.Return(m_NodesMaterial);
                if (m_EdgesMaterial != null) m_Pool.Return(m_EdgesMaterial);
                if (m_HighlightMaterial != null) m_Pool.Return(m_HighlightMaterial);
            }
            DestroyMesh(m_NodesMesh);
            DestroyMesh(m_EdgesMesh);
            DestroyMesh(m_HighlightMesh);
        }

        static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
        }
    }
}

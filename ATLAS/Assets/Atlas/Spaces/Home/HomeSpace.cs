using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Spaces.Home
{
    /// <summary>
    /// Home, space S0 (Master Plan sections 4.5 and 4.9, decision D18): the scanned room rebuilt 1:1, flat
    /// and registered, so a virtual wall is the real wall. The room itself is drawn by the shared room
    /// content; this module shows it in full and adds the floor marks.
    /// </summary>
    public sealed class HomeSpace : MonoBehaviour, ISpaceModule
    {
        readonly HomeSpaceCore m_Core = new HomeSpaceCore();

        MaterialPool m_Pool;
        Material m_MarkMaterial;
        Mesh m_MarkMesh;
        MeshFilter m_MarkFilter;
        MeshRenderer m_MarkRenderer;
        bool m_MarksShown;

        public SpaceId Id { get { return SpaceId.Home; } }
        public bool Registered { get { return m_Core.Registered; } }
        /// <summary>The logic behind this component.</summary>
        public HomeSpaceCore Core { get { return m_Core; } }
        /// <summary>The renderer of the floor marks, or null when there is no material pool.</summary>
        public Renderer MarkRenderer { get { return m_MarkRenderer; } }

        /// <summary>The factory given to SpaceRegistry: a new object named like the scene, with the module.</summary>
        public static ISpaceModule Create()
        {
            var go = new GameObject(SpaceIds.SceneName(SpaceId.Home));
            return go.AddComponent<HomeSpace>();
        }

        /// <summary>Declares the parameters of group "home".</summary>
        public static void DeclareParams(ParamRegistry registry)
        {
            HomeSpaceCore.DeclareParams(registry);
        }

        public void Configure(RoomModel room, ParamRegistry registry)
        {
            AtlasServices.TryGet(out IRoomContent content);
            m_Core.Configure(room, registry, content);
            ReleaseMarks();
            if (AtlasServices.TryGet(out MaterialPool pool)) BuildMarks(room, pool);
        }

        public void Enter(in WalkerPose start)
        {
            // Registered: the module's root is the room frame itself (contract README F4).
            Transform root = transform;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
            m_Core.Enter();
            ApplyMarks();
        }

        public void Tick(in FrameContext frame)
        {
            // The only thing that can change while Home runs is the operator's switch for the marks.
            if (m_Core.CentreMarkShown != m_MarksShown) ApplyMarks();
        }

        public void Exit()
        {
            m_Core.Exit();
            ReleaseMarks();
        }

        public void WriteState(ref StateMessage state)
        {
            HomeSpaceCore.WriteState(ref state);
        }

        void OnDestroy()
        {
            // Exit has returned the material already; this only covers a root destroyed without Exit.
            DestroyObject(m_MarkMesh);
            m_MarkMesh = null;
        }

        void BuildMarks(RoomModel room, MaterialPool pool)
        {
            var data = new AtlasMeshBuilder(128, 1024);
            HomeMarks.Build(room, data);
            m_MarkMesh = AtlasMesh.Create(data, "Home Marks");

            if (m_MarkRenderer == null)
            {
                var go = new GameObject("Marks");
                go.transform.SetParent(transform, false);
                m_MarkFilter = go.AddComponent<MeshFilter>();
                m_MarkRenderer = go.AddComponent<MeshRenderer>();
                // The look has no shadows, probes or motion vectors (contract README F7).
                m_MarkRenderer.shadowCastingMode = ShadowCastingMode.Off;
                m_MarkRenderer.receiveShadows = false;
                m_MarkRenderer.lightProbeUsage = LightProbeUsage.Off;
                m_MarkRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                m_MarkRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }
            m_MarkFilter.sharedMesh = m_MarkMesh;

            m_Pool = pool;
            m_MarkMaterial = pool.Rent();
            m_MarkRenderer.sharedMaterial = m_MarkMaterial;
            m_MarkRenderer.enabled = false;
            m_MarksShown = false;
        }

        void ApplyMarks()
        {
            m_MarksShown = m_Core.CentreMarkShown;
            if (m_MarkRenderer != null) m_MarkRenderer.enabled = m_MarksShown;
        }

        void ReleaseMarks()
        {
            m_MarksShown = false;
            if (m_MarkRenderer != null)
            {
                m_MarkRenderer.enabled = false;
                m_MarkRenderer.sharedMaterial = null;
                m_MarkFilter.sharedMesh = null;
            }
            if (m_MarkMaterial != null && m_Pool != null) m_Pool.Return(m_MarkMaterial);
            m_MarkMaterial = null;
            m_Pool = null;
            DestroyObject(m_MarkMesh);
            m_MarkMesh = null;
        }

        static void DestroyObject(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}

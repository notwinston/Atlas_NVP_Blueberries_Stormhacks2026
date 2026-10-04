using Atlas.Rendering;
using Atlas.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Blades.Playground.Shell
{
    /// <summary>
    /// Draws one flat floor mesh built by an engine-free builder: the centre disc of beat 4 and the arrows of
    /// beat 6. One pooled material, no shadows or probes (contract README F7). The mesh is replaced by
    /// <see cref="Show"/>; <see cref="Hide"/> turns the renderer off and keeps the material.
    /// </summary>
    public sealed class PlaygroundFloorView : MonoBehaviour
    {
        MaterialPool m_Pool;
        Material m_Material;
        Mesh m_Mesh;
        MeshFilter m_Filter;
        MeshRenderer m_Renderer;

        /// <summary>True while the mesh is drawn.</summary>
        public bool Shown { get { return m_Renderer != null && m_Renderer.enabled; } }

        /// <summary>Uploads the builder's contents and shows them.</summary>
        public void Show(AtlasMeshBuilder data, string name)
        {
            EnsureRenderer();
            if (m_Mesh == null) m_Mesh = AtlasMesh.Create(data, name);
            else AtlasMesh.Apply(data, m_Mesh);
            m_Filter.sharedMesh = m_Mesh;
            m_Renderer.enabled = data.TriangleCount > 0;
        }

        public void Hide()
        {
            if (m_Renderer != null) m_Renderer.enabled = false;
        }

        void EnsureRenderer()
        {
            if (m_Renderer != null) return;
            m_Filter = gameObject.AddComponent<MeshFilter>();
            m_Renderer = gameObject.AddComponent<MeshRenderer>();
            m_Renderer.shadowCastingMode = ShadowCastingMode.Off;
            m_Renderer.receiveShadows = false;
            m_Renderer.lightProbeUsage = LightProbeUsage.Off;
            m_Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            m_Renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            if (AtlasServices.TryGet(out MaterialPool pool))
            {
                m_Pool = pool;
                m_Material = pool.Rent();
                m_Renderer.sharedMaterial = m_Material;
            }
            m_Renderer.enabled = false;
        }

        void OnDestroy()
        {
            if (m_Material != null && m_Pool != null) m_Pool.Return(m_Material);
            m_Material = null;
            if (m_Mesh != null) Destroy(m_Mesh);
            m_Mesh = null;
        }
    }
}

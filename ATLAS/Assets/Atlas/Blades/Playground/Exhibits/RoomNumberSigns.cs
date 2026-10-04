using System.Collections.Generic;
using Atlas.Rendering;
using Atlas.Spaces.Portal;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Blades.Playground.Exhibits
{
    /// <summary>
    /// Draws the room-number signs of the loop corridor (Master Plan task P2-C16): one mesh per corridor room,
    /// joined to the room it belongs to in the portal renderer, so the stencil shows it only where the room
    /// is seen. The mesh data is <see cref="RoomSigns"/>; this is the thin adapter that makes objects of it.
    /// </summary>
    public sealed class RoomNumberSigns : MonoBehaviour
    {
        readonly List<GameObject> m_Pieces = new List<GameObject>(8);
        readonly List<Mesh> m_Meshes = new List<Mesh>(8);
        readonly List<Material> m_Materials = new List<Material>(8);
        MaterialPool m_Pool;

        /// <summary>Signs standing now.</summary>
        public int Count { get { return m_Pieces.Count; } }

        /// <summary>
        /// Replaces the signs with those of the layout, as parts of the renderer's rooms. Call after the portal
        /// space has registered the layout's rooms. A layout with no loop gets none.
        /// </summary>
        public void Rebuild(PortalLayout layout, PortalRenderer portals)
        {
            Release();
            if (layout == null || portals == null) return;
            if (m_Pool == null) AtlasServices.TryGet(out m_Pool);
            if (m_Pool == null) return;

            var data = new AtlasMeshBuilder(256, 512);
            List<RoomSign> signs = RoomSigns.Layout(layout);
            for (int k = 0; k < signs.Count; k++)
            {
                RoomSign sign = signs[k];
                if (sign.Room >= portals.RoomCount) continue;
                data.Clear();
                RoomSigns.BuildSign(sign, data, AtlasPalette.Beacon);
                if (data.IsEmpty) continue;

                var go = new GameObject("Room " + sign.Number + " sign");
                go.transform.SetParent(transform, false);
                m_Pieces.Add(go);
                Mesh mesh = AtlasMesh.Create(data, "Room sign " + sign.Number);
                m_Meshes.Add(mesh);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;

                var meshRenderer = go.AddComponent<MeshRenderer>();
                // The look has no shadows, probes or motion vectors (contract README F7).
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
                meshRenderer.lightProbeUsage = LightProbeUsage.Off;
                meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                Material material = m_Pool.Rent();
                MaterialPool.SetLines(material, 0f, 0f, float2.zero);
                m_Materials.Add(material);
                meshRenderer.sharedMaterial = material;

                portals.AddRoomPart(sign.Room, go.transform);
                portals.RefreshRoom(sign.Room);
            }
        }

        /// <summary>Removes every sign and returns its material to the pool.</summary>
        public void Release()
        {
            for (int k = 0; k < m_Pieces.Count; k++) DestroyObject(m_Pieces[k]);
            m_Pieces.Clear();
            for (int k = 0; k < m_Meshes.Count; k++) DestroyObject(m_Meshes[k]);
            m_Meshes.Clear();
            if (m_Pool != null)
                for (int k = 0; k < m_Materials.Count; k++) m_Pool.Return(m_Materials[k]);
            m_Materials.Clear();
        }

        void OnDestroy()
        {
            Release();
        }

        static void DestroyObject(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}

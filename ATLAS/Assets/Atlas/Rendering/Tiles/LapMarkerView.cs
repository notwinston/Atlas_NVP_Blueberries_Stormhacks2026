using System;
using Atlas.Kernel;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Rendering
{
    // P1-B09: draws the lap flag (Beacon 0) and the next beacon of a LapWalk (Master Plan §3.3, §4.4, §10.1.3), each as a warped object
    // (§8.3: one material instance each, no MaterialPropertyBlock). Anchor A = inverse(U) * F0 * Beacon_k, uploaded through
    // WarpAnchor.ToMatrix (the only double-to-float cast, §4.6). At kappa = 0 the marker is an ordinary object at head + (A.o).xz.
    public sealed class LapMarkerView : IDisposable
    {
        readonly Marker flag, next;
        readonly GameObject root;
        bool visible = true;

        sealed class Marker
        {
            public GameObject Go; public MeshRenderer Renderer; public Material Material; public Mesh Mesh;
        }

        public LapMarkerView(Transform parent, Material shared)
        {
            if (shared == null) throw new ArgumentNullException(nameof(shared));
            root = new GameObject("LapMarkers") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(parent, false);
            flag = Make("LapFlag", shared, LapMarkerMesh.FlagHeightM, LapMarkerMesh.FlagUv);
            next = Make("LapBeacon", shared, LapMarkerMesh.BeaconHeightM, LapMarkerMesh.BeaconUv);
        }

        Marker Make(string name, Material shared, float height, float2 uv)
        {
            var m = new Marker { Go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave } };
            m.Go.transform.SetParent(root.transform, false);
            m.Mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            LapMarkerMesh.Build(m.Mesh, height, LapMarkerMesh.RadiusM, uv);
            m.Go.GetComponent<MeshFilter>().sharedMesh = m.Mesh;
            m.Renderer = m.Go.GetComponent<MeshRenderer>();
            m.Material = new Material(shared) { hideFlags = HideFlags.DontSave };
            m.Renderer.sharedMaterial = m.Material;
            AtlasWarpBounds.Apply(m.Renderer);
            m.Renderer.shadowCastingMode = ShadowCastingMode.Off; m.Renderer.receiveShadows = false;
            m.Renderer.enabled = false;
            return m;
        }

        public bool Visible
        {
            get => visible;
            set { visible = value; if (!value) { flag.Renderer.enabled = false; next.Renderer.enabled = false; } }
        }

        public MeshRenderer FlagRenderer => flag.Renderer;
        public MeshRenderer BeaconRenderer => next.Renderer;
        public Material FlagMaterial => flag.Material;
        public Material BeaconMaterial => next.Material;

        public void Upload(LapWalk walk, in Iso2 tileZeroInCurrent, in Iso2 walker, double kappa, double lengthScaleM, Vector3 headPosWS)
        {
            if (!visible || walk == null || !walk.Armed) { flag.Renderer.enabled = false; next.Renderer.enabled = false; return; }
            Place(flag, walk, 0, tileZeroInCurrent, walker, kappa, headPosWS);
            int nb = walk.NextBeacon;
            if (nb > 0) Place(next, walk, nb, tileZeroInCurrent, walker, kappa, headPosWS);
            else next.Renderer.enabled = false;
        }

        static void Place(Marker m, LapWalk walk, int k, in Iso2 f0, in Iso2 walker, double kappa, Vector3 head)
        {
            Iso2 a = walker.Inverse() * f0 * walk.Beacon(k);   // anchor in double, MP §4.6
            if (kappa == 0)
            {
                m.Material.SetFloat(AtlasShaderIDs.WarpMode, AtlasWarpCpu.ModeNone);
                double3 p = a.M.c2;
                m.Go.transform.position = new Vector3(head.x + (float)p.x, 0f, head.z + (float)p.y);
            }
            else
            {
                m.Material.SetFloat(AtlasShaderIDs.WarpMode, kappa < 0 ? AtlasWarpCpu.ModeHyperbolic : AtlasWarpCpu.ModeSpherical);
                m.Material.SetMatrix(AtlasShaderIDs.Anchor, WarpAnchor.ToMatrix(a));
                m.Go.transform.position = Vector3.zero;
            }
            m.Renderer.enabled = true;
        }

        public void Dispose()
        {
            Kill(flag); Kill(next);
            if (root != null) Obj(root);
        }

        static void Kill(Marker m)
        {
            if (m == null) return;
            Obj(m.Go); Obj(m.Material); Obj(m.Mesh);
        }

        static void Obj(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}

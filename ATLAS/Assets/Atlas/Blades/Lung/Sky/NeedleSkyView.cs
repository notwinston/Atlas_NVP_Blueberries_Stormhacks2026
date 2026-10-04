using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>The sky sphere GameObject, its owned Material and the 11 textures (MP §10.2.5). No MaterialPropertyBlock (MP §8.3).</summary>
    public sealed class NeedleSkyView : IDisposable
    {
        readonly GameObject _go;
        readonly Transform _tf;
        readonly Material _mat;
        readonly Mesh _mesh;
        readonly SkyTextures _tex;
        int _phase = -1;

        public NeedleSkyView(SkyMaps maps, Shader shader, Transform parent)
        {
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            _tex = new SkyTextures(maps);
            _mat = new Material(shader) { name = "NeedleSky" };
            _mat.SetTexture(NeedleSkyShaderIDs.RobustTex, _tex.Robust);
            _mat.SetFloat(NeedleSkyShaderIDs.MmPerUnit, maps.Meta.mm_per_unit);
            _mat.SetFloat(NeedleSkyShaderIDs.ThresholdMm, maps.Meta.threshold_mm);
            _mesh = NeedleSkyMesh.Build();
            _go = new GameObject("NeedleSky");
            _tf = _go.transform;
            if (parent != null) _tf.SetParent(parent, false);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            _go.SetActive(false);
        }

        public Material Material => _mat;
        public GameObject GameObject => _go;
        public SkyTextures Textures => _tex;

        public void SetVisible(bool v) { if (_go != null && _go.activeSelf != v) _go.SetActive(v); }

        public void Present(in NeedleSkyFrame f)
        {
            _tf.SetPositionAndRotation(f.HeadPosition, Quaternion.identity);
            if (f.Phase != _phase) { _phase = f.Phase; _mat.SetTexture(NeedleSkyShaderIDs.PhaseTex, _tex.Phase(_phase)); }
            _mat.SetFloat(NeedleSkyShaderIDs.RobustOn, f.RobustOn ? 1f : 0f);
            _mat.SetFloat(NeedleSkyShaderIDs.ArcsOn, f.ArcsOn ? 1f : 0f);
            _mat.SetVector(NeedleSkyShaderIDs.PickA, new Vector4(f.Pick0.x, f.Pick0.y, f.Pick0.z, f.PickCount > 0 ? 1f : 0f));
            _mat.SetVector(NeedleSkyShaderIDs.PickB, new Vector4(f.Pick1.x, f.Pick1.y, f.Pick1.z, f.PickCount > 1 ? 1f : 0f));
            _mat.SetVector(NeedleSkyShaderIDs.PickC, new Vector4(f.Pick2.x, f.Pick2.y, f.Pick2.z, f.PickCount > 2 ? 1f : 0f));
            _mat.SetVector(NeedleSkyShaderIDs.Cursor, new Vector4(f.Cursor.x, f.Cursor.y, f.Cursor.z, 1f));
        }

        public void Dispose()
        {
            _tex.Dispose();
            Kill(_mat); Kill(_mesh); Kill(_go);
        }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}

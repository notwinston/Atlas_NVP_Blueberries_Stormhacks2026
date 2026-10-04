// P1-B06: the tile renderer. A pool of tile objects with one material instance each (MP 8.3: no MaterialPropertyBlock),
// per-tile anchor uploaded every frame, fog from rho - 1 and opaque at rho (MP 8.5). No Update/LateUpdate: the frame loop
// drives KernelStep (MP 6.3 step 4) and Upload (step 6).
using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.World;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Rendering
{
    [ExecuteAlways, DisallowMultipleComponent]   // [P1-B06 D15] ExecuteAlways so OnEnable/OnDisable/OnDestroy also run in EditMode tests
    public sealed class TileRenderer : MonoBehaviour
    {
        public const int PoolSize = TileSelection.MaxDrawn;
        static readonly ProfilerMarker SelectMarker = new ProfilerMarker("Atlas.Tiles.Select");   // [P1-B06 D11]
        static readonly ProfilerMarker UploadMarker = new ProfilerMarker("Atlas.Tiles.Upload");

        TileSelection selection;
        TileShellCache shells;
        readonly List<MeshRenderer> slots = new List<MeshRenderer>(PoolSize);
        MeshFilter[] filters;
        Material[] mats;
        int[] slotLod;
        bool[] slotOn;
        int drawnCount;
        float savedFogStart, savedFogEnd; bool fogCaptured;

        public TileSelection Selection => selection;
        public TileShellCache Shells => shells;
        public int DrawnCount => drawnCount;
        public IReadOnlyList<MeshRenderer> Slots => slots;

        public double CullRho { get => selection.Rho; set => selection.Rho = value; }   // curved.cullRho, MP 12.4

        /// <summary>Allocates the pool once. shared is the one tile material (Atlas/AtlasSurface); every slot gets its own instance.</summary>
        public void Configure(ITileGraph graph, double lengthScale, Material shared)
        {
            if (shared == null) throw new ArgumentNullException(nameof(shared));
            Release();
            CaptureFog();
            selection = new TileSelection(graph, lengthScale);
            shells = new TileShellCache(graph.Q, lengthScale);
            filters = new MeshFilter[PoolSize]; mats = new Material[PoolSize]; slotLod = new int[PoolSize]; slotOn = new bool[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var child = new GameObject("Tile" + i.ToString("000")) { hideFlags = HideFlags.DontSave };
                child.transform.SetParent(transform, false);   // identity transform: the warp ignores it
                filters[i] = child.AddComponent<MeshFilter>();
                var r = child.AddComponent<MeshRenderer>();
                var mat = new Material(shared) { hideFlags = HideFlags.DontSave };
                mat.SetFloat(AtlasShaderIDs.WarpMode, AtlasWarpCpu.ModeHyperbolic);
                r.sharedMaterial = mat;
                AtlasWarpBounds.Apply(r);
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.enabled = false;
                mats[i] = mat; slots.Add(r);
            }
        }

        public void SetLengthScale(double lengthScale)
        {
            if (selection == null) return;
            selection.L = lengthScale;
            if (lengthScale != shells.L) shells.Rebuild(lengthScale);
        }

        /// <summary>MP 6.3 step 4: one draw-rule evaluation per frame for both eyes.</summary>
        public void KernelStep(in WalkerPose pose, in FrameContext frame)
        {
            double halfSide = TileDrawRule.DefaultHalfSideDeg, halfUp = TileDrawRule.DefaultHalfUpDownDeg;   // [P1-B06 D8]
            var rig = StereoRig.Current;
            if (rig != null && rig.LeftEye != null)
            {
                var cam = rig.LeftEye;
                halfUp = cam.fieldOfView * 0.5;
                halfSide = Math.Atan(Math.Tan(halfUp * Math.PI / 180.0) * cam.aspect) * 180.0 / Math.PI;
            }
            var cone = TileViewCone.FromHead(frame.HeadPosition, frame.HeadRotation, halfSide, halfUp);
            using (SelectMarker.Auto()) selection.Update(pose, cone);
        }

        /// <summary>MP 6.3 step 6: globals once per frame, then _Anchor per drawn tile (float cast last).</summary>
        public void Upload(in FrameContext frame)
        {
            using (UploadMarker.Auto())
            {
                double L = selection.L, rho = selection.Rho;
                Shader.SetGlobalFloat(AtlasShaderIDs.HypL, (float)L);                                           // kernel.md 6.2
                Shader.SetGlobalVector(AtlasShaderIDs.HeadPosWS, new Vector4(frame.HeadPosition.x, frame.HeadPosition.y, frame.HeadPosition.z, 0f));
                Shader.SetGlobalFloat(AtlasShaderIDs.FogStart, (float)((rho - 1.0) * L));                       // MP 8.5 "fog starts at rho - 1", metres (PH-7)
                Shader.SetGlobalFloat(AtlasShaderIDs.FogEnd, (float)(rho * L));                                 // "fully opaque at rho"
                int n = selection.DrawnCount;
                var drawn = selection.Drawn;
                for (int i = 0; i < n; i++)
                {
                    mats[i].SetMatrix(AtlasShaderIDs.Anchor, WarpAnchor.ToMatrix(drawn[i].Anchor));
                    int lod = (int)drawn[i].Lod;
                    if (slotLod[i] != lod) { filters[i].sharedMesh = shells.Get(drawn[i].Lod); slotLod[i] = lod; }
                    if (!slotOn[i]) { slots[i].enabled = true; slotOn[i] = true; }
                }
                for (int i = n; i < PoolSize; i++)
                    if (slotOn[i]) { slots[i].enabled = false; slotOn[i] = false; }
                drawnCount = n;
            }
        }

        /// <summary>Destroys children, materials and meshes; restores the fog globals.</summary>
        public void Release()
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null) Kill(slots[i].gameObject);
                if (mats != null) Kill(mats[i]);
            }
            slots.Clear();
            filters = null; mats = null; slotLod = null; slotOn = null; drawnCount = 0;
            shells?.Dispose(); shells = null;
            selection = null;
            RestoreFog();
        }

        void OnEnable() => CaptureFog();
        void OnDisable() => RestoreFog();
        void OnDestroy() => Release();

        void CaptureFog()
        {
            savedFogStart = Shader.GetGlobalFloat(AtlasShaderIDs.FogStart);
            savedFogEnd = Shader.GetGlobalFloat(AtlasShaderIDs.FogEnd);
            fogCaptured = true;
        }

        void RestoreFog()
        {
            if (!fogCaptured) return;
            Shader.SetGlobalFloat(AtlasShaderIDs.FogStart, savedFogStart);
            Shader.SetGlobalFloat(AtlasShaderIDs.FogEnd, savedFogEnd);
        }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}

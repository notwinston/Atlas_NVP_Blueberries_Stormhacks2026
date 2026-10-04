// P1-B06: the tile renderer. A pool of tile objects with one pooled material instance each (MP 8.3: no MaterialPropertyBlock),
// per-tile anchor uploaded every frame, fog from rho - 1 and opaque at rho (MP 8.5). No Update/LateUpdate: the frame loop
// drives KernelStep (MP 6.3 step 4) and Upload (step 6). The globals _HypL and _HeadPosWS are AtlasLook.Upload's (the frame
// loop's look step, from IWalkerKernel.MetresPerUnit and the head): MetresPerUnit must equal this renderer's length scale.
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
        public const float EdgeLineStrength = 1f;   // the tile edges, drawn by the look's lines (MP 8.1)
        static readonly ProfilerMarker SelectMarker = new ProfilerMarker("Atlas.Tiles.Select");   // [P1-B06 D11]
        static readonly ProfilerMarker UploadMarker = new ProfilerMarker("Atlas.Tiles.Upload");

        TileSelection selection;
        TileShellCache shells;
        readonly List<MeshRenderer> slots = new List<MeshRenderer>(PoolSize);
        MeshFilter[] filters;
        Material[] mats;
        MaterialPool pool;
        int[] slotLod;
        bool[] slotOn;
        int drawnCount;
        float savedFogStart, savedFogEnd; bool fogCaptured;

        public TileSelection Selection => selection;
        public TileShellCache Shells => shells;
        public int DrawnCount => drawnCount;
        public IReadOnlyList<MeshRenderer> Slots => slots;

        public double CullRho { get => selection.Rho; set => selection.Rho = value; }   // curved.cullRho, MP 12.4

        /// <summary>The rig whose eye frustum the draw rule widens. Configure takes the published one; null gives the MP 8.5 defaults.</summary>
        public IStereoRig Rig { get; set; }

        /// <summary>
        /// False while WorldContentRenderer draws the tiles (shell and generated content in one mesh, P1-B07): this renderer then
        /// keeps the selection, the anchors and the fog, and neither creates nor shows its own tile objects. Set before Configure.
        /// </summary>
        public bool DrawShells { get; set; } = true;

        /// <summary>Allocates the tile objects once. Every slot rents its own Atlas/Surface instance from the pool; Release returns them.</summary>
        public void Configure(ITileGraph graph, double lengthScale, MaterialPool pool)
        {
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            Release();
            CaptureFog();
            this.pool = pool;
            if (Rig == null && AtlasServices.TryGet(out IStereoRig published)) Rig = published;
            selection = new TileSelection(graph, lengthScale);
            shells = new TileShellCache(graph.Q, lengthScale);
            filters = new MeshFilter[PoolSize]; mats = new Material[PoolSize]; slotLod = new int[PoolSize]; slotOn = new bool[PoolSize];
            for (int i = 0; DrawShells && i < PoolSize; i++)
            {
                var child = new GameObject("Tile" + i.ToString("000")) { hideFlags = HideFlags.DontSave };
                child.transform.SetParent(transform, false);   // identity transform: the warp ignores it
                filters[i] = child.AddComponent<MeshFilter>();
                var r = child.AddComponent<MeshRenderer>();
                var mat = pool.Rent();
                mat.SetFloat(AtlasShaderIDs.WarpMode, AtlasWarpCpu.ModeHyperbolic);
                MaterialPool.SetLines(mat, EdgeLineStrength, 0f, float2.zero);
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
            var cam = Rig != null ? Rig.GetEyeCamera(Eye.Left) : null;
            if (cam != null)
            {
                // The eye frustum is off-axis and the right eye mirrors the left: take the wider tangent of each pair.
                Matrix4x4 m = cam.projectionMatrix;
                double side = Math.Max(Math.Abs((-1.0 + m[0, 2]) / m[0, 0]), Math.Abs((1.0 + m[0, 2]) / m[0, 0]));
                double up = Math.Max(Math.Abs((-1.0 + m[1, 2]) / m[1, 1]), Math.Abs((1.0 + m[1, 2]) / m[1, 1]));
                halfSide = Math.Atan(side) * 180.0 / Math.PI;
                halfUp = Math.Atan(up) * 180.0 / Math.PI;
            }
            var cone = TileViewCone.FromHead(frame.HeadPosition, frame.HeadRotation, halfSide, halfUp);
            using (SelectMarker.Auto()) selection.Update(pose, cone);
        }

        /// <summary>MP 6.3 step 6: the fog once per frame, then _Anchor per drawn tile (float cast last).</summary>
        public void Upload(in FrameContext frame)
        {
            using (UploadMarker.Auto())
            {
                double L = selection.L, rho = selection.Rho;
                AtlasLook.SetFog((float)((rho - 1.0) * L), (float)(rho * L));   // MP 8.5 "fog starts at rho - 1", "fully opaque at rho", metres
                int n = DrawShells ? Math.Min(selection.DrawnCount, slots.Count) : 0;
                var drawn = selection.Drawn;
                for (int i = 0; i < n; i++)
                {
                    mats[i].SetMatrix(AtlasShaderIDs.Anchor, WarpAnchor.ToMatrix(drawn[i].Anchor));
                    int lod = (int)drawn[i].Lod;
                    if (slotLod[i] != lod) { filters[i].sharedMesh = shells.Get(drawn[i].Lod); slotLod[i] = lod; }
                    if (!slotOn[i]) { slots[i].enabled = true; slotOn[i] = true; }
                }
                for (int i = n; i < slots.Count; i++)
                    if (slotOn[i]) { slots[i].enabled = false; slotOn[i] = false; }
                drawnCount = n;
            }
        }

        /// <summary>Destroys children and meshes, returns the materials to the pool; restores the fog.</summary>
        public void Release()
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null) Kill(slots[i].gameObject);
                if (mats != null && pool.Owns(mats[i])) pool.Return(mats[i]);
            }
            slots.Clear();
            filters = null; mats = null; pool = null; slotLod = null; slotOn = null; drawnCount = 0;
            shells?.Dispose(); shells = null;
            selection = null;
            RestoreFog();
        }

        void OnEnable() => CaptureFog();
        void OnDisable() => RestoreFog();
        void OnDestroy() => Release();

        void CaptureFog()
        {
            savedFogStart = AtlasLook.State.FogStart;
            savedFogEnd = AtlasLook.State.FogEnd;
            fogCaptured = true;
        }

        void RestoreFog()
        {
            if (!fogCaptured) return;
            AtlasLook.SetFog(savedFogStart, savedFogEnd);
            // Once only: after Release, a deferred OnDisable / OnDestroy must not write over the fog of the space entered since.
            fogCaptured = false;
        }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}

using System;
using Atlas.Rendering;
using UnityEngine;

namespace Atlas.Blades.Lung.AirwayKit
{
    // Brief G5 2.6: 5 kinds x 3 LODs for one scale (kappa -1), or 5 meshes (kappa 0). Built at load and on an L change, never per frame.
    // LOD selection is P1-B06's MP 8.4 rule; the kit does not choose LODs.
    public sealed class AirwayKitMeshes : IDisposable
    {
        const int Kinds = 5, Lods = 3;
        readonly AirwayKitMeshBuilder builder;
        readonly Mesh[] meshes = new Mesh[Kinds * Lods];
        readonly KitMeshLayout[] layouts = new KitMeshLayout[Kinds * Lods];

        public int Kappa { get; private set; } = int.MinValue;
        public double Scale { get; private set; }

        public AirwayKitMeshes(AirwayKitMeshBuilder builder) { this.builder = builder ?? throw new ArgumentNullException(nameof(builder)); }

        static int Slot(KitKind k, TileLod lod) => (int)k * Lods + (lod == TileLod.Near ? 0 : lod == TileLod.Mid ? 1 : 2);

        public void Rebuild(int kappa, double scale)
        {
            if (kappa != -1 && kappa != 0) throw new ArgumentOutOfRangeException(nameof(kappa));
            Release();
            for (int k = 0; k < Kinds; k++)
                for (int l = 0; l < (kappa < 0 ? Lods : 1); l++)
                {
                    var lod = l == 0 ? TileLod.Near : l == 1 ? TileLod.Mid : TileLod.Far;
                    var spec = kappa < 0 ? TileMeshSpec.Hyperbolic(5, scale, lod, 0, 0) : TileMeshSpec.FlatCell(scale, 0, 0);
                    var m = new Mesh { name = $"AirwayKit.{(KitKind)k}.{(kappa < 0 ? lod.ToString() : "Flat")}" };
                    layouts[k * Lods + l] = builder.BuildTile((KitKind)k, spec, m);
                    meshes[k * Lods + l] = m;
                }
            Kappa = kappa; Scale = scale;
        }

        public Mesh Get(KitKind kind, TileLod lod) => meshes[Slot(kind, Kappa == 0 ? TileLod.Near : lod)] ?? throw new InvalidOperationException("AirwayKitMeshes.Rebuild has not run");
        public KitMeshLayout Layout(KitKind kind, TileLod lod) => layouts[Slot(kind, Kappa == 0 ? TileLod.Near : lod)];

        void Release()
        {
            for (int i = 0; i < meshes.Length; i++)
            {
                if (meshes[i] == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(meshes[i]); else UnityEngine.Object.DestroyImmediate(meshes[i]);
                meshes[i] = null;
            }
        }

        public void Dispose() { Release(); Kappa = int.MinValue; }
    }
}

using System;
using Atlas.Blades.Lung.AirwayKit;
using Atlas.Kernel;
using Atlas.Rendering;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Blades.Lung.Endoluminal
{
    // Brief G5 3.3 (D-15): an open, double-sided cylinder along an airway segment, vertex AO carrying the depth cue (the shared shader has no lighting).
    // Build has an engine-free form that fills Vertices and Indices, and a form that then uploads them to a Mesh.
    public sealed class AirwayTubeMeshBuilder
    {
        public const int Sides = 24, MaxVertices = 4096, MaxIndices = 16384;

        readonly AtlasVertex[] verts = new AtlasVertex[MaxVertices];
        readonly ushort[] idx = new ushort[MaxIndices];
        readonly float2 uv;

        public AirwayTubeMeshBuilder() : this(AirwayPalette.Default) { }
        public AirwayTubeMeshBuilder(AirwayPalette palette) { uv = palette.Tissue; }

        /// <summary>The vertices and indices of the last Build; <see cref="VertexCount"/> and <see cref="IndexCount"/> are valid.</summary>
        public AtlasVertex[] Vertices => verts;
        public ushort[] Indices => idx;
        public int VertexCount { get; private set; }
        public int IndexCount { get; private set; }

        public void Build(in AirwaySegmentMm seg, double fromMm, double toMm, double3 originMm, double s, double3 cameraMm, double parentRadiusMm, Mesh target)
        {
            Build(seg, fromMm, toMm, originMm, s, cameraMm, parentRadiusMm);
            Upload(target);
        }

        /// <summary>Engine-free: fills Vertices and Indices and uploads nothing.</summary>
        public void Build(in AirwaySegmentMm seg, double fromMm, double toMm, double3 originMm, double s, double3 cameraMm, double parentRadiusMm)
        {
            if (!(toMm > fromMm)) throw new ArgumentException("toMm must exceed fromMm");
            double r = seg.RadiusMm;
            int rings = Math.Max(2, (int)Math.Ceiling((toMm - fromMm) / (0.5 * r)) + 1);
            int vc = rings * Sides, ic = (rings - 1) * Sides * 12;
            if (vc > MaxVertices || ic > MaxIndices) throw new InvalidOperationException("AirwayTubeMeshBuilder: buffer budget exceeded");

            double3 d = seg.Dir;
            double3 helper = Math.Abs(d.x) < 0.9 ? new double3(1, 0, 0) : new double3(0, 1, 0);
            double3 e1 = math.normalize(math.cross(d, helper)), e2 = math.cross(d, e1);
            for (int k = 0; k < rings; k++)
            {
                double a = fromMm + (toMm - fromMm) * k / (rings - 1);
                for (int j = 0; j < Sides; j++)
                {
                    double th = 2 * Math.PI * j / Sides;
                    double3 p = seg.PosMm + d * a + r * (Math.Cos(th) * e1 + Math.Sin(th) * e2);
                    double dist = math.length(p - cameraMm);
                    double sat = math.saturate((dist - EndoluminalCamera.AoNearRadii * parentRadiusMm) / ((EndoluminalCamera.AoFarRadii - EndoluminalCamera.AoNearRadii) * parentRadiusMm));
                    byte c = (byte)math.round(255.0 * (1.0 - (1.0 - EndoluminalCamera.AoMin) * sat));
                    verts[k * Sides + j] = new AtlasVertex
                    {
                        Position = EndoluminalCamera.ToScene(p, originMm, s), Normal = new float3(0f, 1f, 0f), Color = AtlasVertex.PackColor(c, 0),
                        Uv0 = uv, Uv1 = new float2(0.5f, 0.5f),
                    };
                }
            }
            int n = 0;
            for (int k = 0; k < rings - 1; k++)
                for (int j = 0; j < Sides; j++)
                {
                    int j1 = (j + 1) % Sides;
                    int a = k * Sides + j, b = k * Sides + j1, c = (k + 1) * Sides + j1, e = (k + 1) * Sides + j;
                    idx[n++] = (ushort)a; idx[n++] = (ushort)b; idx[n++] = (ushort)c;
                    idx[n++] = (ushort)a; idx[n++] = (ushort)c; idx[n++] = (ushort)e;
                    idx[n++] = (ushort)a; idx[n++] = (ushort)c; idx[n++] = (ushort)b;
                    idx[n++] = (ushort)a; idx[n++] = (ushort)e; idx[n++] = (ushort)c;
                }

            VertexCount = vc; IndexCount = ic;
        }

        void Upload(Mesh target)
        {
            int vc = VertexCount, ic = IndexCount;
            float3 mn = verts[0].Position, mx = mn;
            for (int i = 1; i < vc; i++) { mn = math.min(mn, verts[i].Position); mx = math.max(mx, verts[i].Position); }
            var bounds = new Bounds((mn + mx) * 0.5f, mx - mn);
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            var arr = Mesh.AllocateWritableMeshData(1);
            var md = arr[0];
            md.SetVertexBufferParams(vc, AtlasMesh.Layout);
            md.SetIndexBufferParams(ic, IndexFormat.UInt16);
            NativeArray<AtlasVertex>.Copy(verts, md.GetVertexData<AtlasVertex>(), vc);
            NativeArray<ushort>.Copy(idx, md.GetIndexData<ushort>(), ic);
            md.subMeshCount = 1;
            md.SetSubMesh(0, new SubMeshDescriptor(0, ic) { bounds = bounds, vertexCount = vc }, flags);
            Mesh.ApplyAndDisposeWritableMeshData(arr, target, flags);
            target.bounds = bounds;
        }
    }
}

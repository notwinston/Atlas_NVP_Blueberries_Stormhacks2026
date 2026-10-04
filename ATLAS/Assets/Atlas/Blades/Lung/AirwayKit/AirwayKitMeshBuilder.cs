using System;
using Atlas.Kernel;
using Atlas.Rendering;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Blades.Lung.AirwayKit
{
    // Index ranges of a kit tile mesh (brief G5 2.4). One submesh; ranges are in the index buffer.
    public struct KitMeshLayout
    {
        public RangeInt FloorVertices, FloorIndices, KerbIndices, MembraneIndices, StripIndices, GlyphIndices;
        public int VertexCount, IndexCount;
        public int TriangleCount => IndexCount / 3;
    }

    // Index ranges of the valve shell overlay (brief G5 2.5, D-6).
    public struct ValveShellLayout
    {
        public RangeInt ShellIndices, IrisIndices, RoofIndices, FloorIndices, GlyphIndices;
        public int VertexCount, IndexCount;
    }

    // Brief G5 2.4-2.5. Builds the kit in the kit frame (entry edge = edge 0); placement is the caller's anchor product.
    // Vertices are the one layout of the look shader (AtlasVertex). Every Build has an engine-free form that fills Vertices and
    // Indices and uploads nothing, and a form that then uploads them to a Mesh, as TileMeshGenerator does.
    public sealed class AirwayKitMeshBuilder
    {
        public const int MaxVertices = 8192, MaxIndices = 24576;

        readonly AirwayPalette pal;
        readonly TileMeshGenerator gen;
        readonly AtlasVertex[] verts = new AtlasVertex[MaxVertices];
        readonly ushort[] idx = new ushort[MaxIndices];
        readonly WallRun[] runs = new WallRun[8];
        readonly int[] kerbBase = new int[8], memBase = new int[8], cols = new int[8];
        int vc, ic;

        public AirwayKitMeshBuilder(TilePalette tilePalette, AirwayPalette palette)
        {
            pal = palette;
            gen = new TileMeshGenerator(tilePalette);
        }

        /// <summary>The vertices and indices of the last Build, valid up to the layout's VertexCount and IndexCount.</summary>
        public AtlasVertex[] Vertices => verts;
        public ushort[] Indices => idx;

        // ---- low level ----

        int AddV(double2 c, float y, float2 uv, float ao)
        {
            if (vc >= MaxVertices) throw new InvalidOperationException("AirwayKitMeshBuilder: vertex budget exceeded");
            byte b = (byte)math.round(255f * ao);
            // Kit parts carry no lines: a constant line coordinate and line weight 0. The shader does not light by the normal.
            verts[vc] = new AtlasVertex
            {
                Position = new float3((float)c.x, y, (float)c.y), Normal = new float3(0f, 1f, 0f), Color = AtlasVertex.PackColor(b, 0),
                Uv0 = uv, Uv1 = new float2(0.5f, 0.5f),
            };
            return vc++;
        }

        void Tri(int a, int b, int c)
        {
            if (ic + 3 > MaxIndices) throw new InvalidOperationException("AirwayKitMeshBuilder: index budget exceeded");
            idx[ic++] = (ushort)a; idx[ic++] = (ushort)b; idx[ic++] = (ushort)c;
        }

        void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

        // Brief 2.4 step 2: AO 0.6 (TileChart.AoMin) at y = 0, 0.8 at 0.2, 1 above.
        static float WallAo(int level) => level == 0 ? (float)TileChart.AoMin : level == 1 ? 0.8f : 1f;

        // Lattice of (columns + 1) x (j1 - j0 + 1) vertices along a run; level j is the height 0.2 j. Returns the base index.
        int AddLattice(in WallRun run, double radius, int columns, int j0, int j1, float2 uv, int kappa, double scale)
        {
            int baseV = vc;
            for (int i = 0; i <= columns; i++)
            {
                double s = (double)i / columns;
                double2 p;
                if (run.Type == WallRunType.Line) p = KitGeometry.RunPoint(run, s, kappa, scale);
                else { double phi = run.Phi0 + (run.Phi1 - run.Phi0) * s; p = radius * new double2(Math.Cos(phi), Math.Sin(phi)); }
                for (int j = j0; j <= j1; j++) AddV(p, (float)(KitGeometry.RowStep * j), uv, WallAo(j));
            }
            return baseV;
        }

        // Cells (i, j) for j in [jLo, jHi], double-sided (every quad twice, opposite winding); dither keeps (i + j) even.
        void EmitCells(int baseV, int columns, int j0, int j1, int jLo, int jHi, bool dither)
        {
            int stride = j1 - j0 + 1;
            for (int i = 0; i < columns; i++)
                for (int j = jLo; j <= jHi; j++)
                {
                    if (dither && ((i + j) & 1) != 0) continue;
                    int a = baseV + i * stride + (j - j0), b = a + 1, d = a + stride, c = d + 1;
                    Quad(a, b, c, d);
                    Quad(a, d, c, b);
                }
        }

        // Centre + rings x segments disc at height y; radius r (chart metres).
        void AddDisc(double radius, float y, int rings, int segs, float2 uv, bool doubleSided)
        {
            int centre = AddV(double2.zero, y, uv, 1f);
            int first = vc;
            for (int k = 1; k <= rings; k++)
                for (int s = 0; s < segs; s++)
                {
                    double phi = 2 * Math.PI * s / segs, r = radius * k / rings;
                    AddV(r * new double2(Math.Cos(phi), Math.Sin(phi)), y, uv, 1f);
                }
            for (int s = 0; s < segs; s++)
            {
                int s1 = (s + 1) % segs;
                Tri(centre, first + s1, first + s);
                if (doubleSided) Tri(centre, first + s, first + s1);
            }
            for (int k = 1; k < rings; k++)
                for (int s = 0; s < segs; s++)
                {
                    int s1 = (s + 1) % segs;
                    int i0 = first + (k - 1) * segs + s, i1 = first + (k - 1) * segs + s1, o0 = first + k * segs + s, o1 = first + k * segs + s1;
                    Quad(i0, i1, o1, o0);
                    if (doubleSided) Quad(i0, o0, o1, i1);
                }
        }

        void Upload(Mesh target)
        {
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

        // ---- static tile (brief 2.4) ----

        public KitMeshLayout BuildTile(KitKind kind, in TileMeshSpec floorSpec, Mesh target)
        {
            var lay = BuildTile(kind, floorSpec);
            Upload(target);
            return lay;
        }

        /// <summary>Engine-free: fills Vertices and Indices and uploads nothing.</summary>
        public KitMeshLayout BuildTile(KitKind kind, in TileMeshSpec floorSpec)
        {
            int kappa = floorSpec.Kappa; double scale = floorSpec.Scale;
            if (kappa == 1) throw new ArgumentOutOfRangeException(nameof(floorSpec), "airway kit has no spherical variant (D-10)");
            if (kappa == -1 && floorSpec.Q != 5) throw new ArgumentException("airway kit is {4,5} only", nameof(floorSpec));
            if (floorSpec.KerbMask != 0 || floorSpec.PostMask != 0) throw new ArgumentException("airway tiles have no tile-edge kerbs or posts (D-2)", nameof(floorSpec));
            double H = KitGeometry.InRadiusMetres(kappa, scale);   // validates kappa / scale (D-10)

            // 1. floor: unchanged copy of the P1-B05 generator (LODs, 16 boundary segments, triangle caps)
            var lay = new KitMeshLayout();
            var floor = gen.Build(floorSpec);
            int fv = floor.VertexCount, fi = floor.IndexCount;   // no kerbs and no posts (checked above): the floor is all of it
            Array.Copy(gen.Vertices, verts, fv);
            Array.Copy(gen.Indices, idx, fi);
            vc = fv; ic = fi;
            lay.FloorVertices = new RangeInt(0, fv); lay.FloorIndices = new RangeInt(0, fi);

            // 2. walls
            int nRuns = KitGeometry.WallPath(kind, kappa, scale, runs);
            for (int r = 0; r < nRuns; r++)
            {
                cols[r] = KitGeometry.ColumnCount(runs[r], kappa, scale);
                kerbBase[r] = AddLattice(runs[r], KitGeometry.ChamberRadius, cols[r], 0, KitGeometry.KerbRows, pal.Kerb, kappa, scale);
                memBase[r] = AddLattice(runs[r], KitGeometry.ChamberRadius, cols[r], KitGeometry.KerbRows, KitGeometry.Levels - 1, pal.Membrane, kappa, scale);
            }
            int i0 = ic;
            for (int r = 0; r < nRuns; r++) EmitCells(kerbBase[r], cols[r], 0, KitGeometry.KerbRows, 0, KitGeometry.KerbRows - 1, false);
            lay.KerbIndices = new RangeInt(i0, ic - i0);
            i0 = ic;
            for (int r = 0; r < nRuns; r++) EmitCells(memBase[r], cols[r], KitGeometry.KerbRows, KitGeometry.Levels - 1, KitGeometry.KerbRows, KitGeometry.LastRow, true);
            lay.MembraneIndices = new RangeInt(i0, ic - i0);

            // 3. centre strips: |u| <= 0.1, t from GlyphRadius to H, y = DecalHeight, AO 1; up-facing, single sided
            i0 = ic;
            int mask = KitGeometry.KitMask(kind);
            int nT = Math.Max(1, (int)Math.Ceiling((H - KitGeometry.GlyphRadius) / KitGeometry.ColumnStep - 1e-9));
            for (int e = 0; e < 4; e++)
            {
                if ((mask & (1 << e)) == 0) continue;
                int b0 = vc;
                for (int k = 0; k <= nT; k++)
                {
                    double t = KitGeometry.GlyphRadius + (H - KitGeometry.GlyphRadius) * k / nT;
                    for (int m = 0; m < 3; m++)
                        AddV(KitGeometry.FermiToChart(e, t, (m - 1) * KitGeometry.StripHalfWidth, kappa, scale), (float)KitGeometry.DecalHeight, pal.Strip, 1f);
                }
                for (int k = 0; k < nT; k++)
                    for (int m = 0; m < 2; m++)
                    {
                        int a = b0 + k * 3 + m, b = a + 1, d = a + 3, c = d + 1;
                        Quad(a, b, c, d);
                    }
            }
            lay.StripIndices = new RangeInt(i0, ic - i0);

            // 4. glyph: disc r 0.15, centre + 16 rim vertices
            i0 = ic;
            AddDisc(KitGeometry.GlyphRadius, (float)KitGeometry.DecalHeight, 1, KitGeometry.GlyphSegments, pal.Glyph, false);
            lay.GlyphIndices = new RangeInt(i0, ic - i0);

            lay.VertexCount = vc; lay.IndexCount = ic;
            return lay;
        }

        // ---- valve shell overlay (brief 2.5, D-6) ----

        public ValveShellLayout BuildValveShell(KitKind kind, int openIrisKitMask, bool opaque, int kappa, double scale, Mesh target)
        {
            var lay = BuildValveShell(kind, openIrisKitMask, opaque, kappa, scale);
            if (lay.VertexCount == 0) target.Clear(); else Upload(target);
            return lay;
        }

        /// <summary>Engine-free: fills Vertices and Indices and uploads nothing.</summary>
        public ValveShellLayout BuildValveShell(KitKind kind, int openIrisKitMask, bool opaque, int kappa, double scale)
        {
            int mask = KitGeometry.KitMask(kind);
            if ((openIrisKitMask & ~mask) != 0) throw new ArgumentException("openIrisKitMask must be a subset of the kind's open edges", nameof(openIrisKitMask));
            KitGeometry.InRadiusMetres(kappa, scale);
            vc = 0; ic = 0;
            var lay = new ValveShellLayout();
            const int J1 = KitGeometry.Levels - 1;
            int i0 = ic;

            // Shell: the Arc-run sectors, solid
            if (opaque)
            {
                int nRuns = KitGeometry.WallPath(kind, kappa, scale, runs);
                for (int r = 0; r < nRuns; r++)
                {
                    if (runs[r].Type != WallRunType.Arc) continue;
                    int c = KitGeometry.IrisColumns(runs[r].Phi0, runs[r].Phi1, kappa, scale);
                    int b0 = AddLattice(runs[r], KitGeometry.ValveShellRadius, c, 0, J1, pal.Shell, kappa, scale);
                    EmitCells(b0, c, 0, J1, 0, KitGeometry.LastRow, false);
                }
            }
            lay.ShellIndices = new RangeInt(i0, ic - i0);

            // Iris: sectors of the open edges that are not in openIrisKitMask; solid when opaque, checkerboard when dithered
            i0 = ic;
            for (int e = 0; e < 4; e++)
            {
                if ((mask & (1 << e)) == 0 || (openIrisKitMask & (1 << e)) != 0) continue;
                KitGeometry.IrisSector(kind, e, kappa, scale, out double p0, out double p1);
                var sector = new WallRun(WallRunType.Arc, e, 0, 0, 0, p0, p1);
                int c = KitGeometry.IrisColumns(p0, p1, kappa, scale);
                int b0 = AddLattice(sector, KitGeometry.ValveShellRadius, c, 0, J1, pal.Iris, kappa, scale);
                EmitCells(b0, c, 0, J1, 0, KitGeometry.LastRow, !opaque);
            }
            lay.IrisIndices = new RangeInt(i0, ic - i0);

            i0 = ic;
            if (opaque) AddDisc(KitGeometry.ValveCapRadius, (float)KitGeometry.MembraneTop, KitGeometry.CapRings, KitGeometry.CapSegments, pal.Shell, true);
            lay.RoofIndices = new RangeInt(i0, ic - i0);
            i0 = ic;
            if (opaque) AddDisc(KitGeometry.ValveCapRadius, (float)KitGeometry.ValveFloorHeight, KitGeometry.CapRings, KitGeometry.CapSegments, pal.ValveFloor, true);
            lay.FloorIndices = new RangeInt(i0, ic - i0);
            i0 = ic;
            if (opaque) AddDisc(KitGeometry.GlyphRadius, (float)KitGeometry.ValveGlyphHeight, 1, KitGeometry.GlyphSegments, pal.Glyph, true);
            lay.GlyphIndices = new RangeInt(i0, ic - i0);

            lay.VertexCount = vc; lay.IndexCount = ic;
            return lay;
        }
    }
}

// P1-B07: the props of the generated world (Master Plan §3.4 table): the Tower's beacon, the Garden's low props, the Doorway's
// frame and the two halves of a spanning beam. Engine-free. Kerbs and corner wedges are the tile shell's (TileMeshGenerator).
//
// Every prop is built flat, in metres, about its own base, then placed by the exp map about that base (as the room's objects
// are, MP §4.6) and written in the tile's log chart, so it rides the tile's anchor and material. The exp map stretches a length
// at distance d from the base by at most sinh(d/L)/(d/L): the strict subdivision runs before placement with the limit lowered
// by that factor, so no edge is longer than 0.25 m in the curved metric, and the warp never lengthens one (MP §8.4, §8.5).
using System;
using Atlas.Kernel;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Rendering
{
    public sealed class WorldPropMeshBuilder
    {
        // Tower: a plinth, a shaft and an emissive head, as tall as a corner post.
        public const float TowerPlinth = 0.5f, TowerPlinthHeight = 0.25f, TowerShaftRadius = 0.11f, TowerHeadRadius = 0.18f, TowerHeadHeight = 0.45f;
        public const float TowerHeight = WorldPropBudget.MaxHeight;
        // Garden: up to five props on a ring round the tile centre, kept clear of the edges.
        public const double GardenRing = 0.55, GardenClearance = 0.40, GardenSpacing = 0.75;
        // Doorway: a frame just inside the feature edge, clear of the two corner posts.
        public const float DoorJamb = 0.15f, DoorHeight = 2.25f, DoorMaxHalfWidth = 0.65f, DoorMinHalfWidth = 0.25f;
        public const double DoorInset = 0.10, DoorPostGap = 0.10;
        // Span: each tile carries the half of the beam on its side of the shared edge; the open ends meet on the edge.
        public const float SpanWidth = 0.14f, SpanThickness = 0.12f, SpanBottom = 0.55f, SpanLeg = 0.12f, SpanMaxHalfLength = 1.5f;
        public const double SpanReach = 0.75;   // of the way from the edge to the tile centre

        readonly AtlasMeshBuilder local = new AtlasMeshBuilder(1024, 4096);
        double L, inM, halfEdgeM;

        /// <summary>A.3, p = 4: distance from the tile centre to an edge midpoint, units of L.</summary>
        public static double InRadius(int q) => Math.Acosh(Math.Cos(Math.PI / q) / Math.Sin(Math.PI / 4));
        /// <summary>A.3, p = 4: half the length of a tile edge, units of L.</summary>
        public static double HalfEdge(int q) => Math.Acosh(Math.Cos(Math.PI / 4) / Math.Sin(Math.PI / q));

        /// <summary>Half length of one half of the spanning beam, metres.</summary>
        public static float SpanHalfLength(int q, double lengthScale) => (float)Math.Min(SpanMaxHalfLength, SpanReach * InRadius(q) * lengthScale);

        /// <summary>
        /// Appends the props of one tile, in the tile's chart (metres, y up), and returns the triangles added. Home, Plaza and
        /// Corridor add none. The same key and length scale give the same vertices on every run.
        /// </summary>
        public int Build(int q, double lengthScale, in WorldTileKey key, AtlasMeshBuilder into)
        {
            if (q != 5 && q != 6) throw new ArgumentOutOfRangeException(nameof(q));
            if (double.IsNaN(lengthScale) || double.IsInfinity(lengthScale) || lengthScale < 0.4) throw new ArgumentOutOfRangeException(nameof(lengthScale), "L must be finite and at least 0.4 m");
            if (into == null) throw new ArgumentNullException(nameof(into));
            L = lengthScale; inM = InRadius(q) * L; halfEdgeM = HalfEdge(q) * L;
            int before = into.TriangleCount;
            if (key.HasProps)
                switch (key.Archetype)
                {
                    case Archetype.Tower: Tower(into); break;
                    case Archetype.Garden: Garden(key.Variant, into); break;
                    case Archetype.Doorway: Doorway(key.FeatureEdge, into); break;
                    case Archetype.SpanA: Span(key.FeatureEdge, true, q, into); break;
                    case Archetype.SpanB: Span(key.FeatureEdge, false, q, into); break;
                }
            return into.TriangleCount - before;
        }

        /// <summary>
        /// Length of the longest triangle edge from index firstIndex on, in the curved metric: the hyperbolic distance on the floor
        /// (points are chart positions, A.2) joined with the height difference. The warp places no edge longer than this.
        /// </summary>
        public static double LongestCurvedEdge(AtlasMeshBuilder data, int firstIndex, double lengthScale)
        {
            double longest = 0;
            for (int t = firstIndex; t + 2 < data.IndexCount; t += 3)
                for (int k = 0; k < 3; k++)
                {
                    float3 a = data.Vertices[data.Indices[t + k]].Position, b = data.Vertices[data.Indices[t + (k + 1) % 3]].Position;
                    double d = lengthScale * Geometry2.Distance(Geometry2.Exp(new double2(a.x, a.z) / lengthScale, -1), Geometry2.Exp(new double2(b.x, b.z) / lengthScale, -1), -1);
                    double dy = (double)a.y - b.y;
                    longest = Math.Max(longest, Math.Sqrt(d * d + dy * dy));
                }
            return longest;
        }

        // Frame whose origin is on the line from the tile centre to edge e's midpoint, insetM short of the edge; its +x points out
        // of the tile and its z runs along the edge (edge 0 faces +x, kernel.md §5.3).
        Iso2 EdgeFrame(int edge, double insetM) =>
            Iso2.Rot(edge * (Math.PI / 2), -1) * Iso2.Boost(new double2((inM - insetM) / L, 0), -1);

        // Places what is in the local builder: strict subdivision in the prop's own frame, then exp about the base, then the chart.
        void Emit(in Iso2 frame, AtlasMeshBuilder into)
        {
            double reach = 0;
            for (int i = 0; i < local.VertexCount; i++) reach = Math.Max(reach, math.length(local.Vertices[i].Position.xz));
            double rho = reach / L;
            double shrink = rho > 1e-6 ? rho / Math.Sinh(rho) : 1.0;
            local.Subdivide((float)(WorldPropBudget.MaxEdge * shrink * 0.999));

            into.Reserve(local.VertexCount, local.IndexCount);
            int offset = into.VertexCount;
            for (int i = 0; i < local.VertexCount; i++)
            {
                AtlasVertex v = local.Vertices[i];
                double3 p = math.mul(frame.M, Geometry2.Exp(new double2(v.Position.x, v.Position.z) / L, -1));
                double2 c = L * Geometry2.Log(p, -1);
                v.Position = new float3((float)c.x, v.Position.y, (float)c.y);
                into.AddVertex(v);
            }
            for (int i = 0; i < local.IndexCount; i += 3)
                into.AddTriangle(offset + local.Indices[i], offset + local.Indices[i + 1], offset + local.Indices[i + 2]);
            local.Clear();
        }

        // A horizontal disc as a fan; AddPost has a top only.
        void Disc(float3 centre, float radius, int sides, bool faceUp, float2 uv)
        {
            var normal = new float3(0f, faceUp ? 1f : -1f, 0f);
            int c = local.AddVertex(centre, normal, uv, float2.zero, 1f, 0);
            int rim = local.VertexCount;
            float step = 2f * math.PI / sides;
            for (int i = 0; i < sides; i++)
            {
                math.sincos(step * i, out float sin, out float cos);
                local.AddVertex(centre + new float3(radius * sin, 0f, radius * cos), normal, uv, float2.zero, 1f, 0);
            }
            for (int i = 0; i < sides; i++)
            {
                int a = rim + i, b = rim + (i + 1) % sides;
                if (faceUp) local.AddTriangle(c, a, b); else local.AddTriangle(c, b, a);
            }
        }

        // MP §3.4 "a tall emissive beacon at the tile centre".
        void Tower(AtlasMeshBuilder into)
        {
            const float g = WorldPropBudget.GridEdge;
            float2 body = AtlasPalette.Uv(AtlasPalette.Landmark), glow = AtlasPalette.Uv(AtlasPalette.Beacon);
            float headBase = TowerHeight - TowerHeadHeight;
            local.AddBox(float3.zero, new float3(TowerPlinth, TowerPlinthHeight, TowerPlinth), 0f, body, false, 0, g, (float)TileChart.AoMin);
            local.AddPost(new float3(0f, TowerPlinthHeight, 0f), TowerShaftRadius, headBase - TowerPlinthHeight, body, 6, false, 0, g);
            local.AddPost(new float3(0f, headBase, 0f), TowerHeadRadius, TowerHeadHeight, glow, 8, true, 0, g);
            Disc(new float3(0f, headBase, 0f), TowerHeadRadius, 8, false, glow);
            Emit(Iso2.IdentityFor(-1), into);
        }

        static ulong SplitMix(ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
                x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
                return x ^ (x >> 31);
            }
        }

        // MP §3.4 "low props only (under 0.5 m)": planters, drums and slabs, laid out from the variant alone.
        void Garden(int variant, AtlasMeshBuilder into)
        {
            const float g = WorldPropBudget.GridEdge;
            float ao = (float)TileChart.AoMin;
            ulong s = SplitMix(0x6A09E667F3BCC909UL + (ulong)variant);
            double ring = Math.Min(GardenRing * inM, inM - GardenClearance);
            int count = ring <= 0.05 ? 1 : Math.Max(1, Math.Min(3 + variant % 3, (int)(2 * Math.PI * ring / GardenSpacing)));
            double a0 = (s & 1023) / 1024.0 * (2 * Math.PI);
            for (int i = 0; i < count; i++)
            {
                s = SplitMix(s);
                double angle = a0 + 2 * Math.PI * i / count;
                double radius = count == 1 ? 0.0 : ring * (0.75 + 0.25 * ((s >> 10) & 255) / 255.0);
                float yaw = (float)(((s >> 20) & 255) / 255.0 * (Math.PI / 2));
                float2 uv = AtlasPalette.Uv(AtlasPalette.Object(3 + (int)((s >> 34) % 3), 2));   // hues 90, 120, 150
                switch ((int)((s >> 30) % 3))
                {
                    case 0: local.AddBox(float3.zero, new float3(0.45f, 0.30f, 0.30f), yaw, uv, false, 0, g, ao); break;
                    case 1: local.AddPost(float3.zero, 0.14f, 0.42f, uv, 6, true, 0, g, ao); break;
                    default: local.AddBox(float3.zero, new float3(0.50f, 0.12f, 0.50f), yaw, uv, false, 0, g, ao); break;
                }
                Emit(Iso2.Rot(angle, -1) * Iso2.Boost(new double2(radius / L, 0), -1), into);
            }
        }

        // MP §3.4 "one edge with a framed opening": two jambs and a lintel between them.
        void Doorway(int edge, AtlasMeshBuilder into)
        {
            const float g = WorldPropBudget.GridEdge;
            float2 uv = AtlasPalette.Uv(AtlasPalette.DoorFrame);
            float half = (float)Math.Max(DoorMinHalfWidth, Math.Min(DoorMaxHalfWidth, halfEdgeM - TileChart.PostRadius - DoorPostGap));
            float z = half - 0.5f * DoorJamb;
            var jamb = new float3(DoorJamb, DoorHeight, DoorJamb);
            local.AddBox(new float3(0f, 0f, -z), jamb, 0f, uv, false, 0, g, (float)TileChart.AoMin);
            local.AddBox(new float3(0f, 0f, z), jamb, 0f, uv, false, 0, g, (float)TileChart.AoMin);
            local.AddBox(new float3(0f, DoorHeight - DoorJamb, 0f), new float3(DoorJamb, DoorJamb, 2f * (half - DoorJamb)), 0f, uv, true, 0, g);
            Emit(EdgeFrame(edge, DoorInset), into);
        }

        // MP §3.4 "two neighbouring tiles that each carry half of one long object": the beam runs along the line joining the two
        // tile centres. Its cross-section is symmetric, so the two halves end on the same points of the shared edge; neither
        // closes its end there. Span A carries the emissive marker.
        void Span(int edge, bool first, int q, AtlasMeshBuilder into)
        {
            const float g = WorldPropBudget.GridEdge;
            float2 uv = AtlasPalette.Uv(AtlasPalette.Landmark);
            float2 none = float2.zero;
            float len = SpanHalfLength(q, L), hw = 0.5f * SpanWidth, y0 = SpanBottom, y1 = SpanBottom + SpanThickness;
            var along = new float3(len, 0f, 0f); var across = new float3(0f, 0f, SpanWidth); var up = new float3(0f, SpanThickness, 0f);
            local.AddQuadGrid(new float3(-len, y1, -hw), along, across, uv, none, none, none, 1f, 0, g);    // top
            local.AddQuadGrid(new float3(-len, y0, -hw), across, along, uv, none, none, none, 1f, 0, g);    // bottom
            local.AddQuadGrid(new float3(0f, y0, hw), -along, up, uv, none, none, none, 1f, 0, g);          // +z side
            local.AddQuadGrid(new float3(-len, y0, -hw), along, up, uv, none, none, none, 1f, 0, g);        // -z side
            local.AddQuadGrid(new float3(-len, y0, hw), -across, up, uv, none, none, none, 1f, 0, g);       // inner end
            var leg = new float3(SpanLeg, SpanBottom, SpanLeg);
            local.AddBox(new float3(-len + 0.5f * SpanLeg, 0f, 0f), leg, 0f, uv, false, 0, g, (float)TileChart.AoMin);
            local.AddBox(new float3(-0.4f * len, 0f, 0f), leg, 0f, uv, false, 0, g, (float)TileChart.AoMin);
            if (first)
                local.AddBox(new float3(-len + 0.5f * SpanWidth, y1, 0f), new float3(SpanWidth, SpanWidth, SpanWidth), 0f, AtlasPalette.Uv(AtlasPalette.Flag), false, 0, g);
            Emit(EdgeFrame(edge, 0.0), into);
        }
    }
}

using System;
using Atlas.Blades.Network.Core;
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Blades.Network
{
    /// <summary>
    /// Builds the three merged meshes of the V2 floor map (section 4 V2; contract NET-06) into preallocated
    /// <see cref="AtlasMeshBuilder"/>s: nodes (posts whose height grows with ln(degree)), edges (geodesic strips) and highlight (the focus node and the lit route). Engine-free. The meshes are in disc metres at the nominal
    /// radius 1.2 m, centred on the origin, on y = 0: the view scales x and z to the room (section 6.2). Nothing allocates
    /// after construction, and the node and edge meshes are rebuilt only while <see cref="NetworkFocus"/> animates.
    /// </summary>
    public sealed class NetworkFloorMesher
    {
        /// <summary>Section 6.2: the disc is 2.4 m across.</summary>
        public const float DiscRadiusMetres = 1.2f;
        /// <summary>Section 4 V2 budget: 16 segments per edge, 17 points.</summary>
        public const int EdgeSegments = 16, EdgePoints = EdgeSegments + 1;
        /// <summary>The longest route the highlight holds: the greedy router's limit, so a lit route is never cut.</summary>
        public const int HighlightMaxEdges = GreedyRouter.Limit - 1;
        /// <summary>Contract NET-06: the whole floor map stays within this many triangles, for any route up to the router's limit.</summary>
        public const int TriangleBudget = 25000;

        // Not from the doc (contract NET-06 lists the first two; the rest are its own sizes). Metres.
        /// <summary>Height of a degree-1 post; also its square footprint.</summary>
        public const float PostBaseHeight = 0.03f;
        /// <summary>Added post height per ln(degree).</summary>
        public const float PostHeightPerLnDegree = 0.03f;
        public const float EdgeWidth = 0.006f, FloorLift = 0.004f;
        public const float HighlightWidth = 0.014f, HighlightLift = 0.008f, HighlightPostExtra = 0.04f, FocusPostExtra = 0.10f;

        /// <summary>Section 6.2: radius of the disc in a room, 1.2 m or half the shorter side of the free rectangle.</summary>
        public static float DiscRadius(FreeRect free)
        {
            return math.min(DiscRadiusMetres, 0.5f * math.min(free.Wf, free.Df));
        }

        readonly NetworkModel m_Model;
        readonly double2[] m_Points;          // node positions, re-centred
        readonly float[] m_Height;
        readonly double2[] m_Geo;             // every edge's 17 disc points in the file's own picture
        readonly double2[] m_Scratch;
        readonly int[] m_Path = new int[GreedyRouter.Limit];
        readonly double[] m_OneMinus;
        readonly uint m_UpColour;
        readonly float2 m_UvNode, m_UvEdge, m_UvHighlight;
        readonly int m_StripBudget;           // triangles left for the highlight's strips once nodes, edges and posts are paid for
        int m_PathCount;
        bool m_HighlightDirty;

        public NetworkFloorMesher(NetworkModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            m_Model = model;
            int n = model.NodeCount, edges = model.EdgeCount;
            m_Points = new double2[n];
            m_OneMinus = new double[n];
            m_Height = new float[n];
            for (int i = 0; i < n; i++)
                m_Height[i] = PostBaseHeight + PostHeightPerLnDegree * (float)Math.Log(Math.Max(1, model.Degree(i)));
            m_Geo = new double2[edges * EdgePoints];
            m_Scratch = new double2[EdgePoints];
            for (int e = 0; e < edges; e++)
                PoincareMap.Geodesic(model, model.EdgeA(e), model.EdgeB(e), double2.zero, EdgePoints, m_Geo, e * EdgePoints);
            m_UpColour = AtlasVertex.PackColor(AtlasShading.ToByte(AtlasShading.FaceShade(new float3(0f, 1f, 0f)), 1f), 0);
            m_UvNode = AtlasPalette.Uv(AtlasPalette.Post);
            m_UvEdge = AtlasPalette.Uv(AtlasPalette.GridLine);
            m_UvHighlight = AtlasPalette.Uv(AtlasPalette.Beacon);

            // Exact capacities: a build never grows an array.
            Nodes = new AtlasMeshBuilder(n * 20, n * 30);
            Edges = new AtlasMeshBuilder(edges * 2 * EdgePoints, edges * EdgeSegments * 6);
            int hb = HighlightMaxEdges + 2;
            // Posts are worst-cased at one per path node plus the focus; the strips get what is left of the budget.
            m_StripBudget = Math.Max(TriangleBudget - n * 10 - edges * EdgeSegments * 2 - hb * 10, 2 * HighlightMaxEdges);
            Highlight = new AtlasMeshBuilder(hb * 20 + m_StripBudget + 2 * HighlightMaxEdges, hb * 30 + m_StripBudget * 3);

            BuildGeometry(double2.zero);
            BuildHighlight(double2.zero, -1);
        }

        public NetworkModel Model { get { return m_Model; } }
        public AtlasMeshBuilder Nodes { get; }
        public AtlasMeshBuilder Edges { get; }
        public AtlasMeshBuilder Highlight { get; }

        /// <summary>How many times the node and edge meshes were built (the first build included), and the highlight mesh.</summary>
        public int GeometryBuilds { get; private set; }
        public int HighlightBuilds { get; private set; }
        /// <summary>What the last <see cref="Step"/> rebuilt.</summary>
        public bool GeometryRebuilt { get; private set; }
        public bool HighlightRebuilt { get; private set; }

        public int TriangleCount { get { return Nodes.TriangleCount + Edges.TriangleCount + Highlight.TriangleCount; } }

        /// <summary>Lights a route (node ids, at most <see cref="GreedyRouter.Limit"/> nodes); the highlight mesh only is rebuilt, at the next Step.</summary>
        public void SetHighlight(int[] path, int count)
        {
            m_PathCount = path == null ? 0 : Math.Min(Math.Min(count, path.Length), m_Path.Length);
            for (int i = 0; i < m_PathCount; i++) m_Path[i] = path[i];
            m_HighlightDirty = true;
        }

        /// <summary>
        /// One frame: advances the focus by dt and rebuilds the node and edge meshes (and the highlight) only if its centre
        /// moved; a changed highlight rebuilds the highlight mesh alone. True when anything was rebuilt.
        /// </summary>
        public bool Step(NetworkFocus focus, float dt)
        {
            GeometryRebuilt = false;
            HighlightRebuilt = false;
            if (focus.Advance(dt))
            {
                BuildGeometry(focus.Centre);
                BuildHighlight(focus.Centre, focus.FocusNode);
            }
            else if (m_HighlightDirty)
            {
                BuildHighlight(focus.Centre, focus.FocusNode);
            }
            return GeometryRebuilt || HighlightRebuilt;
        }

        /// <summary>Where a node's post stands now, in mesh space (before the view's scale). Valid after construction and after every rebuild.</summary>
        public float3 NodePosition(int node) { return World(m_Points[node], 0f); }

        // Section 6.2: disc point (x, y) lies at (x * radius, y, y * radius); the mesh is in disc metres at radius 1.2.
        static float3 World(double2 p, float y)
        {
            return new float3((float)p.x * DiscRadiusMetres, y, (float)p.y * DiscRadiusMetres);
        }

        // A post's centre, pulled in only as far as needed to keep its square footprint inside the disc.
        static float3 PostCentre(double2 p, float footprint)
        {
            float3 c = World(p, 0f);
            float limit = DiscRadiusMetres - 0.7072f * footprint;
            float len = math.sqrt(c.x * c.x + c.z * c.z);
            if (len > limit) { float k = limit / len; c.x *= k; c.z *= k; }
            return c;
        }

        void BuildGeometry(double2 centre)
        {
            PoincareMap.ProjectAll(m_Model, centre, m_Points, m_OneMinus);

            Nodes.Clear();
            float footprint = PostBaseHeight;
            for (int i = 0, n = m_Model.NodeCount; i < n; i++)
                Nodes.AddBox(PostCentre(m_Points[i], footprint), new float3(footprint, m_Height[i], footprint), 0f, m_UvNode);

            Edges.Clear();
            for (int e = 0, ne = m_Model.EdgeCount; e < ne; e++)
            {
                int o = e * EdgePoints;
                for (int k = 0; k < EdgePoints; k++) m_Scratch[k] = PoincareMap.Mobius(m_Geo[o + k], centre);
                AddStrip(Edges, m_Scratch, EdgePoints, EdgeWidth, FloorLift, m_UvEdge);
            }
            WidenBounds(Edges, FloorLift);
            GeometryBuilds++;
            GeometryRebuilt = true;
        }

        void BuildHighlight(double2 centre, int focusNode)
        {
            Highlight.Clear();
            int edges = Math.Max(m_PathCount - 1, 0);
            // Every hop is drawn; a long route gets fewer segments per hop so the total stays inside the budget.
            int segs = edges == 0 ? EdgeSegments : Math.Max(1, Math.Min(EdgeSegments, m_StripBudget / (2 * edges)));
            int points = segs + 1;
            for (int h = 0; h < edges; h++)
            {
                PoincareMap.Geodesic(m_Model, m_Path[h], m_Path[h + 1], centre, points, m_Scratch, 0);
                AddStrip(Highlight, m_Scratch, points, HighlightWidth, HighlightLift, m_UvHighlight);
            }
            float footprint = PostBaseHeight * 1.6f;
            for (int h = 0; h <= edges && m_PathCount > 0; h++)
            {
                int node = m_Path[h];
                if (node == focusNode) continue;
                AddHighlightPost(node, centre, footprint, HighlightPostExtra);
            }
            if (focusNode >= 0) AddHighlightPost(focusNode, centre, PostBaseHeight * 2f, FocusPostExtra);
            WidenBounds(Highlight, HighlightLift);
            m_HighlightDirty = false;
            HighlightBuilds++;
            HighlightRebuilt = true;
        }

        void AddHighlightPost(int node, double2 centre, float footprint, float extra)
        {
            double2 p = PoincareMap.Mobius(PoincareMap.Project(m_Model.Radius(node), m_Model.Theta(node)), centre);
            Highlight.AddBox(PostCentre(p, footprint), new float3(footprint, m_Height[node] + extra, footprint), 0f, m_UvHighlight);
        }

        // A flat strip along a polyline of disc points: 2 vertices per point, 2 triangles per segment, facing up. Written
        // straight into the builder's arrays (reserved first); the bounds are widened by the caller's disc box.
        void AddStrip(AtlasMeshBuilder b, double2[] pts, int count, float width, float y, float2 uv)
        {
            b.Reserve(2 * count, 6 * (count - 1));
            AtlasVertex[] verts = b.Vertices;
            int first = b.VertexCount, vc = first;
            float half = 0.5f * width;
            float3 up = new float3(0f, 1f, 0f);
            for (int k = 0; k < count; k++)
            {
                double2 a = pts[k > 0 ? k - 1 : 0], c = pts[k < count - 1 ? k + 1 : count - 1];
                double tx = c.x - a.x, tz = c.y - a.y;
                double len = Math.Sqrt(tx * tx + tz * tz);
                float nx, nz;
                if (len > 1e-12) { nx = (float)(-tz / len); nz = (float)(tx / len); }
                else { nx = 0f; nz = 1f; }
                float px = (float)pts[k].x * DiscRadiusMetres, pz = (float)pts[k].y * DiscRadiusMetres;
                verts[vc++] = new AtlasVertex { Position = InDisc(px + nx * half, y, pz + nz * half), Normal = up, Color = m_UpColour, Uv0 = uv };
                verts[vc++] = new AtlasVertex { Position = InDisc(px - nx * half, y, pz - nz * half), Normal = up, Color = m_UpColour, Uv0 = uv };
            }
            b.VertexCount = vc;
            // L = even, R = odd; clockwise seen from above (+Y), the way Unity faces the front.
            int[] ix = b.Indices;
            int ic = b.IndexCount;
            for (int k = 0; k < count - 1; k++)
            {
                int l0 = first + 2 * k, r0 = l0 + 1, l1 = l0 + 2, r1 = l0 + 3;
                ix[ic++] = l0; ix[ic++] = l1; ix[ic++] = r1;
                ix[ic++] = l0; ix[ic++] = r1; ix[ic++] = r0;
            }
            b.IndexCount = ic;
        }

        // A strip corner beside a point at the very rim is pulled back onto the circle.
        static float3 InDisc(float x, float y, float z)
        {
            float len = math.sqrt(x * x + z * z);
            if (len > DiscRadiusMetres) { float k = DiscRadiusMetres / len; x *= k; z *= k; }
            return new float3(x, y, z);
        }

        // The disc as a bounding box at height y, for the strips, which do not track bounds themselves.
        static void WidenBounds(AtlasMeshBuilder b, float y)
        {
            float e = DiscRadiusMetres;
            b.BoundsMin = math.min(b.BoundsMin, new float3(-e, y, -e));
            b.BoundsMax = math.max(b.BoundsMax, new float3(e, y, e));
        }
    }
}

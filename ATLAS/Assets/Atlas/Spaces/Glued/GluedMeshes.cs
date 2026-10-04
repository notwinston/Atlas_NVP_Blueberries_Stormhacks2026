using System;
using Atlas.Rendering;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.Glued
{
    /// <summary>
    /// The open-frame cell of S1 (Master Plan sections 3.1 and 8.4): a floor whose grid the shader draws from
    /// the line coordinates, a quarter of a slim post in each corner with a lit cap, half a lintel along each edge,
    /// and one chiral landmark near the low corner. Engine-free.
    /// </summary>
    /// <remarks>
    /// The mesh is centred on the cell: x from -sizeX/2 to sizeX/2, z likewise, floor at y = 0. Everything but
    /// the landmark is symmetric under x to -x, so a mirrored copy (a Klein row) meets its neighbours exactly
    /// and only the landmark tells the two hands apart. Four cells meet at a lattice corner and each brings a
    /// quarter of the post, so no two copies ever draw the same surface; likewise each cell brings half of the lintel
    /// on each edge. The posts, the lintels and their lit caps make every copy read as one frame of a long hall, so
    /// the repetition is seen at a glance; the landmark stands near a corner, out of the walker's way. A cell frame is
    /// never warped, so the 0.25 m edge rule does not apply to it (lane A contract, section 10.2).
    /// </remarks>
    public static class GluedCellMesh
    {
        /// <summary>Spacing the floor grid aims for, metres. The real spacing divides the cell exactly.</summary>
        public const float GridTarget = 0.5f;
        /// <summary>Full width of a corner post, metres; each cell holds a quarter of it.</summary>
        public const float PostWidth = 0.05f;
        /// <summary>Height of the posts, metres: below the vertical period of the 3-torus.</summary>
        public const float PostHeight = 2.2f;
        /// <summary>Rows of a post, so that the shade at its foot has somewhere to fade.</summary>
        public const int PostRows = 4;
        /// <summary>The lit cap on top of each post, metres.</summary>
        public const float CapHeight = 0.1f;
        /// <summary>Full width and height of the lintel that joins the posts along a cell edge, metres; each cell holds half.</summary>
        public const float LintelWidth = 0.024f, LintelHeight = 0.03f;

        /// <summary>The landmark stands this share of the half cell away from the centre, towards the low corner.</summary>
        public const float LandmarkOffset = 0.62f;
        /// <summary>The height of the screw that sets the landmark's hand (with <see cref="LandmarkStep"/>), metres.</summary>
        public const float LandmarkPoleHeight = 1.7f;
        /// <summary>The tetracube: the side of a cube, the width of its lit edges, the height of its lowest face, metres.</summary>
        public const float LandmarkCube = 0.15f, LandmarkEdgeWidth = 0.012f, LandmarkLift = 1.0f;
        /// <summary>The radius of the ball on top and of the glowing ring on the floor, metres.</summary>
        public const float LandmarkBall = 0.06f, LandmarkRing = 0.21f;
        /// <summary>Palette cells: the cubes' faces, their lit edges, the ball and the ring.</summary>
        public const int LandmarkFace = AtlasPalette.Booth, LandmarkEdge = AtlasPalette.Beacon,
                         LandmarkBallColour = AtlasPalette.CueStrong, LandmarkGlow = AtlasPalette.PortalFrame;
        /// <summary>Steps of the spiral stair round the pole.</summary>
        public const int LandmarkSteps = 14;
        /// <summary>Turns of the stair, bottom to top.</summary>
        public const float LandmarkTurns = 1.75f;
        public const float LandmarkStepRadius = 0.11f, LandmarkFirstStep = 0.25f, LandmarkRise = 0.095f;

        /// <summary>Floor grid squares along a cell side of this length: lines fall on the cell edges and divide the cell evenly.</summary>
        public static int GridDivisions(float size)
        {
            return math.max(1, (int)math.floor(size / GridTarget + 0.5f));
        }

        /// <summary>How far the landmark reaches from its pole (the rail and the treads' outer ends), and the gap it keeps from the cell edge, metres.</summary>
        public const float LandmarkReach = 0.22f, LandmarkMargin = 0.02f;

        /// <summary>
        /// Where the landmark's pole stands, cell-centred floor coordinates (x, z): towards the low corner, and never so
        /// far that it reaches past the cell's edge into the next copy (a small standing room).
        /// </summary>
        public static float2 LandmarkBase(float sizeX, float sizeZ)
        {
            float x = math.min(0.5f * sizeX * LandmarkOffset, 0.5f * sizeX - LandmarkReach - LandmarkMargin);
            float z = math.min(0.5f * sizeZ * LandmarkOffset, 0.5f * sizeZ - LandmarkReach - LandmarkMargin);
            return new float2(-math.max(x, 0f), -math.max(z, 0f));
        }

        /// <summary>
        /// Cube k of the tetracube, in cube units from its low corner: one step along +X, one along +Z, one up. Three
        /// steps along three different axes make a shape with a hand.
        /// </summary>
        public static int3 TetracubeCell(int k)
        {
            switch (k)
            {
                case 0: return new int3(0, 0, 0);
                case 1: return new int3(1, 0, 0);
                case 2: return new int3(1, 0, 1);
                default: return new int3(1, 1, 1);
            }
        }

        /// <summary>
        /// A point k of the screw that sets the landmark's hand, relative to its foot: seen from above it turns
        /// counter-clockwise (from +X towards +Z) as it rises, as the tetracube does; its mirror image turns
        /// clockwise. The chiral kit and the handedness tests read the hand from it.
        /// </summary>
        public static float3 LandmarkStep(int k, out float angle)
        {
            angle = 2f * math.PI * LandmarkTurns * k / LandmarkSteps;
            math.sincos(angle, out float sin, out float cos);
            return new float3(LandmarkStepRadius * cos, LandmarkFirstStep + LandmarkRise * k, LandmarkStepRadius * sin);
        }

        /// <summary>
        /// Appends the cell for this cell size. With a kit landmark (a piece modelled in Blender, foot at its
        /// origin) the piece stands at <see cref="LandmarkBase"/> in place of the built-in stair.
        /// </summary>
        public static void Build(float sizeX, float sizeZ, AtlasMeshBuilder data, MeshData landmark = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (!(sizeX > 0f) || !(sizeZ > 0f)) throw new ArgumentOutOfRangeException(nameof(sizeX), "A cell needs a size");
            float hx = 0.5f * sizeX, hz = 0.5f * sizeZ;

            // Floor. Line coordinates run from 0 to the number of grid squares, so every cell edge is an integer
            // and a multiple of the major period: the cell outline is drawn stronger than the grid inside it.
            int kx = GridDivisions(sizeX), kz = GridDivisions(sizeZ);
            float pitch = math.max(sizeX / kx, sizeZ / kz) * 1.001f;
            data.AddQuadGrid(new float3(-hx, 0f, -hz), new float3(sizeX, 0f, 0f), new float3(0f, 0f, sizeZ),
                             AtlasPalette.Uv(AtlasPalette.Floor),
                             float2.zero, new float2(kx, 0f), new float2(0f, kz), 1f, 255, pitch);

            // A quarter post inside each corner, its top lit; the cap shows the lattice through the fog and in the dark.
            float q = 0.5f * PostWidth;
            float2 post = AtlasPalette.Uv(AtlasPalette.Post);
            float2 cap = AtlasPalette.Uv(AtlasPalette.GridLine);
            float shaft = PostHeight - CapHeight;
            float rows = shaft / PostRows * 1.001f;
            for (int corner = 0; corner < 4; corner++)
            {
                float sx = (corner & 1) == 0 ? -1f : 1f, sz = (corner & 2) == 0 ? -1f : 1f;
                var foot = new float3(sx * (hx - 0.5f * q), 0f, sz * (hz - 0.5f * q));
                data.AddBox(foot, new float3(q, shaft, q), 0f, post, false, 0, rows, 0.6f);
                data.AddBox(foot + new float3(0f, shaft, 0f), new float3(q, CapHeight, q), 0f, cap, false, 0, 4f);
            }

            // Half a lintel inside each edge, just under the caps: with the neighbour's half it is one beam.
            float l = 0.5f * LintelWidth, top = shaft - LintelHeight;
            float2 lintel = AtlasPalette.Uv(AtlasPalette.Kerb);
            for (int side = -1; side <= 1; side += 2)
            {
                data.AddBox(new float3(0f, top, side * (hz - 0.5f * l)), new float3(sizeX - 2f * q, LintelHeight, l), 0f,
                            lintel, true, 0, 4f);
                data.AddBox(new float3(side * (hx - 0.5f * l), top, 0f), new float3(l, LintelHeight, sizeZ - 2f * q), 0f,
                            lintel, true, 0, 4f);
            }

            // The landmark: a chiral tetracube floating above a glowing ring, its edges lit, a glowing red ball on its
            // top cube. Its four cubes step along +X, then +Z, then up: the same screw sense as LandmarkStep. No turning
            // brings it onto its mirror image, so a mirrored Klein row is told apart at a glance. A kit landmark has a
            // hand of its own.
            float2 base2 = LandmarkBase(sizeX, sizeZ);
            float3 pole = new float3(base2.x, 0f, base2.y);
            if (landmark != null)
            {
                data.Append(landmark, pole);
                return;
            }
            float2 face = AtlasPalette.Uv(LandmarkFace), edge = AtlasPalette.Uv(LandmarkEdge);
            float c = LandmarkCube;
            // The footprint is two cubes by two, centred on the pole.
            float3 origin = pole + new float3(-0.5f * c, LandmarkLift, -0.5f * c);
            for (int k = 0; k < 4; k++)
                GluedShapes.AddNeonCube(data, origin + (float3)TetracubeCell(k) * c + new float3(0f, 0.5f * c, 0f), c, LandmarkEdgeWidth,
                                        face, edge);
            float3 ball = origin + (float3)TetracubeCell(3) * c + new float3(0f, c + LandmarkBall + 0.01f, 0f);
            GluedShapes.AddSphere(data, ball, LandmarkBall, 12, 8, AtlasPalette.Uv(LandmarkBallColour));
            GluedShapes.AddRing(data, pole + new float3(0f, 0.004f, 0f), LandmarkRing - 0.035f, LandmarkRing, 24,
                                AtlasPalette.Uv(LandmarkGlow));
        }
    }

    /// <summary>
    /// The walker's copy as seen in the other cells, after the players of the UIC "playing ball in the 3-torus" demo: a
    /// white cube head with lit edges and a glowing visor, and two glowing controllers held at the waist, green in the
    /// left hand and blue in the right, so that a mirrored copy visibly holds them the other way round (Master Plan
    /// section 3.1). No body: the copies stay light, and many of them read at once. Engine-free.
    /// </summary>
    /// <remarks>
    /// The origin is the tracked head (the midpoint between the eyes), +Z is where the walker faces, +X is the
    /// walker's right. The controllers hang from the head, so the copy crouches when the walker does. This is the
    /// body alone, built once; the walker's hands, when they are tracked, are added after it by
    /// <see cref="GluedAvatarRig"/> in the same frame (<see cref="GluedAvatarHands"/>), each with its forearm from
    /// <see cref="Elbow"/>.
    /// </remarks>
    public static class GluedAvatarMesh
    {
        /// <summary>Nothing of the avatar rises above this, metres above the eyes.</summary>
        public const float HeadRadius = 0.105f;
        /// <summary>The head's centre lies this far behind the eyes, metres.</summary>
        public const float HeadBack = 0.08f;
        /// <summary>The side of the head cube and the width of its lit edges, metres.</summary>
        public const float HeadCube = 0.19f, EdgeWidth = 0.012f;
        /// <summary>Palette cells: the head, its edges and visor, the left and the right controller, their tips.</summary>
        public const int HeadColour = AtlasPalette.CueStandStill, Glow = AtlasPalette.PortalFrame,
                         LeftHand = AtlasPalette.HomeBeacon, RightHand = AtlasPalette.CueRescan, Tip = AtlasPalette.SliceEdge;
        /// <summary>Where the controllers are held, from the eyes: out to each side, down, and forward, metres.</summary>
        public static readonly float3 Grip = new float3(0.24f, -0.6f, 0.06f);
        /// <summary>How far each tracked hand's elbow stands from the middle of the body, metres.</summary>
        public const float ArmX = 0.235f;
        /// <summary>Height of each tracked hand's elbow relative to the head, metres.</summary>
        public const float ElbowY = -0.68f;

        /// <summary>
        /// The elbow of the arm on one side in the avatar's own frame, where a tracked hand's forearm starts. The
        /// walker's left is -X.
        /// </summary>
        public static float3 Elbow(HandSide side)
        {
            return new float3(side == HandSide.Left ? -ArmX : ArmX, ElbowY, -HeadBack);
        }

        /// <summary>Appends the avatar; a kit avatar (a piece modelled in Blender, same frame) is copied instead.</summary>
        public static void Build(AtlasMeshBuilder data, MeshData kit = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (kit != null)
            {
                data.Append(kit, float3.zero);
                return;
            }
            float z = -HeadBack;
            GluedShapes.AddNeonCube(data, new float3(0f, 0f, z), HeadCube, EdgeWidth, AtlasPalette.Uv(HeadColour),
                                    AtlasPalette.Uv(Glow));
            // A visor on the front face shows which way the copy faces.
            data.AddBox(new float3(0f, -0.035f, z + 0.5f * HeadCube + 0.004f), new float3(0.14f, 0.04f, 0.008f), 0f,
                        AtlasPalette.Uv(Glow), true, 0, 4f);

            // The controllers: a glowing stick in each hand, pointing forward, with a white tip.
            for (int side = -1; side <= 1; side += 2)
            {
                var grip = new float3(side * Grip.x, Grip.y, Grip.z);
                float2 hand = AtlasPalette.Uv(side < 0 ? LeftHand : RightHand);
                data.AddBox(grip, new float3(0.045f, 0.045f, 0.2f), 0f, hand, true, 0, 4f);
                data.AddBox(grip + new float3(0f, -0.005f, 0.115f), new float3(0.055f, 0.055f, 0.03f), 0f,
                            AtlasPalette.Uv(Tip), true, 0, 4f);
            }
        }
    }

    /// <summary>Small closed shapes for the cell and the avatar: a sphere, a cube with lit edges, a flat ring. Engine-free.</summary>
    static class GluedShapes
    {
        // A latitude and longitude sphere with smooth normals, faces outwards.
        public static void AddSphere(AtlasMeshBuilder data, float3 centre, float radius, int segments, int rings, float2 uv0)
        {
            data.Reserve((segments + 1) * (rings + 1), segments * rings * 6);
            int first = data.VertexCount;
            for (int j = 0; j <= rings; j++)
            {
                math.sincos(math.PI * j / rings, out float sinPolar, out float cosPolar);
                for (int i = 0; i <= segments; i++)
                {
                    math.sincos(2f * math.PI * (i % segments) / segments, out float sin, out float cos);
                    float3 normal = new float3(sinPolar * sin, cosPolar, sinPolar * cos);
                    data.AddVertex(centre + radius * normal, normal, uv0, float2.zero, 1f, 0);
                }
            }
            int stride = segments + 1;
            for (int j = 0; j < rings; j++)
            {
                for (int i = 0; i < segments; i++)
                {
                    int p00 = first + j * stride + i, p10 = p00 + 1, p01 = p00 + stride, p11 = p01 + 1;
                    // The top and bottom rows collapse to the poles: one triangle each instead of two.
                    if (j > 0) AddOutward(data, centre, p00, p10, p11);
                    if (j < rings - 1) AddOutward(data, centre, p00, p11, p01);
                }
            }
        }

        // Adds the triangle wound so that its front (clockwise, Unity's rule) faces away from the centre.
        static void AddOutward(AtlasMeshBuilder data, float3 centre, int a, int b, int c)
        {
            float3 pa = data.Vertices[a].Position, pb = data.Vertices[b].Position, pc = data.Vertices[c].Position;
            float3 front = math.cross(pb - pa, pc - pa);
            if (math.dot(front, (pa + pb + pc) / 3f - centre) >= 0f) data.AddTriangle(a, b, c);
            else data.AddTriangle(a, c, b);
        }

        /// <summary>
        /// A cube of side `side` about `centre` in colour `face`, with its twelve edges drawn as bars `edge` wide in colour
        /// `edgeUv` (a lit colour makes a neon frame). The bars straddle the edges, so they show from every side.
        /// </summary>
        public static void AddNeonCube(AtlasMeshBuilder data, float3 centre, float side, float edge, float2 face, float2 edgeUv)
        {
            float h = 0.5f * side;
            data.AddBox(centre - new float3(0f, h, 0f), new float3(side, side, side), 0f, face, true, 0, 4f);
            for (int i = -1; i <= 1; i += 2)
            {
                for (int j = -1; j <= 1; j += 2)
                {
                    // Vertical edges at the four corners.
                    data.AddBox(centre + new float3(i * h, -h - 0.5f * edge, j * h), new float3(edge, side + edge, edge), 0f,
                                edgeUv, true, 0, 4f);
                    // Edges along X on the top and bottom, front and back.
                    data.AddBox(centre + new float3(0f, i * h - 0.5f * edge, j * h), new float3(side, edge, edge), 0f,
                                edgeUv, true, 0, 4f);
                    // Edges along Z on the top and bottom, left and right.
                    data.AddBox(centre + new float3(j * h, i * h - 0.5f * edge, 0f), new float3(edge, edge, side), 0f,
                                edgeUv, true, 0, 4f);
                }
            }
        }

        /// <summary>A flat ring on the horizontal plane through `centre`, facing up, between two radii.</summary>
        public static void AddRing(AtlasMeshBuilder data, float3 centre, float inner, float outer, int segments, float2 uv0)
        {
            var up = new float3(0f, 1f, 0f);
            for (int i = 0; i < segments; i++)
            {
                math.sincos(2f * math.PI * i / segments, out float s0, out float c0);
                math.sincos(2f * math.PI * (i + 1) / segments, out float s1, out float c1);
                int first = data.VertexCount;
                data.AddVertex(centre + new float3(inner * c0, 0f, inner * s0), up, uv0, float2.zero, 1f, 0);
                data.AddVertex(centre + new float3(outer * c0, 0f, outer * s0), up, uv0, float2.zero, 1f, 0);
                data.AddVertex(centre + new float3(outer * c1, 0f, outer * s1), up, uv0, float2.zero, 1f, 0);
                data.AddVertex(centre + new float3(inner * c1, 0f, inner * s1), up, uv0, float2.zero, 1f, 0);
                AddOutward(data, centre - up, first, first + 1, first + 2);
                AddOutward(data, centre - up, first, first + 2, first + 3);
            }
        }

    }

    /// <summary>The wrap-walk prompt: three chevrons on the floor pointing across a cell edge. Engine-free.</summary>
    /// <remarks>The origin is on the edge; the chevrons point along +Z and lie just above the floor.</remarks>
    public static class GluedPromptMesh
    {
        public const int Chevrons = 3;
        /// <summary>Half width, arm thickness and spacing of the chevrons, metres.</summary>
        public const float HalfWidth = 0.16f, Thickness = 0.06f, Spacing = 0.14f;
        /// <summary>Height above the floor, so the chevrons never fight the floor for depth, metres.</summary>
        public const float Lift = 0.006f;

        public static void Build(AtlasMeshBuilder data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            float2 uv = AtlasPalette.Uv(AtlasPalette.Beacon);
            float w = HalfWidth, rise = 0.6f * HalfWidth;
            float first = -0.5f * ((Chevrons - 1) * Spacing + rise + Thickness);
            float3 thick = new float3(0f, 0f, Thickness);
            for (int k = 0; k < Chevrons; k++)
            {
                float z0 = first + k * Spacing;
                // Two parallelograms that meet at the tip. With u towards +X and v towards +Z the quad faces up.
                data.AddQuad(new float3(-w, Lift, z0), new float3(w, 0f, rise), thick, uv);
                data.AddQuad(new float3(0f, Lift, z0 + rise), new float3(w, 0f, -rise), thick, uv);
            }
        }
    }
}

using System;
using Atlas.Rendering;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.Glued
{
    /// <summary>
    /// The open-frame cell of S1 (Master Plan sections 3.1 and 8.4): a floor whose grid the shader draws from
    /// the line coordinates, a quarter of a post in each corner, and one chiral landmark. Engine-free.
    /// </summary>
    /// <remarks>
    /// The mesh is centred on the cell: x from -sizeX/2 to sizeX/2, z likewise, floor at y = 0. Everything but
    /// the landmark is symmetric under x to -x, so a mirrored copy (a Klein row) meets its neighbours exactly
    /// and only the landmark tells the two hands apart. Four cells meet at a lattice corner and each brings a
    /// quarter of the post, so no two copies ever draw the same surface. A cell frame is never warped, so the
    /// 0.25 m edge rule does not apply to it (lane A contract, section 10.2).
    /// </remarks>
    public static class GluedCellMesh
    {
        /// <summary>Spacing the floor grid aims for, metres. The real spacing divides the cell exactly.</summary>
        public const float GridTarget = 0.5f;
        /// <summary>Full width of a corner post, metres; each cell holds a quarter of it.</summary>
        public const float PostWidth = 0.08f;
        /// <summary>Height of the posts, metres: below the vertical period of the 3-torus.</summary>
        public const float PostHeight = 2.2f;
        /// <summary>Rows of a post, so that the shade at its foot has somewhere to fade.</summary>
        public const int PostRows = 4;

        /// <summary>The landmark stands this share of the half cell away from the centre, towards the low corner.</summary>
        public const float LandmarkOffset = 0.5f;
        public const float LandmarkPoleRadius = 0.02f, LandmarkPoleHeight = 1.7f;
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

        /// <summary>Where the landmark's pole stands, cell-centred floor coordinates (x, z).</summary>
        public static float2 LandmarkBase(float sizeX, float sizeZ)
        {
            return new float2(-0.5f * sizeX, -0.5f * sizeZ) * LandmarkOffset;
        }

        /// <summary>
        /// Centre of the base of step k of the stair, relative to the foot of the pole. Seen from above the
        /// stair climbs counter-clockwise (from +X towards +Z); its mirror image climbs clockwise.
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

            // A quarter post inside each corner.
            float q = 0.5f * PostWidth;
            float2 post = AtlasPalette.Uv(AtlasPalette.Post);
            float rows = PostHeight / PostRows * 1.001f;
            for (int corner = 0; corner < 4; corner++)
            {
                float sx = (corner & 1) == 0 ? -1f : 1f, sz = (corner & 2) == 0 ? -1f : 1f;
                data.AddBox(new float3(sx * (hx - 0.5f * q), 0f, sz * (hz - 0.5f * q)), new float3(q, PostHeight, q), 0f,
                            post, false, 0, rows, 0.6f);
            }

            // The landmark: a pole with a spiral stair and a pennant. A spiral has a hand, so a mirrored cell
            // shows at a glance that it is mirrored. A kit landmark has a hand of its own.
            float2 foot = LandmarkBase(sizeX, sizeZ);
            float3 pole = new float3(foot.x, 0f, foot.y);
            if (landmark != null)
            {
                data.Append(landmark, pole);
                return;
            }
            data.AddPost(pole, LandmarkPoleRadius, LandmarkPoleHeight, post, 6, true, 0, 4f, 0.6f);
            float2 tread = AtlasPalette.Uv(AtlasPalette.Landmark);
            for (int k = 0; k < LandmarkSteps; k++)
            {
                float3 step = LandmarkStep(k, out float angle);
                // Local +X of a box turned by yaw points along (cos yaw, 0, -sin yaw): radial for yaw = -angle.
                data.AddBox(pole + step, new float3(0.18f, 0.03f, 0.08f), -angle, tread, true, 0, 4f);
            }
            data.AddBox(pole + new float3(0.11f, LandmarkPoleHeight - 0.14f, 0f), new float3(0.20f, 0.12f, 0.012f), 0f,
                        AtlasPalette.Uv(AtlasPalette.Flag), true, 0, 4f);
        }
    }

    /// <summary>
    /// The walker's copy as seen in the other cells: head, torso and two shoulders in two colours, so that a
    /// mirrored copy visibly raises the other hand (Master Plan section 3.1). Engine-free.
    /// </summary>
    /// <remarks>
    /// The origin is the tracked head (the midpoint between the eyes), +Z is where the walker faces, +X is the
    /// walker's right. The body hangs from the head, so the copy crouches when the walker does. This is the
    /// body alone, built once; the walker's hands, when they are tracked, are added after it by
    /// <see cref="GluedAvatarRig"/> in the same frame (<see cref="GluedAvatarHands"/>).
    /// </remarks>
    public static class GluedAvatarMesh
    {
        public const float HeadRadius = 0.105f;
        /// <summary>The head's centre lies this far behind the eyes, metres.</summary>
        public const float HeadBack = 0.08f;
        public const int HeadSegments = 10, HeadRings = 6;
        /// <summary>How far each upper arm's axis stands from the middle of the body, metres.</summary>
        public const float ArmX = 0.235f;
        /// <summary>Height of the elbow, the lower end of the upper arm, relative to the head, metres.</summary>
        public const float ElbowY = -0.68f;

        /// <summary>
        /// The elbow of the arm on one side in the avatar's own frame: the middle of the lower end of the upper arm,
        /// where a forearm starts. The walker's left is -X.
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
            float2 body = AtlasPalette.Uv(AtlasPalette.AvatarBody);
            float2 left = AtlasPalette.Uv(AtlasPalette.AvatarLeft);
            float2 right = AtlasPalette.Uv(AtlasPalette.AvatarRight);
            float z = -HeadBack;

            AddSphere(data, new float3(0f, 0f, z), HeadRadius, HeadSegments, HeadRings, body);
            // A visor in front of the eyes shows which way the copy faces.
            data.AddBox(new float3(0f, -0.025f, 0.03f), new float3(0.15f, 0.05f, 0.05f), 0f,
                        AtlasPalette.Uv(AtlasPalette.Text), true, 0, 4f);
            data.AddBox(new float3(0f, -0.25f, z), new float3(0.08f, 0.16f, 0.08f), 0f, body, false, 0, 4f);          // neck
            data.AddBox(new float3(0f, -0.78f, z), new float3(0.32f, 0.53f, 0.18f), 0f, body, true, 0, 4f);           // torso
            data.AddBox(new float3(-0.225f, -0.38f, z), new float3(0.13f, 0.13f, 0.16f), 0f, left, true, 0, 4f);      // left shoulder
            data.AddBox(new float3(-ArmX, ElbowY, z), new float3(0.09f, 0.30f, 0.10f), 0f, left, true, 0, 4f);        // left upper arm
            data.AddBox(new float3(0.225f, -0.38f, z), new float3(0.13f, 0.13f, 0.16f), 0f, right, true, 0, 4f);      // right shoulder
            data.AddBox(new float3(ArmX, ElbowY, z), new float3(0.09f, 0.30f, 0.10f), 0f, right, true, 0, 4f);        // right upper arm
        }

        // A latitude and longitude sphere with smooth normals, faces outwards.
        static void AddSphere(AtlasMeshBuilder data, float3 centre, float radius, int segments, int rings, float2 uv0)
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

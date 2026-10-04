using Atlas.Rendering;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.AlignedCells
{
    // S3 duck maze design (2026-10-04) §3, §4: the rubber duck that stands on one tile of the maze. The kit piece "duck"
    // (modelled in Blender, 2,400 triangles, 0.3 m tall, standing on y = 0) when the kit has it; otherwise a procedural duck
    // of the same size on the same palette cells: a yellow sphere body, a smaller head, an orange beak and two black eyes.
    // Build is engine-free (tests run it headless); Create uploads the result.
    public static class DuckMesh
    {
        public const string KitPiece = "duck";
        public const int Body = AtlasPalette.DuckYellow, Beak = AtlasPalette.DuckOrange, Eye = AtlasPalette.DuckBlack;

        // The procedural duck, metres: the body stands on the floor, the head sits above its front, the beak points along +z.
        public const float BodyRadius = 0.11f, BodyHeight = 0.11f, HeadRadius = 0.065f, EyeRadius = 0.012f;
        public static readonly float3 HeadCentre = new float3(0f, 0.24f, 0.06f);
        public static readonly float3 BeakCentre = new float3(0f, 0.235f, 0.13f), BeakSize = new float3(0.05f, 0.02f, 0.05f);
        public static readonly float3 EyeCentre = new float3(0.03f, 0.26f, 0.115f);   // and its mirror in x

        /// <summary>
        /// Replaces the contents of <paramref name="into"/> with the duck: the kit piece when <paramref name="kit"/> has a
        /// valid one (true), otherwise the procedural duck (false). kitError says why a kit's piece was refused; it is null
        /// when the piece is used or no kit was given.
        /// </summary>
        public static bool Build(KitSet kit, AtlasMeshBuilder into, out string kitError)
        {
            into.Clear();
            kitError = null;
            if (kit != null)
            {
                var piece = new MeshData(1536, 7680);
                if (kit.TryGet(KitPiece, piece, out kitError))
                {
                    into.Append(piece, float3.zero);
                    return true;
                }
            }
            BuildProcedural(into);
            return false;
        }

        /// <summary>A new builder holding the duck (see <see cref="Build(KitSet, AtlasMeshBuilder, out string)"/>).</summary>
        public static AtlasMeshBuilder Build(KitSet kit)
        {
            var data = new AtlasMeshBuilder(1536, 7680);
            Build(kit, data, out _);
            return data;
        }

        /// <summary>The procedural duck, appended: about 560 triangles.</summary>
        public static void BuildProcedural(AtlasMeshBuilder data)
        {
            AddSphere(data, new float3(0f, BodyHeight, 0f), BodyRadius, 16, 10, AtlasPalette.Uv(Body));
            AddSphere(data, HeadCentre, HeadRadius, 12, 8, AtlasPalette.Uv(Body));
            data.AddBox(BeakCentre - new float3(0f, 0.5f * BeakSize.y, 0f), BeakSize, 0f, AtlasPalette.Uv(Beak), true, 0);
            for (int side = -1; side <= 1; side += 2)
                AddSphere(data, new float3(side * EyeCentre.x, EyeCentre.y, EyeCentre.z), EyeRadius, 8, 4, AtlasPalette.Uv(Eye));
        }

        /// <summary>The duck as a Mesh. A kit whose piece is refused is reported once here and the procedural duck is used.</summary>
        public static Mesh Create(KitSet kit)
        {
            var data = new AtlasMeshBuilder(1536, 7680);
            if (!Build(kit, data, out string error) && error != null)
                Debug.LogWarning("Atlas: kit piece '" + KitPiece + "' missing or invalid; using the built-in duck. (" + error + ")");
            return AtlasMesh.Create(data, "Duck");
        }

        // A latitude and longitude sphere with smooth normals, faces outwards, no lines: GluedShapes.AddSphere, which is
        // internal to the Glued space.
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
                    var normal = new float3(sinPolar * sin, cosPolar, sinPolar * cos);
                    data.AddVertex(centre + radius * normal, normal, uv0, float2.zero, 1f, 0);
                }
            }
            int stride = segments + 1;
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < segments; i++)
                {
                    int p00 = first + j * stride + i, p10 = p00 + 1, p01 = p00 + stride, p11 = p01 + 1;
                    // The top and bottom rows collapse to the poles: one triangle each instead of two.
                    if (j > 0) AddOutward(data, centre, p00, p10, p11);
                    if (j < rings - 1) AddOutward(data, centre, p00, p11, p01);
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
}

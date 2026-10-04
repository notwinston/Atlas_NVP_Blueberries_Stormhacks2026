using NUnit.Framework;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Rendering.Tests.Room
{
    /// <summary>Measurements shared by the room tests.</summary>
    static class RoomTestUtil
    {
        public const float EdgeLimit = 0.25f * 1.0002f;

        public static RoomModel Fixture(string name)
        {
            return RoomFixtures.LoadFromProject(name);
        }

        /// <summary>A room from an outline alone: one wall per edge, 2.4 m high, no openings, no objects.</summary>
        public static RoomModel FromOutline(params float2[] outline)
        {
            var room = new RoomModel { Id = "test", Outline = outline, Walls = new RoomWall[outline.Length] };
            for (int k = 0; k < outline.Length; k++)
                room.Walls[k] = new RoomWall { A = outline[k], B = outline[(k + 1) % outline.Length], Height = 2.4f };
            return room;
        }

        public static RoomModel Rectangle(float width, float depth)
        {
            float x = 0.5f * width, z = 0.5f * depth;
            return FromOutline(new float2(-x, -z), new float2(x, -z), new float2(x, z), new float2(-x, z));
        }

        /// <summary>Sum of the triangles' areas, square metres.</summary>
        public static double Area(AtlasMeshBuilder mesh)
        {
            double total = 0.0;
            for (int t = 0; t < mesh.IndexCount; t += 3)
            {
                float3 a = mesh.Vertices[mesh.Indices[t]].Position;
                float3 b = mesh.Vertices[mesh.Indices[t + 1]].Position;
                float3 c = mesh.Vertices[mesh.Indices[t + 2]].Position;
                total += 0.5 * math.length(math.cross((double3)(b - a), (double3)(c - a)));
            }
            return total;
        }

        public static float3 Centroid(AtlasMeshBuilder mesh, int triangle)
        {
            int t = triangle * 3;
            return (mesh.Vertices[mesh.Indices[t]].Position + mesh.Vertices[mesh.Indices[t + 1]].Position
                    + mesh.Vertices[mesh.Indices[t + 2]].Position) / 3f;
        }

        /// <summary>Every triangle is clockwise seen from the side its vertex normals point to, and none is degenerate.</summary>
        public static void AssertWindingAgreesWithNormals(AtlasMeshBuilder mesh)
        {
            Assert.That(mesh.IndexCount % 3, Is.EqualTo(0));
            for (int t = 0; t < mesh.IndexCount; t += 3)
            {
                int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
                Assert.That(a >= 0 && a < mesh.VertexCount && b >= 0 && b < mesh.VertexCount && c >= 0 && c < mesh.VertexCount, Is.True);
                float3 pa = mesh.Vertices[a].Position, pb = mesh.Vertices[b].Position, pc = mesh.Vertices[c].Position;
                float3 face = math.cross(pb - pa, pc - pa);
                Assert.That(math.length(face), Is.GreaterThan(1e-10f), "degenerate triangle " + t / 3);
                float3 normal = math.normalize(mesh.Vertices[a].Normal + mesh.Vertices[b].Normal + mesh.Vertices[c].Normal);
                Assert.That(math.dot(math.normalize(face), normal), Is.GreaterThan(0.5f), "triangle " + t / 3);
            }
        }

        /// <summary>
        /// Number of T-junctions: vertices that lie strictly inside an edge of a triangle they do not belong to.
        /// A mesh with none has no cracks when it is bent.
        /// </summary>
        public static int CountTJunctions(AtlasMeshBuilder mesh, float tolerance = 1e-4f)
        {
            int found = 0;
            for (int t = 0; t < mesh.IndexCount; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    float3 a = mesh.Vertices[mesh.Indices[t + e]].Position;
                    float3 b = mesh.Vertices[mesh.Indices[t + (e + 1) % 3]].Position;
                    float3 ab = b - a;
                    float lengthSq = math.lengthsq(ab);
                    for (int v = 0; v < mesh.VertexCount; v++)
                    {
                        float3 p = mesh.Vertices[v].Position;
                        float s = math.dot(p - a, ab) / lengthSq;
                        if (s <= 0f || s >= 1f) continue;
                        if (math.distancesq(p, a) < tolerance * tolerance || math.distancesq(p, b) < tolerance * tolerance) continue;
                        if (math.distancesq(p, a + ab * s) < tolerance * tolerance) found++;
                    }
                }
            }
            return found;
        }

        /// <summary>Area of a wall with its openings cut out, square metres.</summary>
        public static double WallArea(RoomWall wall)
        {
            double area = (double)wall.Length * wall.Height;
            for (int k = 0; k < wall.Openings.Length; k++)
            {
                WallOpening o = wall.Openings[k];
                area -= (double)o.Width * (math.min(o.Top, wall.Height) - o.Bottom);
            }
            return area;
        }

        /// <summary>Distance from a floor point to a wall's segment, and the distance along the wall of the nearest point.</summary>
        public static float DistanceToWall(RoomWall wall, float2 point, out float along)
        {
            float2 ab = wall.B - wall.A;
            float length = math.length(ab);
            along = math.clamp(math.dot(point - wall.A, ab) / length, 0f, length);
            return math.distance(point, wall.A + ab / length * along);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Objects.Tests
{
    /// <summary>
    /// Shared inputs of the Atlas.Objects tests (docs/plans/standalone-phone/INDEX.md, task TT2): the reference
    /// vectors that tools/props/reference.py wrote from the Forge's Python code, the three fixture props of
    /// props_min, and small mesh helpers. Written from the plan's section 6.3 and 6.4 and reference.json, not
    /// from the code under test.
    /// </summary>
    static class ObjectsTestData
    {
        /// <summary>reference.json: floats within this, choices and profile bytes exactly.</summary>
        public const float Tolerance = 1e-4f;

        static JsonValue s_reference;
        static PropEntry[] s_fixtureProps;

        public static string ProjectDir =>
            Environment.GetEnvironmentVariable("ATLAS_PROJECT_DIR") ?? Directory.GetCurrentDirectory();

        public static string FixtureDir =>
            Path.Combine(ProjectDir, "Assets", "Atlas", "Tests", "Objects", "Fixtures");

        public static JsonValue Reference
        {
            get
            {
                if (s_reference == null)
                    s_reference = JsonValue.Parse(File.ReadAllText(Path.Combine(FixtureDir, "reference.json")));
                return s_reference;
            }
        }

        /// <summary>The props of Fixtures/props_min, in manifest order, each as Telemetry's PropSet builds it
        /// (section 6.4): id, category, tags, reference size, profile, slot default cells and the GLB's mesh.</summary>
        public static PropEntry[] FixtureProps
        {
            get
            {
                if (s_fixtureProps == null) s_fixtureProps = LoadFixtureProps();
                return s_fixtureProps;
            }
        }

        public static PropEntry FixtureProp(string id)
        {
            foreach (var p in FixtureProps)
                if (p.Id == id) return p;
            throw new ArgumentException("No fixture prop " + id);
        }

        static PropEntry[] LoadFixtureProps()
        {
            string dir = Path.Combine(FixtureDir, "props_min");
            JsonValue manifest = JsonValue.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json")));
            Assert.That(manifest.GetString("schema"), Is.EqualTo("atlas.props/1"));
            JsonValue props = manifest["props"];
            var result = new PropEntry[props.Count];
            for (int k = 0; k < props.Count; k++)
            {
                JsonValue p = props[k];
                JsonValue slots = p["slots"];
                var cells = new byte[slots.Count];
                for (int s = 0; s < cells.Length; s++) cells[s] = (byte)slots[s].GetInt("cell");
                string id = p.GetString("id");
                MeshData mesh = ReadGlb(File.ReadAllBytes(Path.Combine(dir, id + ".glb.bytes")));
                Assert.That(mesh.TriangleCount, Is.EqualTo(p.GetInt("triangles")), id + ": triangles in the manifest");
                result[k] = new PropEntry
                {
                    Id = id,
                    Category = Category(p.GetString("category")),
                    Tags = Strings(p["tags"]),
                    Size = Float3(p["size"]),
                    Profile = Convert.FromBase64String(p.GetString("profile")),
                    SlotCells = cells,
                    Mesh = mesh,
                };
            }
            return result;
        }

        // ------------------------------------------------------------------ JSON helpers

        public static ObjectCategory Category(string jsonName)
        {
            ObjectCategory category;
            Assert.That(ObjectCategories.TryParse(jsonName, out category), Is.True, "category " + jsonName);
            return category;
        }

        public static ObjectFacingRule Facing(string name)
        {
            return (ObjectFacingRule)Enum.Parse(typeof(ObjectFacingRule), name);
        }

        public static float3 Float3(JsonValue a)
        {
            return new float3(a[0].AsFloat(), a[1].AsFloat(), a[2].AsFloat());
        }

        public static string[] Strings(JsonValue a)
        {
            if (a == null || !a.IsArray) return Array.Empty<string>();
            var result = new string[a.Count];
            for (int k = 0; k < result.Length; k++) result[k] = a[k].AsString();
            return result;
        }

        /// <summary>A base64 profile, or null for JSON null.</summary>
        public static byte[] Profile(JsonValue v)
        {
            return v == null || v.IsNull ? null : Convert.FromBase64String(v.AsString());
        }

        public static string Hex(byte[] bytes)
        {
            if (bytes == null) return "null";
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ GLB (kit contract)

        /// <summary>
        /// The mesh of a kit-contract GLB (tools/kit/kitglb.py), turned back into Unity's box-local frame the way
        /// the phone's reader does it (docs/contracts/lane-d-link.md section 4.1): x negated in positions and
        /// normals, v flipped in uv0, the winding reversed, COLOR_0 packed R lowest. uv1 is zero.
        /// </summary>
        public static MeshData ReadGlb(byte[] glb)
        {
            Assert.That(BitConverter.ToUInt32(glb, 0), Is.EqualTo(0x46546C67u), "glTF magic");
            int jsonLength = BitConverter.ToInt32(glb, 12);
            JsonValue doc = JsonValue.Parse(Encoding.UTF8.GetString(glb, 20, jsonLength).Trim());
            int bin = 20 + jsonLength + 8;
            JsonValue primitive = doc["meshes"][0]["primitives"][0];
            JsonValue attributes = primitive["attributes"];

            int count;
            int pos = Accessor(doc, attributes.GetInt("POSITION"), bin, out count, out int posStride, 12);
            int nor = Accessor(doc, attributes.GetInt("NORMAL"), bin, out int nCount, out int norStride, 12);
            int uv = Accessor(doc, attributes.GetInt("TEXCOORD_0"), bin, out int uCount, out int uvStride, 8);
            int col = Accessor(doc, attributes.GetInt("COLOR_0"), bin, out int cCount, out int colStride, 4);
            JsonValue indexAccessor = doc["accessors"][primitive.GetInt("indices")];
            int indexSize = indexAccessor.GetInt("componentType") == 5123 ? 2 : 4;
            int idx = Accessor(doc, primitive.GetInt("indices"), bin, out int iCount, out int idxStride, indexSize);
            Assert.That(nCount == count && uCount == count && cCount == count && iCount % 3 == 0, Is.True);

            var mesh = new MeshData(count, iCount);
            AtlasVertex v;
            v.Uv1 = float2.zero;
            for (int k = 0; k < count; k++)
            {
                int p = pos + k * posStride, n = nor + k * norStride, t = uv + k * uvStride, c = col + k * colStride;
                v.Position = new float3(-F(glb, p), F(glb, p + 4), F(glb, p + 8));
                v.Normal = new float3(-F(glb, n), F(glb, n + 4), F(glb, n + 8));
                v.Uv0 = new float2(F(glb, t), 1f - F(glb, t + 4));
                v.Color = glb[c] | ((uint)glb[c + 1] << 8) | ((uint)glb[c + 2] << 16) | ((uint)glb[c + 3] << 24);
                mesh.AddVertex(in v);
            }
            for (int k = 0; k < iCount; k += 3)
            {
                int a = I(glb, idx + k * idxStride, indexSize);
                int b = I(glb, idx + (k + 1) * idxStride, indexSize);
                int c = I(glb, idx + (k + 2) * idxStride, indexSize);
                mesh.AddTriangle(a, c, b);
            }
            return mesh;
        }

        static int Accessor(JsonValue doc, int index, int bin, out int count, out int stride, int tight)
        {
            JsonValue a = doc["accessors"][index];
            JsonValue view = doc["bufferViews"][a.GetInt("bufferView")];
            count = a.GetInt("count");
            stride = view.GetInt("byteStride", tight);
            return bin + view.GetInt("byteOffset", 0) + a.GetInt("byteOffset", 0);
        }

        static float F(byte[] b, int offset) { return BitConverter.ToSingle(b, offset); }

        static int I(byte[] b, int offset, int size)
        {
            return size == 2 ? BitConverter.ToUInt16(b, offset) : (int)BitConverter.ToUInt32(b, offset);
        }

        // ------------------------------------------------------------------ palette

        /// <summary>The centre of palette cell index (lane A section 10.7): 16 x 16 cells, row 0 at v = 0.</summary>
        public static float2 PaletteUv(int cell)
        {
            return new float2(((cell & 15) + 0.5f) / 16f, (((cell >> 4) & 15) + 0.5f) / 16f);
        }

        /// <summary>The palette cell a uv0 lies in.</summary>
        public static int Cell(float2 uv0)
        {
            int column = math.clamp((int)math.floor(uv0.x * 16f), 0, 15);
            int row = math.clamp((int)math.floor(uv0.y * 16f), 0, 15);
            return row * 16 + column;
        }

        /// <summary>The 8-bit sRGB colour of an object cell (64..127), from its linear colour.</summary>
        public static byte[] CellSrgb(int cell)
        {
            float3 linear = ObjectPaletteColours.Linear(cell);
            return new[] { ToSrgbByte(linear.x), ToSrgbByte(linear.y), ToSrgbByte(linear.z) };
        }

        static byte ToSrgbByte(float c)
        {
            c = math.saturate(c);
            float e = c <= 0.0031308f ? c * 12.92f : 1.055f * math.pow(c, 1f / 2.4f) - 0.055f;
            return (byte)math.round(e * 255f);
        }

        // ------------------------------------------------------------------ mesh helpers

        /// <summary>A new MeshData holding the same vertices and indices.</summary>
        public static MeshData Copy(MeshData m)
        {
            var copy = new MeshData(math.max(m.VertexCount, 4), math.max(m.IndexCount, 6));
            for (int k = 0; k < m.VertexCount; k++) copy.AddVertex(in m.Vertices[k]);
            for (int k = 0; k + 2 < m.IndexCount; k += 3) copy.AddTriangle(m.Indices[k], m.Indices[k + 1], m.Indices[k + 2]);
            return copy;
        }

        /// <summary>Byte-equal vertices (every field) and indices.</summary>
        public static void AssertMeshEqual(MeshData a, MeshData b, string what)
        {
            Assert.That(b.VertexCount, Is.EqualTo(a.VertexCount), what + ": vertex count");
            Assert.That(b.IndexCount, Is.EqualTo(a.IndexCount), what + ": index count");
            for (int k = 0; k < a.VertexCount; k++)
            {
                AtlasVertex x = a.Vertices[k], y = b.Vertices[k];
                bool same = Bits(x.Position).Equals(Bits(y.Position)) && Bits(x.Normal).Equals(Bits(y.Normal))
                            && x.Color == y.Color && Bits(x.Uv0).Equals(Bits(y.Uv0)) && Bits(x.Uv1).Equals(Bits(y.Uv1));
                if (!same) Assert.Fail(what + ": vertex " + k + " differs");
            }
            for (int k = 0; k < a.IndexCount; k++)
                if (a.Indices[k] != b.Indices[k]) Assert.Fail(what + ": index " + k + " differs");
        }

        static int3 Bits(float3 v) { return math.asint(v); }
        static int2 Bits(float2 v) { return math.asint(v); }

        /// <summary>Bounds of the vertices the indices use (not MeshData.BoundsMin/Max, so a writer that fills the
        /// arrays directly is measured the same).</summary>
        public static void UsedBounds(MeshData m, out float3 min, out float3 max)
        {
            min = new float3(float.MaxValue);
            max = new float3(float.MinValue);
            for (int k = 0; k < m.IndexCount; k++)
            {
                float3 p = m.Vertices[m.Indices[k]].Position;
                min = math.min(min, p);
                max = math.max(max, p);
            }
        }

        public static float Area(MeshData m)
        {
            double area = 0;
            for (int k = 0; k + 2 < m.IndexCount; k += 3)
            {
                float3 a = m.Vertices[m.Indices[k]].Position, b = m.Vertices[m.Indices[k + 1]].Position,
                    c = m.Vertices[m.Indices[k + 2]].Position;
                area += 0.5 * math.length(math.cross(b - a, c - a));
            }
            return (float)area;
        }

        /// <summary>Every index within VertexCount and a whole number of triangles.</summary>
        public static void AssertIndicesValid(MeshData m, string what)
        {
            Assert.That(m.IndexCount % 3, Is.EqualTo(0), what + ": whole triangles");
            for (int k = 0; k < m.IndexCount; k++)
                if ((uint)m.Indices[k] >= (uint)m.VertexCount) Assert.Fail(what + ": index " + k + " outside the vertices");
        }

        static PosKey Key(float3 p)
        {
            return new PosKey((long)math.round(p.x * 1e5), (long)math.round(p.y * 1e5), (long)math.round(p.z * 1e5));
        }

        struct PosKey : IEquatable<PosKey>
        {
            public long x, y, z;
            public PosKey(long x, long y, long z) { this.x = x; this.y = y; this.z = z; }
            public bool Equals(PosKey o) { return x == o.x && y == o.y && z == o.z; }
            public override bool Equals(object o) { return o is PosKey l && Equals(l); }
            public override int GetHashCode() { return (int)(x * 73856093 ^ y * 19349663 ^ z * 83492791); }
            public int CompareTo(PosKey o) { return x != o.x ? x.CompareTo(o.x) : y != o.y ? y.CompareTo(o.y) : z.CompareTo(o.z); }
        }

        /// <summary>
        /// Undirected edges, keyed by their end positions (to 0.01 mm, so unwelded copies of a vertex count as
        /// one), that an odd number of triangles use. In a closed mesh this is 0; an edge split on one side and
        /// not the other (a T-junction, a crack in the warp) makes at least one odd edge.
        /// </summary>
        public static int OddEdges(MeshData m)
        {
            var counts = new Dictionary<(PosKey, PosKey), int>();
            for (int k = 0; k + 2 < m.IndexCount; k += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    PosKey a = Key(m.Vertices[m.Indices[k + e]].Position);
                    PosKey b = Key(m.Vertices[m.Indices[k + (e + 1) % 3]].Position);
                    var key = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
                    counts.TryGetValue(key, out int n);
                    counts[key] = n + 1;
                }
            }
            int odd = 0;
            foreach (var n in counts.Values) if ((n & 1) != 0) odd++;
            return odd;
        }

        /// <summary>
        /// Vertices that lie strictly inside an edge of a triangle they are not a corner of (T-junctions), by
        /// position. Quadratic: for small meshes only.
        /// </summary>
        public static int TJunctions(MeshData m)
        {
            int found = 0;
            for (int k = 0; k + 2 < m.IndexCount; k += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    float3 a = m.Vertices[m.Indices[k + e]].Position;
                    float3 b = m.Vertices[m.Indices[k + (e + 1) % 3]].Position;
                    float3 ab = b - a;
                    float len2 = math.lengthsq(ab);
                    if (len2 < 1e-12f) continue;
                    for (int v = 0; v < m.VertexCount; v++)
                    {
                        float3 p = m.Vertices[v].Position;
                        float t = math.dot(p - a, ab) / len2;
                        if (t <= 1e-4f || t >= 1f - 1e-4f) continue;
                        if (math.lengthsq(a + ab * t - p) < 1e-10f) found++;
                    }
                }
            }
            return found;
        }

        /// <summary>
        /// A mesh's float3 positions and indices, for building a ScanMesh from MeshData-made geometry.
        /// </summary>
        public static void Arrays(MeshData m, out float3[] positions, out int[] indices)
        {
            positions = new float3[m.VertexCount];
            for (int k = 0; k < positions.Length; k++) positions[k] = m.Vertices[k].Position;
            indices = new int[m.IndexCount];
            Array.Copy(m.Indices, indices, m.IndexCount);
        }

        /// <summary>A prop with the given slot cells and mesh, for tests that need control over the geometry.</summary>
        public static PropEntry SyntheticProp(string id, ObjectCategory category, float3 size, byte[] slotCells, MeshData mesh,
                                              byte[] profile = null, string[] tags = null)
        {
            return new PropEntry
            {
                Id = id,
                Category = category,
                Tags = tags ?? Array.Empty<string>(),
                Size = size,
                Profile = profile,
                SlotCells = slotCells,
                Mesh = mesh,
            };
        }

        /// <summary>A profile with every cell set to value.</summary>
        public static byte[] FlatProfile(byte value)
        {
            var p = new byte[256];
            for (int k = 0; k < p.Length; k++) p[k] = value;
            return p;
        }
    }
}

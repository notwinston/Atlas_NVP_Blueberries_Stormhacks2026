using System;
using System.Security.Cryptography;
using Atlas.Link;

namespace Atlas.Telemetry
{
    /// <summary>Which copy of an object's mesh a file is: the limits differ (brief S6, design section 5.4).</summary>
    public enum GlbLevel : byte { Full = 0, Light = 1 }

    /// <summary>What a downloaded asset is checked against: the object's box, its triangle target and its hash.</summary>
    public struct GlbExpectation
    {
        /// <summary>RoomModel size [w, h, d], metres.</summary>
        public float DimX, DimY, DimZ;
        /// <summary>The job's target for this copy (3000..15000 full, 1500..5000 light); 0 skips the check.</summary>
        public int TriTarget;
        /// <summary>The copy: full (the default) or light.</summary>
        public GlbLevel Level;
        /// <summary>64 lower-case hexadecimal digits; null or empty skips the check.</summary>
        public string Sha256;
    }

    /// <summary>The validator's answer, with what it measured.</summary>
    public struct GlbReport
    {
        public bool Ok;
        /// <summary>Null when Ok; else the code of the first failed check (lane D section 4.2).</summary>
        public string FailedCheck;
        public int Bytes, Vertices, Triangles;
        /// <summary>Metres.</summary>
        public float MaxEdge;
        public float BoundsX, BoundsY, BoundsZ;
    }

    /// <summary>
    /// The asset contract on the phone (Master Plan sections 6.4 and 7.4; docs/contracts/lane-d-link.md
    /// sections 4.1 and 4.2): checks 1 to 13 on the raw file, before anything is built from it. No Unity API,
    /// so it runs on a worker thread.
    /// </summary>
    public static class GlbValidator
    {
        /// <summary>The full copy: 1,500..15,000 triangles, at most 2 MiB. <see cref="MaxBytes"/> is the larger of the two files.</summary>
        public const int MaxBytes = 2097152, MinTriangles = 1500, MaxTriangles = 15000;
        /// <summary>The light copy: 1,500..5,000 triangles, at most 1 MiB.</summary>
        public const int MaxBytesLight = 1048576, MaxTrianglesLight = 5000;
        public const float MaxEdgeMetres = 0.25f, EdgeSlack = 0.0005f;
        public const float BoundsTolerance = 0.10f, CentreTolerance = 0.05f, BaseTolerance = 0.02f;
        /// <summary>A triangle smaller than this has no area. Square metres.</summary>
        public const double MinTriangleArea = 1e-10;
        /// <summary>A template of the built-in set has at most this many triangles (section 4.4).</summary>
        public const int MaxTemplateTriangles = 300;

        public const string CodeContainer = "container", CodeSize = "size", CodeHash = "hash", CodeStructure = "structure",
            CodeNoMaterial = "no_material", CodeAttributes = "attributes", CodeTrisMin = "tris_min", CodeTrisMax = "tris_max",
            CodeTrisTarget = "tris_target", CodeEdge = "edge", CodeBounds = "bounds", CodeOrigin = "origin", CodeFinite = "finite";

        static readonly string[] s_forbidden =
        {
            "materials", "textures", "images", "samplers", "skins", "animations", "cameras", "extensionsUsed", "extensionsRequired"
        };

        /// <summary>Checks 1 to 13 on a whole file.</summary>
        public static GlbReport Validate(byte[] glb, in GlbExpectation expect)
        {
            return Validate(glb, glb != null ? glb.Length : 0, in expect);
        }

        /// <summary>The largest file of a copy, bytes.</summary>
        public static int MaxBytesOf(GlbLevel level) { return level == GlbLevel.Light ? MaxBytesLight : MaxBytes; }

        /// <summary>The most triangles a copy may have, before the job's own target.</summary>
        public static int MaxTrianglesOf(GlbLevel level) { return level == GlbLevel.Light ? MaxTrianglesLight : MaxTriangles; }

        /// <summary>Checks 1 to 13 on the first <paramref name="length"/> bytes of the array.</summary>
        public static GlbReport Validate(byte[] glb, int length, in GlbExpectation expect)
        {
            var report = new GlbReport { Bytes = length };
            GlbFile file;
            string error;
            if (!GlbFile.TryParse(glb, length, out file, out error)) return Fail(ref report, CodeContainer);
            if (length > MaxBytesOf(expect.Level)) return Fail(ref report, CodeSize);
            if (!string.IsNullOrEmpty(expect.Sha256) && !string.Equals(Sha256Hex(glb, length), expect.Sha256, StringComparison.OrdinalIgnoreCase))
                return Fail(ref report, CodeHash);

            JsonValue primitive;
            if (!CheckStructure(file, out primitive)) return Fail(ref report, CodeStructure);
            if (HasForbidden(file.Json) || primitive.Has("material")) return Fail(ref report, CodeNoMaterial);

            GlbAccessor position, normal, index;
            if (!CheckAttributes(file, primitive, true, out position, out normal, out index)) return Fail(ref report, CodeAttributes);

            Measure(file, in position, in normal, in index, ref report, out bool finite, out float minX, out float minY, out float minZ,
                out float maxX, out float maxY, out float maxZ);

            if (report.Triangles < MinTriangles) return Fail(ref report, CodeTrisMin);
            if (report.Triangles > MaxTrianglesOf(expect.Level)) return Fail(ref report, CodeTrisMax);
            if (expect.TriTarget > 0 && report.Triangles > expect.TriTarget) return Fail(ref report, CodeTrisTarget);
            // A position that is not a number has no edge, bounds or origin to speak of.
            if (!finite) return Fail(ref report, CodeFinite);
            if (!(report.MaxEdge <= MaxEdgeMetres + EdgeSlack)) return Fail(ref report, CodeEdge);

            float longest = Math.Max(expect.DimX, Math.Max(expect.DimY, expect.DimZ));
            if (longest > 0f)
            {
                float allowed = BoundsTolerance * longest;
                if (Math.Abs(report.BoundsX - expect.DimX) > allowed || Math.Abs(report.BoundsY - expect.DimY) > allowed
                    || Math.Abs(report.BoundsZ - expect.DimZ) > allowed)
                    return Fail(ref report, CodeBounds);
            }
            float centre = longest > 0f ? CentreTolerance * longest : BaseTolerance;
            if (Math.Abs(0.5f * (minX + maxX)) > centre || Math.Abs(0.5f * (minZ + maxZ)) > centre || Math.Abs(minY) > BaseTolerance)
                return Fail(ref report, CodeOrigin);

            report.Ok = true;
            return report;
        }

        /// <summary>
        /// The template row of section 4.4: one mesh with POSITION and NORMAL only, no material, at most 300
        /// triangles, filling the unit box (x and z in [-0.5, 0.5], y in [0, 1]) to within a millimetre.
        /// </summary>
        public static GlbReport ValidateTemplate(byte[] glb)
        {
            int length = glb != null ? glb.Length : 0;
            var report = new GlbReport { Bytes = length };
            GlbFile file;
            string error;
            if (!GlbFile.TryParse(glb, length, out file, out error)) return Fail(ref report, CodeContainer);
            JsonValue primitive;
            if (!CheckStructure(file, out primitive)) return Fail(ref report, CodeStructure);
            if (HasForbidden(file.Json) || primitive.Has("material")) return Fail(ref report, CodeNoMaterial);
            GlbAccessor position, normal, index;
            if (!CheckAttributes(file, primitive, false, out position, out normal, out index)) return Fail(ref report, CodeAttributes);

            Measure(file, in position, in normal, in index, ref report, out bool finite, out float minX, out float minY, out float minZ,
                out float maxX, out float maxY, out float maxZ);
            if (report.Triangles < 1) return Fail(ref report, CodeTrisMin);
            if (report.Triangles > MaxTemplateTriangles) return Fail(ref report, CodeTrisMax);
            if (!finite) return Fail(ref report, CodeFinite);
            const float eps = 0.001f;
            if (Math.Abs(report.BoundsX - 1f) > eps || Math.Abs(report.BoundsY - 1f) > eps || Math.Abs(report.BoundsZ - 1f) > eps)
                return Fail(ref report, CodeBounds);
            if (Math.Abs(minX + 0.5f) > eps || Math.Abs(minZ + 0.5f) > eps || Math.Abs(minY) > eps) return Fail(ref report, CodeOrigin);
            report.Ok = true;
            return report;
        }

        /// <summary>SHA-256 of the first <paramref name="length"/> bytes as 64 lower-case hexadecimal digits.</summary>
        public static string Sha256Hex(byte[] bytes, int length)
        {
            byte[] hash;
            using (SHA256 sha = SHA256.Create()) hash = sha.ComputeHash(bytes, 0, length);
            var text = new char[hash.Length * 2];
            const string digits = "0123456789abcdef";
            for (int i = 0; i < hash.Length; i++)
            {
                text[2 * i] = digits[hash[i] >> 4];
                text[2 * i + 1] = digits[hash[i] & 15];
            }
            return new string(text);
        }

        static GlbReport Fail(ref GlbReport report, string code)
        {
            report.Ok = false;
            report.FailedCheck = code;
            return report;
        }

        // Check 4: one scene, one node with no transform, one mesh, one primitive, triangles.
        static bool CheckStructure(GlbFile file, out JsonValue primitive)
        {
            primitive = JsonValue.Null;
            JsonValue json = file.Json;
            JsonValue scenes = json["scenes"], nodes = json["nodes"];
            if (!scenes.IsArray || scenes.Count != 1 || !nodes.IsArray || nodes.Count != 1) return false;
            JsonValue node = nodes[0];
            if (!node.IsObject || node.GetInt("mesh", -1) != 0 || HasTransform(node)) return false;
            if (!file.TryPrimitive(out primitive)) return false;
            return primitive.GetInt("mode", 4) == 4;
        }

        static bool HasTransform(JsonValue node)
        {
            return !Matches(node["translation"], 0f, 0f, 0f, 0f, 3) || !Matches(node["rotation"], 0f, 0f, 0f, 1f, 4)
                || !Matches(node["scale"], 1f, 1f, 1f, 1f, 3) || !IsIdentity(node["matrix"]);
        }

        static bool Matches(JsonValue value, float x, float y, float z, float w, int count)
        {
            if (value.IsNull) return true;
            if (!value.IsArray || value.Count != count) return false;
            const double eps = 1e-6;
            return Math.Abs(value[0].AsDouble() - x) <= eps && Math.Abs(value[1].AsDouble() - y) <= eps
                && Math.Abs(value[2].AsDouble() - z) <= eps && (count < 4 || Math.Abs(value[3].AsDouble() - w) <= eps);
        }

        static bool IsIdentity(JsonValue matrix)
        {
            if (matrix.IsNull) return true;
            if (!matrix.IsArray || matrix.Count != 16) return false;
            for (int i = 0; i < 16; i++)
                if (Math.Abs(matrix[i].AsDouble() - (i % 5 == 0 ? 1.0 : 0.0)) > 1e-6) return false;
            return true;
        }

        // Check 5: nothing the phone would have to interpret.
        static bool HasForbidden(JsonValue json)
        {
            for (int i = 0; i < s_forbidden.Length; i++)
            {
                JsonValue v = json[s_forbidden[i]];
                if (v.IsArray ? v.Count > 0 : !v.IsNull) return true;
            }
            return false;
        }

        // Check 6: exactly the attributes of the contract with their types and equal counts, and indices that
        // describe triangles of this vertex buffer.
        static bool CheckAttributes(GlbFile file, JsonValue primitive, bool asset,
            out GlbAccessor position, out GlbAccessor normal, out GlbAccessor index)
        {
            position = default(GlbAccessor);
            normal = default(GlbAccessor);
            index = default(GlbAccessor);
            JsonValue attributes = primitive["attributes"];
            if (!attributes.IsObject || attributes.Count != (asset ? 4 : 2)) return false;
            if (!file.TryAccessor(attributes.GetInt("POSITION", -1), out position) || !position.Is(GlbAccessor.Float, 3)) return false;
            if (!file.TryAccessor(attributes.GetInt("NORMAL", -1), out normal) || !normal.Is(GlbAccessor.Float, 3)) return false;
            if (normal.Count != position.Count) return false;
            if (asset)
            {
                GlbAccessor uv, colour;
                if (!file.TryAccessor(attributes.GetInt("TEXCOORD_0", -1), out uv) || !uv.Is(GlbAccessor.Float, 2)) return false;
                if (!file.TryAccessor(attributes.GetInt("COLOR_0", -1), out colour) || !colour.Is(GlbAccessor.UnsignedByte, 4) || !colour.Normalized) return false;
                if (uv.Count != position.Count || colour.Count != position.Count) return false;
            }
            if (!file.TryAccessor(primitive.GetInt("indices", -1), out index) || index.Components != 1) return false;
            if (index.ComponentType != GlbAccessor.UnsignedShort && index.ComponentType != GlbAccessor.UnsignedInt) return false;
            if (index.Count % 3 != 0) return false;
            uint vertices = (uint)position.Count;
            for (int k = 0; k < index.Count; k++)
                if (file.Index(in index, k) >= vertices) return false;
            return true;
        }

        // Counts, bounds, the longest edge, and check 13 (finite positions and normals, no triangle without area).
        static void Measure(GlbFile file, in GlbAccessor position, in GlbAccessor normal, in GlbAccessor index, ref GlbReport report,
            out bool finite, out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ)
        {
            report.Vertices = position.Count;
            report.Triangles = index.Count / 3;
            finite = true;
            minX = minY = minZ = float.MaxValue;
            maxX = maxY = maxZ = float.MinValue;
            for (int v = 0; v < position.Count; v++)
            {
                int at = position.Offset + v * position.Stride;
                float x = file.Float(at), y = file.Float(at + 4), z = file.Float(at + 8);
                int nAt = normal.Offset + v * normal.Stride;
                float nx = file.Float(nAt), ny = file.Float(nAt + 4), nz = file.Float(nAt + 8);
                if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z) || !IsFinite(nx) || !IsFinite(ny) || !IsFinite(nz)) { finite = false; continue; }
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
                if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
            }
            if (!finite || position.Count == 0) { finite = false; return; }
            report.BoundsX = maxX - minX;
            report.BoundsY = maxY - minY;
            report.BoundsZ = maxZ - minZ;

            double longestSq = 0.0;
            for (int k = 0; k + 2 < index.Count; k += 3)
            {
                int a = position.Offset + (int)file.Index(in index, k) * position.Stride;
                int b = position.Offset + (int)file.Index(in index, k + 1) * position.Stride;
                int c = position.Offset + (int)file.Index(in index, k + 2) * position.Stride;
                double ax = file.Float(a), ay = file.Float(a + 4), az = file.Float(a + 8);
                double ux = file.Float(b) - ax, uy = file.Float(b + 4) - ay, uz = file.Float(b + 8) - az;
                double vx = file.Float(c) - ax, vy = file.Float(c + 4) - ay, vz = file.Float(c + 8) - az;
                double wx = vx - ux, wy = vy - uy, wz = vz - uz;
                double ab = ux * ux + uy * uy + uz * uz, ac = vx * vx + vy * vy + vz * vz, bc = wx * wx + wy * wy + wz * wz;
                if (ab > longestSq) longestSq = ab;
                if (ac > longestSq) longestSq = ac;
                if (bc > longestSq) longestSq = bc;
                double cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
                if (0.5 * Math.Sqrt(cx * cx + cy * cy + cz * cz) < MinTriangleArea) finite = false;
            }
            report.MaxEdge = (float)Math.Sqrt(longestSq);
        }

        static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}

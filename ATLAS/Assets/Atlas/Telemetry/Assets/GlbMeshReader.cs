using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Telemetry
{
    /// <summary>
    /// Turns a validated Forge asset into mesh data in the look's vertex format (lane A section 10.2), with no
    /// engine call, so the work is done on a worker thread and the frame only uploads the finished buffers
    /// (Master Plan section 7.4: no frame over 20 ms while furniture arrives).
    /// <para>A GLB is right-handed: x is stored negated, v runs downwards and front faces are counter-clockwise.
    /// All three are turned back here, which gives the box-local frame of the contract exactly
    /// (docs/contracts/lane-d-link.md section 4.1).</para>
    /// </summary>
    public static class GlbMeshReader
    {
        /// <summary>
        /// Replaces the contents of <paramref name="into"/> with the mesh of the file. False when the file is not
        /// an asset of the contract; run <see cref="GlbValidator.Validate(byte[], in GlbExpectation)"/> first.
        /// </summary>
        public static bool TryRead(byte[] glb, int length, MeshData into)
        {
            into.Clear();
            GlbFile file;
            string error;
            JsonValue primitive;
            if (!GlbFile.TryParse(glb, length, out file, out error) || !file.TryPrimitive(out primitive)) return false;
            JsonValue attributes = primitive["attributes"];
            GlbAccessor position, normal, uv, colour, index;
            if (!file.TryAccessor(attributes.GetInt("POSITION", -1), out position) || !position.Is(GlbAccessor.Float, 3)) return false;
            if (!file.TryAccessor(attributes.GetInt("NORMAL", -1), out normal) || !normal.Is(GlbAccessor.Float, 3)) return false;
            if (!file.TryAccessor(attributes.GetInt("TEXCOORD_0", -1), out uv) || !uv.Is(GlbAccessor.Float, 2)) return false;
            if (!file.TryAccessor(attributes.GetInt("COLOR_0", -1), out colour) || !colour.Is(GlbAccessor.UnsignedByte, 4)) return false;
            if (!file.TryAccessor(primitive.GetInt("indices", -1), out index) || index.Components != 1) return false;
            if (index.ComponentType != GlbAccessor.UnsignedShort && index.ComponentType != GlbAccessor.UnsignedInt) return false;
            int count = position.Count;
            if (normal.Count != count || uv.Count != count || colour.Count != count || index.Count % 3 != 0) return false;

            into.Reserve(count, index.Count);
            byte[] bytes = file.Bytes;
            AtlasVertex vertex;
            vertex.Uv1 = float2.zero;                       // furniture carries no line coordinates
            for (int v = 0; v < count; v++)
            {
                int p = position.Offset + v * position.Stride;
                int n = normal.Offset + v * normal.Stride;
                int t = uv.Offset + v * uv.Stride;
                int c = colour.Offset + v * colour.Stride;
                vertex.Position = new float3(-file.Float(p), file.Float(p + 4), file.Float(p + 8));
                vertex.Normal = new float3(-file.Float(n), file.Float(n + 4), file.Float(n + 8));
                vertex.Uv0 = new float2(file.Float(t), 1f - file.Float(t + 4));
                vertex.Color = bytes[c] | ((uint)bytes[c + 1] << 8) | ((uint)bytes[c + 2] << 16) | ((uint)bytes[c + 3] << 24);
                into.AddVertex(in vertex);
            }
            for (int k = 0; k + 2 < index.Count; k += 3)
            {
                uint a = file.Index(in index, k), b = file.Index(in index, k + 1), c = file.Index(in index, k + 2);
                if (a >= (uint)count || b >= (uint)count || c >= (uint)count) { into.Clear(); return false; }
                into.AddTriangle((int)a, (int)c, (int)b);   // mirrored in x, so the winding turns
            }
            return into.TriangleCount > 0;
        }
    }
}

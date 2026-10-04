using System;
using Atlas.Link;

namespace Atlas.Telemetry
{
    /// <summary>One accessor of a GLB, resolved to a byte range of the file.</summary>
    public struct GlbAccessor
    {
        public const int Byte = 5120, UnsignedByte = 5121, Short = 5122, UnsignedShort = 5123, UnsignedInt = 5125, Float = 5126;

        /// <summary>Offset of the first element from the start of the file.</summary>
        public int Offset;
        /// <summary>Bytes from one element to the next.</summary>
        public int Stride;
        public int Count;
        public int ComponentType;
        /// <summary>1 (SCALAR) to 4 (VEC4); 0 for a type this reader does not use.</summary>
        public int Components;
        public bool Normalized;

        public bool Is(int componentType, int components)
        {
            return ComponentType == componentType && Components == components;
        }
    }

    /// <summary>
    /// A parsed GLB container: the JSON document and where the binary chunk lies in the file
    /// (docs/contracts/lane-d-link.md section 4.1). Engine-free, so the validator and the mesh readers run on a
    /// worker thread and in headless tests. Little-endian hosts only, which every ATLAS target is.
    /// </summary>
    public sealed class GlbFile
    {
        const uint Magic = 0x46546C67, JsonChunk = 0x4E4F534A, BinChunk = 0x004E4942;

        public byte[] Bytes { get; private set; }
        public int Length { get; private set; }
        public JsonValue Json { get; private set; }
        public int BinOffset { get; private set; }
        public int BinLength { get; private set; }

        GlbFile() { }

        /// <summary>
        /// Reads the header and the two chunks. False with a reason when the bytes are not a glTF 2.0 binary
        /// file of one JSON chunk followed by one BIN chunk whose declared length is the file's length.
        /// </summary>
        public static bool TryParse(byte[] bytes, int length, out GlbFile file, out string error)
        {
            file = null;
            if (bytes == null || length < 28 || length > bytes.Length) { error = "too short"; return false; }
            if (ReadU32(bytes, 0) != Magic) { error = "no glTF magic"; return false; }
            if (ReadU32(bytes, 4) != 2u) { error = "not version 2"; return false; }
            if (ReadU32(bytes, 8) != (uint)length) { error = "declared length differs from the file length"; return false; }

            uint jsonLength = ReadU32(bytes, 12);
            if (ReadU32(bytes, 16) != JsonChunk || jsonLength > (uint)(length - 20)) { error = "no JSON chunk"; return false; }
            int binHeader = 20 + (int)jsonLength;
            if (binHeader + 8 > length) { error = "no BIN chunk"; return false; }
            uint binLength = ReadU32(bytes, binHeader);
            if (ReadU32(bytes, binHeader + 4) != BinChunk || binLength > (uint)(length - binHeader - 8)) { error = "no BIN chunk"; return false; }
            if (binHeader + 8 + (int)binLength != length) { error = "data after the BIN chunk"; return false; }

            JsonValue json;
            string jsonError;
            if (!AtlasJson.TryParseUtf8(bytes, 20, (int)jsonLength, true, out json, out jsonError) || !json.IsObject)
            {
                error = "JSON chunk cannot be parsed";
                return false;
            }
            file = new GlbFile { Bytes = bytes, Length = length, Json = json, BinOffset = binHeader + 8, BinLength = (int)binLength };
            error = null;
            return true;
        }

        /// <summary>Resolves accessor <paramref name="index"/>. False when it is missing, sparse, or runs past the buffer.</summary>
        public bool TryAccessor(int index, out GlbAccessor accessor)
        {
            accessor = default(GlbAccessor);
            JsonValue accessors = Json["accessors"];
            if (!accessors.IsArray || index < 0 || index >= accessors.Count) return false;
            JsonValue a = accessors[index];
            if (!a.IsObject || a.Has("sparse") || !a["bufferView"].IsNumber) return false;
            JsonValue views = Json["bufferViews"];
            int viewIndex = a.GetInt("bufferView", -1);
            if (!views.IsArray || viewIndex < 0 || viewIndex >= views.Count) return false;
            JsonValue view = views[viewIndex];
            if (!view.IsObject || view.GetInt("buffer") != 0) return false;

            int componentType = a.GetInt("componentType");
            int componentBytes = ComponentBytes(componentType);
            int components = Components(a.GetString("type", ""));
            long count = a.GetLong("count");
            if (componentBytes == 0 || components == 0 || count <= 0 || count > int.MaxValue / 64) return false;

            int element = componentBytes * components;
            long stride = view.GetLong("byteStride");
            if (stride == 0) stride = element;
            long start = view.GetLong("byteOffset") + a.GetLong("byteOffset");
            long viewEnd = view.GetLong("byteOffset") + view.GetLong("byteLength");
            long end = start + (count - 1) * stride + element;
            if (stride < element || start < 0 || end > viewEnd || viewEnd > BinLength) return false;

            accessor.Offset = BinOffset + (int)start;
            accessor.Stride = (int)stride;
            accessor.Count = (int)count;
            accessor.ComponentType = componentType;
            accessor.Components = components;
            accessor.Normalized = a.GetBool("normalized");
            return true;
        }

        /// <summary>The one primitive of the one mesh, or false when the file has another shape.</summary>
        public bool TryPrimitive(out JsonValue primitive)
        {
            primitive = JsonValue.Null;
            JsonValue meshes = Json["meshes"];
            if (!meshes.IsArray || meshes.Count != 1) return false;
            JsonValue primitives = meshes[0]["primitives"];
            if (!primitives.IsArray || primitives.Count != 1 || !primitives[0].IsObject) return false;
            primitive = primitives[0];
            return true;
        }

        public float Float(int offset) { return BitConverter.ToSingle(Bytes, offset); }

        /// <summary>Index k of an UNSIGNED_SHORT or UNSIGNED_INT accessor.</summary>
        public uint Index(in GlbAccessor accessor, int k)
        {
            int at = accessor.Offset + k * accessor.Stride;
            return accessor.ComponentType == GlbAccessor.UnsignedShort ? BitConverter.ToUInt16(Bytes, at) : BitConverter.ToUInt32(Bytes, at);
        }

        static uint ReadU32(byte[] bytes, int offset) { return BitConverter.ToUInt32(bytes, offset); }

        static int ComponentBytes(int componentType)
        {
            switch (componentType)
            {
                case GlbAccessor.Byte:
                case GlbAccessor.UnsignedByte: return 1;
                case GlbAccessor.Short:
                case GlbAccessor.UnsignedShort: return 2;
                case GlbAccessor.UnsignedInt:
                case GlbAccessor.Float: return 4;
                default: return 0;
            }
        }

        static int Components(string type)
        {
            switch (type)
            {
                case "SCALAR": return 1;
                case "VEC2": return 2;
                case "VEC3": return 3;
                case "VEC4": return 4;
                default: return 0;
            }
        }
    }
}

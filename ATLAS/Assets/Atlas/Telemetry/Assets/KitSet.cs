using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Telemetry
{
    /// <summary>
    /// The kit pieces modelled in Blender (kit hero assets design, 2026-10-03): GLBs in the look's format that
    /// tools/kit/export_kit.py writes, with a manifest of their triangle counts and SHA-256 hashes. A space asks
    /// for a piece by name and builds its own procedural mesh when the piece is refused. Engine-free: the caller
    /// says how a file is read (Resources on the phone, the disk in tests).
    /// </summary>
    /// <remarks>
    /// A piece is read with <see cref="GlbMeshReader.TryRead"/>, so it lands in Unity's frame exactly as a
    /// Forge asset does. The reader leaves the line coordinates at zero; they are read here from TEXCOORD_1,
    /// which the file stores with v downwards like TEXCOORD_0.
    /// </remarks>
    public sealed class KitSet
    {
        public const string ResourceFolder = "AtlasKit";
        public const string ManifestName = "manifest";

        sealed class Piece
        {
            public byte[] Glb;
            public int Triangles;
            /// <summary>Why the piece is refused, or null.</summary>
            public string Error;
        }

        readonly Dictionary<string, Piece> _pieces = new Dictionary<string, Piece>();

        /// <summary>Pieces whose file was read and matches its hash.</summary>
        public int Count { get; private set; }
        /// <summary>Why the manifest or a piece is missing, or null.</summary>
        public string LastError { get; private set; }

        /// <summary>
        /// Reads the manifest and every piece it names, and checks each file against its hash.
        /// </summary>
        /// <param name="read">Gives the bytes of "manifest" or "&lt;piece&gt;.glb", or null when there is no such file.</param>
        public static KitSet Load(Func<string, byte[]> read)
        {
            var set = new KitSet();
            if (read == null) { set.LastError = "no reader for the kit"; return set; }
            byte[] manifestBytes = read(ManifestName);
            JsonValue manifest;
            string error;
            if (manifestBytes == null || !AtlasJson.TryParseUtf8(manifestBytes, 0, manifestBytes.Length, true, out manifest, out error)
                || !manifest["pieces"].IsObject)
            {
                set.LastError = "the kit manifest is missing or unreadable";
                return set;
            }
            JsonValue pieces = manifest["pieces"];
            for (int i = 0; i < pieces.Count; i++)
            {
                string name = pieces.KeyAt(i);
                JsonValue entry = pieces[name];
                var piece = new Piece { Glb = read(name + ".glb"), Triangles = entry.GetInt("triangles", -1) };
                if (piece.Glb == null) piece.Error = "kit piece '" + name + "': the file is missing";
                else if (GlbValidator.Sha256Hex(piece.Glb, piece.Glb.Length) != entry.GetString("sha256", ""))
                    piece.Error = "kit piece '" + name + "': the file's hash differs from the manifest";
                if (piece.Error != null) set.LastError = piece.Error;
                else set.Count++;
                set._pieces[name] = piece;
            }
            return set;
        }

        /// <summary>
        /// Replaces the contents of <paramref name="into"/> with the piece, in Unity's frame with its line
        /// coordinates. False with a reason when the piece is unknown, missing, altered or unreadable; the mesh
        /// is then left empty. Never throws.
        /// </summary>
        public bool TryGet(string name, MeshData into, out string error)
        {
            if (into == null) { error = "no mesh to read the kit piece into"; return false; }
            into.Clear();
            Piece piece;
            if (name == null || !_pieces.TryGetValue(name, out piece))
            {
                error = _pieces.Count == 0 && LastError != null ? LastError : "kit piece '" + name + "' is not in the manifest";
                return false;
            }
            if (piece.Error != null) { error = piece.Error; return false; }
            if (!GlbMeshReader.TryRead(piece.Glb, piece.Glb.Length, into) || !TryReadLines(piece.Glb, into))
            {
                into.Clear();
                error = "kit piece '" + name + "' cannot be read as a mesh of the look";
                return false;
            }
            if (into.TriangleCount != piece.Triangles)
            {
                error = "kit piece '" + name + "' has " + into.TriangleCount + " triangles; the manifest says " + piece.Triangles;
                into.Clear();
                return false;
            }
            error = null;
            return true;
        }

        // TEXCOORD_1 into Uv1, one per vertex the mesh reader added. A piece without it keeps zero: no outlines.
        static bool TryReadLines(byte[] glb, MeshData into)
        {
            GlbFile file;
            string error;
            JsonValue primitive;
            if (!GlbFile.TryParse(glb, glb.Length, out file, out error) || !file.TryPrimitive(out primitive)) return false;
            JsonValue attributes = primitive["attributes"];
            if (!attributes.Has("TEXCOORD_1")) return true;
            GlbAccessor lines;
            if (!file.TryAccessor(attributes.GetInt("TEXCOORD_1", -1), out lines) || !lines.Is(GlbAccessor.Float, 2)
                || lines.Count != into.VertexCount) return false;
            AtlasVertex[] vertices = into.Vertices;
            for (int v = 0; v < into.VertexCount; v++)
            {
                int t = lines.Offset + v * lines.Stride;
                vertices[v].Uv1 = new float2(file.Float(t), 1f - file.Float(t + 4));
            }
            return true;
        }
    }
}

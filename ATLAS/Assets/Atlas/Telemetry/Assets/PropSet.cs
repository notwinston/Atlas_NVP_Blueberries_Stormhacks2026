using System;
using System.Collections.Generic;
using Atlas.Link;
using Atlas.Objects;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Telemetry
{
    /// <summary>
    /// The prop library of the on-phone object path (standalone-phone plan D4, D5 and section 6.3): the manifest
    /// and the GLBs in Resources/AtlasProps, each checked against its SHA-256 and triangle count and read into a
    /// <see cref="PropEntry"/> in Unity's box-local frame. Engine-free: the caller says how a file is read
    /// (Resources on the phone, the disk in tests), as for <see cref="KitSet"/>.
    /// </summary>
    /// <remarks>
    /// A prop that breaks a rule is left out and the rest are kept; <see cref="Error"/> says why the first one was
    /// refused. A missing manifest gives an empty library: every object then keeps its first shape. The entries
    /// are read-only once loaded, so worker threads share them.
    /// </remarks>
    public sealed class PropSet
    {
        public const string ResourceFolder = "AtlasProps";
        public const string ManifestName = "manifest";
        public const string Schema = "atlas.props/1";

        /// <summary>A profile holds 16 x 16 bytes (RoomObject.Profile).</summary>
        public const int ProfileBytes = 256;
        /// <summary>A prop has 1 to <see cref="RoomObject.MaxPropCells"/> colour slots, each a distinct cell 64..127.</summary>
        public const int MaxSlots = RoomObject.MaxPropCells;

        static readonly PropEntry[] s_none = new PropEntry[0];

        PropSet(PropEntry[] props, string error)
        {
            Props = props;
            Error = error;
        }

        /// <summary>The props that passed every check, in manifest order. Never null; empty when none did.</summary>
        public PropEntry[] Props { get; }
        /// <summary>Why the manifest or a prop was refused (the first problem, and how many props were left out), or null.</summary>
        public string Error { get; }
        /// <summary>The number of props loaded.</summary>
        public int Count { get { return Props.Length; } }

        /// <summary>A library with no prop: every object keeps its first shape.</summary>
        public static PropSet Empty(string error = null) { return new PropSet(s_none, error); }

        /// <summary>The id of the k-th prop, in manifest order.</summary>
        public string IdAt(int index) { return Props[index].Id; }

        /// <summary>The index of the prop with this id (ordinal), or -1.</summary>
        public int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < Props.Length; i++) if (string.Equals(Props[i].Id, id, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>
        /// Reads the manifest and every prop it names. Never throws for bad content: a prop that is missing, altered,
        /// unreadable or outside the format is left out.
        /// </summary>
        /// <param name="read">Gives the bytes of "manifest" or "&lt;id&gt;.glb", or null when there is no such file.</param>
        public static PropSet Load(Func<string, byte[]> read)
        {
            if (read == null) return Empty("no reader for the props");
            byte[] manifestBytes = read(ManifestName);
            JsonValue manifest;
            string error;
            if (manifestBytes == null || !AtlasJson.TryParseUtf8(manifestBytes, 0, manifestBytes.Length, true, out manifest, out error)
                || !manifest.IsObject)
                return Empty("the prop manifest is missing or unreadable");
            if (manifest.GetString("schema", "") != Schema) return Empty("the prop manifest is not " + Schema);
            JsonValue list = manifest["props"];
            if (!list.IsArray) return Empty("the prop manifest has no props");

            var props = new List<PropEntry>(list.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            string first = null;
            int refused = 0;
            for (int i = 0; i < list.Count; i++)
            {
                PropEntry entry;
                string problem = TryReadProp(list[i], read, ids, out entry);
                if (problem == null)
                {
                    props.Add(entry);
                    ids.Add(entry.Id);
                    continue;
                }
                refused++;
                if (first == null) first = problem;
            }
            string summary = first == null ? null
                : refused == 1 ? first
                : first + " (and " + (refused - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " more props refused)";
            return new PropSet(props.ToArray(), summary);
        }

        // Null when the prop is good; otherwise why it is refused.
        static string TryReadProp(JsonValue p, Func<string, byte[]> read, HashSet<string> ids, out PropEntry entry)
        {
            entry = null;
            if (!p.IsObject) return "a prop entry is not an object";
            string id = p.GetString("id", "");
            if (!IsId(id)) return "a prop has no usable id";
            string name = "prop '" + id + "'";
            if (ids.Contains(id)) return name + ": the id is used twice";

            ObjectCategory category;
            if (!ObjectCategories.TryParse(p.GetString("category", ""), out category)) return name + ": unknown category";

            float3 size;
            if (!TryFloat3(p["size"], out size)) return name + ": the size is not three positive numbers";

            string[] tags = Array.Empty<string>();
            JsonValue tagList = p["tags"];
            if (!tagList.IsNull)
            {
                if (!tagList.IsArray) return name + ": tags is not a list";
                tags = new string[tagList.Count];
                for (int t = 0; t < tags.Length; t++)
                {
                    tags[t] = tagList[t].AsString();
                    if (tags[t] == null) return name + ": a tag is not a string";
                }
            }

            JsonValue slotList = p["slots"];
            if (!slotList.IsArray || slotList.Count < 1 || slotList.Count > MaxSlots) return name + ": needs 1 to " + MaxSlots + " colour slots";
            var cells = new byte[slotList.Count];
            for (int s = 0; s < cells.Length; s++)
            {
                JsonValue slot = slotList[s];
                int cell = slot.IsObject && slot["cell"].IsInteger ? slot.GetInt("cell", -1) : -1;
                if (cell < RoomObject.FirstPropCell || cell > RoomObject.LastPropCell) return name + ": a slot cell is outside 64..127";
                for (int k = 0; k < s; k++) if (cells[k] == cell) return name + ": two slots share a cell";
                cells[s] = (byte)cell;
            }

            byte[] profile = null;
            JsonValue profileText = p["profile"];
            if (!profileText.IsNull)
            {
                try { profile = Convert.FromBase64String(profileText.AsString() ?? ""); }
                catch (FormatException) { return name + ": the profile is not base64"; }
                if (profile.Length != ProfileBytes) return name + ": the profile is not 256 bytes";
            }

            int triangles = p["triangles"].IsInteger ? p.GetInt("triangles", -1) : -1;
            string sha = p.GetString("sha256", "");
            byte[] glb = read(id + ".glb");
            if (glb == null) return name + ": the file is missing";
            if (!string.Equals(GlbValidator.Sha256Hex(glb, glb.Length), sha, StringComparison.OrdinalIgnoreCase))
                return name + ": the file's hash differs from the manifest";
            var mesh = new MeshData(1024, 4096);
            if (!GlbMeshReader.TryRead(glb, glb.Length, mesh)) return name + ": the file cannot be read as a mesh of the look";
            if (mesh.TriangleCount != triangles)
                return name + ": has " + mesh.TriangleCount + " triangles; the manifest says " + triangles;

            entry = new PropEntry
            {
                Id = id, Category = category, Tags = tags, Size = size, Profile = profile, SlotCells = cells, Mesh = mesh
            };
            return null;
        }

        // An id is a file name and goes into a RoomModel's "prop": letters, digits, '_', '-' and '.', at most 64.
        static bool IsId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > RoomObject.MaxPropLength) return false;
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.';
                if (!ok) return false;
            }
            return id[0] != '.';
        }

        static bool TryFloat3(JsonValue a, out float3 value)
        {
            value = float3.zero;
            if (!a.IsArray || a.Count != 3) return false;
            for (int k = 0; k < 3; k++)
            {
                if (!a[k].IsNumber) return false;
                float v = a[k].AsFloat();
                if (!(v > 0f) || float.IsInfinity(v)) return false;
                value[k] = v;
            }
            return true;
        }
    }
}

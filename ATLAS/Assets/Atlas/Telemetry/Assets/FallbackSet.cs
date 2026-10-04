using System;
using Atlas.Link;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Telemetry
{
    /// <summary>One template of the built-in set: a mesh that fills the unit box, in the box-local frame.</summary>
    public sealed class FallbackTemplate
    {
        public string Name;
        /// <summary>Object colour of the palette, 64..127.</summary>
        public int PaletteCell;
        /// <summary>x and z in [-0.5, 0.5], y in [0, 1].</summary>
        public float3[] Positions;
        public float3[] Normals;
        /// <summary>Clockwise seen from the front (Unity's rule).</summary>
        public int[] Indices;

        public int TriangleCount { get { return Indices.Length / 3; } }
    }

    /// <summary>
    /// The built-in fallback set (Master Plan section 4.5, task P1-D12; docs/contracts/lane-d-link.md
    /// section 4.4): one template per object category, read from the files that tools/gen_fallback_set.py
    /// writes. Engine-free: the caller says how a file is read (Resources on the phone, the disk in tests).
    /// </summary>
    public sealed class FallbackSet
    {
        public const string ResourceFolder = "AtlasFallback";
        public const string ManifestName = "manifest";

        readonly FallbackTemplate[] _templates = new FallbackTemplate[ObjectCategories.Count];

        /// <summary>Templates that were read.</summary>
        public int Count { get; private set; }
        /// <summary>Why a template is missing, or null.</summary>
        public string LastError { get; private set; }

        /// <summary>
        /// Reads the manifest and every template it names.
        /// </summary>
        /// <param name="read">Gives the bytes of "manifest" or "&lt;category&gt;.glb", or null when there is no such file.</param>
        public static FallbackSet Load(Func<string, byte[]> read)
        {
            var set = new FallbackSet();
            if (read == null) { set.LastError = "no reader"; return set; }
            byte[] manifestBytes = read(ManifestName);
            JsonValue manifest;
            string error;
            if (manifestBytes == null || !AtlasJson.TryParseUtf8(manifestBytes, 0, manifestBytes.Length, true, out manifest, out error)
                || !manifest["templates"].IsObject)
            {
                set.LastError = "the manifest of the built-in set is missing or unreadable";
                return set;
            }
            JsonValue templates = manifest["templates"];
            for (int i = 0; i < templates.Count; i++)
            {
                string name = templates.KeyAt(i);
                ObjectCategory category;
                if (!ObjectCategories.TryParse(name, out category) || !HasTemplate(category))
                {
                    set.LastError = "unknown category in the manifest: " + name;
                    continue;
                }
                int cell = templates[name].GetInt("palette", 0);
                byte[] glb = read(name + ".glb");
                FallbackTemplate template;
                if (glb == null || cell < 64 || cell > 127 || !TryReadTemplate(glb, out template))
                {
                    set.LastError = "template cannot be read: " + name;
                    continue;
                }
                template.Name = name;
                template.PaletteCell = cell;
                if (set._templates[(int)category] == null) set.Count++;
                set._templates[(int)category] = template;
            }
            return set;
        }

        /// <summary>Every category except Unknown and Other has a template; those two keep their placeholder box.</summary>
        public static bool HasTemplate(ObjectCategory category)
        {
            return category != ObjectCategory.Unknown && category != ObjectCategory.Other && (int)category < ObjectCategories.Count;
        }

        /// <summary>The template of a category, or null.</summary>
        public FallbackTemplate Get(ObjectCategory category)
        {
            int i = (int)category;
            return i < _templates.Length ? _templates[i] : null;
        }

        /// <summary>Reads one template file: POSITION, NORMAL and indices, turned into the box-local frame.</summary>
        public static bool TryReadTemplate(byte[] glb, out FallbackTemplate template)
        {
            template = null;
            if (!GlbValidator.ValidateTemplate(glb).Ok) return false;
            GlbFile file;
            string error;
            JsonValue primitive;
            if (!GlbFile.TryParse(glb, glb.Length, out file, out error) || !file.TryPrimitive(out primitive)) return false;
            GlbAccessor position, normal, index;
            JsonValue attributes = primitive["attributes"];
            if (!file.TryAccessor(attributes.GetInt("POSITION", -1), out position)) return false;
            if (!file.TryAccessor(attributes.GetInt("NORMAL", -1), out normal)) return false;
            if (!file.TryAccessor(primitive.GetInt("indices", -1), out index)) return false;

            var t = new FallbackTemplate
            {
                Positions = new float3[position.Count],
                Normals = new float3[position.Count],
                Indices = new int[index.Count]
            };
            for (int v = 0; v < position.Count; v++)
            {
                int p = position.Offset + v * position.Stride;
                int n = normal.Offset + v * normal.Stride;
                // Right-handed in the file: x comes back negated.
                t.Positions[v] = new float3(-file.Float(p), file.Float(p + 4), file.Float(p + 8));
                t.Normals[v] = new float3(-file.Float(n), file.Float(n + 4), file.Float(n + 8));
            }
            for (int k = 0; k + 2 < index.Count; k += 3)
            {
                t.Indices[k] = (int)file.Index(in index, k);
                t.Indices[k + 1] = (int)file.Index(in index, k + 2);
                t.Indices[k + 2] = (int)file.Index(in index, k + 1);
            }
            template = t;
            return true;
        }
    }
}

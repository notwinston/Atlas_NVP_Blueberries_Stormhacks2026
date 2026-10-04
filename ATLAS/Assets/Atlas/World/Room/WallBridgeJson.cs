using System;
using System.Collections.Generic;
using Atlas.Link;
using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>What RoomPlan calls a surface of the room.</summary>
    public enum BridgeSurfaceKind : byte { Wall = 0, Door = 1, Window = 2, Opening = 3 }

    /// <summary>
    /// One upright rectangle of a RoomPlan capture (a wall, or a door, window or plain opening in one), in the
    /// AR session frame as Unity has it: metres, +Y up, left-handed.
    /// </summary>
    public struct BridgeSurface
    {
        public BridgeSurfaceKind Kind;
        /// <summary>Centre of the rectangle.</summary>
        public float3 Centre;
        /// <summary>Unit floor direction (x, z) the width runs along. Its sign carries no meaning.</summary>
        public float2 Along;
        /// <summary>Metres along the floor.</summary>
        public float Width;
        /// <summary>Metres, bottom edge to top edge.</summary>
        public float Height;
    }

    /// <summary>
    /// Reader of the file the RoomPlan wall bridge writes (scan route 4, Master Plan section 4.2, task
    /// P1-C05; the writer is Assets/Plugins/iOS/AtlasRoomPlan.swift). Engine-free.
    /// <para>The file, in ARKit's own world frame (metres, +Y up, right-handed):</para>
    /// <para>{"schema": "atlas.walls/1", "walls": [S, ...], "doors": [S, ...], "windows": [S, ...],
    /// "openings": [S, ...]} with S = {"c": [x, y, z], "x": [x, y, z], "w": width, "h": height}: c is the
    /// centre of the surface, x the direction its width runs along (the first column of RoomPlan's
    /// transform), w and h its dimensions. A list may be missing; unknown keys are ignored.</para>
    /// <para>The reader turns the frame into Unity's session frame, which is ARKit's with z negated.</para>
    /// </summary>
    public static class WallBridgeJson
    {
        public const string Schema = "atlas.walls/1";
        /// <summary>More surfaces than this in one list is not a room.</summary>
        public const int MaxSurfaces = 256;
        /// <summary>A narrower, lower, wider or higher surface is a capture artefact, metres.</summary>
        public const float MinSide = 0.05f, MaxWidth = 30f, MaxHeight = 10f;
        /// <summary>A surface whose width axis leans further out of the floor plane than this is not upright (cosine).</summary>
        public const float MinLevel = 0.9f;

        static readonly string[] s_lists = { "walls", "doors", "windows", "openings" };

        /// <summary>
        /// The usable surfaces of a file, walls first. False (and no surface) when the text is empty, is not
        /// JSON or is of another schema. A surface that is not usable (a number missing or not finite, a
        /// side out of range, not upright) is left out and counted in skipped.
        /// </summary>
        public static bool TryParse(string json, out BridgeSurface[] surfaces, out int skipped, out string error)
        {
            surfaces = Array.Empty<BridgeSurface>();
            skipped = 0;
            if (string.IsNullOrEmpty(json))
            {
                error = "empty";
                return false;
            }
            JsonValue doc;
            if (!AtlasJson.TryParse(json, out doc, out error)) return false;
            if (!doc.IsObject || doc.GetString("schema", "") != Schema)
            {
                error = "schema: this reader supports " + Schema + " only";
                return false;
            }
            var found = new List<BridgeSurface>(16);
            for (int list = 0; list < s_lists.Length; list++)
            {
                JsonValue items;
                if (!doc.TryGet(s_lists[list], out items) || items.IsNull) continue;
                if (!items.IsArray || items.Count > MaxSurfaces)
                {
                    error = s_lists[list] + ": expected an array of at most 256 surfaces";
                    return false;
                }
                for (int k = 0; k < items.Count; k++)
                {
                    BridgeSurface surface;
                    if (TrySurface(items[k], (BridgeSurfaceKind)list, out surface)) found.Add(surface);
                    else skipped++;
                }
            }
            surfaces = found.ToArray();
            error = null;
            return true;
        }

        static bool TrySurface(JsonValue v, BridgeSurfaceKind kind, out BridgeSurface surface)
        {
            surface = default(BridgeSurface);
            if (v == null || !v.IsObject) return false;
            float3 centre, axis;
            if (!TryVector(v["c"], out centre) || !TryVector(v["x"], out axis)) return false;
            JsonValue w = v["w"], h = v["h"];
            if (w == null || h == null || !w.IsNumber || !h.IsNumber) return false;
            float width = w.AsFloat(), height = h.AsFloat();
            if (!(width >= MinSide && width <= MaxWidth) || !(height >= MinSide && height <= MaxHeight)) return false;
            float length = math.length(axis);
            if (!(length > 1e-6f)) return false;
            // ARKit's frame to Unity's session frame: z changes sign.
            var along = new float2(axis.x, -axis.z);
            float level = math.length(along);
            if (!(level >= MinLevel * length)) return false;
            surface = new BridgeSurface
            {
                Kind = kind,
                Centre = new float3(centre.x, centre.y, -centre.z),
                Along = along / level,
                Width = width,
                Height = height,
            };
            return true;
        }

        static bool TryVector(JsonValue v, out float3 vector)
        {
            vector = float3.zero;
            if (v == null || !v.IsArray || v.Count != 3) return false;
            for (int k = 0; k < 3; k++)
            {
                if (!v[k].IsNumber) return false;
                double d = v[k].AsDouble();
                if (double.IsNaN(d) || Math.Abs(d) > 1000.0) return false;
                vector[k] = (float)d;
            }
            return true;
        }
    }
}

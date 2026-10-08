using System;
using System.Collections.Generic;
using Atlas.Link;
using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>One RoomPlan object (CapturedRoom.Object) in Unity's AR session frame: metres, +Y up, left-handed.</summary>
    public struct RoomPlanObject
    {
        /// <summary>RoomPlan's identifier (a UUID as text), or "".</summary>
        public string Id;
        /// <summary>RoomPlan's category name, verbatim (bathtub, bed, ... sofa, television, washerDryer, or a name this
        /// build does not know); ObjectCategories.FromRoomPlan turns it into ours. "" when the file had none.</summary>
        public string Category;
        /// <summary>Centre of the box.</summary>
        public float3 Centre;
        /// <summary>The box's turn: local +X, +Y and +Z are its axes, as AR Foundation converts an ARKit pose.</summary>
        public quaternion Rotation;
        /// <summary>Full size along the box's own X, Y and Z (RoomPlan's dimensions).</summary>
        public float3 Size;
        /// <summary>RoomPlan's attribute strings, verbatim. Never null; may be empty.</summary>
        public string[] Attributes;
    }

    /// <summary>What one atlas.room/1 file holds, in Unity's AR session frame. Every array is never null.
    /// The surfaces are BridgeSurface (declared with WallBridgeJson), so WallBridgeMerge takes them as they are.</summary>
    public sealed class RoomPlanCapture
    {
        /// <summary>Upright rectangles, each with the Kind of its list.</summary>
        public BridgeSurface[] Walls = Array.Empty<BridgeSurface>();
        public BridgeSurface[] Doors = Array.Empty<BridgeSurface>();
        public BridgeSurface[] Windows = Array.Empty<BridgeSurface>();
        public BridgeSurface[] Openings = Array.Empty<BridgeSurface>();
        public RoomPlanObject[] Objects = Array.Empty<RoomPlanObject>();
        /// <summary>RoomPlan's last coaching instruction, verbatim: normal, moveCloseToWall, moveAwayFromWall,
        /// slowDown, turnOnLight or lowTexture. "" when the file had none.</summary>
        public string Instruction = "";
        /// <summary>Surfaces and objects left out because they were not usable (a number missing or not finite, a
        /// side out of range, a surface not upright, box axes that are zero or parallel).</summary>
        public int Skipped;
    }

    /// <summary>
    /// Reader of the file the RoomPlan plugin writes (standalone-phone plan section 6.2; the writer is
    /// Assets/Plugins/iOS/AtlasRoomPlan.swift). Replaces atlas.walls/1 (WallBridgeJson). Engine-free, thread-safe.
    /// <para>The file, in ARKit's own world frame (metres, +Y up, right-handed):</para>
    /// <para>{"schema": "atlas.room/1", "walls": [S], "doors": [S], "windows": [S], "openings": [S], "objects": [O],
    /// "instruction": "..."} with S = {"c": [x, y, z], "x": [x, y, z], "w": w, "h": h} (as atlas.walls/1) and
    /// O = {"id": "...", "cat": "...", "c": [x, y, z], "x": [x, y, z], "z": [x, y, z], "d": [w, h, d],
    /// "conf": "low|medium|high", "attrs": ["..."]}: c is the centre of the box (column 3 of its transform), x and z
    /// its axis columns 0 and 2, d its dimensions. A list may be missing; unknown keys are ignored.</para>
    /// <para>Unity's session frame is ARKit's with z negated (M = diag(1, 1, -1)). A point p becomes M p, and a box
    /// rotation R becomes M R M, as AR Foundation converts an ARKit pose: local +X is M x, local +Y is M y and local +Z
    /// is -M z.</para>
    /// </summary>
    public static class RoomPlanJson
    {
        public const string Schema = "atlas.room/1";
        /// <summary>More surfaces than this in one list is not a room.</summary>
        public const int MaxSurfaces = 256;
        /// <summary>A narrower, lower, wider or higher surface is a capture artefact, metres (as atlas.walls/1).</summary>
        public const float MinSide = 0.05f, MaxWidth = 30f, MaxHeight = 10f;
        /// <summary>A surface whose width axis leans further out of the floor plane than this is not upright (cosine).</summary>
        public const float MinLevel = 0.9f;
        /// <summary>RoomPlan reports far fewer objects; more than this is not a room.</summary>
        public const int MaxObjects = 256;
        /// <summary>A box side longer than this is a capture artefact, metres.</summary>
        public const float MaxObjectSide = 30f;

        static readonly string[] s_lists = { "walls", "doors", "windows", "openings" };

        /// <summary>
        /// False (and an empty capture) when the text is null or empty, is not JSON, is not an object or is of another
        /// schema, or when a list is not an array or is too long. A surface or object that is not usable is left out
        /// and counted in Skipped.
        /// </summary>
        public static bool TryRead(string json, out RoomPlanCapture capture)
        {
            capture = new RoomPlanCapture();
            if (string.IsNullOrEmpty(json)) return false;
            JsonValue doc;
            string error;
            if (!AtlasJson.TryParse(json, out doc, out error)) return false;
            if (doc == null || !doc.IsObject || doc.GetString("schema", "") != Schema) return false;

            var result = new RoomPlanCapture();
            var found = new List<BridgeSurface>(16);
            for (int list = 0; list < s_lists.Length; list++)
            {
                JsonValue items;
                found.Clear();
                if (doc.TryGet(s_lists[list], out items) && !items.IsNull)
                {
                    if (!items.IsArray || items.Count > MaxSurfaces) return false;
                    for (int k = 0; k < items.Count; k++)
                    {
                        BridgeSurface surface;
                        if (TrySurface(items[k], (BridgeSurfaceKind)list, out surface)) found.Add(surface);
                        else result.Skipped++;
                    }
                }
                BridgeSurface[] surfaces = found.Count == 0 ? Array.Empty<BridgeSurface>() : found.ToArray();
                switch (list)
                {
                    case 0: result.Walls = surfaces; break;
                    case 1: result.Doors = surfaces; break;
                    case 2: result.Windows = surfaces; break;
                    default: result.Openings = surfaces; break;
                }
            }

            JsonValue objects;
            if (doc.TryGet("objects", out objects) && !objects.IsNull)
            {
                if (!objects.IsArray || objects.Count > MaxObjects) return false;
                var boxes = new List<RoomPlanObject>(objects.Count);
                for (int k = 0; k < objects.Count; k++)
                {
                    RoomPlanObject box;
                    if (TryObject(objects[k], out box)) boxes.Add(box);
                    else result.Skipped++;
                }
                if (boxes.Count > 0) result.Objects = boxes.ToArray();
            }

            JsonValue instruction;
            if (doc.TryGet("instruction", out instruction) && instruction.IsString) result.Instruction = instruction.AsString();
            capture = result;
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
            if (!(width >= MinSide && width <= MaxWidth) ||
                !(height >= MinSide && height <= MaxHeight)) return false;
            float length = math.length(axis);
            if (!(length > 1e-6f)) return false;
            var along = new float2(axis.x, -axis.z);
            float level = math.length(along);
            if (!(level >= MinLevel * length)) return false;
            surface = new BridgeSurface
            {
                Kind = kind,
                Centre = Mirror(centre),
                Along = along / level,
                Width = width,
                Height = height,
            };
            return true;
        }

        static bool TryObject(JsonValue v, out RoomPlanObject box)
        {
            box = default(RoomPlanObject);
            if (v == null || !v.IsObject) return false;
            float3 centre, xAxis, zAxis, size;
            if (!TryVector(v["c"], out centre) || !TryVector(v["x"], out xAxis) || !TryVector(v["z"], out zAxis) ||
                !TryVector(v["d"], out size)) return false;
            if (!(math.cmin(size) >= 0f) || !(math.cmax(size) <= MaxObjectSide)) return false;
            quaternion rotation;
            if (!TryRotation(xAxis, zAxis, out rotation)) return false;
            box = new RoomPlanObject
            {
                Id = Text(v, "id"),
                Category = Text(v, "cat"),
                Centre = Mirror(centre),
                Rotation = rotation,
                Size = size,
                Attributes = Strings(v["attrs"]),
            };
            return true;
        }

        /// <summary>
        /// The Unity rotation of a box from its ARKit x and z columns: M R M, whose columns are M x, M y and -M z. The
        /// columns are made orthonormal first (x kept, z made square to it), since the file carries decimals.
        /// </summary>
        static bool TryRotation(float3 xArKit, float3 zArKit, out quaternion rotation)
        {
            rotation = quaternion.identity;
            float3 x = Mirror(xArKit), z = -Mirror(zArKit);
            float lx = math.length(x);
            if (!(lx > 1e-6f)) return false;
            x /= lx;
            z -= math.dot(z, x) * x;
            float lz = math.length(z);
            if (!(lz > 1e-3f)) return false;
            z /= lz;
            // For a rotation's columns (x, y, z): z = x cross y, so y = z cross x.
            float3 y = math.cross(z, x);
            rotation = math.normalize(new quaternion(new float3x3(x, y, z)));
            return true;
        }

        static float3 Mirror(float3 v) { return new float3(v.x, v.y, -v.z); }

        static string Text(JsonValue v, string key)
        {
            JsonValue item;
            return v.TryGet(key, out item) && item.IsString ? item.AsString() : "";
        }

        // An array of strings; anything in it that is not a string is left out.
        static string[] Strings(JsonValue v)
        {
            if (v == null || !v.IsArray || v.Count == 0) return Array.Empty<string>();
            var list = new List<string>(v.Count);
            for (int k = 0; k < v.Count; k++)
                if (v[k] != null && v[k].IsString) list.Add(v[k].AsString());
            return list.Count == 0 ? Array.Empty<string>() : list.ToArray();
        }

        static bool TryVector(JsonValue v, out float3 vector)
        {
            vector = float3.zero;
            if (v == null || !v.IsArray || v.Count != 3) return false;
            for (int k = 0; k < 3; k++)
            {
                if (v[k] == null || !v[k].IsNumber) return false;
                double d = v[k].AsDouble();
                if (double.IsNaN(d) || Math.Abs(d) > 1000.0) return false;
                vector[k] = (float)d;
            }
            return true;
        }
    }
}

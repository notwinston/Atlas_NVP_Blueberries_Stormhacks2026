using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Atlas.Link;
using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>A RoomModel file that cannot be read: the message names the key at fault.</summary>
    public sealed class RoomModelFormatException : Exception
    {
        public RoomModelFormatException(string message) : base(message) { }
    }

    /// <summary>
    /// Reads and writes RoomModel files (docs/contracts/roommodel.schema.json; Master Plan section 4.3).
    /// Engine-free, thread-safe. The text it writes is canonical: keys in schema order, one top-level key per
    /// line, numbers in fixed notation with at most six decimals, so that a file written twice is identical.
    /// </summary>
    public static class RoomModelJson
    {
        /// <summary>A file is at most 300 kB (Master Plan section 7.4).</summary>
        public const int MaxFileBytes = 300 * 1000;

        static readonly string[] s_facingNames = { "box", "profile", "table", "clearance" };
        static readonly string[] s_meshNames = { "placeholder", "requested", "ready", "builtIn", "standIn" };
        static readonly string[] s_openingNames = { "door", "window", "opening" };
        static readonly byte[] s_hashMagic = Encoding.ASCII.GetBytes("ATLAS-ROOM-1");

        // ---- reading -----------------------------------------------------------------------------------

        /// <summary>Parses and checks a file. RoomModelFormatException names the first key that is missing, of the
        /// wrong type, out of range, or of another version. Unknown keys are ignored.</summary>
        public static RoomModel Read(string json)
        {
            JsonValue doc;
            string error;
            if (!AtlasJson.TryParse(json, out doc, out error))
                throw new RoomModelFormatException("RoomModel is not valid JSON: " + error);
            if (!doc.IsObject) throw new RoomModelFormatException("RoomModel: the document is not an object");

            JsonValue version = Req(doc, "version", "version");
            if (!version.IsNumber || version.AsDouble() != RoomModel.SchemaVersion)
                throw new RoomModelFormatException("version: this reader supports version " +
                    RoomModel.SchemaVersion.ToString(CultureInfo.InvariantCulture) + " only");

            var room = new RoomModel();
            room.Id = Str(doc, "id", "id");
            if (!IsRoomId(room.Id)) throw new RoomModelFormatException("id: must be 1 to 40 characters of A-Z, a-z, 0-9 or _");
            room.CreatedUtc = Str(doc, "createdUtc", "createdUtc");

            JsonValue anchor = Obj(doc, "anchor", "anchor");
            room.Anchor = new RoomAnchor
            {
                Id = Str(anchor, "id", "anchor.id"),
                Position = Vec3(Req(anchor, "position", "anchor.position"), "anchor.position", -100.0, 100.0),
                Yaw = (float)Num(anchor, "yaw", "anchor.yaw", -1.5707964, 1.5707964)
            };

            room.Outline = Ring(Req(doc, "outline", "outline"), "outline", 3, RoomModel.MaxOutlinePoints);

            JsonValue sdf = Obj(doc, "sdf", "sdf");
            double cell = Num(sdf, "cell", "sdf.cell", 0.0, 1.0);
            if (Math.Abs(cell - RoomSdf.CellSize) > 1e-9) throw new RoomModelFormatException("sdf.cell: must be 0.05");
            int width = Int(sdf, "width", "sdf.width", 12, 400);
            int height = Int(sdf, "height", "sdf.height", 12, 400);
            float originX = (float)Num(sdf, "originX", "sdf.originX", -20.0, 20.0);
            float originZ = (float)Num(sdf, "originZ", "sdf.originZ", -20.0, 20.0);
            room.Sdf = new RoomSdf(width, height, originX, originZ,
                DecodeSdf(Str(sdf, "data", "sdf.data"), width, height));

            JsonValue walkable = Obj(doc, "walkable", "walkable");
            var polygon = new WalkablePolygon();
            polygon.Outer = Ring(Req(walkable, "outer", "walkable.outer"), "walkable.outer", 4, 4096);
            JsonValue holes = Arr(walkable, "holes", "walkable.holes", 64);
            polygon.Holes = new float2[holes.Count][];
            for (int k = 0; k < holes.Count; k++)
                polygon.Holes[k] = Ring(holes[k], "walkable.holes[" + Text(k) + "]", 4, 4096);
            room.Walkable = polygon;

            JsonValue freeRect = Obj(doc, "freeRect", "freeRect");
            room.FreeRect = new FreeRect((float)Num(freeRect, "wf", "freeRect.wf", 0.5, 20.0),
                (float)Num(freeRect, "df", "freeRect.df", 0.5, 20.0));
            room.LapSide = (float)Num(Obj(doc, "lapSquare", "lapSquare"), "side", "lapSquare.side", 1.0, 3.2);

            JsonValue curvature = Obj(doc, "curvature", "curvature");
            room.Curvature = new RoomCurvature
            {
                L5 = (float)Positive(curvature, "l5", "curvature.l5"),
                L6 = (float)Positive(curvature, "l6", "curvature.l6"),
                R3 = (float)Positive(curvature, "r3", "curvature.r3"),
                FloorL = (float)Num(curvature, "floorL", "curvature.floorL", 0.5, 5.0),
                FloorR = (float)Num(curvature, "floorR", "curvature.floorR", 0.5, 5.0)
            };

            JsonValue cells = Obj(doc, "cells", "cells");
            JsonValue glued = Obj(cells, "glued", "cells.glued");
            room.Glued = new GluedGrid
            {
                Nx = Int(glued, "nx", "cells.glued.nx", 1, 16),
                Nz = Int(glued, "nz", "cells.glued.nz", 1, 16),
                SizeX = Positive(glued, "sizeX", "cells.glued.sizeX"),
                SizeZ = Positive(glued, "sizeZ", "cells.glued.sizeZ")
            };
            JsonValue aligned = Obj(cells, "aligned", "cells.aligned");
            room.Aligned = new AlignedGrid
            {
                N = Int(aligned, "n", "cells.aligned.n", 2, 16),
                M = Int(aligned, "m", "cells.aligned.m", 2, 32),
                G = Positive(aligned, "g", "cells.aligned.g")
            };

            JsonValue standing = Req(doc, "standing", "standing");
            if (!standing.IsBool) throw new RoomModelFormatException("standing: expected true or false");
            room.Standing = standing.AsBool();

            JsonValue walls = Arr(doc, "walls", "walls", 255);
            room.Walls = new RoomWall[walls.Count];
            for (int k = 0; k < walls.Count; k++) room.Walls[k] = ReadWall(walls[k], "walls[" + Text(k) + "]");

            JsonValue objects = Arr(doc, "objects", "objects", RoomModel.MaxObjects);
            room.Objects = new RoomObject[objects.Count];
            for (int k = 0; k < objects.Count; k++) room.Objects[k] = ReadObject(objects[k], "objects[" + Text(k) + "]");

            JsonValue quality = Obj(doc, "quality", "quality");
            room.Quality = new RoomQuality
            {
                UnknownShare = (float)Num(quality, "unknownShare", "quality.unknownShare", 0.0, 1.0),
                Route = (ScanRoute)Int(quality, "route", "quality.route", 0, 4)
            };
            return room;
        }

        /// <summary>As Read, but returns false and a message instead of throwing.</summary>
        public static bool TryRead(string json, out RoomModel room, out string error)
        {
            try
            {
                room = Read(json);
                error = null;
                return true;
            }
            catch (RoomModelFormatException e)
            {
                room = null;
                error = e.Message;
                return false;
            }
        }

        static RoomWall ReadWall(JsonValue v, string path)
        {
            if (!v.IsObject) throw new RoomModelFormatException(path + ": expected an object");
            var wall = new RoomWall
            {
                A = Point(Req(v, "a", path + ".a"), path + ".a"),
                B = Point(Req(v, "b", path + ".b"), path + ".b"),
                Height = (float)Num(v, "height", path + ".height", 0.5, 6.0)
            };
            JsonValue openings = Arr(v, "openings", path + ".openings", 16);
            wall.Openings = new WallOpening[openings.Count];
            for (int m = 0; m < openings.Count; m++)
            {
                string p = path + ".openings[" + Text(m) + "]";
                JsonValue o = openings[m];
                if (!o.IsObject) throw new RoomModelFormatException(p + ": expected an object");
                int kind = Array.IndexOf(s_openingNames, Str(o, "kind", p + ".kind"));
                if (kind < 0) throw new RoomModelFormatException(p + ".kind: must be door, window or opening");
                wall.Openings[m] = new WallOpening
                {
                    Kind = (OpeningKind)kind,
                    Offset = (float)Num(o, "offset", p + ".offset", 0.0, 1e6),
                    Width = (float)Num(o, "width", p + ".width", 0.1, 1e6),
                    Bottom = (float)Num(o, "bottom", p + ".bottom", 0.0, 1e6),
                    Top = (float)Positive(o, "top", p + ".top")
                };
            }
            return wall;
        }

        static RoomObject ReadObject(JsonValue v, string path)
        {
            if (!v.IsObject) throw new RoomModelFormatException(path + ": expected an object");
            var obj = new RoomObject { Id = Int(v, "id", path + ".id", 1, 255) };
            ObjectCategory category;
            if (!ObjectCategories.TryParse(Str(v, "category", path + ".category"), out category))
                throw new RoomModelFormatException(path + ".category: not one of the 23 category names");
            obj.Category = category;
            obj.Base = Vec3(Req(v, "base", path + ".base"), path + ".base", -100.0, 100.0);
            obj.Size = Vec3(Req(v, "size", path + ".size"), path + ".size", 0.02, 6.0);
            obj.Yaw = (float)Num(v, "yaw", path + ".yaw", -3.1415928, 3.1415928);
            int mesh = Array.IndexOf(s_meshNames, Str(v, "mesh", path + ".mesh"));
            if (mesh < 0) throw new RoomModelFormatException(path + ".mesh: not a mesh state");
            obj.Mesh = (MeshState)mesh;
            obj.AssetHash = Str(v, "assetHash", path + ".assetHash");
            if (obj.AssetHash.Length != 0 && !IsSha256(obj.AssetHash))
                throw new RoomModelFormatException(path + ".assetHash: must be empty or 64 lower-case hexadecimal digits");
            obj.TriTarget = Int(v, "triTarget", path + ".triTarget", 0, ObjectMeshRules.TriTargetMax);
            JsonValue optional;
            if (v.TryGet("assetHashLight", out optional) && !optional.IsNull)
            {
                obj.AssetHashLight = Str(v, "assetHashLight", path + ".assetHashLight");
                if (!IsSha256(obj.AssetHashLight))
                    throw new RoomModelFormatException(path + ".assetHashLight: must be 64 lower-case hexadecimal digits");
            }
            if (v.TryGet("facing", out optional) && !optional.IsNull)
            {
                int facing = Array.IndexOf(s_facingNames, Str(v, "facing", path + ".facing"));
                if (facing < 0) throw new RoomModelFormatException(path + ".facing: must be box, profile, table or clearance");
                obj.Facing = (ObjectFacingRule)facing;
            }
            if (v.TryGet("profile", out optional) && !optional.IsNull)
            {
                string text = Str(v, "profile", path + ".profile");
                foreach (char c in text)
                    if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '+' || c == '/' || c == '='))
                        throw new RoomModelFormatException(path + ".profile: not standard base64");
                try { obj.Profile = Convert.FromBase64String(text); }
                catch (FormatException) { throw new RoomModelFormatException(path + ".profile: not standard base64"); }
            }
            return obj;
        }

        static JsonValue Req(JsonValue parent, string key, string path)
        {
            JsonValue v;
            if (!parent.TryGet(key, out v) || v.IsNull) throw new RoomModelFormatException(path + ": missing");
            return v;
        }

        static JsonValue Obj(JsonValue parent, string key, string path)
        {
            JsonValue v = Req(parent, key, path);
            if (!v.IsObject) throw new RoomModelFormatException(path + ": expected an object");
            return v;
        }

        static JsonValue Arr(JsonValue parent, string key, string path, int maxItems)
        {
            JsonValue v = Req(parent, key, path);
            if (!v.IsArray) throw new RoomModelFormatException(path + ": expected an array");
            if (v.Count > maxItems) throw new RoomModelFormatException(path + ": more than " + Text(maxItems) + " items");
            return v;
        }

        static string Str(JsonValue parent, string key, string path)
        {
            JsonValue v = Req(parent, key, path);
            if (!v.IsString) throw new RoomModelFormatException(path + ": expected a string");
            return v.AsString();
        }

        static double Num(JsonValue parent, string key, string path, double min, double max)
        {
            return Number(Req(parent, key, path), path, min, max);
        }

        static double Number(JsonValue v, string path, double min, double max)
        {
            if (!v.IsNumber) throw new RoomModelFormatException(path + ": expected a number");
            double d = v.AsDouble();
            if (double.IsNaN(d) || d < min || d > max) throw new RoomModelFormatException(path + ": out of range");
            return d;
        }

        static double Positive(JsonValue parent, string key, string path)
        {
            double d = Num(parent, key, path, 0.0, 1e6);
            if (d <= 0.0) throw new RoomModelFormatException(path + ": out of range");
            return d;
        }

        static int Int(JsonValue parent, string key, string path, int min, int max)
        {
            JsonValue v = Req(parent, key, path);
            if (!v.IsNumber || v.AsDouble() != Math.Floor(v.AsDouble()))
                throw new RoomModelFormatException(path + ": expected an integer");
            double d = v.AsDouble();
            if (d < min || d > max) throw new RoomModelFormatException(path + ": out of range");
            return (int)d;
        }

        static float2 Point(JsonValue v, string path)
        {
            if (!v.IsArray || v.Count != 2) throw new RoomModelFormatException(path + ": expected [x, z]");
            return new float2((float)Number(v[0], path, -20.0, 20.0), (float)Number(v[1], path, -20.0, 20.0));
        }

        static float3 Vec3(JsonValue v, string path, double min, double max)
        {
            if (!v.IsArray || v.Count != 3) throw new RoomModelFormatException(path + ": expected three numbers");
            return new float3((float)Number(v[0], path, min, max), (float)Number(v[1], path, min, max),
                (float)Number(v[2], path, min, max));
        }

        static float2[] Ring(JsonValue v, string path, int minItems, int maxItems)
        {
            if (!v.IsArray) throw new RoomModelFormatException(path + ": expected an array of points");
            if (v.Count < minItems || v.Count > maxItems)
                throw new RoomModelFormatException(path + ": needs " + Text(minItems) + " to " + Text(maxItems) + " points");
            var ring = new float2[v.Count];
            for (int k = 0; k < ring.Length; k++) ring[k] = Point(v[k], path + "[" + Text(k) + "]");
            return ring;
        }

        static bool IsRoomId(string id)
        {
            if (id.Length < 1 || id.Length > 40) return false;
            for (int k = 0; k < id.Length; k++)
            {
                char c = id[k];
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) return false;
            }
            return true;
        }

        static bool IsSha256(string text)
        {
            if (text.Length != 64) return false;
            for (int k = 0; k < 64; k++)
            {
                char c = text[k];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return true;
        }

        static string Text(int value) { return value.ToString(CultureInfo.InvariantCulture); }

        // ---- the distance field as text ----------------------------------------------------------------

        /// <summary>mm = floor(metres * 1000 + 0.5), clamped to a signed 16-bit integer.</summary>
        internal static short ToMillimetres(float metres)
        {
            double mm = Math.Floor((double)metres * 1000.0 + 0.5);
            return (short)(mm > 32767.0 ? 32767.0 : mm < -32768.0 ? -32768.0 : mm);
        }

        /// <summary>The field as little-endian signed 16-bit millimetres, row-major, row 0 at the lowest z.</summary>
        internal static byte[] SdfBytes(RoomSdf sdf)
        {
            float[] metres = sdf.Metres;
            var bytes = new byte[metres.Length * 2];
            for (int k = 0; k < metres.Length; k++)
            {
                short mm = ToMillimetres(metres[k]);
                bytes[2 * k] = (byte)(mm & 0xFF);
                bytes[2 * k + 1] = (byte)((mm >> 8) & 0xFF);
            }
            return bytes;
        }

        static float[] DecodeSdf(string data, int width, int height)
        {
            byte[] raw;
            try { raw = Convert.FromBase64String(data); }
            catch (FormatException) { throw new RoomModelFormatException("sdf.data: not base64"); }
            if (raw.Length != 2 * width * height)
                throw new RoomModelFormatException("sdf.data: holds " + Text(raw.Length) + " bytes, expected " +
                    Text(2 * width * height));
            var metres = new float[width * height];
            for (int k = 0; k < metres.Length; k++)
                metres[k] = (float)((short)(raw[2 * k] | (raw[2 * k + 1] << 8)) / 1000.0);
            return metres;
        }

        // ---- writing -----------------------------------------------------------------------------------

        /// <summary>The canonical text. Write(Read(text)) == text for every fixture.</summary>
        public static string Write(RoomModel room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (room.Sdf == null) throw new ArgumentException("The room has no distance field", nameof(room));
            var sb = new StringBuilder(room.Sdf.Metres.Length * 3 + 4096);
            sb.Append("{\n");
            sb.Append("  \"version\": ").Append(Text(room.Version)).Append(",\n");
            sb.Append("  \"id\": "); Quoted(sb, room.Id); sb.Append(",\n");
            sb.Append("  \"createdUtc\": "); Quoted(sb, room.CreatedUtc); sb.Append(",\n");

            sb.Append("  \"anchor\": {\"id\": "); Quoted(sb, room.Anchor.Id);
            sb.Append(", \"position\": "); Vec(sb, room.Anchor.Position);
            sb.Append(", \"yaw\": "); Num(sb, room.Anchor.Yaw); sb.Append("},\n");

            sb.Append("  \"outline\": "); Ring(sb, room.Outline); sb.Append(",\n");

            RoomSdf sdf = room.Sdf;
            sb.Append("  \"sdf\": {\"cell\": 0.05, \"width\": ").Append(Text(sdf.Width));
            sb.Append(", \"height\": ").Append(Text(sdf.Height));
            sb.Append(", \"originX\": "); Num(sb, sdf.OriginX);
            sb.Append(", \"originZ\": "); Num(sb, sdf.OriginZ);
            sb.Append(", \"data\": \"").Append(Convert.ToBase64String(SdfBytes(sdf))).Append("\"},\n");

            WalkablePolygon walk = room.Walkable ?? new WalkablePolygon();
            sb.Append("  \"walkable\": {\"outer\": "); Ring(sb, walk.Outer);
            sb.Append(", \"holes\": [");
            for (int k = 0; k < walk.Holes.Length; k++)
            {
                if (k > 0) sb.Append(", ");
                Ring(sb, walk.Holes[k]);
            }
            sb.Append("]},\n");

            sb.Append("  \"freeRect\": {\"wf\": "); Num(sb, room.FreeRect.Wf);
            sb.Append(", \"df\": "); Num(sb, room.FreeRect.Df); sb.Append("},\n");
            sb.Append("  \"lapSquare\": {\"side\": "); Num(sb, room.LapSide); sb.Append("},\n");

            sb.Append("  \"curvature\": {\"l5\": "); Num(sb, room.Curvature.L5);
            sb.Append(", \"l6\": "); Num(sb, room.Curvature.L6);
            sb.Append(", \"r3\": "); Num(sb, room.Curvature.R3);
            sb.Append(", \"floorL\": "); Num(sb, room.Curvature.FloorL);
            sb.Append(", \"floorR\": "); Num(sb, room.Curvature.FloorR); sb.Append("},\n");

            sb.Append("  \"cells\": {\"glued\": {\"nx\": ").Append(Text(room.Glued.Nx));
            sb.Append(", \"nz\": ").Append(Text(room.Glued.Nz));
            sb.Append(", \"sizeX\": "); Num(sb, room.Glued.SizeX);
            sb.Append(", \"sizeZ\": "); Num(sb, room.Glued.SizeZ);
            sb.Append("}, \"aligned\": {\"n\": ").Append(Text(room.Aligned.N));
            sb.Append(", \"m\": ").Append(Text(room.Aligned.M));
            sb.Append(", \"g\": "); Num(sb, room.Aligned.G); sb.Append("}},\n");

            sb.Append("  \"standing\": ").Append(room.Standing ? "true" : "false").Append(",\n");

            RoomWall[] walls = room.Walls ?? Array.Empty<RoomWall>();
            if (walls.Length == 0) sb.Append("  \"walls\": [],\n");
            else
            {
                sb.Append("  \"walls\": [\n");
                for (int k = 0; k < walls.Length; k++)
                {
                    sb.Append("    ");
                    WriteWall(sb, walls[k]);
                    sb.Append(k + 1 < walls.Length ? ",\n" : "\n");
                }
                sb.Append("  ],\n");
            }

            RoomObject[] objects = room.Objects ?? Array.Empty<RoomObject>();
            if (objects.Length == 0) sb.Append("  \"objects\": [],\n");
            else
            {
                sb.Append("  \"objects\": [\n");
                for (int k = 0; k < objects.Length; k++)
                {
                    sb.Append("    ");
                    WriteObject(sb, objects[k]);
                    sb.Append(k + 1 < objects.Length ? ",\n" : "\n");
                }
                sb.Append("  ],\n");
            }

            sb.Append("  \"quality\": {\"unknownShare\": "); Num(sb, room.Quality.UnknownShare);
            sb.Append(", \"route\": ").Append(Text((int)room.Quality.Route)).Append("}\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        static void WriteWall(StringBuilder sb, RoomWall wall)
        {
            sb.Append("{\"a\": "); Pt(sb, wall.A);
            sb.Append(", \"b\": "); Pt(sb, wall.B);
            sb.Append(", \"height\": "); Num(sb, wall.Height);
            sb.Append(", \"openings\": [");
            WallOpening[] openings = wall.Openings ?? Array.Empty<WallOpening>();
            for (int m = 0; m < openings.Length; m++)
            {
                if (m > 0) sb.Append(", ");
                sb.Append("{\"kind\": \"").Append(OpeningName(openings[m].Kind));
                sb.Append("\", \"offset\": "); Num(sb, openings[m].Offset);
                sb.Append(", \"width\": "); Num(sb, openings[m].Width);
                sb.Append(", \"bottom\": "); Num(sb, openings[m].Bottom);
                sb.Append(", \"top\": "); Num(sb, openings[m].Top);
                sb.Append('}');
            }
            sb.Append("]}");
        }

        static void WriteObject(StringBuilder sb, RoomObject obj)
        {
            sb.Append("{\"id\": ").Append(Text(obj.Id));
            sb.Append(", \"category\": \"").Append(ObjectCategories.Name(obj.Category));
            sb.Append("\", \"base\": "); Vec(sb, obj.Base);
            sb.Append(", \"size\": "); Vec(sb, obj.Size);
            sb.Append(", \"yaw\": "); Num(sb, obj.Yaw);
            sb.Append(", \"mesh\": \"").Append(MeshStateName(obj.Mesh));
            sb.Append("\", \"assetHash\": "); Quoted(sb, obj.AssetHash);
            sb.Append(", \"triTarget\": ").Append(Text(obj.TriTarget));
            if (!string.IsNullOrEmpty(obj.AssetHashLight))
            {
                sb.Append(", \"assetHashLight\": "); Quoted(sb, obj.AssetHashLight);
            }
            if (obj.Facing != ObjectFacingRule.Box)
                sb.Append(", \"facing\": \"").Append(s_facingNames[(int)obj.Facing]).Append('"');
            if (obj.Profile != null)
                sb.Append(", \"profile\": \"").Append(Convert.ToBase64String(obj.Profile)).Append('"');
            sb.Append('}');
        }

        /// <summary>JSON name of a mesh state: placeholder, requested, ready, builtIn, standIn.</summary>
        public static string MeshStateName(MeshState state)
        {
            int i = (int)state;
            return s_meshNames[i < s_meshNames.Length ? i : 0];
        }

        /// <summary>JSON name of an opening kind: door, window, opening.</summary>
        public static string OpeningName(OpeningKind kind)
        {
            int i = (int)kind;
            return s_openingNames[i < s_openingNames.Length ? i : 2];
        }

        /// <summary>
        /// Canonical number text: fixed notation, at most six decimals, trailing zeros removed, always one
        /// decimal at least, never an exponent, never "-0.0". Invariant culture.
        /// </summary>
        public static string FormatNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException("A RoomModel number must be finite");
            string text = value.ToString("0.0#####", CultureInfo.InvariantCulture);
            return text == "-0.0" ? "0.0" : text;
        }

        static void Num(StringBuilder sb, double value) { sb.Append(FormatNumber(value)); }

        // A float is rounded to six decimals from its exact value. Going through its shortest decimal text
        // first would round twice (1.51865148 -> "1.5186515" -> 1.518652).
        static void Num(StringBuilder sb, float value) { sb.Append(FormatNumber((double)value)); }

        /// <summary>The decimal value a float was meant to hold: 2.4f gives 2.4, not 2.4000000954. Used where a
        /// length is turned into whole millimetres or tested against the 5 cm lattice.</summary>
        internal static double Widen(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return value;
            return double.Parse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float,
                CultureInfo.InvariantCulture);
        }

        static void Pt(StringBuilder sb, float2 p)
        {
            sb.Append('['); Num(sb, p.x); sb.Append(", "); Num(sb, p.y); sb.Append(']');
        }

        static void Vec(StringBuilder sb, float3 v)
        {
            sb.Append('['); Num(sb, v.x); sb.Append(", "); Num(sb, v.y); sb.Append(", "); Num(sb, v.z); sb.Append(']');
        }

        static void Ring(StringBuilder sb, float2[] ring)
        {
            sb.Append('[');
            if (ring != null)
            {
                for (int k = 0; k < ring.Length; k++)
                {
                    if (k > 0) sb.Append(", ");
                    Pt(sb, ring[k]);
                }
            }
            sb.Append(']');
        }

        static void Quoted(StringBuilder sb, string text) { AtlasJson.AppendQuoted(sb, text ?? ""); }

        // ---- geometry hash -----------------------------------------------------------------------------

        /// <summary>
        /// SHA-256 over the room's geometry, 64 lower-case hexadecimal digits. It covers what the Spectator must
        /// agree on to draw the room (distance field, free rectangle, outline, walls, object boxes) and nothing
        /// that changes after the scan. Integers only (millimetres), so every platform agrees bit for bit.
        /// </summary>
        public static string GeometryHash(RoomModel room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (room.Sdf == null) throw new ArgumentException("The room has no distance field", nameof(room));
            var buffer = new ByteSink(room.Sdf.Metres.Length * 2 + 1024);
            buffer.Bytes(s_hashMagic);
            RoomSdf sdf = room.Sdf;
            buffer.Int(sdf.Width); buffer.Int(sdf.Height); buffer.Int(Mm(sdf.OriginX)); buffer.Int(Mm(sdf.OriginZ));
            buffer.Bytes(SdfBytes(sdf));
            buffer.Int(Mm(room.FreeRect.Wf)); buffer.Int(Mm(room.FreeRect.Df));
            buffer.Int(room.Outline.Length);
            for (int k = 0; k < room.Outline.Length; k++)
            {
                buffer.Int(Mm(room.Outline[k].x)); buffer.Int(Mm(room.Outline[k].y));
            }
            buffer.Int(room.Walls.Length);
            for (int k = 0; k < room.Walls.Length; k++)
            {
                RoomWall wall = room.Walls[k];
                buffer.Int(Mm(wall.A.x)); buffer.Int(Mm(wall.A.y)); buffer.Int(Mm(wall.B.x)); buffer.Int(Mm(wall.B.y));
                buffer.Int(Mm(wall.Height));
                buffer.Int(wall.Openings.Length);
                for (int m = 0; m < wall.Openings.Length; m++)
                {
                    WallOpening o = wall.Openings[m];
                    buffer.Int((int)o.Kind); buffer.Int(Mm(o.Offset)); buffer.Int(Mm(o.Width));
                    buffer.Int(Mm(o.Bottom)); buffer.Int(Mm(o.Top));
                }
            }
            buffer.Int(room.Objects.Length);
            for (int k = 0; k < room.Objects.Length; k++)
            {
                RoomObject o = room.Objects[k];
                buffer.Int(o.Id); buffer.Int((int)o.Category);
                buffer.Int(Mm(o.Base.x)); buffer.Int(Mm(o.Base.y)); buffer.Int(Mm(o.Base.z));
                buffer.Int(Mm(o.Size.x)); buffer.Int(Mm(o.Size.y)); buffer.Int(Mm(o.Size.z));
                buffer.Int((int)Math.Floor(Widen(o.Yaw) * 10000.0 + 0.5));
            }
            byte[] digest;
            using (SHA256 sha = SHA256.Create()) digest = sha.ComputeHash(buffer.Data, 0, buffer.Length);
            var hex = new StringBuilder(64);
            const string digits = "0123456789abcdef";
            for (int k = 0; k < digest.Length; k++) hex.Append(digits[digest[k] >> 4]).Append(digits[digest[k] & 15]);
            return hex.ToString();
        }

        static int Mm(float metres) { return (int)Math.Floor(Widen(metres) * 1000.0 + 0.5); }

        struct ByteSink
        {
            public byte[] Data;
            public int Length;

            public ByteSink(int capacity) { Data = new byte[capacity]; Length = 0; }

            void Reserve(int more)
            {
                if (Length + more <= Data.Length) return;
                Array.Resize(ref Data, Math.Max(Data.Length * 2, Length + more));
            }

            public void Int(int value)
            {
                Reserve(4);
                Data[Length++] = (byte)value;
                Data[Length++] = (byte)(value >> 8);
                Data[Length++] = (byte)(value >> 16);
                Data[Length++] = (byte)(value >> 24);
            }

            public void Bytes(byte[] bytes)
            {
                Reserve(bytes.Length);
                Buffer.BlockCopy(bytes, 0, Data, Length, bytes.Length);
                Length += bytes.Length;
            }
        }

        // ---- checking ----------------------------------------------------------------------------------

        /// <summary>Cheap consistency check for a room built in code: the free rectangle, the loop-fit values, the
        /// cell grids, the standing flag and the object rules (R6, R10 to R13, R16, R17, R21 of the contract page).
        /// Returns null when they hold, otherwise the first violated rule as text. The full rule set is
        /// RoomModelValidator.Validate.</summary>
        public static string Check(RoomModel room)
        {
            return RoomModelValidator.FirstCheapError(room);
        }
    }
}

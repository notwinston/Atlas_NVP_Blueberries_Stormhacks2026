using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Telemetry.Tests
{
    /// <summary>
    /// The on-phone object path (docs/plans/standalone-phone/INDEX.md, section 6.5, task T8): the prop library, the
    /// order per object (first shape, then match, fit, colour or cut, then place), the saved room rebuilt without
    /// photos, the route that keeps the Forge out of it, and the budget and allocation rules. Uses the fixture props
    /// of Tests/Objects/Fixtures/props_min (a box chair, a slab table, a three-box couch) and the fixture room
    /// sample_4x5_table. Engine-free: no Atlas.Objects type is named here (this assembly does not reference it).
    /// </summary>
    public class OnPhoneObjectsCoreTests
    {
        const double Step = 0.05;
        const float FrameMs = 16.7f;

        const string ChairProp = "chair_box_min", TableProp = "table_slab_min", CouchProp = "couch_box3_min";
        static readonly byte[] s_chairSlots = { 97, 70 }, s_tableSlots = { 84, 66 }, s_couchSlots = { 105, 75 };

        // ---- Files ---------------------------------------------------------------------------------------------

        static string Project()
        {
            string project = Environment.GetEnvironmentVariable("ATLAS_PROJECT_DIR");
            return string.IsNullOrEmpty(project) ? Directory.GetCurrentDirectory() : project;
        }

        static string PropsMinDirectory() { return Path.Combine(Project(), "Assets", "Atlas", "Tests", "Objects", "Fixtures", "props_min"); }

        static string ShippedPropsDirectory() { return Path.Combine(Project(), "Assets", "Atlas", "Telemetry", "Resources", PropSet.ResourceFolder); }

        /// <summary>The reader the app uses, with a folder on disk in place of Resources.</summary>
        static Func<string, byte[]> Reader(string directory)
        {
            return name =>
            {
                string path = Path.Combine(directory, name == PropSet.ManifestName ? "manifest.json" : name + ".bytes");
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            };
        }

        static PropSet MinProps() { return PropSet.Load(Reader(PropsMinDirectory())); }

        // ---- Fakes ---------------------------------------------------------------------------------------------

        /// <summary>
        /// The room content and the app's adapter in one: what is placed, in order, and the RoomModel entries updated
        /// as the room content and AssetClient.PlaceOnPhone update them (Mesh, Prop, PropCells).
        /// </summary>
        sealed class PhoneRoom : IAssetRoom
        {
            public RoomModel Model;
            public readonly List<string> Calls = new List<string>();
            public readonly Dictionary<int, string> Shown = new Dictionary<int, string>();
            public readonly Dictionary<int, string> Hash = new Dictionary<int, string>();
            public readonly Dictionary<int, int> Triangles = new Dictionary<int, int>();
            public readonly Dictionary<int, float> LongestEdge = new Dictionary<int, float>();

            public bool SetRequested(int objectId) { Calls.Add("requested:" + objectId); return true; }
            public bool PlaceGenerated(int objectId, MeshData mesh, string sha256) { Calls.Add("generated:" + objectId); return true; }
            public bool PlaceGeneratedLight(int objectId, MeshData mesh, string sha256) { return false; }

            public bool PlaceBuiltIn(int objectId, MeshData mesh)
            {
                Calls.Add("builtin:" + objectId);
                Shown[objectId] = "builtin";
                RoomObject o = Find(objectId);
                if (o != null) { o.Mesh = MeshState.BuiltIn; o.AssetHash = ""; }
                return true;
            }

            public bool PlaceOnPhone(int objectId, MeshData mesh, MeshState state, string prop, byte[] propCells)
            {
                string shape = state == MeshState.Matched ? "matched" : state == MeshState.ScanCut ? "scanCut" : "?";
                Calls.Add(shape + ":" + objectId);
                Shown[objectId] = shape;
                Hash[objectId] = MeshHash(mesh);
                Triangles[objectId] = mesh.TriangleCount;
                LongestEdge[objectId] = mesh.LongestEdge();
                RoomObject o = Find(objectId);
                if (o != null)
                {
                    o.Mesh = state;
                    o.Prop = state == MeshState.Matched ? prop ?? "" : "";
                    o.PropCells = state == MeshState.Matched && propCells != null ? (byte[])propCells.Clone() : null;
                }
                return true;
            }

            public string ShownFor(int objectId)
            {
                string shown;
                return Shown.TryGetValue(objectId, out shown) ? shown : "box";
            }

            public int CountOf(string prefix)
            {
                int n = 0;
                foreach (string call in Calls) if (call.StartsWith(prefix, StringComparison.Ordinal)) n++;
                return n;
            }

            RoomObject Find(int id)
            {
                if (Model == null) return null;
                foreach (RoomObject o in Model.Objects) if (o.Id == id) return o;
                return null;
            }
        }

        /// <summary>A "JPEG" is three bytes, the colour of every pixel of a 200 x 200 crop; it counts calls per tick.</summary>
        sealed class SolidDecoder : ICropDecoder
        {
            public const int Side = 200;
            public int Calls;
            public int CallsThisTick;
            public int MostInOneTick;

            public void NewTick() { CallsThisTick = 0; }

            public bool TryDecode(byte[] jpeg, out int width, out int height, out byte[] rgb)
            {
                Calls++;
                CallsThisTick++;
                if (CallsThisTick > MostInOneTick) MostInOneTick = CallsThisTick;
                width = height = Side;
                rgb = new byte[Side * Side * 3];
                for (int i = 0; i < rgb.Length; i += 3)
                {
                    rgb[i] = jpeg[0];
                    rgb[i + 1] = jpeg[1];
                    rgb[i + 2] = jpeg[2];
                }
                return true;
            }
        }

        sealed class Rig
        {
            public readonly PhoneRoom Room = new PhoneRoom();
            public readonly RecordingEvents Events = new RecordingEvents();
            public readonly SolidDecoder Decoder = new SolidDecoder();
            public readonly OnPhoneObjectsCore Core;
            public readonly List<RoomModel> Settled = new List<RoomModel>();
            public double Time;

            public Rig(PropSet props = null)
            {
                Core = new OnPhoneObjectsCore(Room, props ?? MinProps(), new InlineAssetWorker(), AssetFixtures.LoadFallback(), Events, Decoder);
                Core.Settled = Settled.Add;
                Core.Tick(0.0);
            }

            public void Run(double seconds)
            {
                double end = Time + seconds;
                while (Time < end - 1e-9)
                {
                    Time += Step;
                    Decoder.NewTick();
                    Core.Tick(Time);
                }
            }

            /// <summary>A scan as the app hands it over: the scanned mesh, BeginRoom, the build, the photos, EndRoom.</summary>
            public void Scan(RoomModel room, ScanMesh mesh, bool photos = true)
            {
                Room.Model = room;
                Core.SetScanMesh(room.Id, mesh);
                Core.BeginRoom(room, RoomModelJson.Write(room));
                Core.RoomBuilt(room);
                if (photos) foreach (RoomObject o in room.Objects) if (o.TriTarget > 0) Core.Submit(Photos(o, Red));
                Core.EndRoom();
            }

            public void Load(RoomModel room)
            {
                Room.Model = room;
                Core.RoomBuilt(room);
            }
        }

        static readonly byte[] Red = { 220, 30, 30 };

        // ---- Rooms ---------------------------------------------------------------------------------------------

        static RoomObject Box(int id, ObjectCategory category, float3 basePoint, float3 size, float yaw = 0f)
        {
            return new RoomObject { Id = id, Category = category, Base = basePoint, Size = size, Yaw = yaw };
        }

        /// <summary>A table, a chair, a couch, an unknown thing standing alone, and an object of a category with no prop.</summary>
        /// <remarks>The shell is sample_4x5_table's, so the room can be written and read as a file.</remarks>
        static RoomModel MixedRoom(string id = "room_phone")
        {
            RoomObject[] objects =
            {
                Box(1, ObjectCategory.Table, new float3(-1f, 0f, 0f), new float3(1.2f, 0.75f, 0.8f), 0.5f),
                Box(2, ObjectCategory.Chair, new float3(0.5f, 0f, -1f), new float3(0.5f, 0.9f, 0.5f), 1.5f),
                Box(3, ObjectCategory.Couch, new float3(-2.5f, 0f, 1f), new float3(1.8f, 0.8f, 0.9f)),
                Box(4, ObjectCategory.Unknown, new float3(-3f, 0f, -1.25f), new float3(0.6f, 0.6f, 0.6f)),
                Box(5, ObjectCategory.Bed, new float3(0f, 0f, 1f), new float3(1.4f, 0.5f, 1.8f)),
            };
            ObjectMeshRules.Assign(objects);
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample4x5Table);
            room.Id = id;
            room.Objects = objects;
            return room;
        }

        /// <summary>
        /// A scanned mesh in the room frame: the floor as one large grid, and the four sides and the top of each
        /// given box as 8 x 8 grids (640 triangles a box, all inside it).
        /// </summary>
        static ScanMesh ScanOf(params RoomObject[] boxes)
        {
            var vertices = new List<float3>();
            var indices = new List<int>();
            Grid(vertices, indices, new float3(-4f, 0f, -4f), new float3(8f, 0f, 0f), new float3(0f, 0f, 8f), 16);
            foreach (RoomObject o in boxes)
            {
                quaternion turn = quaternion.RotateY(o.Yaw);
                float3 h = o.Size * 0.5f;
                float3 Corner(float x, float y, float z) => o.Base + math.mul(turn, new float3(x, y, z));
                float3 Axis(float3 v) => math.mul(turn, v);
                // Top, front (+z), back, right (+x), left.
                Grid(vertices, indices, Corner(-h.x, o.Size.y, -h.z), Axis(new float3(o.Size.x, 0f, 0f)), Axis(new float3(0f, 0f, o.Size.z)), 8);
                Grid(vertices, indices, Corner(-h.x, 0.01f, h.z), Axis(new float3(o.Size.x, 0f, 0f)), Axis(new float3(0f, o.Size.y - 0.01f, 0f)), 8);
                Grid(vertices, indices, Corner(h.x, 0.01f, -h.z), Axis(new float3(-o.Size.x, 0f, 0f)), Axis(new float3(0f, o.Size.y - 0.01f, 0f)), 8);
                Grid(vertices, indices, Corner(h.x, 0.01f, h.z), Axis(new float3(0f, 0f, -o.Size.z)), Axis(new float3(0f, o.Size.y - 0.01f, 0f)), 8);
                Grid(vertices, indices, Corner(-h.x, 0.01f, -h.z), Axis(new float3(0f, 0f, o.Size.z)), Axis(new float3(0f, o.Size.y - 0.01f, 0f)), 8);
            }
            return new ScanMesh(vertices.ToArray(), indices.ToArray(), null);
        }

        static void Grid(List<float3> vertices, List<int> indices, float3 origin, float3 u, float3 v, int n)
        {
            int first = vertices.Count;
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                    vertices.Add(origin + u * (i / (float)n) + v * (j / (float)n));
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    int a = first + j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    indices.Add(a); indices.Add(c); indices.Add(b);
                    indices.Add(b); indices.Add(c); indices.Add(d);
                }
            }
        }

        /// <summary>One crop of the object's front, from a camera 3 m in front of it at half its height, looking at it.</summary>
        static ObjectPhotoSet Photos(RoomObject o, byte[] colour)
        {
            float4x4 camToBox = float4x4.TRS(new float3(0f, o.Size.y * 0.5f, 3f), quaternion.RotateY(math.PI), new float3(1f));
            return new ObjectPhotoSet
            {
                ObjectId = o.Id, Category = o.Category, Size = o.Size, Yaw = o.Yaw, TriTarget = o.TriTarget, Profile = o.Profile,
                Views = new[]
                {
                    new ObjectView
                    {
                        Jpeg = (byte[])colour.Clone(), Width = SolidDecoder.Side, Height = SolidDecoder.Side, Score = 1f,
                        Intrinsics = new float4(200f, 200f, 100f, 100f), CamToBox = camToBox,
                    }
                },
            };
        }

        static string MeshHash(MeshData mesh)
        {
            var bytes = new List<byte>(mesh.VertexCount * 40 + mesh.IndexCount * 4);
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                AtlasVertex x = mesh.Vertices[v];
                Add(bytes, x.Position.x); Add(bytes, x.Position.y); Add(bytes, x.Position.z);
                Add(bytes, x.Normal.x); Add(bytes, x.Normal.y); Add(bytes, x.Normal.z);
                bytes.AddRange(BitConverter.GetBytes(x.Color));
                Add(bytes, x.Uv0.x); Add(bytes, x.Uv0.y);
            }
            for (int i = 0; i < mesh.IndexCount; i++) bytes.AddRange(BitConverter.GetBytes(mesh.Indices[i]));
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes.ToArray()));
        }

        static void Add(List<byte> bytes, float value) { bytes.AddRange(BitConverter.GetBytes(value)); }

        static RoomObject ObjectOf(RoomModel room, int id)
        {
            foreach (RoomObject o in room.Objects) if (o.Id == id) return o;
            return null;
        }

        // ---- PropSet -------------------------------------------------------------------------------------------

        [Test]
        public void PropSet_LoadsFixtureProps_InManifestOrder()
        {
            PropSet props = MinProps();
            Assert.That(props.Error, Is.Null);
            Assert.That(props.Count, Is.EqualTo(3));
            Assert.That(props.IdAt(0), Is.EqualTo(ChairProp));
            Assert.That(props.IdAt(1), Is.EqualTo(TableProp));
            Assert.That(props.IdAt(2), Is.EqualTo(CouchProp));
            Assert.That(props.IndexOf(CouchProp), Is.EqualTo(2));
            Assert.That(props.IndexOf("no_such_prop"), Is.EqualTo(-1));
        }

        [Test]
        public void PropSet_RefusesAlteredOrMissingFiles_KeepsTheRest()
        {
            Func<string, byte[]> read = Reader(PropsMinDirectory());
            PropSet altered = PropSet.Load(name =>
            {
                byte[] bytes = read(name);
                if (name == TableProp + ".glb") { bytes = (byte[])bytes.Clone(); bytes[bytes.Length - 1] ^= 0x5a; }
                return bytes;
            });
            Assert.That(altered.Count, Is.EqualTo(2));
            Assert.That(altered.IndexOf(TableProp), Is.EqualTo(-1));
            Assert.That(altered.Error, Does.Contain(TableProp).And.Contain("hash"));

            PropSet partial = PropSet.Load(name => name == CouchProp + ".glb" ? null : read(name));
            Assert.That(partial.Count, Is.EqualTo(2));
            Assert.That(partial.Error, Does.Contain(CouchProp).And.Contain("missing"));
        }

        [Test]
        public void PropSet_MissingOrBadManifest_IsEmpty_NeverThrows()
        {
            Assert.That(PropSet.Load(name => null).Count, Is.EqualTo(0));
            Assert.That(PropSet.Load(name => null).Error, Is.Not.Null);
            Assert.That(PropSet.Load(null).Count, Is.EqualTo(0));
            Assert.That(PropSet.Load(name => new byte[] { 1, 2, 3 }).Count, Is.EqualTo(0));
            Assert.That(PropSet.Load(name => ForgeProtocol.Utf8("{\"schema\":\"atlas.props/9\",\"props\":[]}")).Error, Does.Contain("atlas.props/1"));
        }

        [Test]
        public void PropSet_RefusesAPropOutsideTheFormat()
        {
            Func<string, byte[]> read = Reader(PropsMinDirectory());
            JsonValue manifest = AtlasJson.ParseUtf8(read(PropSet.ManifestName));
            manifest["props"][0]["slots"][0].Set("cell", 12);           // not an object colour
            manifest["props"][1].Set("category", "spaceship");
            manifest["props"][2].Set("triangles", 5);
            byte[] bytes = AtlasJson.ToUtf8(manifest);
            PropSet props = PropSet.Load(name => name == PropSet.ManifestName ? bytes : read(name));
            Assert.That(props.Count, Is.EqualTo(0));
            Assert.That(props.Error, Does.Contain(ChairProp).And.Contain("2 more"));
        }

        [Test]
        public void PropSet_ShippedLibrary_LoadsEveryPropItLists()
        {
            string directory = ShippedPropsDirectory();
            string manifestPath = Path.Combine(directory, "manifest.json");
            Assume.That(File.Exists(manifestPath), "no shipped prop library yet");
            int listed = AtlasJson.ParseUtf8(File.ReadAllBytes(manifestPath))["props"].Count;
            PropSet props = PropSet.Load(Reader(directory));
            Assert.That(props.Error, Is.Null);
            Assert.That(props.Count, Is.EqualTo(listed));
        }

        // ---- The order per object ------------------------------------------------------------------------------

        [Test]
        public void FixtureRoom_EveryObjectLeavesItsFirstShape_ForAMatchedProp()
        {
            var rig = new Rig();
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample4x5Table);
            rig.Load(room);
            rig.Run(LoadedRoomSeconds());

            foreach (RoomObject o in room.Objects)
            {
                Assert.That(rig.Room.ShownFor(o.Id), Is.EqualTo("matched"), "object " + o.Id);
                Assert.That(rig.Room.Calls.IndexOf("builtin:" + o.Id), Is.LessThan(rig.Room.Calls.IndexOf("matched:" + o.Id)),
                    "the first shape comes first");
            }
            RoomObject table = room.Objects[0];
            Assert.That(table.Prop, Is.EqualTo(TableProp));
            Assert.That(table.PropCells, Is.EqualTo(s_tableSlots), "no photo: the prop's default colours");
            Assert.That(rig.Core.PlacedCount, Is.EqualTo(room.Objects.Length));
            Assert.That(rig.Core.PendingCount, Is.EqualTo(0));
            Assert.That(rig.Settled, Is.Empty, "a room from a file is not a scan: nothing is saved again");
        }

        [Test]
        public void Scan_MatchesNamedObjects_CutsTheUnknownOne_KeepsTheFirstShapeWithoutAProp()
        {
            var rig = new Rig();
            RoomModel room = MixedRoom();
            rig.Scan(room, ScanOf(ObjectOf(room, 4)));
            rig.Run(2.0);

            Assert.That(rig.Room.ShownFor(1), Is.EqualTo("matched"));
            Assert.That(rig.Room.ShownFor(2), Is.EqualTo("matched"));
            Assert.That(rig.Room.ShownFor(3), Is.EqualTo("matched"));
            Assert.That(rig.Room.ShownFor(4), Is.EqualTo("scanCut"), "an unknown object is cut out of the scan");
            Assert.That(rig.Room.ShownFor(5), Is.EqualTo("builtin"), "no bed prop: the first shape stays");
            Assert.That(ObjectOf(room, 1).Prop, Is.EqualTo(TableProp));
            Assert.That(ObjectOf(room, 2).Prop, Is.EqualTo(ChairProp));
            Assert.That(ObjectOf(room, 3).Prop, Is.EqualTo(CouchProp));
            Assert.That(ObjectOf(room, 4).Prop, Is.EqualTo(""));
            Assert.That(ObjectOf(room, 4).PropCells, Is.Null);
            Assert.That(ObjectOf(room, 4).Mesh, Is.EqualTo(MeshState.ScanCut));
            Assert.That(rig.Events.CountOf("kept", 5), Is.EqualTo(1));
            Assert.That(rig.Events.Last("kept", 5).GetString("reason"), Is.EqualTo("no_prop"));
            foreach (RoomObject o in room.Objects)
                Assert.That(rig.Events.CountOf("builtin", o.Id), Is.EqualTo(1), "every object gets its first shape at once");
            // An unknown object has no template, so its first shape is the box it already shows (as today).
            foreach (int id in new[] { 1, 2, 3, 5 })
                Assert.That(rig.Room.Calls.IndexOf("builtin:" + id), Is.GreaterThanOrEqualTo(0), "object " + id);
            Assert.That(rig.Room.CountOf("generated:"), Is.EqualTo(0));
        }

        [Test]
        public void Scan_ColoursSeenSlotsFromTheCrops_OneCellForOneColour()
        {
            var rig = new Rig();
            RoomModel room = MixedRoom();
            rig.Scan(room, ScanOf(ObjectOf(room, 4)));
            rig.Run(2.0);

            var changed = new HashSet<byte>();
            foreach (var pair in new[] { (1, s_tableSlots), (2, s_chairSlots), (3, s_couchSlots) })
            {
                byte[] cells = ObjectOf(room, pair.Item1).PropCells;
                Assert.That(cells, Is.Not.Null);
                Assert.That(cells.Length, Is.EqualTo(pair.Item2.Length), "one cell per slot");
                bool any = false;
                for (int s = 0; s < cells.Length; s++)
                {
                    Assert.That(cells[s], Is.InRange(RoomObject.FirstPropCell, RoomObject.LastPropCell));
                    if (cells[s] == pair.Item2[s]) continue;
                    any = true;
                    changed.Add(cells[s]);
                }
                Assert.That(any, Is.True, "object " + pair.Item1 + ": a slot the camera sees takes the crop's colour");
            }
            Assert.That(changed.Count, Is.EqualTo(1), "one solid colour gives one cell everywhere");
        }

        [Test]
        public void ReportNoPhoto_GivesDefaultColours_NotNoMesh()
        {
            var rig = new Rig();
            RoomModel room = MixedRoom();
            rig.Room.Model = room;
            rig.Core.SetScanMesh(room.Id, null);
            rig.Core.BeginRoom(room, "{}");
            rig.Core.RoomBuilt(room);
            rig.Core.ReportNoPhoto(1);
            rig.Core.Submit(Photos(ObjectOf(room, 2), Red));
            rig.Core.EndRoom();                                 // 3, 4, 5 neither submitted nor reported
            rig.Run(2.0);

            Assert.That(ObjectOf(room, 1).PropCells, Is.EqualTo(s_tableSlots));
            Assert.That(ObjectOf(room, 2).PropCells, Is.Not.EqualTo(s_chairSlots));
            Assert.That(ObjectOf(room, 3).PropCells, Is.EqualTo(s_couchSlots));
            Assert.That(rig.Room.ShownFor(4), Is.EqualTo("box"), "an unknown object with no scanned mesh keeps its first shape: the box");
            Assert.That(rig.Events.Last("kept", 4).GetString("reason"), Is.EqualTo("no_scan_mesh"));
        }

        [Test]
        public void PoorProfileFit_TakesTheScanCut_WhenTheScanHasEnough_ElseTheProp()
        {
            // A chair whose scan saw full height everywhere: the box chair fits it poorly (F > 0.20).
            var profile = new byte[256];
            for (int i = 0; i < profile.Length; i++) profile[i] = 255;

            var rig = new Rig();
            RoomModel room = MixedRoom();
            RoomObject chair = ObjectOf(room, 2);
            chair.Profile = profile;
            chair.Facing = ObjectFacingRule.Profile;
            rig.Scan(room, ScanOf(chair));
            rig.Run(2.0);
            Assert.That(rig.Room.ShownFor(2), Is.EqualTo("scanCut"));
            Assert.That(rig.Events.Last("onphone", 2).GetDouble("fit"), Is.GreaterThan(0.20));

            var noScan = new Rig();
            RoomModel again = MixedRoom("room_noscan");
            ObjectOf(again, 2).Profile = profile;
            ObjectOf(again, 2).Facing = ObjectFacingRule.Profile;
            noScan.Scan(again, null);
            noScan.Run(2.0);
            Assert.That(noScan.Room.ShownFor(2), Is.EqualTo("matched"), "no scanned mesh: the prop, poor or not");
        }

        [Test]
        public void EmptyLibrary_EveryObjectKeepsItsFirstShape()
        {
            var rig = new Rig(PropSet.Empty("no library"));
            RoomModel room = MixedRoom();
            rig.Scan(room, null);
            rig.Run(2.0);
            foreach (RoomObject o in room.Objects)
                Assert.That(rig.Room.ShownFor(o.Id), Is.EqualTo(o.Category == ObjectCategory.Unknown ? "box" : "builtin"), "object " + o.Id);
            Assert.That(rig.Room.CountOf("matched:") + rig.Room.CountOf("scanCut:"), Is.EqualTo(0));
        }

        [Test]
        public void Budget_PlacedTrianglesStayWithinEachCap_AndEdgesWithinTheWarpRule()
        {
            var rig = new Rig();
            RoomModel room = MixedRoom();
            rig.Scan(room, ScanOf(ObjectOf(room, 4)));
            rig.Run(2.0);
            int sum = 0;
            foreach (KeyValuePair<int, int> placed in rig.Room.Triangles)
            {
                RoomObject o = ObjectOf(room, placed.Key);
                Assert.That(placed.Value, Is.LessThanOrEqualTo(Math.Max(o.TriTarget, OnPhoneObjectsCore.MinTriangleCap)), "object " + o.Id);
                Assert.That(rig.Room.LongestEdge[placed.Key], Is.LessThanOrEqualTo(0.25f + 1e-4f), "object " + o.Id);
                sum += placed.Value;
            }
            Assert.That(sum, Is.LessThanOrEqualTo(ObjectMeshRules.FurnitureTriangles));
        }

        [Test]
        public void Crops_AreDecodedAtMostTwoObjectsPerFrame()
        {
            var rig = new Rig();
            RoomModel room = MixedRoom();
            rig.Scan(room, ScanOf(ObjectOf(room, 4)));
            rig.Run(2.0);
            Assert.That(rig.Decoder.Calls, Is.EqualTo(room.Objects.Length), "one crop per object, each decoded once");
            Assert.That(rig.Decoder.MostInOneTick, Is.LessThanOrEqualTo(OnPhoneObjectsCore.DecodesPerTick));
        }

        // ---- Settling and the saved room -----------------------------------------------------------------------

        [Test]
        public void Settled_IsRaisedOnce_AfterTheRoomShowsEveryPlacement_AndTheScanMeshIsDropped()
        {
            var rig = new Rig();
            RoomModel room = MixedRoom();
            rig.Scan(room, ScanOf(ObjectOf(room, 4)));
            Assert.That(rig.Core.HoldsScanMesh, Is.True);
            rig.Run(2.0);
            Assert.That(rig.Settled.Count, Is.EqualTo(1));
            Assert.That(rig.Settled[0], Is.SameAs(room));
            Assert.That(rig.Core.HoldsScanMesh, Is.False, "the scanned mesh is kept only until the objects are built");
            rig.Run(2.0);
            Assert.That(rig.Settled.Count, Is.EqualTo(1));
        }

        [Test]
        public void SavedRoom_RebuildsTheSamePropsColoursAndMeshes_WithoutPhotos()
        {
            var rig = new Rig();
            RoomModel room = MixedRoom();
            rig.Scan(room, ScanOf(ObjectOf(room, 4)));
            rig.Run(2.0);
            string json = RoomModelJson.Write(room);

            // Loaded again, as the room store gives it: the same props, cells and meshes, no photo, no scan.
            for (int round = 0; round < 2; round++)
            {
                var reload = new Rig();
                RoomModel loaded = RoomModelJson.Read(json);
                Assert.That(RoomModelJson.GeometryHash(loaded), Is.EqualTo(RoomModelJson.GeometryHash(room)));
                reload.Load(loaded);
                reload.Run(LoadedRoomSeconds());
                foreach (int id in new[] { 1, 2, 3 })
                {
                    Assert.That(reload.Room.ShownFor(id), Is.EqualTo("matched"), "round " + round + ", object " + id);
                    Assert.That(ObjectOf(loaded, id).Prop, Is.EqualTo(ObjectOf(room, id).Prop));
                    Assert.That(ObjectOf(loaded, id).PropCells, Is.EqualTo(ObjectOf(room, id).PropCells));
                    Assert.That(reload.Room.Hash[id], Is.EqualTo(rig.Room.Hash[id]), "round " + round + ", object " + id + ": the same mesh");
                }
                Assert.That(reload.Room.ShownFor(4), Is.EqualTo("box"), "a cut-out is not saved: it reloads as its first shape (the box)");
                Assert.That(reload.Events.Last("kept", 4).GetString("reason"), Is.EqualTo("no_scan_mesh"));
                Assert.That(reload.Decoder.Calls, Is.EqualTo(0));
                Assert.That(reload.Settled, Is.Empty);
            }
        }

        [Test]
        public void SavedRoom_WithAPropNoLongerInTheLibrary_KeepsTheFirstShape_AndForgetsTheProp()
        {
            RoomModel loaded = RoomFixtures.LoadFromProject(RoomFixtures.Sample4x5Table);
            RoomObject table = loaded.Objects[0];
            table.Mesh = MeshState.Matched;
            table.Prop = "table_gone_01";
            table.PropCells = new byte[] { 80, 70 };
            var rig = new Rig();
            rig.Load(loaded);
            rig.Run(LoadedRoomSeconds());
            Assert.That(rig.Room.ShownFor(table.Id), Is.EqualTo("builtin"));
            Assert.That(table.Prop, Is.EqualTo(""));
            Assert.That(table.PropCells, Is.Null);
            Assert.That(rig.Events.Last("kept", table.Id).GetString("reason"), Is.EqualTo("prop_missing"));
        }

        [Test]
        public void RoomBuiltAgain_PlacesTheSameMeshesOnTheNewViews()
        {
            var rig = new Rig();
            RoomModel room = MixedRoom();
            rig.Scan(room, ScanOf(ObjectOf(room, 4)));
            rig.Run(2.0);
            var first = new Dictionary<int, string>(rig.Room.Hash);
            rig.Room.Hash.Clear();
            rig.Room.Shown.Clear();                             // the new views show boxes
            rig.Core.RoomBuilt(room);
            rig.Run(2.0);
            foreach (int id in new[] { 1, 2, 3 })
                Assert.That(rig.Room.Hash[id], Is.EqualTo(first[id]), "object " + id);
            Assert.That(rig.Room.ShownFor(4), Is.EqualTo("box"), "the scanned mesh is gone: the cut-out cannot be made again");
            Assert.That(rig.Events.Last("kept", 4).GetString("reason"), Is.EqualTo("scan_mesh_gone"));
        }

        // ---- The route: no Forge on the phone path -------------------------------------------------------------

        [Test]
        public void Route_BackendOff_ServesTheRoomOnThePhone_AndAsksTheForgeNothing()
        {
            var forge = new FakeForge();
            var room = new PhoneRoom();
            var core = new AssetClientCore(forge, room, new MemoryAssetCache(), new InlineAssetWorker(), AssetFixtures.LoadFallback(), null)
            {
                ForgeUrl = FakeForge.Url, Backend = AssetClientCore.BackendOff
            };
            var onPhone = new OnPhoneObjectsCore(room, MinProps(), new InlineAssetWorker(), AssetFixtures.LoadFallback(), null, new SolidDecoder());
            var route = new AssetRoute(core, onPhone);
            Assert.That(route.WouldServeOnPhone, Is.True);

            RoomModel model = MixedRoom();
            room.Model = model;
            route.SetScanMesh(model.Id, ScanOf(ObjectOf(model, 4)));
            route.BeginRoom(model, RoomModelJson.Write(model));
            route.RoomBuilt(model);
            foreach (RoomObject o in model.Objects) route.Submit(Photos(o, Red));
            route.EndRoom();
            for (double t = Step; t < 10.0; t += Step) { forge.Now = t; route.Tick(t, FrameMs); }

            Assert.That(route.ServesOnPhone, Is.True);
            Assert.That(forge.Requests, Is.Empty, "no request reaches the Forge");
            Assert.That(room.CountOf("matched:"), Is.EqualTo(3));
            Assert.That(room.CountOf("scanCut:"), Is.EqualTo(1));
            Assert.That(core.ObjectCount, Is.EqualTo(0), "the Forge's core never heard of the room");
            Assert.That(route.PlacedCount, Is.EqualTo(4));
        }

        [Test]
        public void Route_BackendLocal_LeavesTheRoomToTheForge_Unchanged()
        {
            var forge = new FakeForge();
            var room = new PhoneRoom();
            var core = new AssetClientCore(forge, room, new MemoryAssetCache(), new InlineAssetWorker(), AssetFixtures.LoadFallback(), null)
            {
                ForgeUrl = FakeForge.Url, Backend = AssetClientCore.BackendLocal
            };
            var onPhone = new OnPhoneObjectsCore(room, MinProps(), new InlineAssetWorker(), AssetFixtures.LoadFallback(), null, new SolidDecoder());
            var route = new AssetRoute(core, onPhone);

            RoomModel model = MixedRoom();
            room.Model = model;
            route.SetScanMesh(model.Id, ScanOf(ObjectOf(model, 4)));
            route.BeginRoom(model, RoomModelJson.Write(model));
            route.RoomBuilt(model);
            foreach (RoomObject o in model.Objects) route.Submit(Photos(o, Red));
            route.EndRoom();
            for (double t = Step; t < 3.0; t += Step) { forge.Now = t; route.Tick(t, FrameMs); }

            Assert.That(route.ServesOnPhone, Is.False);
            Assert.That(forge.Requests, Is.Not.Empty, "the Forge's round trip runs as before");
            Assert.That(room.CountOf("matched:") + room.CountOf("scanCut:"), Is.EqualTo(0));
            Assert.That(onPhone.ObjectCount, Is.EqualTo(0));
            Assert.That(onPhone.HoldsScanMesh, Is.False, "the scanned mesh is not kept for a room the phone does not build");
        }

        // ---- The frame path ------------------------------------------------------------------------------------

        [Test]
        public void Tick_AllocatesNothingWhenIdle()
        {
            var rig = new Rig();
            RoomModel room = MixedRoom();
            rig.Scan(room, ScanOf(ObjectOf(room, 4)));
            rig.Run(3.0);
            Assert.That(rig.Core.PendingCount, Is.EqualTo(0));

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 600; i++) rig.Core.Tick(rig.Time + 0.001 * i);
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.EqualTo(0L), "the frame path of a settled room is free of garbage");
        }

        [Test]
        public void EmptyInput_NeverThrows()
        {
            var rig = new Rig();
            rig.Core.Submit(null);
            rig.Core.ReportNoPhoto(7);
            rig.Core.BeginRoom(null, null);
            rig.Core.RoomBuilt(null);
            rig.Core.EndRoom();
            rig.Core.SetScanMesh(null, null);
            rig.Core.RoomBuilt(new RoomModel { Id = "empty", Objects = new RoomObject[0] });
            rig.Run(3.0);
            Assert.That(rig.Core.ObjectCount, Is.EqualTo(0));
        }

        static double LoadedRoomSeconds() { return OnPhoneObjectsCore.LoadedRoomGraceSeconds + 1.0; }
    }
}

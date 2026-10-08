using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.World.Tests
{
    /// <summary>
    /// The standalone-phone additions to the RoomModel (docs/plans/standalone-phone/INDEX.md, sections 6.1 and 8 TT1):
    /// the optional object keys tags, prop and propCells, the mesh states matched and scanCut, the geometry hash that
    /// ignores them, ObjectCategories.FromRoomPlan and the ScanMesh contract. Written from the plan, not the code.
    /// </summary>
    public class RoomModelStandaloneTests
    {
        static string Sample() { return RoomFixtures.ReadTextFromProject(RoomFixtures.Sample4x5Table); }

        // Full geometry hashes of the six fixtures, from the independent Python implementation
        // (tools/rooms/roommath.py geometry_hash) before this plan's changes. They must not move (plan section 8, T1).
        static readonly object[] GoldenHashes =
        {
            new object[] { "sample_3x3", "1111bf11f2152ef055a8f3e49e37b57fdcf6ee8825c484e24fdca38440fb0fcb" },
            new object[] { "sample_L_3x4", "9c3eaf728bc2e6e6be6f1471cb2a150aef4723af92052c7b8816daf1ff2580cb" },
            new object[] { "sample_4x5_table", "61de428db392e65b98aee11addde4c9a33108158301ced3c554a2daeea1e27b0" },
            new object[] { "budget_room", "d7ee2b85f3310cf1ec4d135e74e4f8bbaac6c35e998e16ea2ab759136d480403" },
            new object[] { "tiny_standing", "c11478f2305ddd479841ac06c01b6f8ee062c3ea5983e7b3c72c67f4672c1d34" },
            new object[] { "budget_room_120k", "e8c30d74ba68deded3e482efa8353e1c11bb6a1cd1ec662a7715ad68058285c6" },
        };

        static readonly string[] SampleTags = { "dining", "rectangular", "ChairType.dining" };
        static readonly byte[] SampleCells = { 64, 97, 127 };
        const string SampleProp = "table_kenney_dining_01";

        // ---- enum and defaults ---------------------------------------------------------------------------

        [Test]
        public void MeshState_HasTheTwoNewStatesWithFixedValues()
        {
            Assert.That((byte)MeshState.Placeholder, Is.EqualTo(0));
            Assert.That((byte)MeshState.Requested, Is.EqualTo(1));
            Assert.That((byte)MeshState.Ready, Is.EqualTo(2));
            Assert.That((byte)MeshState.BuiltIn, Is.EqualTo(3));
            Assert.That((byte)MeshState.StandIn, Is.EqualTo(4));
            Assert.That((byte)MeshState.Matched, Is.EqualTo(5));
            Assert.That((byte)MeshState.ScanCut, Is.EqualTo(6));
            Assert.That(RoomModelJson.MeshStateName(MeshState.Matched), Is.EqualTo("matched"));
            Assert.That(RoomModelJson.MeshStateName(MeshState.ScanCut), Is.EqualTo("scanCut"));
            Assert.That(RoomModelJson.MeshStateName(MeshState.StandIn), Is.EqualTo("standIn"));
        }

        [Test]
        public void RoomObject_NewFieldsDefaultToEmpty()
        {
            var obj = new RoomObject();
            Assert.That(obj.Tags, Is.Not.Null);
            Assert.That(obj.Tags, Is.Empty);
            Assert.That(obj.Prop, Is.EqualTo(""));
            Assert.That(obj.PropCells, Is.Null);
        }

        // ---- the new keys in the file --------------------------------------------------------------------

        [Test]
        public void NewKeys_RoundTrip()
        {
            RoomModel room = RoomModelJson.Read(Sample());
            Dress(room.Objects[0]);
            string text = RoomModelJson.Write(room);
            Assert.That(text, Does.Contain("\"tags\""));
            Assert.That(text, Does.Contain("\"prop\""));
            Assert.That(text, Does.Contain("\"propCells\""));
            Assert.That(text, Does.Contain("\"matched\""));

            RoomModel again = RoomModelJson.Read(text);
            RoomObject o = again.Objects[0];
            Assert.That(o.Mesh, Is.EqualTo(MeshState.Matched));
            Assert.That(o.Tags, Is.EqualTo(SampleTags));
            Assert.That(o.Prop, Is.EqualTo(SampleProp));
            Assert.That(o.PropCells, Is.EqualTo(SampleCells));
            // The writer stays canonical with the new keys: a file written twice is identical.
            Assert.That(RoomModelJson.Write(again), Is.EqualTo(text));
        }

        [Test]
        public void NewKeys_RoundTripAtTheirSchemaLimits()
        {
            RoomModel room = RoomModelJson.Read(Sample());
            RoomObject obj = room.Objects[0];
            obj.Mesh = MeshState.Matched;
            obj.Tags = new string[16];
            for (int k = 0; k < obj.Tags.Length; k++) obj.Tags[k] = new string((char)('a' + k), 48);
            obj.Prop = new string('p', 64);
            obj.PropCells = new byte[] { 64, 65, 80, 96, 100, 112, 126, 127 };
            // A tag that needs escaping comes back verbatim.
            obj.Tags[3] = "quote\" back\\slash";

            RoomObject again = RoomModelJson.Read(RoomModelJson.Write(room)).Objects[0];
            Assert.That(again.Tags, Is.EqualTo(obj.Tags));
            Assert.That(again.Prop, Is.EqualTo(obj.Prop));
            Assert.That(again.PropCells, Is.EqualTo(obj.PropCells));

            obj.PropCells = new byte[] { 70 };
            Assert.That(RoomModelJson.Read(RoomModelJson.Write(room)).Objects[0].PropCells, Is.EqualTo(new byte[] { 70 }));
        }

        [Test]
        public void ScanCutState_RoundTrips()
        {
            RoomModel room = RoomModelJson.Read(Sample());
            room.Objects[0].Mesh = MeshState.ScanCut;
            string text = RoomModelJson.Write(room);
            Assert.That(text, Does.Contain("\"mesh\": \"scanCut\""));
            Assert.That(RoomModelJson.Read(text).Objects[0].Mesh, Is.EqualTo(MeshState.ScanCut));
        }

        [Test]
        public void MeshStates_AreReadByTheirJsonNames()
        {
            string text = Sample();
            Assert.That(text, Does.Contain("\"mesh\": \"placeholder\""));
            Assert.That(RoomModelJson.Read(text.Replace("\"mesh\": \"placeholder\"", "\"mesh\": \"matched\"")).Objects[0].Mesh,
                Is.EqualTo(MeshState.Matched));
            Assert.That(RoomModelJson.Read(text.Replace("\"mesh\": \"placeholder\"", "\"mesh\": \"scanCut\"")).Objects[0].Mesh,
                Is.EqualTo(MeshState.ScanCut));
        }

        [Test]
        public void AbsentKeys_GiveTheDefaults()
        {
            RoomObject obj = RoomModelJson.Read(Sample()).Objects[0];
            Assert.That(obj.Tags, Is.Not.Null);
            Assert.That(obj.Tags, Is.Empty);
            Assert.That(obj.Prop, Is.EqualTo(""));
            Assert.That(obj.PropCells, Is.Null);
        }

        [Test]
        public void EmptyKeys_AreNotWritten()
        {
            RoomModel room = RoomModelJson.Read(Sample());
            string plain = RoomModelJson.Write(room);
            Assert.That(plain, Is.EqualTo(Sample()));
            Assert.That(plain, Does.Not.Contain("\"tags\""));
            Assert.That(plain, Does.Not.Contain("\"prop\""));
            Assert.That(plain, Does.Not.Contain("\"propCells\""));

            // Empty values are as good as absent: the file is byte for byte the old one.
            RoomObject obj = room.Objects[0];
            obj.Tags = Array.Empty<string>();
            obj.Prop = "";
            obj.PropCells = new byte[0];
            Assert.That(RoomModelJson.Write(room), Is.EqualTo(plain));
            obj.PropCells = null;
            Assert.That(RoomModelJson.Write(room), Is.EqualTo(plain));

            // Each key alone is written when it is the only one set.
            obj.Tags = new[] { "swivel" };
            string tagsOnly = RoomModelJson.Write(room);
            Assert.That(tagsOnly, Does.Contain("\"tags\""));
            Assert.That(tagsOnly, Does.Not.Contain("\"prop\""));
            Assert.That(tagsOnly, Does.Not.Contain("\"propCells\""));
            obj.Tags = Array.Empty<string>();
            obj.Prop = SampleProp;
            string propOnly = RoomModelJson.Write(room);
            Assert.That(propOnly, Does.Contain("\"prop\""));
            Assert.That(propOnly, Does.Not.Contain("\"tags\""));
            Assert.That(propOnly, Does.Not.Contain("\"propCells\""));
        }

        [Test]
        public void NewKeys_OutsideTheSchema_AreRefused()
        {
            string ok = WithKeys("[\"dining\"]", "\"" + SampleProp + "\"", "[64, 127]");
            Assert.That(RoomModelJson.Read(ok).Objects[0].PropCells, Is.EqualTo(new byte[] { 64, 127 }));

            var seventeen = new string[17];
            for (int k = 0; k < seventeen.Length; k++) seventeen[k] = "\"t" + k + "\"";
            AssertRefused(WithKeys("[" + string.Join(", ", seventeen) + "]", null, null), "tags");
            AssertRefused(WithKeys("[\"" + new string('x', 49) + "\"]", null, null), "tags");
            AssertRefused(WithKeys("[3]", null, null), "tags");
            AssertRefused(WithKeys("\"dining\"", null, null), "tags");

            AssertRefused(WithKeys(null, "\"" + new string('p', 65) + "\"", null), "prop");
            AssertRefused(WithKeys(null, "7", null), "prop");

            AssertRefused(WithKeys(null, null, "[63]"), "propCells");
            AssertRefused(WithKeys(null, null, "[128]"), "propCells");
            AssertRefused(WithKeys(null, null, "[64.5]"), "propCells");
            AssertRefused(WithKeys(null, null, "[]"), "propCells");
            AssertRefused(WithKeys(null, null, "[64, 65, 66, 67, 68, 69, 70, 71, 72]"), "propCells");
            AssertRefused(WithKeys(null, null, "\"QEA=\""), "propCells");
        }

        // ---- old files -----------------------------------------------------------------------------------

        [Test]
        public void EveryFixture_StillLoadsAndWritesBackUnchanged()
        {
            foreach (string name in RoomFixtures.All)
            {
                string text = RoomFixtures.ReadTextFromProject(name);
                Assert.That(RoomModelJson.TryRead(text, out RoomModel room, out string error), Is.True, name + ": " + error);
                Assert.That(RoomModelJson.Write(room), Is.EqualTo(text), name);
                foreach (RoomObject obj in room.Objects)
                {
                    Assert.That(obj.Tags, Is.Not.Null.And.Empty, name + " object " + obj.Id);
                    Assert.That(obj.Prop, Is.EqualTo(""), name + " object " + obj.Id);
                    Assert.That(obj.PropCells, Is.Null, name + " object " + obj.Id);
                }
            }
        }

        [Test]
        public void EveryFileInTheRoomsFolder_StillLoads()
        {
            string folder = Path.GetDirectoryName(RoomFixtures.ProjectPath(RoomFixtures.Sample3x3));
            string[] files = Directory.GetFiles(folder, "*.json");
            Assert.That(files.Length, Is.GreaterThanOrEqualTo(RoomFixtures.All.Length));
            foreach (string path in files)
            {
                string text = File.ReadAllText(path);
                Assert.That(RoomModelJson.TryRead(text, out RoomModel _, out string error), Is.True, path + ": " + error);
            }
        }

        // ---- the geometry hash ---------------------------------------------------------------------------

        [TestCaseSource(nameof(GoldenHashes))]
        public void GeometryHash_OfEveryFixtureHasNotMoved(string name, string hash)
        {
            Assert.That(RoomModelJson.GeometryHash(RoomFixtures.LoadFromProject(name)), Is.EqualTo(hash));
        }

        [Test]
        public void GeometryHash_IgnoresTheNewKeysAndStates()
        {
            RoomModel room = RoomModelJson.Read(Sample());
            string hash = RoomModelJson.GeometryHash(room);

            Dress(room.Objects[0]);
            Assert.That(RoomModelJson.GeometryHash(room), Is.EqualTo(hash));

            room.Objects[0].Mesh = MeshState.ScanCut;
            room.Objects[0].Tags = new[] { "other", "tags" };
            room.Objects[0].Prop = "another_prop";
            room.Objects[0].PropCells = new byte[] { 120 };
            Assert.That(RoomModelJson.GeometryHash(room), Is.EqualTo(hash));

            // Through a file and back.
            Dress(room.Objects[0]);
            RoomModel again = RoomModelJson.Read(RoomModelJson.Write(room));
            Assert.That(RoomModelJson.GeometryHash(again), Is.EqualTo(hash));

            // And the hash still follows the geometry.
            again.Objects[0].Size.y += 0.01f;
            Assert.That(RoomModelJson.GeometryHash(again), Is.Not.EqualTo(hash));
        }

        // ---- the schema validator ------------------------------------------------------------------------

        [Test]
        public void ValidateRoomPy_AcceptsTheNewKeysAndStates()
        {
            RoomModel room = RoomModelJson.Read(Sample());
            room.Id = "tt1_standalone";
            Dress(room.Objects[0]);
            Assert.That(RunValidator(RoomModelJson.Write(room), out string output), Is.EqualTo(0), output);

            room.Objects[0].Mesh = MeshState.ScanCut;
            room.Objects[0].Prop = "";
            room.Objects[0].PropCells = null;
            Assert.That(RunValidator(RoomModelJson.Write(room), out output), Is.EqualTo(0), output);
        }

        [Test]
        public void ValidateRoomPy_RefusesNewKeysOutsideTheSchema()
        {
            RoomModel room = RoomModelJson.Read(Sample());
            room.Id = "tt1_standalone";
            Dress(room.Objects[0]);
            string text = RoomModelJson.Write(room);
            Assert.That(RunValidator(text, out string output), Is.EqualTo(0), output);

            room.Objects[0].PropCells = null;
            string noCells = RoomModelJson.Write(room);
            Assert.That(noCells, Does.Not.Contain("propCells"));

            Assert.That(RunValidator(Insert(noCells, "\"propCells\": [128]"), out output), Is.Not.EqualTo(0), "cell 128: " + output);
            Assert.That(RunValidator(Insert(noCells, "\"propCells\": [63]"), out output), Is.Not.EqualTo(0), "cell 63: " + output);
            Assert.That(RunValidator(Insert(noCells, "\"propCells\": []"), out output), Is.Not.EqualTo(0), "no cells: " + output);
            Assert.That(RunValidator(Insert(noCells, "\"propCells\": [64, 65, 66, 67, 68, 69, 70, 71, 72]"), out output),
                Is.Not.EqualTo(0), "nine cells: " + output);
            room.Objects[0].Tags = Array.Empty<string>();
            room.Objects[0].Prop = "";
            string bare = RoomModelJson.Write(room);
            Assert.That(RunValidator(Insert(bare, "\"tags\": [\"" + new string('x', 49) + "\"]"), out output), Is.Not.EqualTo(0),
                "long tag: " + output);
            var seventeen = new string[17];
            for (int k = 0; k < seventeen.Length; k++) seventeen[k] = "\"t" + k + "\"";
            Assert.That(RunValidator(Insert(bare, "\"tags\": [" + string.Join(", ", seventeen) + "]"), out output),
                Is.Not.EqualTo(0), "17 tags: " + output);
            Assert.That(RunValidator(Insert(bare, "\"prop\": \"" + new string('p', 65) + "\""), out output), Is.Not.EqualTo(0),
                "long prop: " + output);
            Assert.That(RunValidator(text.Replace("\"matched\"", "\"melted\""), out output), Is.Not.EqualTo(0), "melted: " + output);
        }

        // ---- FromRoomPlan --------------------------------------------------------------------------------

        [TestCase("bathtub", ObjectCategory.Bathtub)]
        [TestCase("bed", ObjectCategory.Bed)]
        [TestCase("chair", ObjectCategory.Chair)]
        [TestCase("dishwasher", ObjectCategory.Dishwasher)]
        [TestCase("fireplace", ObjectCategory.Fireplace)]
        [TestCase("oven", ObjectCategory.Oven)]
        [TestCase("refrigerator", ObjectCategory.Refrigerator)]
        [TestCase("sink", ObjectCategory.Sink)]
        [TestCase("sofa", ObjectCategory.Couch)]
        [TestCase("stairs", ObjectCategory.Stairs)]
        [TestCase("storage", ObjectCategory.Storage)]
        [TestCase("stove", ObjectCategory.Stove)]
        [TestCase("table", ObjectCategory.Table)]
        [TestCase("television", ObjectCategory.Screen)]
        [TestCase("toilet", ObjectCategory.Toilet)]
        [TestCase("washerDryer", ObjectCategory.WasherDryer)]
        public void FromRoomPlan_MapsEveryRoomPlanCategory(string name, ObjectCategory category)
        {
            Assert.That(ObjectCategories.FromRoomPlan(name), Is.EqualTo(category));
        }

        [TestCase("piano")]
        [TestCase("")]
        [TestCase((string)null)]
        [TestCase("lamp")]
        [TestCase("unknown")]
        public void FromRoomPlan_AnythingElseIsOther(string name)
        {
            Assert.That(ObjectCategories.FromRoomPlan(name), Is.EqualTo(ObjectCategory.Other));
        }

        // ---- ScanMesh and its sink -----------------------------------------------------------------------

        [Test]
        public void ScanMesh_HoldsItsArraysAndCountsTriangles()
        {
            var vertices = new[] { new float3(0f, 0f, 0f), new float3(1f, 0f, 0f), new float3(0f, 1f, 0f), new float3(0f, 0f, 1f) };
            var indices = new[] { 0, 1, 2, 0, 2, 3 };
            var classes = new byte[] { 4, 2 };
            var mesh = new ScanMesh(vertices, indices, classes);
            Assert.That(mesh.TriangleCount, Is.EqualTo(2));
            Assert.That(mesh.Vertices, Is.EqualTo(vertices));
            Assert.That(mesh.Indices, Is.EqualTo(indices));
            Assert.That(mesh.Classes, Is.EqualTo(classes));

            var unclassified = new ScanMesh(vertices, indices, null);
            Assert.That(unclassified.TriangleCount, Is.EqualTo(2));
            Assert.That(unclassified.Classes, Is.Null);
        }

        [Test]
        public void NullScanMeshSink_AcceptsMeshesAndNull()
        {
            IScanMeshSink sink = new NullScanMeshSink();
            var mesh = new ScanMesh(new[] { float3.zero, new float3(1f, 0f, 0f), new float3(0f, 0f, 1f) }, new[] { 0, 1, 2 }, null);
            Assert.DoesNotThrow(() => sink.SetScanMesh("room_a", mesh));
            Assert.DoesNotThrow(() => sink.SetScanMesh("room_a", null));
        }

#if !ATLAS_HEADLESS
        [Test, Category("Unity")]
        public void IRoomContent_SetOnPhoneMeshFadesInAThirdOfASecondByDefault()
        {
            var method = typeof(IRoomContent).GetMethod("SetOnPhoneMesh");
            Assert.That(method, Is.Not.Null);
            Assert.That(method.ReturnType, Is.EqualTo(typeof(bool)));
            var parameters = method.GetParameters();
            Assert.That(parameters.Length, Is.EqualTo(4));
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(int)));
            Assert.That(parameters[1].ParameterType, Is.EqualTo(typeof(UnityEngine.Mesh)));
            Assert.That(parameters[2].ParameterType, Is.EqualTo(typeof(MeshState)));
            Assert.That(parameters[3].ParameterType, Is.EqualTo(typeof(float)));
            Assert.That(parameters[3].HasDefaultValue, Is.True);
            Assert.That((float)parameters[3].DefaultValue, Is.EqualTo(0.3f));
        }
#endif

        // ---- helpers -------------------------------------------------------------------------------------

        static void Dress(RoomObject obj)
        {
            obj.Mesh = MeshState.Matched;
            obj.Tags = (string[])SampleTags.Clone();
            obj.Prop = SampleProp;
            obj.PropCells = (byte[])SampleCells.Clone();
        }

        /// <summary>The sample with the given raw JSON values for tags, prop and propCells on its object (null leaves the key out).</summary>
        static string WithKeys(string tags, string prop, string propCells)
        {
            string text = Sample().Replace("\"mesh\": \"placeholder\"", "\"mesh\": \"matched\"");
            string extra = "";
            if (tags != null) extra += ", \"tags\": " + tags;
            if (prop != null) extra += ", \"prop\": " + prop;
            if (propCells != null) extra += ", \"propCells\": " + propCells;
            string result = text.Replace("\"triTarget\": 15000}", "\"triTarget\": 15000" + extra + "}");
            if (extra.Length > 0) Assert.That(result, Is.Not.EqualTo(text), "the sample's object line changed");
            return result;
        }

        /// <summary>Adds one raw key at the end of the first object of a written file.</summary>
        static string Insert(string text, string rawKey)
        {
            int objects = text.IndexOf("\"objects\": [", StringComparison.Ordinal);
            Assert.That(objects, Is.GreaterThan(0));
            int end = text.IndexOf("}\n", objects, StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThan(objects));
            return text.Substring(0, end) + ", " + rawKey + text.Substring(end);
        }

        static void AssertRefused(string json, string named)
        {
            var e = Assert.Throws<RoomModelFormatException>(() => RoomModelJson.Read(json));
            Assert.That(e.Message, Does.Contain(named));
        }

        static string RepoDir()
        {
            string repo = Environment.GetEnvironmentVariable("ATLAS_REPO_DIR");
            if (!string.IsNullOrEmpty(repo)) return repo;
            string project = Environment.GetEnvironmentVariable(RoomFixtures.ProjectDirVariable);
            if (string.IsNullOrEmpty(project)) project = Directory.GetCurrentDirectory();   // the Editor runs in ATLAS/
            return Path.GetDirectoryName(Path.GetFullPath(project).TrimEnd(Path.DirectorySeparatorChar));
        }

        /// <summary>Runs tools/rooms/validate_room.py on the text; returns its exit code.</summary>
        static int RunValidator(string json, out string output)
        {
            string script = Path.Combine(RepoDir(), "tools", "rooms", "validate_room.py");
            Assert.That(File.Exists(script), Is.True, "no validator at " + script);
            string file = Path.Combine(Path.GetTempPath(), "atlas_tt1_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(file, json);
            try
            {
                var info = new ProcessStartInfo("python3", "\"" + script + "\" \"" + file + "\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(script),
                };
                using (Process p = Process.Start(info))
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    Assert.That(p.WaitForExit(60000), Is.True, "validate_room.py did not finish");
                    output = stdout + stderr;
                    return p.ExitCode;
                }
            }
            finally
            {
                File.Delete(file);
            }
        }
    }
}

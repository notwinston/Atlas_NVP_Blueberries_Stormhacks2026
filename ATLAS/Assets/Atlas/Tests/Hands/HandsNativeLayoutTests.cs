using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Hands.Tests
{
    /// <summary>
    /// The seam between the native detector and C# (documents/ATLAS_Hands_Design.md section 4.2, Appendix C).
    /// Assets/Plugins/iOS/AtlasHands.swift writes its result by byte offset straight into a
    /// <see cref="HandsDetection"/>, and no compiler sees both sides: Swift cannot declare the struct and the
    /// build has no C header. So the layout is written three times (the two tables below, the table in the
    /// Swift file's comment with the constants its code stores through, and the C# structs) and these tests
    /// hold the three together. They also hold the exported function names, the joint order, the ARKit to
    /// Unity conversion of the camera pose and what the source makes of the plugin's answers, none of which
    /// can be run without a phone.
    /// </summary>
    public unsafe class HandsNativeLayoutTests
    {
        /// <summary>One field of the record: where it is, what it is, its name in the plugin and in C#.</summary>
        sealed class Row
        {
            public readonly int Offset;
            public readonly string Type;
            public readonly string Name;
            public readonly string Field;

            public Row(int offset, string type, string name, string field)
            {
                Offset = offset;
                Type = type;
                Name = name;
                Field = field;
            }

            public override string ToString() { return Offset + " " + Type + " " + Name; }
        }

        const int ResultSize = 2712;
        const int HandSize = 1288;

        // The table the Swift file repeats in its comment, row for row.
        static readonly Row[] Result =
        {
            new Row(0, "Double", "captureTime", "CaptureTime"),
            new Row(8, "Double", "pollTime", "PollTime"),
            new Row(16, "Int32", "version", "Version"),
            new Row(20, "Int32", "sequence", "Sequence"),
            new Row(24, "Int32", "handCount", "HandCount"),
            new Row(28, "Int32", "imageWidth", "ImageWidth"),
            new Row(32, "Int32", "imageHeight", "ImageHeight"),
            new Row(36, "Int32", "depthWidth", "DepthWidth"),
            new Row(40, "Int32", "depthHeight", "DepthHeight"),
            new Row(44, "Int32", "skipped", "Skipped"),
            new Row(48, "Float", "fx", "Fx"),
            new Row(52, "Float", "fy", "Fy"),
            new Row(56, "Float", "cx", "Cx"),
            new Row(60, "Float", "cy", "Cy"),
            new Row(64, "Float", "requestMs", "RequestMs"),
            new Row(68, "Float", "reserved", "_reserved"),
            new Row(72, "Float[16]", "cameraToSession", "_cameraToSession"),
            new Row(136, "hand", "hand0", "Hand0"),
            new Row(1424, "hand", "hand1", "Hand1"),
        };

        static readonly Row[] Hand =
        {
            new Row(0, "Int32", "side", "Side"),
            new Row(4, "Float", "confidence", "Confidence"),
            new Row(8, "Int32", "palmCount", "PalmCount"),
            new Row(12, "Int32", "reserved", "_reserved"),
            new Row(16, "Float[64]", "palmU", "_palmU"),
            new Row(272, "Float[64]", "palmV", "_palmV"),
            new Row(528, "Float[64]", "palmDepth", "_palmDepth"),
            new Row(784, "Float[21]", "jointX", "_jointX"),
            new Row(868, "Float[21]", "jointY", "_jointY"),
            new Row(952, "Float[21]", "jointConfidence", "_jointConfidence"),
            new Row(1036, "Float[21]", "jointDepth", "_jointDepth"),
            new Row(1120, "Float[21]", "jointDepthSpread", "_jointDepthSpread"),
            new Row(1204, "Int32[21]", "jointDepthConfidence", "_jointDepthConfidence"),
        };

        // Vision's joint names in the order of HandJoint. Vision calls the thumb's middle joint MP.
        static readonly string[] VisionJoints =
        {
            "wrist",
            "thumbCMC", "thumbMP", "thumbIP", "thumbTip",
            "indexMCP", "indexPIP", "indexDIP", "indexTip",
            "middleMCP", "middlePIP", "middleDIP", "middleTip",
            "ringMCP", "ringPIP", "ringDIP", "ringTip",
            "littleMCP", "littlePIP", "littleDIP", "littleTip",
        };

        // The project folder: the current directory inside Unity, ATLAS_PROJECT_DIR in the headless run.
        static string ProjectDir
        {
            get
            {
                string project = Environment.GetEnvironmentVariable("ATLAS_PROJECT_DIR");
                return string.IsNullOrEmpty(project) ? Directory.GetCurrentDirectory() : project;
            }
        }

        /// <summary>The text of the plugin.</summary>
        static string Plugin
        {
            get { return File.ReadAllText(Path.Combine(ProjectDir, "Assets", "Plugins", "iOS", "AtlasHands.swift")); }
        }

        /// <summary>The text of the C# source that imports it.</summary>
        static string Source
        {
            get { return File.ReadAllText(Path.Combine(ProjectDir, "Assets", "Atlas", "Hands", "ARKit", "VisionHandsSource.cs")); }
        }

        static int SizeOf(string type)
        {
            Match m = Regex.Match(type, @"^(\w+)(?:\[(\d+)\])?$");
            Assert.That(m.Success, Is.True, type);
            int count = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1;
            switch (m.Groups[1].Value)
            {
                case "Double": return 8 * count;
                case "Int32": return 4 * count;
                case "Float": return 4 * count;
                case "hand": return HandSize * count;
                default: Assert.Fail("unknown type " + type); return 0;
            }
        }

        static int Offset(Row[] table, string name)
        {
            foreach (Row row in table)
                if (row.Name == name) return row.Offset;
            Assert.Fail("no row " + name);
            return -1;
        }

        static void AssertNoHole(Row[] table, int size, string what)
        {
            int next = 0;
            foreach (Row row in table)
            {
                Assert.That(row.Offset, Is.EqualTo(next), what + ": " + row.Name + " does not follow the field before it");
                next += SizeOf(row.Type);
            }
            Assert.That(next, Is.EqualTo(size), what + ": the fields do not add up to the record's size");
        }

        static void AssertStruct(Type type, Row[] table, int size)
        {
            Assert.That(Marshal.SizeOf(type), Is.EqualTo(size), type.Name);
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            // Every field of the struct has a row, and every row a field: a field added on one side only fails here.
            Assert.That(fields.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                        Is.EqualTo(table.Select(r => r.Field).OrderBy(n => n, StringComparer.Ordinal).ToArray()), type.Name);
            foreach (Row row in table)
            {
                Assert.That((int)Marshal.OffsetOf(type, row.Field), Is.EqualTo(row.Offset), type.Name + "." + row.Field);
                FieldInfo field = fields.First(f => f.Name == row.Field);
                Assert.That(Marshal.SizeOf(field.FieldType), Is.EqualTo(SizeOf(row.Type)), type.Name + "." + row.Field + " size");
            }
        }

        [Test]
        public void TheTables_HaveNoHoleAndAddUp()
        {
            AssertNoHole(Result, ResultSize, "result");
            AssertNoHole(Hand, HandSize, "hand");
            Assert.That(ResultSize, Is.EqualTo(HandsDetection.NativeSize));
            Assert.That(Offset(Result, "hand1") - Offset(Result, "hand0"), Is.EqualTo(HandSize));
        }

        [Test]
        public void EveryFieldOfTheStructs_IsWhereTheTablesSay()
        {
            Assert.That(sizeof(HandsDetection), Is.EqualTo(ResultSize));
            Assert.That(sizeof(DetectedHand), Is.EqualTo(HandSize));
            AssertStruct(typeof(HandsDetection), Result, ResultSize);
            AssertStruct(typeof(DetectedHand), Hand, HandSize);
            Assert.That(HandsDetection.MaxHands, Is.EqualTo(2));
            Assert.That(DetectedHand.MaxPalmSamples, Is.EqualTo(64));
            Assert.That(HandJoints.Count, Is.EqualTo(21));
        }

        // The plugin's side of the seam, played in C#: bytes stored at the tables' offsets, then read through the
        // public members of HandsDetection, which is what the lift does with a record from the phone.
        [Test]
        public void ARecordWrittenAtTheTablesOffsets_ReadsBackThroughTheStructs()
        {
            byte[] bytes = new byte[ResultSize];
            HandsDetection detection;
            fixed (byte* p = bytes)
            {
                *(double*)(p + Offset(Result, "captureTime")) = 5000.25;
                *(double*)(p + Offset(Result, "pollTime")) = 5000.3125;
                *(int*)(p + Offset(Result, "version")) = 1;
                *(int*)(p + Offset(Result, "sequence")) = 77;
                *(int*)(p + Offset(Result, "handCount")) = 2;
                *(int*)(p + Offset(Result, "imageWidth")) = 1280;
                *(int*)(p + Offset(Result, "imageHeight")) = 720;
                *(int*)(p + Offset(Result, "depthWidth")) = 256;
                *(int*)(p + Offset(Result, "depthHeight")) = 144;
                *(int*)(p + Offset(Result, "skipped")) = 3;
                *(float*)(p + Offset(Result, "fx")) = 931f;
                *(float*)(p + Offset(Result, "fy")) = 932f;
                *(float*)(p + Offset(Result, "cx")) = 641f;
                *(float*)(p + Offset(Result, "cy")) = 358f;
                *(float*)(p + Offset(Result, "requestMs")) = 19.5f;
                float* camera = (float*)(p + Offset(Result, "cameraToSession"));
                for (int i = 0; i < 16; i++) camera[i] = 100f + i;

                for (int h = 0; h < 2; h++)
                {
                    byte* hand = p + Offset(Result, h == 0 ? "hand0" : "hand1");
                    float tag = 1000f * (h + 1);
                    *(int*)(hand + Offset(Hand, "side")) = h == 0 ? 1 : -1;
                    *(float*)(hand + Offset(Hand, "confidence")) = 0.5f + 0.25f * h;
                    *(int*)(hand + Offset(Hand, "palmCount")) = 64 - h;
                    for (int i = 0; i < DetectedHand.MaxPalmSamples; i++)
                    {
                        ((float*)(hand + Offset(Hand, "palmU")))[i] = tag + i;
                        ((float*)(hand + Offset(Hand, "palmV")))[i] = tag + 100f + i;
                        ((float*)(hand + Offset(Hand, "palmDepth")))[i] = tag + 200f + i;
                    }
                    for (int j = 0; j < HandJoints.Count; j++)
                    {
                        ((float*)(hand + Offset(Hand, "jointX")))[j] = tag + 300f + j;
                        ((float*)(hand + Offset(Hand, "jointY")))[j] = tag + 400f + j;
                        ((float*)(hand + Offset(Hand, "jointConfidence")))[j] = tag + 500f + j;
                        ((float*)(hand + Offset(Hand, "jointDepth")))[j] = tag + 600f + j;
                        ((float*)(hand + Offset(Hand, "jointDepthSpread")))[j] = tag + 700f + j;
                        ((int*)(hand + Offset(Hand, "jointDepthConfidence")))[j] = (h + 1) * 100 + j;
                    }
                }
                detection = *(HandsDetection*)p;
            }

            Assert.That(detection.CaptureTime, Is.EqualTo(5000.25));
            Assert.That(detection.PollTime, Is.EqualTo(5000.3125));
            Assert.That(detection.AgeSeconds, Is.EqualTo(0.0625f));
            Assert.That(detection.Version, Is.EqualTo(HandsDetection.CurrentVersion));
            Assert.That(detection.Sequence, Is.EqualTo(77));
            Assert.That(detection.HandCount, Is.EqualTo(2));
            Assert.That(detection.ImageWidth, Is.EqualTo(1280));
            Assert.That(detection.ImageHeight, Is.EqualTo(720));
            Assert.That(detection.DepthWidth, Is.EqualTo(256));
            Assert.That(detection.DepthHeight, Is.EqualTo(144));
            Assert.That(detection.Skipped, Is.EqualTo(3));
            Assert.That(detection.Fx, Is.EqualTo(931f));
            Assert.That(detection.Fy, Is.EqualTo(932f));
            Assert.That(detection.Cx, Is.EqualTo(641f));
            Assert.That(detection.Cy, Is.EqualTo(358f));
            Assert.That(detection.RequestMs, Is.EqualTo(19.5f));
            float4x4 pose = detection.CameraToSession;
            for (int column = 0; column < 4; column++)
                for (int row = 0; row < 4; row++)
                    Assert.That(pose[column][row], Is.EqualTo(100f + column * 4 + row), "column " + column + " row " + row);

            for (int h = 0; h < 2; h++)
            {
                DetectedHand hand = h == 0 ? detection.Hand0 : detection.Hand1;
                float tag = 1000f * (h + 1);
                Assert.That(hand.Side, Is.EqualTo(h == 0 ? 1 : -1));
                Assert.That(hand.Confidence, Is.EqualTo(0.5f + 0.25f * h));
                Assert.That(hand.PalmCount, Is.EqualTo(64 - h));
                for (int i = 0; i < DetectedHand.MaxPalmSamples; i++)
                {
                    float3 sample = hand.GetPalmSample(i);
                    Assert.That(sample.x, Is.EqualTo(tag + i), "palmU " + i);
                    Assert.That(sample.y, Is.EqualTo(tag + 100f + i), "palmV " + i);
                    Assert.That(sample.z, Is.EqualTo(tag + 200f + i), "palmDepth " + i);
                }
                for (int j = 0; j < HandJoints.Count; j++)
                {
                    JointSample joint = hand.GetJoint(j);
                    Assert.That(joint.Pixel.x, Is.EqualTo(tag + 300f + j), "jointX " + j);
                    Assert.That(joint.Pixel.y, Is.EqualTo(tag + 400f + j), "jointY " + j);
                    Assert.That(joint.Confidence, Is.EqualTo(tag + 500f + j), "jointConfidence " + j);
                    Assert.That(joint.Depth, Is.EqualTo(tag + 600f + j), "jointDepth " + j);
                    Assert.That(joint.DepthSpread, Is.EqualTo(tag + 700f + j), "jointDepthSpread " + j);
                    Assert.That(joint.DepthConfidence, Is.EqualTo((h + 1) * 100 + j), "jointDepthConfidence " + j);
                }
            }
        }

        // "//   record result 2712" opens a table; "//      48  Float      fx   ..." is a row of it.
        static Dictionary<string, List<Row>> CommentTables(string swift, out Dictionary<string, int> sizes)
        {
            var tables = new Dictionary<string, List<Row>>();
            sizes = new Dictionary<string, int>();
            List<Row> current = null;
            foreach (string raw in swift.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                Match header = Regex.Match(line, @"^//\s+record\s+(\w+)\s+(\d+)\s*$");
                if (header.Success)
                {
                    current = new List<Row>();
                    tables[header.Groups[1].Value] = current;
                    sizes[header.Groups[1].Value] = int.Parse(header.Groups[2].Value);
                    continue;
                }
                if (current == null) continue;
                Match row = Regex.Match(line, @"^//\s+(\d+)\s+((?:Double|Int32|Float|hand)(?:\[\d+\])?)\s+(\w+)\b");
                if (row.Success) current.Add(new Row(int.Parse(row.Groups[1].Value), row.Groups[2].Value, row.Groups[3].Value, null));
                else current = null;
            }
            return tables;
        }

        // "enum AtlasHandsResultLayout { static let size = 2712 ... }": the numbers the plugin's code stores through.
        static List<KeyValuePair<string, int>> Constants(string swift, string enumName)
        {
            Match body = Regex.Match(swift, @"enum\s+" + enumName + @"\s*\{([^}]*)\}");
            Assert.That(body.Success, Is.True, "AtlasHands.swift has no enum " + enumName);
            var list = new List<KeyValuePair<string, int>>();
            foreach (Match m in Regex.Matches(body.Groups[1].Value, @"static\s+let\s+(\w+)\s*=\s*(\d+)"))
                list.Add(new KeyValuePair<string, int>(m.Groups[1].Value, int.Parse(m.Groups[2].Value)));
            return list;
        }

        static void AssertSwift(string what, Row[] table, int size, List<Row> comment, int commentSize,
                                List<KeyValuePair<string, int>> constants)
        {
            Assert.That(commentSize, Is.EqualTo(size), what + ": size in the comment");
            Assert.That(comment.Select(r => r.ToString()).ToArray(), Is.EqualTo(table.Select(r => r.ToString()).ToArray()),
                        what + ": the comment's table");
            var expected = new List<string> { "size=" + size };
            expected.AddRange(table.Select(r => r.Name + "=" + r.Offset));
            Assert.That(constants.Select(c => c.Key + "=" + c.Value).ToArray(), Is.EqualTo(expected.ToArray()),
                        what + ": the constants the code stores through");
        }

        [Test]
        public void ThePlugin_RepeatsTheTables_InItsCommentAndInItsConstants()
        {
            string swift = Plugin;
            Dictionary<string, int> sizes;
            Dictionary<string, List<Row>> tables = CommentTables(swift, out sizes);
            Assert.That(tables.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), Is.EqualTo(new[] { "hand", "result" }));
            AssertSwift("result", Result, ResultSize, tables["result"], sizes["result"], Constants(swift, "AtlasHandsResultLayout"));
            AssertSwift("hand", Hand, HandSize, tables["hand"], sizes["hand"], Constants(swift, "AtlasHandsHandLayout"));
        }

        static int Arity(string parameters)
        {
            return parameters.Trim().Length == 0 ? 0 : parameters.Split(',').Length;
        }

        // A function exported under one name and imported under another links nowhere but in Xcode.
        [Test]
        public void EveryImportOfTheSource_IsAnExportOfThePlugin_WithTheSameParameters()
        {
            var exported = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(Plugin, @"@_cdecl\(""(\w+)""\)\s*public\s+func\s+(\w+)\(([^)]*)\)"))
            {
                Assert.That(m.Groups[2].Value, Is.EqualTo(m.Groups[1].Value), "the Swift function carries its C name");
                exported[m.Groups[1].Value] = Arity(m.Groups[3].Value);
            }
            var imported = new SortedDictionary<string, int>(StringComparer.Ordinal);
            const string import = @"\[DllImport\(""__Internal""\)\]\s*static\s+extern\s+(?:unsafe\s+)?\w+\s+(\w+)\(([^)]*)\)\s*;";
            foreach (Match m in Regex.Matches(Source, import))
                imported[m.Groups[1].Value] = Arity(m.Groups[2].Value);

            Assert.That(exported.Keys.ToArray(), Is.EqualTo(new[]
            {
                "atlas_hands_poll", "atlas_hands_result_size", "atlas_hands_set_rate",
                "atlas_hands_start", "atlas_hands_stop", "atlas_hands_tick",
            }));
            Assert.That(imported.Select(p => p.Key + "/" + p.Value).ToArray(),
                        Is.EqualTo(exported.Select(p => p.Key + "/" + p.Value).ToArray()));
        }

        [Test]
        public void ThePluginsJoints_AreInTheOrderOfHandJoint()
        {
            const string joints = @"jointNames\s*:\s*\[VNHumanHandPoseObservation\.JointName\]\s*=\s*\[(.*?)\]";
            Match list = Regex.Match(Plugin, joints, RegexOptions.Singleline);
            Assert.That(list.Success, Is.True, "AtlasHands.swift has no jointNames list");
            string[] names = Regex.Matches(list.Groups[1].Value, @"\.(\w+)").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
            Assert.That(names, Is.EqualTo(VisionJoints));
            Assert.That(names.Length, Is.EqualTo(HandJoints.Count));
            for (int joint = 0; joint < HandJoints.Count; joint++)
            {
                string vision = names[joint].ToLowerInvariant().Replace("thumbmp", "thumbmcp");
                Assert.That(vision, Is.EqualTo(((HandJoint)joint).ToString().ToLowerInvariant()), "joint " + joint);
            }
        }

        // ---- What VisionHandsSource does with the plugin's answers ----

        [Test]
        public void ARecord_IsRefused_WhenItsVersionOrItsCountsAreNotThePlugins()
        {
            var detection = new HandsDetection();
            Assert.That(HandsNativeMath.IsRecord(in detection), Is.False, "all zero: version 0");

            HandsSynth.Begin(ref detection, 1, 1280, 720, 930f, 930f, 640f, 360f, 256, 144, 10.0, 10.05, float4x4.identity);
            JointSet open = HandModel.Pose(HandSide.Right, float4.zero, 0f).Transformed(quaternion.identity, new float3(0f, 0f, 0.45f));
            HandsSynth.AddHand(ref detection, in open, 1);
            HandsSynth.AddHand(ref detection, in open, 0);
            Assert.That(HandsNativeMath.IsRecord(in detection), Is.True, "two synthetic hands");

            detection.Version = HandsDetection.CurrentVersion + 1;
            Assert.That(HandsNativeMath.IsRecord(in detection), Is.False, "another version");
            detection.Version = HandsDetection.CurrentVersion;

            foreach (int count in new[] { -1, 3, int.MaxValue })
            {
                detection.HandCount = count;
                Assert.That(HandsNativeMath.IsRecord(in detection), Is.False, count + " hands");
            }
            detection.HandCount = 0;
            Assert.That(HandsNativeMath.IsRecord(in detection), Is.True, "no hand is a record too");
            detection.HandCount = 2;

            foreach (int count in new[] { -1, DetectedHand.MaxPalmSamples + 1 })
            {
                detection.Hand0.PalmCount = count;
                Assert.That(HandsNativeMath.IsRecord(in detection), Is.False, count + " palm samples in the first hand");
                detection.Hand0.PalmCount = DetectedHand.MaxPalmSamples;
                detection.Hand1.PalmCount = count;
                Assert.That(HandsNativeMath.IsRecord(in detection), Is.False, count + " palm samples in the second hand");
                detection.Hand1.PalmCount = 0;
            }
            Assert.That(HandsNativeMath.IsRecord(in detection), Is.True, "64 and 0 palm samples");
        }

        [Test]
        public void TheFault_IsThePluginsNegativeAnswer_ThePollsFirst_AndZeroWhileHealthy()
        {
            Assert.That(HandsNativeMath.Fault(0, 0), Is.EqualTo(0), "nothing due, nothing new");
            Assert.That(HandsNativeMath.Fault(1, 1), Is.EqualTo(0), "a request started, a result copied");
            Assert.That(HandsNativeMath.Fault(1, 0), Is.EqualTo(0));
            Assert.That(HandsNativeMath.Fault(-3, -3), Is.EqualTo(-3), "no camera frame for 2 s");
            Assert.That(HandsNativeMath.Fault(-2, 0), Is.EqualTo(-2), "the tick's error alone");
            Assert.That(HandsNativeMath.Fault(1, -4), Is.EqualTo(-4), "requests failing: only the poll says so");
            Assert.That(HandsNativeMath.Fault(-3, 1), Is.EqualTo(-3), "a last result does not hide that the camera has stopped");
            Assert.That(HandsNativeMath.Fault(-3, -4), Is.EqualTo(-4), "the poll's first");
            Assert.That(HandsNativeMath.Fault(-1, -1), Is.EqualTo(-1), "not started");
        }

        // ---- ARKit's camera pose to Unity's (the conversion VisionHandsSource applies to every result) ----

        static float3 Flip(float3 p) { return new float3(p.x, p.y, -p.z); }

        [Test]
        public void ArKitCamera_WithNoRotation_LooksAlongUnitysForward()
        {
            // ARKit: right-handed, the camera looks along its own -z. Unity's session frame is ARKit's with z negated.
            float4x4 arKit = float4x4.identity;
            arKit.c3 = new float4(1f, 2f, 3f, 1f);
            float4x4 unity = HandsNativeMath.ArKitToUnity(arKit);
            Assert.That(unity.c3, Is.EqualTo(new float4(1f, 2f, -3f, 1f)), "position");
            Assert.That(unity.c0.xyz, Is.EqualTo(new float3(1f, 0f, 0f)), "right");
            Assert.That(unity.c1.xyz, Is.EqualTo(new float3(0f, 1f, 0f)), "up");
            Assert.That(unity.c2.xyz, Is.EqualTo(new float3(0f, 0f, 1f)), "forward");
            Assert.That(math.transform(unity, new float3(0f, 0f, 0.5f)), Is.EqualTo(new float3(1f, 2f, -2.5f)), "half a metre ahead");
        }

        [Test]
        public void ArKitCamera_TurnedLeft_PutsAHandWhereItIs()
        {
            // The camera turned 90 degrees to its left about ARKit's +y (right-handed), standing at (0.5, 1.6, -2):
            // its columns are right (0, 0, -1), up (0, 1, 0), back (1, 0, 0), so it looks along ARKit's -x.
            var arKit = new float4x4(
                new float4(0f, 0f, -1f, 0f),
                new float4(0f, 1f, 0f, 0f),
                new float4(1f, 0f, 0f, 0f),
                new float4(0.5f, 1.6f, -2f, 1f));
            float4x4 unity = HandsNativeMath.ArKitToUnity(arKit);
            Assert.That(unity.c3.xyz, Is.EqualTo(new float3(0.5f, 1.6f, 2f)), "position");
            Assert.That(unity.c2.xyz, Is.EqualTo(new float3(-1f, 0f, 0f)), "forward: still along -x, which the flip leaves alone");
            Assert.That(unity.c1.xyz, Is.EqualTo(new float3(0f, 1f, 0f)), "up");
            Assert.That(unity.c0.xyz, Is.EqualTo(new float3(0f, 0f, 1f)), "right");
            // A fingertip 0.4 m ahead of the camera and 0.1 m to its right, in Unity's camera space.
            float3 tip = math.transform(unity, new float3(0.1f, 0f, 0.4f));
            Assert.That(tip.x, Is.EqualTo(0.1f).Within(1e-6f));
            Assert.That(tip.y, Is.EqualTo(1.6f).Within(1e-6f));
            Assert.That(tip.z, Is.EqualTo(2.1f).Within(1e-6f));
        }

        [Test]
        public void ArKitToUnity_IsTheFlipOnBothSides_ForAnyPose()
        {
            var random = new Unity.Mathematics.Random(20261004u);
            for (int k = 0; k < 50; k++)
            {
                quaternion rotation = random.NextQuaternionRotation();
                float3 position = random.NextFloat3(-5f, 5f);
                float4x4 arKit = float4x4.TRS(position, rotation, new float3(1f, 1f, 1f));
                float4x4 unity = HandsNativeMath.ArKitToUnity(arKit);

                // A point in Unity's camera space is the same point in ARKit's with z negated; so is the session frame.
                for (int n = 0; n < 4; n++)
                {
                    float3 point = random.NextFloat3(-1f, 1f);
                    float3 expected = Flip(math.transform(arKit, Flip(point)));
                    Assert.That(math.distance(math.transform(unity, point), expected), Is.LessThan(1e-5f), "pose " + k);
                }

                // Still a pose: a turn without mirroring, and the bottom row untouched.
                float3x3 turn = new float3x3(unity.c0.xyz, unity.c1.xyz, unity.c2.xyz);
                Assert.That(math.determinant(turn), Is.EqualTo(1f).Within(1e-4f), "pose " + k);
                Assert.That(math.dot(unity.c0.xyz, unity.c1.xyz), Is.EqualTo(0f).Within(1e-5f), "pose " + k);
                var bottomRow = new float4(unity.c0.w, unity.c1.w, unity.c2.w, unity.c3.w);
                Assert.That(bottomRow, Is.EqualTo(new float4(0f, 0f, 0f, 1f)), "pose " + k);

                // The flip is its own inverse.
                float4x4 back = HandsNativeMath.ArKitToUnity(unity);
                for (int column = 0; column < 4; column++)
                    Assert.That(back[column], Is.EqualTo(arKit[column]), "pose " + k + " column " + column);
            }
        }

        [Test]
        public void TheConversion_InPlaceOnADetection_NegatesFiveElements_AndAllocatesNothing()
        {
            var detection = new HandsDetection();
            float* raw = (float*)((byte*)&detection + Offset(Result, "cameraToSession"));
            for (int i = 0; i < 16; i++) raw[i] = i + 1f;

            detection.CameraToSession = HandsNativeMath.ArKitToUnity(detection.CameraToSession);
            for (int i = 0; i < 16; i++)
            {
                // Column-major: 2 and 6 are the z of right and up, 8 and 9 the x and y of the third column, 14 the z of the position.
                bool negated = i == 2 || i == 6 || i == 8 || i == 9 || i == 14;
                Assert.That(raw[i], Is.EqualTo(negated ? -(i + 1f) : i + 1f), "element " + i);
            }

            for (int i = 0; i < 20; i++) detection.CameraToSession = HandsNativeMath.ArKitToUnity(detection.CameraToSession);   // warm up
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) detection.CameraToSession = HandsNativeMath.ArKitToUnity(detection.CameraToSession);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0), "bytes allocated by 100 conversions");
            Assert.That(raw[14], Is.EqualTo(-15f), "120 flips after the first leave it flipped once");
        }
    }
}

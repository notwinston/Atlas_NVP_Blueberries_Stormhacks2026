using System;
using System.IO;
using Atlas.Kernel;
using Atlas.Link;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.World.Tests
{
    /// <summary>The service locator, the space registry and the small value types every lane builds on.</summary>
    public class SpineTests
    {
        interface IClock { int Now { get; } }
        interface IOther { }
        sealed class Clock : IClock, IOther { public int Now { get; set; } }

        sealed class FakeSpace : ISpaceModule
        {
            public FakeSpace(SpaceId id) { Id = id; }
            public SpaceId Id { get; }
            public bool Registered { get { return true; } }
            public int Ticks;
            public void Configure(RoomModel room, ParamRegistry registry) { }
            public void Enter(in WalkerPose start) { }
            public void Tick(in FrameContext frame) { Ticks = frame.FrameIndex; }
            public void Exit() { }
            public void WriteState(ref StateMessage state) { state.Sheet = 7; }
        }

        [SetUp]
        public void SetUp()
        {
            AtlasServices.Clear();
            SpaceRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            AtlasServices.Clear();
            SpaceRegistry.Clear();
        }

        // ---- AtlasServices -------------------------------------------------------------------------------

        [Test]
        public void Services_RegisterGetAndTryGet()
        {
            Assert.That(AtlasServices.TryGet(out IClock none), Is.False);
            Assert.That(none, Is.Null);
            Assert.That(AtlasServices.Has<IClock>(), Is.False);
            var e = Assert.Throws<InvalidOperationException>(() => AtlasServices.Get<IClock>());
            Assert.That(e.Message, Does.Contain("IClock"));

            var clock = new Clock { Now = 5 };
            AtlasServices.Register<IClock>(clock);
            Assert.That(AtlasServices.Get<IClock>(), Is.SameAs(clock));
            Assert.That(AtlasServices.TryGet(out IClock found), Is.True);
            Assert.That(found.Now, Is.EqualTo(5));
            Assert.That(AtlasServices.Has<IClock>(), Is.True);

            // The key is the type argument: the same object is not visible under another key or its class.
            Assert.That(AtlasServices.TryGet(out IOther other), Is.False);
            Assert.That(AtlasServices.TryGet(out Clock asClass), Is.False);
            Assert.That(other, Is.Null);
            Assert.That(asClass, Is.Null);
        }

        [Test]
        public void Services_ReplaceUnregisterAndClear()
        {
            var first = new Clock { Now = 1 };
            var second = new Clock { Now = 2 };
            Assert.Throws<ArgumentNullException>(() => AtlasServices.Register<IClock>(null));

            AtlasServices.Register<IClock>(first);
            AtlasServices.Register<IClock>(second);
            Assert.That(AtlasServices.Get<IClock>(), Is.SameAs(second));
            Assert.That(AtlasServices.Replace<IClock>(first), Is.SameAs(second));
            Assert.That(AtlasServices.Get<IClock>(), Is.SameAs(first));

            // Unregistering a service that is no longer the published one changes nothing.
            AtlasServices.Unregister<IClock>(second);
            Assert.That(AtlasServices.Get<IClock>(), Is.SameAs(first));
            AtlasServices.Unregister<IClock>(first);
            Assert.That(AtlasServices.Has<IClock>(), Is.False);

            AtlasServices.Register<IClock>(first);
            AtlasServices.Register<IOther>(first);
            AtlasServices.Clear();
            Assert.That(AtlasServices.Has<IClock>(), Is.False);
            Assert.That(AtlasServices.Has<IOther>(), Is.False);

            // A slot survives Clear and can be filled again.
            AtlasServices.Register<IClock>(second);
            Assert.That(AtlasServices.Get<IClock>(), Is.SameAs(second));
        }

        [Test]
        public void Services_BootedIsRaisedOnceAndLateSubscribersAreCalledAtOnce()
        {
            int early = 0, late = 0, removed = 0;
            Action removedHandler = () => removed++;
            AtlasServices.Booted += () => early++;
            AtlasServices.Booted += removedHandler;
            AtlasServices.Booted -= removedHandler;
            Assert.That(AtlasServices.BootCompleted, Is.False);
            Assert.That(early, Is.EqualTo(0));

            AtlasServices.MarkBooted();
            AtlasServices.MarkBooted();
            Assert.That(AtlasServices.BootCompleted, Is.True);
            Assert.That(early, Is.EqualTo(1));
            Assert.That(removed, Is.EqualTo(0));

            AtlasServices.Booted += () => late++;
            Assert.That(late, Is.EqualTo(1));

            AtlasServices.Clear();
            Assert.That(AtlasServices.BootCompleted, Is.False);
            AtlasServices.MarkBooted();
            Assert.That(early, Is.EqualTo(1), "Clear drops the subscribers of the previous boot");
        }

        // ---- SpaceRegistry and SpaceId -------------------------------------------------------------------

        [Test]
        public void Registry_RegistersCreatesAndReplaces()
        {
            Assert.That(SpaceRegistry.IsRegistered(SpaceId.Glued), Is.False);
            Assert.That(SpaceRegistry.Create(SpaceId.Glued), Is.Null);
            Assert.That(SpaceRegistry.RegisteredCount, Is.EqualTo(0));

            int made = 0;
            SpaceRegistry.Register(SpaceId.Glued, () => { made++; return new FakeSpace(SpaceId.Glued); });
            Assert.That(SpaceRegistry.IsRegistered(SpaceId.Glued), Is.True);
            Assert.That(SpaceRegistry.IsRegistered(SpaceId.Home), Is.False);
            ISpaceModule a = SpaceRegistry.Create(SpaceId.Glued);
            ISpaceModule b = SpaceRegistry.Create(SpaceId.Glued);
            Assert.That(a.Id, Is.EqualTo(SpaceId.Glued));
            Assert.That(b, Is.Not.SameAs(a));
            Assert.That(made, Is.EqualTo(2));

            // Registering twice replaces the entry: the Editor runs registrations again with no domain reload.
            SpaceRegistry.Register(SpaceId.Glued, () => new FakeSpace(SpaceId.Slicer));
            Assert.That(SpaceRegistry.Create(SpaceId.Glued).Id, Is.EqualTo(SpaceId.Slicer));
            Assert.That(SpaceRegistry.RegisteredCount, Is.EqualTo(1));

            Assert.Throws<ArgumentNullException>(() => SpaceRegistry.Register(SpaceId.Home, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpaceRegistry.Register((SpaceId)9, () => null));
            Assert.That(SpaceRegistry.IsRegistered((SpaceId)9), Is.False);
            Assert.That(SpaceRegistry.Create((SpaceId)9), Is.Null);

            SpaceRegistry.Clear();
            Assert.That(SpaceRegistry.IsRegistered(SpaceId.Glued), Is.False);
        }

        [Test]
        public void Registry_DeclaresParametersInSpaceOrder()
        {
            string order = "";
            SpaceRegistry.Register(SpaceId.Slicer, () => new FakeSpace(SpaceId.Slicer), r => order += "6");
            SpaceRegistry.Register(SpaceId.Home, () => new FakeSpace(SpaceId.Home), r => order += "0");
            SpaceRegistry.Register(SpaceId.Portal, () => new FakeSpace(SpaceId.Portal));
            SpaceRegistry.Register(SpaceId.Glued, () => new FakeSpace(SpaceId.Glued),
                r => { order += "1"; r.DeclareFloat("glued.test", "glued", 0f, 1f, 0.5f); });

            var registry = new ParamRegistry();
            SpaceRegistry.DeclareAllParams(registry);
            Assert.That(order, Is.EqualTo("016"));
            Assert.Throws<ArgumentNullException>(() => SpaceRegistry.DeclareAllParams(null));
        }

        [Test]
        public void SpaceIds_CodesScenesAndGroups()
        {
            Assert.That(SpaceIds.Count, Is.EqualTo(7));
            Assert.That(Enum.GetValues(typeof(SpaceId)).Length, Is.EqualTo(SpaceIds.Count));
            string[] scenes =
            {
                "Space_Home", "Space_Glued", "Space_CurvedWalk", "Space_AlignedCells", "Space_SmallPlanet",
                "Space_Portal", "Space_Slicer"
            };
            string[] groups = { "home", "glued", "curved", "cells", "planet", "portal", "slicer" };
            for (int k = 0; k < SpaceIds.Count; k++)
            {
                var id = (SpaceId)k;
                Assert.That(SpaceIds.Code(id), Is.EqualTo("S" + k));
                Assert.That(SpaceIds.TryParseCode("S" + k, out SpaceId parsed), Is.True);
                Assert.That(parsed, Is.EqualTo(id));
                Assert.That(SpaceIds.SceneName(id), Is.EqualTo(scenes[k]));
                Assert.That(SpaceIds.SceneName(id), Is.EqualTo("Space_" + id));
                Assert.That(SpaceIds.ParamGroup(id), Is.EqualTo(groups[k]));
                Assert.That(SpaceIds.SceneId(id), Is.EqualTo((ushort)k));
            }
            Assert.That(SpaceIds.TryParseCode("S7", out _), Is.False);
            Assert.That(SpaceIds.TryParseCode("s1", out _), Is.False);
            Assert.That(SpaceIds.TryParseCode("S10", out _), Is.False);
            Assert.That(SpaceIds.TryParseCode(null, out _), Is.False);
            Assert.That(SpaceIds.NoSpace, Is.EqualTo(255));
            Assert.That(SpaceIds.NoScene, Is.EqualTo(0xFFFF));
            Assert.That(SpaceIds.DisplayName(SpaceId.Portal), Is.EqualTo("Impossible Rooms"));
        }

        [Test]
        public void SpaceModule_SeesTheFrameAndWritesState()
        {
            var space = new FakeSpace(SpaceId.Home);
            var frame = new FrameContext { FrameIndex = 12, Walker = WalkerPose.Start, Tier = QualityTier.Fair };
            space.Tick(in frame);
            Assert.That(space.Ticks, Is.EqualTo(12));
            var state = new StateMessage();
            space.WriteState(ref state);
            Assert.That(state.Sheet, Is.EqualTo(7));
            // A default frame is not tracked: the zero value of TrackingState is the unsafe one.
            Assert.That(default(FrameContext).Tracking, Is.EqualTo(TrackingState.NotTracking));
        }

        // ---- quality ladder ------------------------------------------------------------------------------

        [Test]
        public void QualityTable_HoldsTheLadderOfSection86()
        {
            QualityRow nominal = QualityTable.Row(QualityTier.Nominal);
            Assert.That(nominal.RenderScale, Is.EqualTo(0.8f));
            Assert.That(nominal.Msaa, Is.EqualTo(4));
            Assert.That(nominal.CullRho, Is.EqualTo(3f));
            Assert.That(nominal.GluedCopies, Is.EqualTo(5));
            Assert.That(QualityTable.Row(QualityTier.Fair).RenderScale, Is.EqualTo(0.7f));
            Assert.That(QualityTable.Row(QualityTier.Serious).Msaa, Is.EqualTo(2));
            Assert.That(QualityTable.Row(QualityTier.Serious).GluedCopies, Is.EqualTo(3));
            Assert.That(QualityTable.Row(QualityTier.Critical).FrameRate, Is.EqualTo(30));
            Assert.That(QualityTable.Row(QualityTier.Critical).Msaa, Is.EqualTo(1));
            Assert.That(QualityTable.Row(QualityTier.High).CullRho, Is.EqualTo(3.5f));
            Assert.That(QualityTable.Row(QualityTier.High).GluedCopies, Is.EqualTo(7));
            for (int k = 0; k < QualityTable.Count; k++)
                Assert.That(QualityTable.Row((QualityTier)k).Tier, Is.EqualTo((QualityTier)k));
            Assert.That(QualityTable.ToWire(QualityTier.High), Is.EqualTo(0));
            Assert.That(QualityTable.ToWire(QualityTier.Critical), Is.EqualTo(3));
        }

        [Test]
        public void QualityLadder_StepsOneRungPerDwellAndWorseFirst()
        {
            var ladder = new QualityLadderCore();
            var inputs = new QualityInputs { Time = 0.0, PinTier = -1, ThermalState = 0 };
            Assert.That(ladder.Step(in inputs), Is.False);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Nominal));

            inputs.ThermalState = 3;
            Assert.That(ladder.Step(in inputs), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Fair));
            inputs.Time = 10.0;
            Assert.That(ladder.Step(in inputs), Is.False, "one automatic step per 20 s");
            inputs.Time = 20.0;
            Assert.That(ladder.Step(in inputs), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Serious));
            inputs.Time = 40.0;
            Assert.That(ladder.Step(in inputs), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Critical));

            // Critical is never left automatically.
            inputs.Time = 500.0;
            inputs.ThermalState = 0;
            Assert.That(ladder.Step(in inputs), Is.False);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Critical));
            ladder.Reset();
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Nominal));
        }

        [Test]
        public void QualityLadder_GpuTimeHasHysteresis()
        {
            var ladder = new QualityLadderCore();
            var inputs = new QualityInputs { Time = 0.0, PinTier = -1, PerfValid = true, GpuP95Ms = 9.5f, FrameP95Ms = 16f };
            Assert.That(ladder.Step(in inputs), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Fair));

            inputs.Time = 30.0;
            inputs.GpuP95Ms = 7.5f;     // below the 9 ms it stepped down at, above the 6.5 ms it steps up at
            Assert.That(ladder.Step(in inputs), Is.False);
            inputs.GpuP95Ms = 6.0f;
            Assert.That(ladder.Step(in inputs), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Nominal));

            // With no perf data only the thermal terms count.
            inputs.Time = 60.0;
            inputs.PerfValid = false;
            inputs.GpuP95Ms = 50f;
            Assert.That(ladder.Step(in inputs), Is.False);

            // Serious to Critical on missed frames.
            ladder.Reset(QualityTier.Serious);
            inputs.PerfValid = true;
            inputs.GpuP95Ms = 10f;
            inputs.FrameP95Ms = 18f;
            Assert.That(ladder.Step(in inputs), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Critical));
        }

        [Test]
        public void QualityLadder_APinAppliesAtOnce()
        {
            var ladder = new QualityLadderCore();
            var inputs = new QualityInputs { Time = 1.0, PinTier = 4 };
            Assert.That(ladder.Step(in inputs), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.High));
            inputs.PinTier = 2;
            Assert.That(ladder.Step(in inputs), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Serious));
            Assert.That(ladder.Step(in inputs), Is.False);

            // Leaving a pin carries on from the pinned rung, and from Nominal after High.
            inputs.PinTier = -1;
            Assert.That(ladder.Step(in inputs), Is.False);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Serious));
            inputs.PinTier = 4;
            ladder.Step(in inputs);
            inputs.PinTier = -1;
            Assert.That(ladder.Step(in inputs), Is.True);
            Assert.That(ladder.Tier, Is.EqualTo(QualityTier.Nominal));
        }

        // ---- pose types ----------------------------------------------------------------------------------

        [Test]
        public void TrackingStates_WireCoding()
        {
            Assert.That((int)TrackingState.NotTracking, Is.EqualTo(0));
            Assert.That(TrackingStates.ToWire(TrackingState.Normal), Is.EqualTo(0));
            Assert.That(TrackingStates.ToWire(TrackingState.Limited), Is.EqualTo(1));
            Assert.That(TrackingStates.ToWire(TrackingState.NotTracking), Is.EqualTo(2));
            Assert.That(TrackingStates.FromWire(0), Is.EqualTo(TrackingState.Normal));
            Assert.That(TrackingStates.FromWire(1), Is.EqualTo(TrackingState.Limited));
            Assert.That(TrackingStates.FromWire(2), Is.EqualTo(TrackingState.NotTracking));
            Assert.That(TrackingStates.FromWire(3), Is.EqualTo(TrackingState.NotTracking));
        }

        [Test]
        public void HeadPose_HeadingAndYawFollowUnitysSense()
        {
            var ahead = new HeadPose(1.0, new float3(0.5f, 1.6f, -0.25f), quaternion.identity, TrackingState.Normal);
            Assert.That(ahead.Floor, Is.EqualTo(new float2(0.5f, -0.25f)));
            Assert.That(ahead.Height, Is.EqualTo(1.6f));
            Assert.That(ahead.Heading.y, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(ahead.Yaw, Is.EqualTo(0f).Within(1e-6f));

            // A positive yaw turns +Z towards +X.
            var turned = new HeadPose(0.0, float3.zero, quaternion.RotateY(math.radians(90f)), TrackingState.Normal);
            Assert.That(turned.Heading.x, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(turned.Yaw, Is.EqualTo(math.radians(90f)).Within(1e-5f));

            // Looking straight down keeps the heading of the body: the head's up vector points ahead.
            quaternion down = math.mul(quaternion.RotateY(math.radians(30f)), quaternion.RotateX(math.radians(90f)));
            var looking = new HeadPose(0.0, float3.zero, down, TrackingState.Normal);
            Assert.That(looking.Forward.y, Is.EqualTo(-1f).Within(1e-5f));
            Assert.That(looking.Yaw, Is.EqualTo(math.radians(30f)).Within(1e-4f));
            quaternion up = math.mul(quaternion.RotateY(math.radians(30f)), quaternion.RotateX(math.radians(-90f)));
            Assert.That(new HeadPose(0.0, float3.zero, up, TrackingState.Normal).Yaw, Is.EqualTo(math.radians(30f)).Within(1e-4f));

            Assert.That(default(HeadPose).Tracking, Is.EqualTo(TrackingState.NotTracking));
            Assert.That(math.all(math.isfinite(default(HeadPose).Heading)), Is.True);
        }

        [Test]
        public void DepthFrame_UnprojectsThroughThePinhole()
        {
            var frame = new DepthFrame
            {
                Depth = new float[4 * 2], Width = 4, Height = 2, Fx = 2f, Fy = 2f, Cx = 2f, Cy = 1f,
                Position = new float3(1f, 1f, 1f), Rotation = quaternion.RotateY(math.radians(90f))
            };
            Assert.That(frame.IsValid, Is.True);
            Assert.That(default(DepthFrame).IsValid, Is.False);
            // Pixel (3, 0) is right of and above the centre; image rows run downwards.
            float3 p = frame.Unproject(3, 0, 2f);
            Assert.That(p.x, Is.EqualTo(1.5f).Within(1e-6f));
            Assert.That(p.y, Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(p.z, Is.EqualTo(2f));
            // Turned 90 degrees, the camera's forward is world +X and its right is world -Z.
            float3 w = frame.ToWorld(3, 0, 2f);
            Assert.That(w.x, Is.EqualTo(3f).Within(1e-5f));
            Assert.That(w.y, Is.EqualTo(1.5f).Within(1e-5f));
            Assert.That(w.z, Is.EqualTo(-0.5f).Within(1e-5f));

            Assert.That(DepthFrame.IsReturn(1f), Is.True);
            Assert.That(DepthFrame.IsReturn(0f), Is.False);
            Assert.That(DepthFrame.IsReturn(float.NaN), Is.False);
            Assert.That(DepthFrame.IsReturn(6f), Is.False);

            var none = new NullDepthSource();
            Assert.That(none.Available, Is.False);
            Assert.That(none.TryRead(0.0, out DepthFrame empty), Is.False);
            Assert.That(empty.IsValid, Is.False);
        }

        [Test]
        public void PoseLog_RoundTrip()
        {
            var log = new PoseLog
            {
                RoomId = "sample_3x3", Frame = PoseFrameKind.Room, CreatedUtc = "2026-10-04T10:15:30Z",
                Samples = new[]
                {
                    new HeadPose(0.0, new float3(0f, 1.6f, 0f), quaternion.identity, TrackingState.Normal),
                    new HeadPose(0.016667, new float3(0.011667f, 1.6f, -0.25f), new quaternion(0f, 0.5f, 0f, 0.866025f), TrackingState.Limited),
                    new HeadPose(0.5, new float3(-1.25f, 1.55f, 0.75f), new quaternion(0.5f, 0.5f, -0.5f, 0.5f), TrackingState.NotTracking),
                }
            };
            var writer = new StringWriter();
            PoseLog.Write(writer, log);
            string text = writer.ToString();
            Assert.That(text, Does.StartWith("ATLAS-POSELOG 1\nroom sample_3x3\nframe room\ncreated 2026-10-04T10:15:30Z\ndata\n" +
                                            "0.000000 0.000000 1.600000 0.000000 0.000000 0.000000 0.000000 1.000000 2\n"));
            Assert.That(text.Contains("\r"), Is.False);

            PoseLog read = PoseLog.Read(new StringReader(text));
            Assert.That(read.RoomId, Is.EqualTo(log.RoomId));
            Assert.That(read.Frame, Is.EqualTo(log.Frame));
            Assert.That(read.CreatedUtc, Is.EqualTo(log.CreatedUtc));
            Assert.That(read.Samples.Length, Is.EqualTo(3));
            for (int k = 0; k < 3; k++)
            {
                Assert.That(read.Samples[k].Time, Is.EqualTo(log.Samples[k].Time));
                Assert.That(read.Samples[k].Position, Is.EqualTo(log.Samples[k].Position));
                Assert.That(read.Samples[k].Rotation.value, Is.EqualTo(log.Samples[k].Rotation.value));
                Assert.That(read.Samples[k].Tracking, Is.EqualTo(log.Samples[k].Tracking));
            }
            Assert.That(read.Duration, Is.EqualTo(0.5));
            Assert.That(new PoseLog().Duration, Is.EqualTo(0.0));

            // Comments, blank lines and unknown header keys are skipped.
            PoseLog loose = PoseLog.Read(new StringReader(
                "ATLAS-POSELOG 1\n# a comment\nframe session\nweather fine\ndata\n\n# another\n0.000000 0 0 0 0 0 0 1 2\n"));
            Assert.That(loose.Frame, Is.EqualTo(PoseFrameKind.Session));
            Assert.That(loose.RoomId, Is.EqualTo(""));
            Assert.That(loose.Samples.Length, Is.EqualTo(1));
        }

        [Test]
        public void PoseLog_RefusesBadFilesAndNamesTheLine()
        {
            AssertBadLog("", "line 1");
            AssertBadLog("ATLAS-POSELOG 2\nframe room\ndata\n", "version");
            AssertBadLog("SOMETHING 1\nframe room\ndata\n", "line 1");
            AssertBadLog("ATLAS-POSELOG 1\nroom a\ndata\n", "frame");
            AssertBadLog("ATLAS-POSELOG 1\nframe sideways\ndata\n", "line 2");
            AssertBadLog("ATLAS-POSELOG 1\nframe room\n", "data");
            AssertBadLog("ATLAS-POSELOG 1\nframe room\ndata\n0.0 0 0 0 0 0 0 1\n", "line 4");
            AssertBadLog("ATLAS-POSELOG 1\nframe room\ndata\n0.5 0 0 0 0 0 0 1 2\n0.4 0 0 0 0 0 0 1 2\n", "line 5");
            AssertBadLog("ATLAS-POSELOG 1\nframe room\ndata\n0.5 0 x 0 0 0 0 1 2\n", "line 4");
            AssertBadLog("ATLAS-POSELOG 1\nframe room\ndata\n0.5 0 0 0 0 0 0 1 7\n", "line 4");
        }

        static void AssertBadLog(string text, string named)
        {
            var e = Assert.Throws<FormatException>(() => PoseLog.Read(new StringReader(text)));
            Assert.That(e.Message, Does.Contain(named));
        }

        // ---- MeshData ------------------------------------------------------------------------------------

        [Test]
        public void MeshData_QuadGridKeepsEdgesShortAndFacesTheNormal()
        {
            var mesh = new MeshData(16, 16);
            // A 1.0 x 0.5 m floor patch: u along +X, v along +Z, so the front faces cross(v, u) = +Y.
            mesh.AddQuadGrid(new float3(0f, 0f, 0f), new float3(1f, 0f, 0f), new float3(0f, 0f, 0.5f),
                new float2(0.25f, 0.75f), new float2(0f, 0f), new float2(2f, 0f), new float2(0f, 1f));
            Assert.That(mesh.VertexCount, Is.EqualTo(5 * 3));
            Assert.That(mesh.TriangleCount, Is.EqualTo(4 * 2 * 2));
            Assert.That(mesh.BoundsMin, Is.EqualTo(new float3(0f, 0f, 0f)));
            Assert.That(mesh.BoundsMax, Is.EqualTo(new float3(1f, 0f, 0.5f)));
            // Cell sides are 0.25 m; the longest edge is a cell's diagonal.
            Assert.That(mesh.LongestEdge(), Is.EqualTo(0.25f * math.sqrt(2f)).Within(1e-5f));

            byte shade = AtlasShading.ToByte(AtlasShading.FaceShade(new float3(0f, 1f, 0f)));
            for (int k = 0; k < mesh.VertexCount; k++)
            {
                Assert.That(mesh.Vertices[k].Normal, Is.EqualTo(new float3(0f, 1f, 0f)));
                Assert.That(mesh.Vertices[k].Uv0, Is.EqualTo(new float2(0.25f, 0.75f)));
                Assert.That(mesh.Vertices[k].Color, Is.EqualTo(AtlasVertex.PackColor(shade, 255)));
            }
            Assert.That(mesh.Vertices[mesh.VertexCount - 1].Uv1, Is.EqualTo(new float2(2f, 1f)));

            // Every triangle is wound clockwise seen from the front: cross(b - a, c - a) points along the normal.
            for (int k = 0; k < mesh.IndexCount; k += 3)
            {
                float3 a = mesh.Vertices[mesh.Indices[k]].Position;
                float3 b = mesh.Vertices[mesh.Indices[k + 1]].Position;
                float3 c = mesh.Vertices[mesh.Indices[k + 2]].Position;
                Assert.That(math.cross(b - a, c - a).y, Is.GreaterThan(0f));
            }

            mesh.Clear();
            Assert.That(mesh.VertexCount, Is.EqualTo(0));
            Assert.That(mesh.TriangleCount, Is.EqualTo(0));
            Assert.That(mesh.LongestEdge(), Is.EqualTo(0f));
        }

        [Test]
        public void MeshData_BoxFacesPointOutwards()
        {
            var mesh = new MeshData();
            var baseCentre = new float3(1f, 0f, -2f);
            var size = new float3(1.0f, 0.75f, 0.6f);
            mesh.AddBox(baseCentre, size, 0.7f, new float2(0.5f, 0.5f));
            int fiveFaces = mesh.TriangleCount;
            // 4 x 3 cells on the top, 3 x 3 on the two X faces, 3 x 4 on the two Z faces; two triangles each.
            Assert.That(fiveFaces, Is.EqualTo(2 * (4 * 3 + 2 * 3 * 3 + 2 * 3 * 4)));
            Assert.That(mesh.BoundsMin.y, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(mesh.BoundsMax.y, Is.EqualTo(0.75f).Within(1e-6f));

            float3 centre = baseCentre + new float3(0f, 0.5f * size.y, 0f);
            for (int k = 0; k < mesh.IndexCount; k += 3)
            {
                AtlasVertex va = mesh.Vertices[mesh.Indices[k]];
                float3 a = va.Position;
                float3 b = mesh.Vertices[mesh.Indices[k + 1]].Position;
                float3 c = mesh.Vertices[mesh.Indices[k + 2]].Position;
                float3 faceNormal = math.normalize(math.cross(b - a, c - a));
                Assert.That(math.dot(faceNormal, va.Normal), Is.EqualTo(1f).Within(1e-4f));
                Assert.That(math.dot(va.Normal, (a + b + c) / 3f - centre), Is.GreaterThan(0f));
                Assert.That((va.Color >> 8) & 0xFF, Is.EqualTo(0u), "a box draws no lines by default");
            }

            mesh.Clear();
            mesh.AddBox(baseCentre, size, 0.7f, new float2(0.5f, 0.5f), true);
            Assert.That(mesh.TriangleCount, Is.EqualTo(fiveFaces + 2 * 4 * 3));
        }

        [Test]
        public void NullPhotoSink_CountsCalls()
        {
            var sink = new NullObjectPhotoSink();
            sink.BeginRoom(null, "{}");
            sink.Submit(new ObjectPhotoSet());
            sink.Submit(new ObjectPhotoSet());
            sink.ReportNoPhoto(3);
            sink.EndRoom();
            Assert.That(sink.RoomsBegun, Is.EqualTo(1));
            Assert.That(sink.Submitted, Is.EqualTo(2));
            Assert.That(sink.NoPhoto, Is.EqualTo(1));
            Assert.That(sink.RoomsEnded, Is.EqualTo(1));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Scan;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using WorldTier = Atlas.World.QualityTier;

namespace Atlas.App.Tests
{
    /// <summary>The registry entries the composition root declares for the assemblies that may not name the
    /// registry (contract README F6), bound to plain properties.</summary>
    public class AppParamsTests
    {
        sealed class Eyes : IEyeTextures
        {
            public readonly List<string> Selects = new List<string>();
            public float RenderScale { get; private set; } = 0.8f;
            public int Msaa { get; private set; } = 4;
            public void Select(float renderScale, int msaa)
            {
                RenderScale = renderScale;
                Msaa = msaa;
                Selects.Add(renderScale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "/" + msaa);
            }
        }

        [Test]
        public void Render_LensAndLadder_AreDrivenByTheRegistry()
        {
            var registry = new ParamRegistry();
            var kernel = new FlatWalkerKernel();
            var eyes = new Eyes();
            var ladder = new QualityLadder(new QualityApplier(eyes));
            var lens = new LensModel();
            bool lensOn = true;
            float lineWidth = 0f;
            AppParams.DeclareRender(registry, ladder, kernel, eyes, null, lens, false, on => lensOn = on, w => lineWidth = w);
            registry.PushAll();

            Assert.That(lensOn, Is.False, "render.lens starts from the value given: off in the Editor");
            Assert.That(lineWidth, Is.EqualTo(1.5f));
            Assert.That(registry.Find(AppParams.RenderScale).OnRelease, Is.True);
            Assert.That(registry.Find(AppParams.PinTier).Kind, Is.EqualTo(ParamKind.Int));

            registry.Set(AppParams.LensK1, 0.2f);
            registry.Set(AppParams.ScreenToLens, 0.05f);
            registry.Set(AppParams.Lens, 1f);
            registry.Set(AppParams.CullRho, 2.5f);
            Assert.That(lens.K1, Is.EqualTo(0.2f));
            Assert.That(lens.ScreenToLens, Is.EqualTo(0.05f));
            Assert.That(lensOn, Is.True);
            Assert.That(kernel.CullRho, Is.EqualTo(2.5).Within(1e-6));

            // render.scale and render.msaa pick an eye-texture set together.
            eyes.Selects.Clear();
            registry.Set(AppParams.RenderMsaa, 2f);
            registry.Set(AppParams.RenderScale, 0.6f);
            Assert.That(eyes.Selects[eyes.Selects.Count - 1], Is.EqualTo("0.6/2"));

            // The pinned tier applies within a second, and the rung's values are written back.
            var frame = new FrameContext { Time = 100.0 };
            WorldTier seen = WorldTier.Nominal;
            ladder.TierChanged += tier => seen = tier;
            registry.Set(AppParams.PinTier, 3f);
            eyes.Selects.Clear();
            ladder.AfterFrame(in frame);
            Assert.That(ladder.Tier, Is.EqualTo(WorldTier.Critical));
            Assert.That(seen, Is.EqualTo(WorldTier.Critical));
            Assert.That(ladder.PinnedTier, Is.EqualTo(3));
            Assert.That(ladder.Row.FrameRate, Is.EqualTo(30));
            Assert.That(eyes.Selects, Is.EqualTo(new[] { "0.6/1" }), "one swap, by the applier; the write-back swaps nothing");
            Assert.That(registry.GetFloat(AppParams.RenderScale), Is.EqualTo(0.6f));
            Assert.That(registry.GetInt(AppParams.RenderMsaa), Is.EqualTo(1));
            Assert.That(registry.GetFloat(AppParams.CullRho), Is.EqualTo(2f));
            Assert.That(registry.GetInt(AppParams.GluedCopies), Is.EqualTo(3));
            Assert.That(kernel.CullRho, Is.EqualTo(2.0).Within(1e-6));
            FramePacing.TargetFrameRate = FramePacing.DefaultFrameRate;
        }

        [Test]
        public void SpaceId_RequestsASwitch_IsWrittenBack_AndSnapsBackWhenRefused()
        {
            var registry = new ParamRegistry();
            var host = new FakeHost();
            AppParams.SpaceBinding binding = AppParams.DeclareSpace(registry, host);
            Assert.That(binding.Entry.Kind, Is.EqualTo(ParamKind.Enum));
            Assert.That(binding.Entry.EnumCount, Is.EqualTo(SpaceIds.Count));

            registry.PushAll();
            registry.Set(AppParams.Space, 5f);
            Assert.That(host.Calls, Is.Empty, "nothing is asked for while the app boots");
            registry.Set(AppParams.Space, 0f);

            binding.Enabled = true;
            registry.Set(AppParams.Space, 5f, ParamSource.Remote);
            Assert.That(host.Calls, Is.EqualTo(new[] { "switch S5" }));

            host.Calls.Clear();
            host.RaiseEntered(SpaceId.Slicer);
            Assert.That(registry.GetInt(AppParams.Space), Is.EqualTo(6), "written back after a switch");
            Assert.That(host.Calls, Is.Empty, "the write-back asks for nothing");

            host.Accept = false;
            registry.Set(AppParams.Space, 2f, ParamSource.Remote);
            Assert.That(host.Calls, Is.EqualTo(new[] { "switch S2" }));
            Assert.That(registry.GetInt(AppParams.Space), Is.EqualTo(6), "the host refused the switch: the value snaps back");
        }

        [Test]
        public void Pose_GyroKillSwitchAndLead_DriveOneProperty()
        {
            var registry = new ParamRegistry();
            var pipeline = new FakePipeline();
            int source = -1;
            float speed = -1f;
            bool loop = false, record = true;
            AppParams.DeclarePose(registry, pipeline, v => source = v, v => speed = v, v => loop = v, v => record = v);
            registry.PushAll();
            Assert.That(pipeline.GyroPredictMs, Is.EqualTo(0f), "off by default");
            Assert.That(source, Is.EqualTo(AppParams.SourceTracked));
            Assert.That(speed, Is.EqualTo(1f));
            Assert.That(loop, Is.True);
            Assert.That(record, Is.False);
            Assert.That(registry.Find(AppParams.GyroPredict).Kind, Is.EqualTo(ParamKind.Bool));

            registry.Set(AppParams.GyroPredict, 1f);
            Assert.That(pipeline.GyroPredictMs, Is.EqualTo(40f));
            registry.Set(AppParams.GyroLeadMs, 55f);
            Assert.That(pipeline.GyroPredictMs, Is.EqualTo(55f));
            registry.Set(AppParams.GyroPredict, 0f);
            Assert.That(pipeline.GyroPredictMs, Is.EqualTo(0f));

            registry.Set(AppParams.PoseSource, AppParams.SourceWalker);
            registry.Set(AppParams.PoseRecord, 1f);
            Assert.That(source, Is.EqualTo(AppParams.SourceWalker));
            Assert.That(record, Is.True);
        }

        [Test]
        public void Scan_RouteChoosesTheOptions()
        {
            var registry = new ParamRegistry();
            var settings = new ScanSettings();
            AppParams.DeclareScan(registry, settings);
            registry.PushAll();
            Assert.That(settings.Options.UseMesh && settings.Options.UseBoxes, Is.True, "route 3 by default");

            registry.Set(AppParams.ScanRoute, 1f);
            Assert.That(settings.Options.UseMesh, Is.False);
            Assert.That(settings.Options.UseBoxes, Is.False);
            registry.Set(AppParams.ScanRoute, 2f);
            Assert.That(settings.Options.UseMesh, Is.True);
            Assert.That(settings.Options.UseBoxes, Is.False);
            registry.Set(AppParams.ScanRoute, 4f);
            Assert.That(settings.Options.UseBoxes, Is.True);

            Assert.That(settings.TeamPrescan, Is.False);
            registry.Set(AppParams.TeamPrescan, 1f);
            Assert.That(settings.TeamPrescan, Is.True);
        }

        [Test]
        public void KillSwitches_OfSection12_4_AllExist_AndAnOwnerCanTakeThemOver()
        {
            var registry = new ParamRegistry();
            var host = new FakeHost();
            var ladder = new QualityLadder(new QualityApplier(null));
            AppParams.DeclareSpace(registry, host);
            AppParams.DeclareRender(registry, ladder, new FlatWalkerKernel(), null, null, null, true, null, null);
            AppParams.DeclareUnownedKillSwitches(registry);
            AppParams.DeclarePose(registry, null, null, null, null, null);
            AppParams.DeclareScan(registry, new ScanSettings());

            // link.enabled, link.stateHz and demoLock are declared by the telemetry installer.
            string[] mine =
            {
                "space.id", "curved.mode", "curved.cullRho", "render.scale", "render.msaa", "glued.copies", "portal.depth",
                "slicer.source", "pose.gyroPredict", "forge.backend", "scan.route", "thermal.pinTier"
            };
            for (int i = 0; i < mine.Length; i++) Assert.That(registry.Find(mine[i]), Is.Not.Null, mine[i]);
            Assert.That(registry[0].Name, Is.EqualTo("space.id"), "wire id 0");
            Assert.That(registry.Find("forge.backend").Int, Is.EqualTo(3), "standalone phone: forge.backend defaults to off");

            // The owning space declares the same name with the same kind and keeps the value.
            registry.Set("slicer.source", 2f);
            ParamEntry again = registry.DeclareEnum("slicer.source", "slicer", new[] { "live", "stepped", "tesseract" }, 0, null, true);
            Assert.That(again.Int, Is.EqualTo(2));
            AppParams.DeclareUnownedKillSwitches(registry);
            Assert.That(registry.GetInt("slicer.source"), Is.EqualTo(2));
        }

        [Test]
        public void Dev_RoomChoices_AreNoneThenTheFixtures()
        {
            string[] names = AppParams.RoomChoices();
            Assert.That(names[0], Is.EqualTo("none"));
            Assert.That(names.Length, Is.EqualTo(RoomFixtures.All.Length + 1));
            Assert.That(names[1], Is.EqualTo(RoomFixtures.Sample3x3));

            var registry = new ParamRegistry();
            int room = -1;
            bool overlay = false;
            AppParams.DeclareDev(registry, 1, v => room = v, true, v => overlay = v);
            registry.PushAll();
            Assert.That(room, Is.EqualTo(1));
            Assert.That(overlay, Is.True);
        }
    }

    /// <summary>The Editor-first path (contract README F9): command line, boot rule, replay clock, walk files.</summary>
    public class EditorFirstTests
    {
        [Test]
        public void BootRule_BootSceneAndMarkedSandboxesOnly()
        {
            Assert.That(AtlasBootstrap.ShouldBoot("Atlas_Boot", false), Is.True);
            Assert.That(AtlasBootstrap.ShouldBoot("Sandbox_Look", true), Is.True);
            Assert.That(AtlasBootstrap.ShouldBoot("Sandbox_Look", false), Is.False);
            Assert.That(AtlasBootstrap.ShouldBoot("Space_Home", true), Is.False);
            Assert.That(AtlasBootstrap.ShouldBoot("", false), Is.False);
            Assert.That(AtlasBootstrap.ShouldBoot(null, true), Is.False);
        }

        [Test]
        public void CommandLine_IsParsed()
        {
            BootOptions none = BootOptions.Parse(new[] { "Unity", "-batchmode" });
            Assert.That(none.Room, Is.Null);
            Assert.That(none.Source, Is.EqualTo(-1));
            Assert.That(none.EntrySpace, Is.EqualTo(SpaceId.Home));
            Assert.That(BootOptions.Parse(null).StayInHand, Is.False);

            BootOptions o = BootOptions.Parse(new[]
            {
                "Unity", "-atlasRoom", "budget_room", "-atlasReplay", "/tmp/walk_a.poselog", "-atlasSpace", "S5", "-atlasHand"
            });
            Assert.That(o.Room, Is.EqualTo("budget_room"));
            Assert.That(o.RoomIsFixture, Is.True);
            Assert.That(o.Replay, Is.EqualTo("/tmp/walk_a.poselog"));
            Assert.That(o.Source, Is.EqualTo(AppParams.SourceReplay), "a replay file implies the replay source");
            Assert.That(o.EntrySpace, Is.EqualTo(SpaceId.Portal));
            Assert.That(o.StayInHand, Is.True);

            BootOptions w = BootOptions.Parse(new[] { "-atlasSource", "walker", "-atlasRoom", "rooms/mine.json", "-atlasSpace", "S9" });
            Assert.That(w.Source, Is.EqualTo(AppParams.SourceWalker));
            Assert.That(w.RoomIsFixture, Is.False);
            Assert.That(w.EntrySpace, Is.EqualTo(SpaceId.Home), "an unknown code is ignored");
        }

        // A 60 Hz log of a straight walk at 0.7 m/s, three metres long.
        static PoseLog WalkAt07()
        {
            const int count = 258;
            var log = new PoseLog { Frame = PoseFrameKind.Room, Samples = new HeadPose[count] };
            for (int i = 0; i < count; i++)
                log.Samples[i] = new HeadPose(i / 60.0, new float3((float)(-1.5 + 0.7 * i / 60.0), 1.6f, 0.25f), quaternion.identity, TrackingState.Normal);
            return log;
        }

        [TestCase(0.5f, 60.0)]      // replay.speed 0.5: every second frame falls between two samples
        [TestCase(1f, 144.0)]       // an Editor above the log's 60 Hz
        [TestCase(0.25f, 90.0)]
        [TestCase(1f, 60.0)]
        public void Replay_AsTheAppPlaysIt_AWalkNeverReadsAsStanding(float speed, double frameRate)
        {
            // Rule 4: curvature changes only while the walker stands. The app's replay is by the clock; without
            // interpolation a frame between two samples repeats the last one, the curved kernel sees a head
            // that did not move, and the ramp runs and the dial unlocks in the middle of a walk.
            PoseLogReplaySource replay = Atlas.Scan.ArPoseSystems.NewReplay(WalkAt07(), false);
            Assert.That(replay.Mode, Is.EqualTo(ReplayMode.RealTime));
            Assert.That(replay.Interpolate, Is.True);
            var clock = new ReplayClock(replay) { Speed = speed };

            const double l5 = 2.0 / 1.0612750619050357;
            IDialKernel kernel = new HolonomyWalkerKernel();
            kernel.Configure(KernelConfig.Curved(5, l5));

            double dt = 1.0 / frameRate;
            double time = 100.0;
            HeadPose pose;
            Assert.That(clock.TryRead(time, (float)dt, out pose), Is.True);
            kernel.HeadHeightMetres = pose.Position.y;
            // As in the app: the frame's step 4, then the space registers the walker under the head.
            kernel.Step(new double2(pose.Position.x, pose.Position.z), true, time);
            kernel.ResetToFloorFlat(new double2(pose.Position.x, pose.Position.z));
            Assert.That(kernel.SetCurvatureTarget(Curvature.Hyperbolic, l5), Is.True);

            // Four seconds of the app's time, all of it inside the walk: long enough for the whole ramp.
            int frames = (int)(4.0 * frameRate);
            for (int i = 1; i <= frames; i++)
            {
                time = 100.0 + i * dt;
                Assert.That(clock.TryRead(time, (float)dt, out pose), Is.True, "frame " + i);
                kernel.HeadHeightMetres = pose.Position.y;
                KernelStep step = kernel.Step(new double2(pose.Position.x, pose.Position.z), pose.Tracking == TrackingState.Normal, time);
                Assert.That(step.Rejected, Is.False, "frame " + i);
                Assert.That(kernel.Still, Is.False, "frame " + i + ": the walk goes at " + (0.7f * speed) + " m/s");
                Assert.That(kernel.CurvatureNow, Is.EqualTo(0.0), "frame " + i + ": no ramp in a walk");
            }
            Assert.That(replay.Ended, Is.False);
            Assert.That(pose.Position.x, Is.EqualTo(-1.5f + 0.7f * speed * 4f).Within(0.02f), "the walk was played at its speed");
        }

        [Test]
        public void Replay_OfAFile_IsByTheClockAndInterpolated()
        {
            string root = Path.Combine(Path.GetTempPath(), "atlas_replay_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "walk_a.poselog");
                using (var writer = new StreamWriter(path)) PoseLog.Write(writer, WalkAt07());
                PoseLogReplaySource replay = Atlas.Scan.ArPoseSystems.OpenReplay(path, true);
                Assert.That(replay.Mode, Is.EqualTo(ReplayMode.RealTime));
                Assert.That(replay.Loop, Is.True);
                Assert.That(replay.Interpolate, Is.True);
                Assert.That(replay.Log.Samples.Length, Is.EqualTo(258));

                // Between two samples the head is where the walk had it.
                HeadPose pose;
                Assert.That(replay.TryRead(10.0, 0f, out pose), Is.True);
                Assert.That(replay.TryRead(10.0 + 1.5 / 60.0, 0.025f, out pose), Is.True);
                Assert.That(pose.Position.x, Is.EqualTo(-1.5f + 0.7f * 1.5f / 60f).Within(1e-4f));
                Assert.That(Atlas.Scan.ArPoseSystems.OpenReplay(path, false).Loop, Is.False);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void ReplayClock_PlaysARecordedWalkAtAnotherSpeed()
        {
            var log = new PoseLog { Frame = PoseFrameKind.Room, Samples = new HeadPose[101] };
            for (int i = 0; i <= 100; i++)
                log.Samples[i] = new HeadPose(i * 0.1, new float3(i * 0.1f, 1.6f, 0f), quaternion.identity, TrackingState.Normal);
            var clock = new ReplayClock(new PoseLogReplaySource(log, ReplayMode.RealTime, false)) { Speed = 2f };
            Assert.That(clock.Name, Is.EqualTo("poselog"));
            Assert.That(clock.Frame, Is.EqualTo(PoseFrameKind.Room));
            Assert.That(clock.DrivesRig, Is.False);

            HeadPose pose = default(HeadPose);
            for (int i = 0; i < 20; i++) Assert.That(clock.TryRead(50.0 + i * 0.1, 0.1f, out pose), Is.True);
            // 1.9 s of app time after the first read, at double speed: 3.8 s into the walk.
            Assert.That(pose.Position.x, Is.EqualTo(3.8f).Within(0.11f));
            Assert.That(pose.Time, Is.EqualTo(51.9).Within(1e-6), "the sample carries the app's time");

            clock.Speed = 0f;
            float held = pose.Position.x;
            for (int i = 0; i < 10; i++) clock.TryRead(60.0 + i, 0.1f, out pose);
            Assert.That(pose.Position.x, Is.EqualTo(held), "speed 0 holds the walk still");
        }

        [Test]
        public void NewestWalk_IsFoundAcrossFolders()
        {
            string root = Path.Combine(Path.GetTempPath(), "atlas_app_tests_" + Guid.NewGuid().ToString("N"));
            string a = Path.Combine(root, "a"), b = Path.Combine(root, "b");
            Directory.CreateDirectory(a);
            Directory.CreateDirectory(b);
            try
            {
                Assert.That(PoseSourceSwitch.FindNewestWalk(new[] { a, b, null, Path.Combine(root, "missing") }), Is.Null);
                string old = Path.Combine(a, "walk_old.poselog");
                string recent = Path.Combine(b, "walk_new.poselog");
                File.WriteAllText(old, "x");
                File.WriteAllText(recent, "x");
                File.WriteAllText(Path.Combine(b, "room_1.json"), "x");
                File.SetLastWriteTimeUtc(old, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                File.SetLastWriteTimeUtc(recent, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
                Assert.That(PoseSourceSwitch.FindNewestWalk(new[] { a, b }), Is.EqualTo(recent));
                Assert.That(PoseSourceSwitch.FindNewestWalk(new[] { a }), Is.EqualTo(old));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void Hooks_DefaultToNullImplementations()
        {
            AppHooks.Clear();
            IOnboardingFlow onboarding = AppHooks.CreateOnboarding();
            Assert.That(onboarding.OwnsFlow, Is.False, "with no onboarding the rehearsal flow owns the scan");
            Assert.DoesNotThrow(() => { onboarding.Install(null); onboarding.Begin(); onboarding.Shutdown(); });
            IPerfTour tour = AppHooks.CreatePerfTour();
            Assert.That(tour.Running, Is.False);
            Assert.DoesNotThrow(() => { tour.Install(null); tour.Shutdown(); });
            IAppMenu menu = AppHooks.CreateMenu();
            Assert.DoesNotThrow(() => { menu.Install(null); menu.Shutdown(); });

            var mine = new NullMenu();
            AppHooks.Menu = () => mine;
            Assert.That(AppHooks.CreateMenu(), Is.SameAs(mine));
            AppHooks.Clear();
        }
    }
}

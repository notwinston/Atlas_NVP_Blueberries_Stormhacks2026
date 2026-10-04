using System.Collections.Generic;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Safety.Tests
{
    /// <summary>
    /// The Bump Guard adapter (Master Plan section 4.10, task P1-C09) driven by SensorFrames built in code over
    /// a fixture room's distance field, with the cue recorded by a stand-in view and haptic channel.
    /// No engine: the depth kernel runs inline.
    /// </summary>
    public class BumpGuardAdapterTests
    {
        const double Dt = 1.0 / 60.0;
        const float HeadHeight = 1.6f;
        const int DepthW = 256, DepthH = 192;

        sealed class RecordingView : IBumpCueView
        {
            public CueIcon Icon;
            public float3 Position;
            public int Shows, Plays;
            public CueSound LastSound;
            public bool LastSpatial;
            public bool Disposed;

            public void Show(CueIcon icon, float3 position, float rollRadians, float scale)
            {
                Icon = icon;
                Position = position;
                Shows++;
            }

            public void Play(CueSound sound, float3 position, bool spatial)
            {
                LastSound = sound;
                LastSpatial = spatial;
                Plays++;
            }

            public void Dispose()
            {
                Disposed = true;
            }
        }

        sealed class RecordingHaptics : IHaptics
        {
            public readonly List<float> Pulses = new List<float>();
            public HapticPattern Pattern;
            public bool Available { get { return true; } }
            public void Pulse(float strength) { Pulses.Add(strength); }
            public void SetPattern(HapticPattern pattern) { Pattern = pattern; }
        }

        RoomModel _room;
        BumpGuard _guard;
        RecordingView _view;
        RecordingHaptics _haptics;
        int _frame;

        [SetUp]
        public void SetUp()
        {
            _room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            _view = new RecordingView();
            _haptics = new RecordingHaptics();
            BumpCue.Initialise(_view, _haptics);
            _guard = new BumpGuard(false);
            _guard.SetRoom(_room);
            _guard.Armed = true;
            _frame = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _guard.Dispose();
            BumpCue.Shutdown();
        }

        double Now { get { return _frame * Dt; } }

        SensorFrame Sensors(float2 floor, float yaw, TrackingState tracking = TrackingState.Normal, bool roomActive = true)
        {
            return new SensorFrame
            {
                Time = Now,
                DeltaTime = _frame == 0 ? 0f : (float)Dt,
                Head = new HeadPose(Now, new float3(floor.x, HeadHeight, floor.y), quaternion.RotateY(yaw), tracking),
                RoomActive = roomActive,
                AnchorTracked = true,
                HasDepth = false
            };
        }

        void Tick(SensorFrame sensors)
        {
            _guard.Tick(in sensors);
            _frame++;
        }

        // A flat wall square to the camera at this distance: every pixel reads the same depth.
        static DepthFrame WallAhead(float metres, float3 cameraPosition, quaternion cameraRotation, double time, int sequence)
        {
            var pixels = new float[DepthW * DepthH];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = metres;
            return new DepthFrame
            {
                Depth = pixels,
                Width = DepthW,
                Height = DepthH,
                Fx = 212f,
                Fy = 212f,
                Cx = 128f,
                Cy = 96f,
                Position = cameraPosition,
                Rotation = cameraRotation,
                Time = time,
                Sequence = sequence
            };
        }

        [Test]
        public void TheFixtureRoom_IsFreeAtItsCentre_AndWalledAlongX()
        {
            Assert.That(_room.Sdf, Is.Not.Null);
            Assert.That(_guard.Core.HasRoom, Is.True);
            Assert.That(_room.Sdf.Sample(float2.zero), Is.GreaterThan(1.0f));
            Assert.That(_room.Sdf.Sample(new float2(1.6f, 0f)), Is.LessThan(0.05f));
        }

        [Test]
        public void WalkingAtAWall_RaisesWarn_ThenStrong_BeforeContact()
        {
            const float speed = 0.7f;
            float yaw = math.PI / 2f;                         // facing +X
            double warnAt = -1.0, strongAt = -1.0;
            float warnClearance = 0f, strongClearance = 0f;
            int cueAtWarn = -1, pulsesAtWarn = -1;

            for (int k = 0; k < 600 && strongAt < 0.0; k++)
            {
                var floor = new float2(-0.5f + speed * (float)(k * Dt), 0f);
                float clearance = _room.Sdf.Sample(floor);
                Assert.That(clearance, Is.GreaterThan(0f), "STRONG must come before the walker reaches the wall");
                Tick(Sensors(floor, yaw));

                if (warnAt < 0.0 && _guard.Level == BumpLevel.Warn)
                {
                    warnAt = k * Dt;
                    warnClearance = clearance;
                    cueAtWarn = BumpCue.FireCount;
                    pulsesAtWarn = _haptics.Pulses.Count;
                    // Indicator, sound and haptic all start in the frame the level rises.
                    Assert.That(BumpCue.Shown, Is.EqualTo(BumpLevel.Warn));
                    Assert.That(_view.Icon, Is.EqualTo(CueIcon.ChevronWarn));
                    Assert.That(_view.Plays, Is.EqualTo(1));
                    Assert.That(_view.LastSound, Is.EqualTo(CueSound.Tick));
                    Assert.That(_view.LastSpatial, Is.True);
                    // The wall is straight ahead: the chevron sits at the top of the circle.
                    Assert.That(_guard.Direction.y, Is.GreaterThan(0.95f));
                    Assert.That(_view.Position.y, Is.GreaterThan(0.28f));
                }
                if (_guard.Level == BumpLevel.Strong)
                {
                    strongAt = k * Dt;
                    strongClearance = clearance;
                }
            }

            Assert.That(warnAt, Is.GreaterThanOrEqualTo(0.0), "WARN was raised");
            Assert.That(strongAt, Is.GreaterThan(warnAt), "then STRONG");
            Assert.That(cueAtWarn, Is.EqualTo(1));
            Assert.That(pulsesAtWarn, Is.EqualTo(1));
            // Lead to predicted contact (clear() = 0) at this speed: at least 0.6 s for WARN (task P1-C09).
            Assert.That(warnClearance / speed, Is.GreaterThanOrEqualTo(0.6f));
            Assert.That(strongClearance, Is.LessThan(warnClearance));
            Assert.That(BumpCue.Shown, Is.EqualTo(BumpLevel.Strong));
            Assert.That(_view.Icon, Is.EqualTo(CueIcon.ChevronStrong));
            Assert.That(_view.LastSound, Is.EqualTo(CueSound.Tone));
            Assert.That(BumpCue.FireCount, Is.EqualTo(2));
            Assert.That(_haptics.Pattern, Is.EqualTo(HapticPattern.Strong));
            Assert.That(_haptics.Pulses[_haptics.Pulses.Count - 1], Is.EqualTo(BumpCueScheduler.StrongStrength));
            Assert.That(_guard.RescanAdvised, Is.False);
        }

        [Test]
        public void StandingInTheMiddle_RaisesNothing()
        {
            for (int k = 0; k < 120; k++) Tick(Sensors(float2.zero, 0f));
            Assert.That(_guard.Level, Is.EqualTo(BumpLevel.Clear));
            Assert.That(math.lengthsq(_guard.Direction), Is.EqualTo(0f));
            Assert.That(BumpCue.Shown, Is.EqualTo(BumpLevel.Clear));
            Assert.That(_haptics.Pulses, Is.Empty);
            Assert.That(_view.Plays, Is.EqualTo(0));
            Assert.That(_view.Icon, Is.EqualTo(CueIcon.None));
        }

        [Test]
        public void TrackingLost_IsRaisedAtHalfASecond()
        {
            for (int k = 0; k < 30; k++) Tick(Sensors(float2.zero, 0f));
            double lostFrom = Now;
            double raisedAt = -1.0;
            for (int k = 0; k < 90 && raisedAt < 0.0; k++)
            {
                Tick(Sensors(float2.zero, 0f, TrackingState.Limited));
                if (_guard.Level == BumpLevel.TrackingLost) raisedAt = Now - Dt;      // the time of the frame just ticked
            }

            Assert.That(raisedAt, Is.GreaterThanOrEqualTo(0.0));
            Assert.That(raisedAt - lostFrom, Is.EqualTo(BumpConstants.TrackingLostSeconds).Within(1.5 * Dt));
            Assert.That(BumpCue.Shown, Is.EqualTo(BumpLevel.TrackingLost));
            Assert.That(_view.Icon, Is.EqualTo(CueIcon.StandStill));
            Assert.That(_view.LastSound, Is.EqualTo(CueSound.Tone));
            Assert.That(_view.LastSpatial, Is.False);
            Assert.That(_haptics.Pulses.Count, Is.EqualTo(1), "the pulse starts with the icon");

            // Not tracking at all is lost in the same way.
            for (int k = 0; k < 60; k++) Tick(Sensors(float2.zero, 0f, TrackingState.NotTracking));
            Assert.That(_guard.Level, Is.EqualTo(BumpLevel.TrackingLost));
            Assert.That(_haptics.Pulses.Count, Is.EqualTo(7).Within(1), "six a second");
        }

        [Test]
        public void ALostAnchor_CountsAsTrackingLost()
        {
            for (int k = 0; k < 60; k++)
            {
                SensorFrame s = Sensors(float2.zero, 0f);
                s.AnchorTracked = false;
                Tick(s);
            }
            Assert.That(_guard.Level, Is.EqualTo(BumpLevel.TrackingLost));
        }

        [Test]
        public void AFrameThatClaimsDepthButCarriesNone_IsTolerated()
        {
            SensorFrame s = Sensors(float2.zero, 0f);
            s.HasDepth = true;                                // Depth is all zeros: no buffer
            Assert.DoesNotThrow(() => Tick(s));

            s = Sensors(float2.zero, 0f);
            s.HasDepth = true;
            s.Depth = WallAhead(2f, new float3(0f, HeadHeight, 0f), quaternion.identity, Now, 1);
            s.Depth.Depth = new float[16];                    // shorter than width x height
            Assert.DoesNotThrow(() => Tick(s));

            s = Sensors(float2.zero, 0f);
            s.HasDepth = true;
            s.Depth = WallAhead(2f, new float3(0f, HeadHeight, 0f), quaternion.identity, Now, 2);
            s.Depth.Fx = 0f;                                  // no intrinsics yet
            Assert.DoesNotThrow(() => Tick(s));

            Assert.That(_guard.Level, Is.EqualTo(BumpLevel.Clear));
        }

        [Test]
        public void FramesWithAndWithoutDepth_MayAlternate()
        {
            // The wall the room's map already holds, 1.5 m ahead: depth agrees, nothing is raised.
            for (int k = 0; k < 120; k++)
            {
                SensorFrame s = Sensors(float2.zero, 0f);
                if (k % 4 == 0)                               // depth at 15 Hz
                {
                    s.HasDepth = true;
                    s.Depth = WallAhead(1.5f, s.Head.Position, s.Head.Rotation, Now, k / 4);
                }
                Tick(s);
            }
            Assert.That(_guard.Level, Is.EqualTo(BumpLevel.Clear));
            Assert.That(float.IsPositiveInfinity(_guard.Core.SectorRange(3)), Is.False, "the image was processed");
            Assert.That(_guard.Core.SectorRange(3), Is.EqualTo(1.5f).Within(0.05f));
        }

        [Test]
        public void WithNoRoom_LiveDepthAloneWarns()
        {
            _guard.SetRoom(null);
            Assert.That(_guard.Core.HasRoom, Is.False);

            // Walking forwards at 1 m/s in the session frame with something 0.6 m ahead in every image.
            const float speed = 1.0f;
            bool warned = false;
            for (int k = 0; k < 60; k++)
            {
                SensorFrame s = Sensors(new float2(0f, speed * (float)(k * Dt)), 0f, TrackingState.Normal, roomActive: false);
                s.HasDepth = true;
                s.Depth = WallAhead(0.6f, s.Head.Position, s.Head.Rotation, Now, k);
                Tick(s);
                warned |= _guard.Level >= BumpLevel.Warn;
            }

            Assert.That(warned, Is.True);
            Assert.That(_guard.Core.Diagnostics.HeadFrameFallback, Is.True);
            Assert.That(_guard.Direction.y, Is.GreaterThan(0.9f), "ahead");
            Assert.That(BumpCue.Shown, Is.GreaterThanOrEqualTo(BumpLevel.Warn));
            Assert.That(_haptics.Pulses, Is.Not.Empty);
        }

        [Test]
        public void WithNoRoomAndNoDepth_NothingIsRaised_AndNothingThrows()
        {
            _guard.SetRoom(null);
            for (int k = 0; k < 60; k++)
                Tick(Sensors(new float2(0f, 0.02f * k), 0f, TrackingState.Normal, roomActive: false));
            Assert.That(_guard.Level, Is.EqualTo(BumpLevel.Clear));
        }

        [Test]
        public void WhileTheRoomFrameIsNotActive_TheMapIsPutAway_AndComesBack()
        {
            // Close to the wall: the map alone gives a level.
            var nearWall = new float2(1.3f, 0f);
            for (int k = 0; k < 10; k++) Tick(Sensors(nearWall, 0f));
            Assert.That(_guard.Level, Is.GreaterThanOrEqualTo(BumpLevel.Warn));

            for (int k = 0; k < 10; k++) Tick(Sensors(nearWall, 0f, TrackingState.Normal, roomActive: false));
            Assert.That(_guard.Core.HasRoom, Is.False, "the head is not in the room frame: the map does not apply");
            Assert.That(_guard.Level, Is.EqualTo(BumpLevel.Clear));
            Assert.That(_guard.Room, Is.SameAs(_room));

            for (int k = 0; k < 10; k++) Tick(Sensors(nearWall, 0f));
            Assert.That(_guard.Core.HasRoom, Is.True);
            Assert.That(_guard.Level, Is.GreaterThanOrEqualTo(BumpLevel.Warn));
        }

        [Test]
        public void Unarmed_TheLevelIsComputed_ButNoCueIsGiven()
        {
            _guard.Armed = false;
            var nearWall = new float2(1.3f, 0f);
            for (int k = 0; k < 30; k++) Tick(Sensors(nearWall, 0f));

            Assert.That(_guard.Level, Is.GreaterThanOrEqualTo(BumpLevel.Warn));
            Assert.That(BumpCue.Shown, Is.EqualTo(BumpLevel.Clear));
            Assert.That(_haptics.Pulses, Is.Empty);
            Assert.That(_view.Icon, Is.EqualTo(CueIcon.None));

            _guard.Armed = true;
            Tick(Sensors(nearWall, 0f));
            Assert.That(BumpCue.Shown, Is.EqualTo(_guard.Level), "armed: the cue starts in that frame");
            Assert.That(_haptics.Pulses.Count, Is.EqualTo(1));
        }

        [Test]
        public void TheOperatorsStop_UsesTheSameCue_EvenUnarmed_AndBumpGuardsLevelIsUntouched()
        {
            _guard.Armed = false;
            for (int k = 0; k < 10; k++) Tick(Sensors(float2.zero, 0f));

            BumpCue.Fire(BumpLevel.Strong, float2.zero, CueSource.Operator);

            // In the call itself: indicator, sound and haptic.
            Assert.That(BumpCue.Shown, Is.EqualTo(BumpLevel.Strong));
            Assert.That(_view.Icon, Is.EqualTo(CueIcon.ChevronStrong));
            Assert.That(_view.Plays, Is.EqualTo(1));
            Assert.That(_view.LastSound, Is.EqualTo(CueSound.Tone));
            Assert.That(_haptics.Pulses.Count, Is.EqualTo(1));
            Assert.That(BumpCue.FireCount, Is.EqualTo(1));

            for (int k = 0; k < 60; k++) Tick(Sensors(float2.zero, 0f));
            Assert.That(BumpCue.Shown, Is.EqualTo(BumpLevel.Strong), "held");
            Assert.That(_guard.Level, Is.EqualTo(BumpLevel.Clear), "the operator's cue is not Bump Guard's level");
            Assert.That(_haptics.Pulses.Count, Is.EqualTo(7).Within(1));

            for (int k = 0; k < 150; k++) Tick(Sensors(float2.zero, 0f));
            Assert.That(BumpCue.Shown, Is.EqualTo(BumpLevel.Clear), "dropped after three seconds");
            Assert.That(_haptics.Pattern, Is.EqualTo(HapticPattern.Off));
        }

        [Test]
        public void AfterDispose_TickDoesNothing()
        {
            _guard.Dispose();
            SensorFrame s = Sensors(new float2(1.4f, 0f), 0f);
            Assert.DoesNotThrow(() => _guard.Tick(in s));
            Assert.DoesNotThrow(() => _guard.Dispose());
        }

        [Test]
        public void Shutdown_DisposesTheView_AndSilencesTheChannels()
        {
            for (int k = 0; k < 5; k++) Tick(Sensors(new float2(1.3f, 0f), 0f));
            Assert.That(_haptics.Pattern, Is.Not.EqualTo(HapticPattern.Off));

            BumpCue.Shutdown();

            Assert.That(_view.Disposed, Is.True);
            Assert.That(_haptics.Pattern, Is.EqualTo(HapticPattern.Off));
            Assert.That(BumpCue.Shown, Is.EqualTo(BumpLevel.Clear));
            int pulses = _haptics.Pulses.Count;
            BumpCue.Fire(BumpLevel.Strong, float2.zero, CueSource.Operator);
            Assert.That(_haptics.Pulses.Count, Is.EqualTo(pulses), "a removed channel gets nothing");
        }

        [Test]
        public void TheSafetySources_NameNoLinkOrTelemetryType()
        {
            string dir = System.IO.Path.Combine(
                System.Environment.GetEnvironmentVariable(RoomFixtures.ProjectDirVariable) ?? System.IO.Directory.GetCurrentDirectory(),
                "Assets", "Atlas", "Safety");
            Assume.That(System.IO.Directory.Exists(dir), "the project folder is reachable");
            foreach (string file in System.IO.Directory.GetFiles(dir, "*.cs", System.IO.SearchOption.AllDirectories))
            {
                string text = System.IO.File.ReadAllText(file);
                Assert.That(text.Contains("Atlas.Link"), Is.False, file);
                Assert.That(text.Contains("Atlas.Telemetry"), Is.False, file);
            }
        }
    }
}

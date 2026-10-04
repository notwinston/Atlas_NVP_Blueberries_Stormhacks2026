using System;
using System.Collections.Generic;
using Atlas.Input;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using static Atlas.Spaces.SmallPlanet.Tests.SmallPlanetTestUtil;

namespace Atlas.Spaces.SmallPlanet.Tests
{
    /// <summary>S4 as a space of the app: registration, the shared kernel at q = 3, the 3-corner walk, the dial, State.</summary>
    public class SmallPlanetSpaceTests
    {
        const double Dt = 1.0 / 60.0;

        /// <summary>Keeps what was logged, as text. Touches no engine object.</summary>
        sealed class RecordingLog : IEventLog
        {
            public readonly List<string> Events = new List<string>(), Data = new List<string>();
            public void Log(string ev) { Events.Add(ev); Data.Add(""); }
            public void Log(string ev, string key, double value) { Events.Add(ev); Data.Add(key); }
            public void Log(string ev, string key, string value) { Events.Add(ev); Data.Add(key); }
            public void Log(string ev, string key1, double value1, string key2, double value2) { Events.Add(ev); Data.Add(key1); }
            public void LogJson(string ev, string dataJson) { Events.Add(ev); Data.Add(dataJson); }
            public int Count(string ev) { int n = 0; foreach (string e in Events) if (e == ev) n++; return n; }
        }

        /// <summary>The app's kernel as SpaceLab leaves it before a space is entered: flat, on the room's lap square.</summary>
        sealed class Rig
        {
            public readonly HolonomyWalkerKernel Kernel = new HolonomyWalkerKernel();
            public readonly ParamRegistry Registry = new ParamRegistry();
            public readonly SmallPlanetCore Core = new SmallPlanetCore();
            public readonly RoomModel Room;
            public double Time;

            /// <param name="roomRadius">planet.R = 0, so R is the room's (the loop-fit radius after the comfort floor),
            /// as these tests of the walk assume; false keeps planet.R's default, the PlanetKit tiles' R0.</param>
            public Rig(string room, double2 head, bool roomRadius = true)
            {
                Room = SmallPlanetTestUtil.Room(room);
                Kernel.Configure(KernelConfig.Flat(Room.LapSide));
                Kernel.ResetToFloor(head);
                SmallPlanetCore.DeclareParams(Registry);
                if (roomRadius) Registry.Find(SmallPlanetCore.ParamRadius).Set(0f);
                Core.Configure(Room, Registry);
                Core.Enter(Kernel, head);
            }

            /// <summary>One frame: step 4 as the frame loop calls it, then the module's step 5.</summary>
            public LapEvent Frame(double2 head, bool left = false, bool right = false, bool tracking = true)
            {
                Time += Dt;
                Kernel.Step(head, tracking, Time);
                return Core.Tick(Kernel.Pose, Kernel.Still, left, right);
            }

            public void Stand(double2 head, int frames) { for (int i = 0; i < frames; i++) Frame(head); }
        }

        /// <summary>The engine-free input core behind the service's interface: no devices, gaze and contexts only.</summary>
        sealed class CoreInput : IAtlasInput
        {
            public readonly AtlasInputCore Core = new AtlasInputCore();
            public void Tick(Vector3 headPosition, Quaternion headRotation, float deltaTime)
            {
                Core.Tick(default(RawInput), new float3(headPosition.x, headPosition.y, headPosition.z),
                    new quaternion(headRotation.x, headRotation.y, headRotation.z, headRotation.w), deltaTime);
            }
            public InputContext Context { get { return Core.Context; } }
            public void PushContext(InputContext context) { Core.PushContext(context); }
            public void PopContext(InputContext context) { Core.PopContext(context); }
            public bool Pressed(AtlasAction action) { return Core.Pressed(action); }
            public bool Held(AtlasAction action) { return Core.Held(action); }
            public float Axis(AtlasAxis axis) { return Core.Axis(axis); }
            public InputSource LastSource(AtlasAction action) { return Core.LastSource(action); }
            public int LastTag(AtlasAction action) { return Core.LastTag(action); }
            public event Action<AtlasAction, InputSource, int> ActionFired
            {
                add { Core.ActionFired += value; }
                remove { Core.ActionFired -= value; }
            }
            public float DwellSeconds { get { return Core.DwellSeconds; } set { Core.DwellSeconds = value; } }
            public GazeTargetId RegisterGazeTarget(in GazeTarget target)
            {
                GazeTargetData data = target.ToData();
                return Core.Targets.Add(in data);
            }
            public void MoveGazeTarget(GazeTargetId id, Vector3 centre) { Core.Targets.Move(id, new float3(centre.x, centre.y, centre.z)); }
            public void SetGazeTargetEnabled(GazeTargetId id, bool enabled) { Core.Targets.SetEnabled(id, enabled); }
            public void UnregisterGazeTarget(GazeTargetId id) { Core.Targets.Remove(id); }
            public GazeState Gaze { get { return new GazeState { Focused = Core.GazeFocused, Progress = Core.GazeProgress }; } }
            public void CopyActionsWithoutGazeTwin(List<AtlasAction> into) { Core.CopyActionsWithoutGazeTwin(into); }
            public bool TryGetTap(out Vector2 screenPosition) { screenPosition = default(Vector2); return false; }
            public bool GamepadPresent { get { return false; } }
            public void SetRumble(float lowFrequency, float highFrequency) { }
            public WalkerInput Walker { get { return default(WalkerInput); } }
        }

        // ---- The dial by gaze (P1-C10; MP §0.5 rule 5) -----------------------------------------------------

        // A walker with no gamepad turns the dial up from the planet: the twin goes live once they stand still, a dwell on
        // it fires DialRight once, and the planet asks for Curved Walk. The real input core, the real S4 logic and kernel,
        // in the frame's order (input, kernel step, the space's Tick, the space's Upload).
        [Test]
        public void Dial_ByGazeAlone_AsksForCurvedWalk()
        {
            var head = new double2(0.2, -0.1);
            var eyes = new float3((float)head.x, 1.6f, (float)head.y);
            var rig = new Rig(RoomFixtures.Sample3x3, head);
            var input = new CoreInput();
            input.PushContext(InputContext.Space);
            var dial = new SmallPlanetDial(input);
            var missing = new List<AtlasAction>();
            quaternion look = quaternion.identity;
            int liveFrame = -1, pressedFrame = -1, presses = 0, asked = 0;
            for (int i = 0; i < 240 && asked == 0; i++)
            {
                input.Tick((Vector3)eyes, (Quaternion)look, (float)Dt);
                bool right = input.Pressed(AtlasAction.DialRight);
                if (right) { presses++; pressedFrame = i; }
                rig.Frame(head, false, right);
                if (rig.Core.SwitchWanted) { asked++; Assert.AreEqual(4, rig.Core.SelectedQ); }
                dial.Place(eyes, quaternion.identity);
                dial.SetLive(rig.Core.Driving && rig.Kernel.Still);
                if (dial.Live)
                {
                    if (liveFrame < 0) liveFrame = i;
                    look = quaternion.LookRotation(math.normalize(dial.Centre - eyes), new float3(0f, 1f, 0f));
                    input.CopyActionsWithoutGazeTwin(missing);
                    Assert.That(missing, Is.Empty, "frame " + i + ": every action S4 reads has a live twin while it can act");
                }
            }
            Assert.GreaterOrEqual(liveFrame, 0, "the detent goes live once the walker stands still");
            Assert.AreEqual(1, presses, "one dwell, one step");
            Assert.GreaterOrEqual((pressedFrame - liveFrame) * Dt, 0.69, "not before the dwell");
            Assert.AreEqual(1, asked, "q left 3: the planet asks for Curved Walk");
            Assert.AreEqual(4, rig.Registry.Find(CurvedDialParams.ParamQ).Int);

            dial.Dispose();
            input.Pressed(AtlasAction.DialRight);
            input.CopyActionsWithoutGazeTwin(missing);
            Assert.That(missing, Is.EquivalentTo(new[] { AtlasAction.DialRight }), "the report is not vacuous, and Dispose removed the target");
        }

        // The dial acts only on a still frame (MP §10.1.4): its twin is live only then, and stands where S2's right detent stands.
        [Test]
        public void Dial_TwinIsLiveOnlyWhileTheWalkerStandsStill()
        {
            var rig = new Rig(RoomFixtures.Sample3x3, double2.zero);
            var input = new CoreInput();
            input.PushContext(InputContext.Space);
            var dial = new SmallPlanetDial(input);
            Assert.IsTrue(dial.Target.IsValid);
            Assert.IsFalse(input.Core.Targets.HasEnabled(AtlasAction.DialRight, InputContext.Space), "dead until the space says the dial can act");
            int moving = 0, still = 0;
            for (int i = 0; i < 300; i++)
            {
                var head = new double2(i < 120 ? 0.005 * i : 0.6, 0);
                rig.Frame(head);
                var eyes = new float3((float)head.x, 1.6f, 0f);
                dial.Place(eyes, quaternion.identity);
                dial.SetLive(rig.Core.Driving && rig.Kernel.Still);
                Assert.AreEqual(rig.Kernel.Still, dial.Live, "frame " + i);
                Assert.AreEqual(dial.Live, input.Core.Targets.HasEnabled(AtlasAction.DialRight, InputContext.Space), "frame " + i);
                Assert.AreEqual(eyes.x + SmallPlanetDial.Chip.x, dial.Centre.x, 1e-5f);
                Assert.AreEqual(eyes.z + SmallPlanetDial.Chip.z, dial.Centre.z, 1e-5f);
                if (dial.Live) still++; else moving++;
            }
            Assert.Greater(moving, 60); Assert.Greater(still, 60);

            // No input service (the Station): still placed and judged, nothing registered.
            var none = new SmallPlanetDial(null);
            none.Place(new float3(1f, 1.6f, 2f), quaternion.identity);
            Assert.IsTrue(none.SetLive(true));
            Assert.AreEqual(1f + SmallPlanetDial.Chip.x, none.Centre.x, 1e-5f);
            Assert.DoesNotThrow(none.Dispose);
        }

        // ---- Registration ----------------------------------------------------------------------------------

        [Test]
        public void Registration_PutsSmallPlanetInTheRegistry()
        {
            SmallPlanetRegistration.Register();
            Assert.IsTrue(SpaceRegistry.IsRegistered(SpaceId.SmallPlanet));
            // Safe to run twice: no domain reload in the Editor.
            SmallPlanetRegistration.Register();
            Assert.IsTrue(SpaceRegistry.IsRegistered(SpaceId.SmallPlanet));
        }

        [Test]
        public void Params_OnceEachInTheirGroups()
        {
            var registry = new ParamRegistry();
            SmallPlanetSpace.DeclareParams(registry);
            int declared = registry.Count;
            SmallPlanetSpace.DeclareParams(registry);
            Assert.AreEqual(declared, registry.Count, "declaring again adds nothing");

            ParamEntry fog = registry.Find("planet.fogRho"), radius = registry.Find("planet.R");
            Assert.AreEqual("planet", fog.Group);
            Assert.AreEqual(SpaceIds.ParamGroup(SpaceId.SmallPlanet), radius.Group);
            Assert.AreEqual(ParamKind.Float, fog.Kind);
            Assert.AreEqual(0.5 * Math.PI, fog.Min, 1e-6);
            Assert.AreEqual(0.8 * Math.PI, fog.Max, 1e-6);
            Assert.AreEqual(0.8 * Math.PI, fog.Float, 1e-6);
            Assert.AreEqual(SmallPlanetConstants.KitRadius, radius.Float, 1e-6, "the PlanetKit tiles' R0: a lower bound, so the props are not stretched");
            Assert.AreEqual(0.0, radius.Min, 0.0, "0 leaves the radius to the room");
            Assert.IsTrue(radius.OnRelease);

            // The dial is Curved Walk's too: one entry, in its group.
            ParamEntry q = registry.Find(CurvedDialParams.ParamQ);
            Assert.AreSame(q, CurvedDialParams.RegisterQ(registry));
            Assert.AreEqual(CurvedDialParams.Group, q.Group);
            Assert.AreEqual(ParamKind.Int, q.Kind);
            Assert.AreEqual(ParamKind.Bool, registry.Find(CurvedDialParams.ParamStrong).Kind);
        }

        // ---- The shared kernel -----------------------------------------------------------------------------

        [Test]
        public void Enter_PutsTheAppsKernelOnTheSphere()
        {
            var head = new double2(0.3, -0.2);
            var registry = new ParamRegistry();
            SmallPlanetCore.DeclareParams(registry);
            registry.Find(SmallPlanetCore.ParamRadius).Set(0f);        // the room's radius
            registry.SetGroupActive(CurvedDialParams.Group, false);   // as SpaceLab leaves it outside Curved Walk
            registry.Find(CurvedDialParams.ParamQ).Set(5);
            var kernel = new HolonomyWalkerKernel();
            RoomModel room = Room(RoomFixtures.Sample3x3);
            kernel.Configure(KernelConfig.Flat(room.LapSide));
            var core = new SmallPlanetCore();
            Assert.Throws<InvalidOperationException>(() => core.Enter(kernel, head));
            Assert.Throws<InvalidOperationException>(() => core.Tick(kernel.Pose, false, false, false));

            core.Configure(room, registry);
            Assert.AreEqual(4, kernel.Q, "Configure leaves the kernel alone");
            Assert.AreEqual(1.27324, core.R, 1e-6);

            core.Enter(kernel, head);
            Assert.Throws<InvalidOperationException>(() => core.Configure(room, registry));
            Assert.AreEqual(3, kernel.Q);
            Assert.AreEqual(3, kernel.Graph.Q);
            Assert.AreEqual(Curvature.Spherical, kernel.Kappa);
            Assert.AreEqual(core.R, kernel.MetresPerUnit, 0.0, "_HypL and State.p[0] are R");
            Assert.AreEqual(core.R, kernel.ConfiguredMetresPerUnit, 0.0);
            Assert.AreEqual(1.0 / (core.R * core.R), kernel.CurvatureNow, 1e-15);
            Assert.AreEqual(6, kernel.Candidates.Count, "six tiles, always");
            // Registered until the first step (§4.9): face 0 is the room's tile and the walker is over the head's floor point.
            Assert.AreEqual(CubeTiling.FaceId(0), kernel.Pose.Tile);
            Assert.IsTrue(ExactEq(TileZeroPlacement.Registered(head, Curvature.Spherical, core.R), kernel.Pose.Local));
            Assert.IsTrue(core.Placed);
            Assert.AreEqual(0, core.Face);
            Assert.IsTrue(ExactEq(kernel.Pose.Local, core.U));
            Assert.AreEqual(3, registry.Find(CurvedDialParams.ParamQ).Int, "the dial reads 3 here");
            Assert.IsTrue(registry.IsGroupActive(CurvedDialParams.Group), "the Console shows the dial S4 uses");
            Assert.IsFalse(kernel.SetCurvatureTarget(Curvature.Hyperbolic, 1.5), "no ramp on the planet");

            core.Exit();
            Assert.IsFalse(registry.IsGroupActive(CurvedDialParams.Group));
            Assert.IsFalse(core.Entered);
            Assert.AreEqual(3, kernel.Q, "SpaceLab, not the space, puts the kernel back to flat");
        }

        // Closure_q3_Replay (P1-B09, MP §3.3, §10.1.3): the recorded lap of the 2 m square on the planet of the 3 x 3 room.
        // On the sphere of R = 2s/pi the walk closes after 3 corners; the log's fourth and fifth sides change nothing.
        [Test]
        public void Closure_q3_Replay()
        {
            HeadPose[] log = Walk("walk_lap5_3x3.poselog");
            Assert.AreEqual(1005, log.Length);
            var start = new double2(log[0].Position.x, log[0].Position.z);
            var rig = new Rig(RoomFixtures.Sample3x3, start);
            Assert.AreEqual(1.27324, rig.Core.R, 1e-6);   // RoomModel stores float32
            var events = new RecordingLog();

            // The walker stands at the flag for half a second before the recorded walk begins.
            rig.Stand(start, 30);
            Assert.IsTrue(rig.Core.Walk.Armed);
            Assert.IsTrue(rig.Core.Walk.Started, "standing on the flag starts the walk");
            Assert.AreEqual(0.0, rig.Core.Walk.DistanceToFlagM, 1e-9);

            var distances = new List<double>();
            var faces = new List<int> { rig.Core.Face };
            double closure = double.NaN, turn = double.NaN;
            double t0 = rig.Time + Dt;
            int closedAt = -1, crossings = 0;
            for (int i = 0; i < log.Length; i++)
            {
                HeadPose s = log[i];
                rig.Time = t0 + s.Time;
                crossings += rig.Kernel.Step(new double2(s.Position.x, s.Position.z), s.Tracking == TrackingState.Normal, rig.Time).Crossings;
                LapEvent ev = rig.Core.Tick(rig.Kernel.Pose, rig.Kernel.Still, false, false);
                WalkEvents.Emit(events, rig.Core.Walk, ev);
                if (faces[faces.Count - 1] != rig.Core.Face) faces.Add(rig.Core.Face);
                if (ev == LapEvent.Corner || ev == LapEvent.Closed) distances.Add(rig.Core.Walk.DistanceToFlagM);
                if (ev == LapEvent.Closed) { closedAt = i; closure = rig.Core.Walk.DistanceToFlagM; turn = rig.Core.Walk.WorldTurnDeg; }
            }
            TestContext.WriteLine($"q = 3 replay: closed at sample {closedAt}, closure {closure:E3} m, world turn {turn:F3} deg, {crossings} crossings");
            Assert.AreEqual(3, distances.Count, "three corners");
            Assert.AreEqual(2.0, distances[0], 0.01); Assert.AreEqual(2.0, distances[1], 0.01);
            Assert.Greater(closedAt, 0);
            Assert.Less(closure, 0.25);
            Assert.Less(closure, 1e-3, "the recorded square is exact: (pi/2) R per side");
            Assert.AreEqual(-90.0, turn, 5.0);
            Assert.IsTrue(rig.Core.Walk.Closed);
            Assert.AreEqual(3, rig.Core.Walk.Corners);

            Assert.AreEqual(3, events.Count("walk.corner"));
            Assert.AreEqual(1, events.Count("walk.closed"));
            Assert.AreEqual(0, events.Count("walk.lap"));
            StringAssert.Contains("\"q\":3", events.Data[0]);
            StringAssert.Contains("\"corners\":3", events.Data[events.Data.Count - 1]);
            WalkEvents.Emit(null, rig.Core.Walk, LapEvent.Closed);   // no log published: nothing happens
        }

        // planet.R's default is the PlanetKit tiles' R0 (2.29 m), a lower bound: a room with a smaller loop-fit radius
        // walks the kit's planet, a room with a larger one keeps its own.
        [Test]
        public void Radius_DefaultsToTheKitRadius_AsALowerBound()
        {
            var rig = new Rig(RoomFixtures.Sample3x3, double2.zero, roomRadius: false);
            Assert.Less(SmallPlanetCore.RadiusFor(rig.Room, false), SmallPlanetConstants.KitRadius, "the 3 x 3 room's own planet is smaller");
            Assert.AreEqual(SmallPlanetConstants.KitRadius, rig.Core.R, 1e-6);
            Assert.AreEqual(rig.Core.R, rig.Kernel.MetresPerUnit, 0.0);
            Assert.AreEqual(3.0, SmallPlanetCore.RadiusFor(rig.Room, false, 3.0), 0.0, "still only a lower bound");
        }

        [Test]
        public void Standing_NoWalkIsArmed()
        {
            var rig = new Rig(RoomFixtures.TinyStanding, double2.zero);
            rig.Stand(double2.zero, 5);
            Assert.IsTrue(rig.Core.Placed);
            Assert.IsFalse(rig.Core.Walk.Armed);
            Assert.AreEqual(rig.Room.Curvature.FloorR, rig.Core.R, 1e-6);
        }

        // ---- The dial --------------------------------------------------------------------------------------

        // MP §10.1.3, §4.6: the dial moves, and asks for Curved Walk, only while the walker stands.
        [Test]
        public void Dial_AsksForCurvedWalkOnlyWhileStanding()
        {
            var rig = new Rig(RoomFixtures.Sample3x3, double2.zero);
            ParamEntry q = rig.Registry.Find(CurvedDialParams.ParamQ);
            rig.Frame(double2.zero);

            // walking at 0.6 m/s: the D-pad does nothing, and a value set from the Console waits
            double x = 0;
            for (int i = 0; i < 10; i++) { x += 0.01; rig.Frame(new double2(x, 0), right: true); }
            Assert.IsFalse(rig.Kernel.Still);
            Assert.AreEqual(3, q.Int);
            Assert.AreEqual(3, rig.Core.SelectedQ);
            q.Set(5);
            x += 0.01; rig.Frame(new double2(x, 0));
            Assert.AreEqual(5, rig.Core.SelectedQ);
            Assert.IsFalse(rig.Core.SwitchWanted, "never while head speed exceeds 0.1 m/s");

            rig.Frame(new double2(x, 0));
            Assert.IsTrue(rig.Kernel.Still);
            Assert.IsTrue(rig.Core.SwitchWanted);
            rig.Frame(new double2(x, 0));
            Assert.IsFalse(rig.Core.SwitchWanted, "asked once");

            rig.Core.SwitchRefused();
            Assert.AreEqual(3, q.Int);
            Assert.AreEqual(3, rig.Core.SelectedQ);
            rig.Frame(new double2(x, 0), left: true);
            Assert.AreEqual(3, q.Int, "3 is the dial's end");
            Assert.IsFalse(rig.Core.SwitchWanted);
            rig.Frame(new double2(x, 0), right: true);
            Assert.AreEqual(4, q.Int);
            Assert.IsTrue(rig.Core.SwitchWanted, "it may ask again after a refusal");
            Assert.AreEqual(3, rig.Kernel.Q, "the planet stays until SpaceLab switches");
        }

        // planet.R and curved.strong change R: the kernel is configured again only while the walker stands (rule 4), at the same place.
        [Test]
        public void Radius_ChangesOnlyWhileStanding_AndKeepsThePlace()
        {
            var rig = new Rig(RoomFixtures.SampleL3x4, double2.zero);
            Assert.AreEqual(1.2, rig.Core.R, 1e-6, "the comfort floor");
            double x = 0;
            for (int i = 0; i < 40; i++) { x += 0.01; rig.Frame(new double2(x, 0.3 * x)); }
            rig.Registry.Find(SmallPlanetCore.ParamRadius).Set(2.0f);
            x += 0.01; rig.Frame(new double2(x, 0.3 * x));
            Assert.AreEqual(1.2, rig.Core.R, 1e-6);
            Assert.IsFalse(rig.Core.RadiusChanged);

            WalkerPose before = rig.Kernel.Pose;
            rig.Frame(new double2(x, 0.3 * x));
            Assert.IsTrue(rig.Core.RadiusChanged);
            Assert.AreEqual(2.0, rig.Core.R, 0.0);
            Assert.AreEqual(2.0, rig.Kernel.MetresPerUnit, 0.0);
            Assert.AreEqual(before.Tile, rig.Kernel.Pose.Tile);
            Assert.Less(MaxAbsDiff(before.Local.M, rig.Kernel.Pose.Local.M), 1e-15);
            rig.Frame(new double2(x, 0.3 * x));
            Assert.IsFalse(rig.Core.RadiusChanged, "once");

            rig.Registry.Find(SmallPlanetCore.ParamRadius).Set(0f);
            rig.Registry.Find(CurvedDialParams.ParamStrong).Set(true);
            rig.Stand(new double2(x, 0.3 * x), 2);
            Assert.AreEqual(SmallPlanetConstants.RadiusFromLapSide(rig.Room.LapSide), rig.Core.R, 1e-6, "strong: the loop-fit radius");
            Assert.AreEqual(rig.Core.R, rig.Kernel.MetresPerUnit, 0.0);
        }

        // ---- State -----------------------------------------------------------------------------------------

        // kernel.md §4.5 and lane D's table: the loop writes the face, U and p[0] = R from the kernel; the module adds p[3..5].
        // The Station's module redraws the phone's frame from that State alone.
        [Test]
        public void State_CarriesWhatTheStationNeeds()
        {
            var rig = new Rig(RoomFixtures.Sample3x3, new double2(-1, -1));
            double x = -1;
            for (int i = 0; i < 150; i++) { x += 0.01; rig.Frame(new double2(x, -1)); }
            rig.Registry.Find(SmallPlanetCore.ParamFogRho).Set(2.0f);
            rig.Frame(new double2(x, -1));

            // what FrameLoopCore.FillState writes for a curved kernel
            StateMessage state = StateMessage.Identity;
            state.SpaceId = (byte)SpaceId.SmallPlanet;
            WalkerPose pose = rig.Kernel.Pose;
            TileId t = pose.Tile;
            state.SetTile(t.Xu, t.Xv, t.Yu, t.Yv, t.Tu, t.Tv);
            Iso2 u = pose.Local;
            state.Xform0 = u.M00; state.Xform1 = u.M01; state.Xform2 = u.M02;
            state.Xform3 = u.M10; state.Xform4 = u.M11; state.Xform5 = u.M12;
            state.Xform6 = u.M20; state.Xform7 = u.M21; state.Xform8 = u.M22;
            state.P0 = (float)rig.Kernel.MetresPerUnit;
            rig.Core.WriteState(ref state);

            Assert.AreEqual(rig.Core.Face, state.Tile0, "B.1: the face");
            Assert.AreEqual(3f, state.P3);
            Assert.AreEqual(1.0 / (rig.Core.R * rig.Core.R), state.P4, 1e-6);
            Assert.AreEqual(2.0f, state.P5);
            Assert.AreEqual((float)rig.Core.R, state.P0, "the module leaves p[0] to the loop");

            var registry = new ParamRegistry();
            var station = new SmallPlanetCore();
            station.Configure(rig.Room, registry);
            station.Enter(null, double2.zero);
            station.Tick(WalkerPose.Start, false, false, false);
            Assert.IsFalse(station.Placed, "a flat pose is no place on the planet: nothing is drawn");

            StateMessage home = StateMessage.Identity;
            station.ReadState(in home);
            station.Tick(WalkerPose.Start, false, false, false);
            Assert.IsFalse(station.Placed, "another space's State is not read");

            station.ReadState(in state);
            station.Tick(WalkerPose.Start, false, true, true);
            Assert.IsTrue(station.Placed);
            Assert.AreEqual(rig.Core.Face, station.Face);
            Assert.IsTrue(ExactEq(rig.Core.U, station.U));
            Assert.AreEqual((float)rig.Core.R, (float)station.R);
            Assert.AreEqual(2.0, station.FogOpaqueRad, 1e-6);
            Assert.IsFalse(station.SwitchWanted, "the Station follows the phone; it has no dial");
            Assert.AreEqual(5, registry.Find(CurvedDialParams.ParamQ).Int, "and writes no parameter");

            Iso2[] a = new Iso2[6], b = new Iso2[6];
            rig.Core.ComputeAnchors(a, out Iso2 beaconA);
            station.ComputeAnchors(b, out Iso2 beaconB);
            for (int f = 0; f < 6; f++) Assert.IsTrue(ExactEq(a[f], b[f]));
            Assert.IsTrue(ExactEq(beaconA, beaconB));
            // §4.6: anchor[t] = inverse(U) * F[t]; the walker's own tile is anchored by inverse(U)
            Assert.Less(MaxAbsDiff(a[rig.Core.Face].M, rig.Core.U.Inverse().M), 1e-15);
            Assert.Throws<ArgumentException>(() => rig.Core.ComputeAnchors(new Iso2[5], out _));
        }

#if !ATLAS_HEADLESS
        // ---- The component (engine) ------------------------------------------------------------------------

        // The module as SpaceLab drives it, with the services AtlasApp publishes: pooled materials only, the fog of
        // §9.5.6 in the look's state (0.5 pi R to 0.8 pi R), and everything handed back on Exit.
        [Test, Category("Unity")]
        public void Module_LifecycleAgainstTheRealServices()
        {
            var pool = Atlas.Rendering.MaterialPool.CreateDefault(16);
            var kernel = new HolonomyWalkerKernel();
            RoomModel room = Room(RoomFixtures.Sample3x3);
            kernel.Configure(KernelConfig.Flat(room.LapSide));
            Atlas.Rendering.MaterialPool oldPool = AtlasServices.Replace(pool);
            IDialKernel oldKernel = AtlasServices.Replace<IDialKernel>(kernel);
            ISpaceModule module = SmallPlanetSpace.Create();
            var space = (SmallPlanetSpace)module;
            try
            {
                Assert.AreEqual(SpaceIds.SceneName(SpaceId.SmallPlanet), space.gameObject.name);
                Assert.AreEqual(SpaceId.SmallPlanet, module.Id);
                Assert.IsFalse(module.Registered);
                var registry = new ParamRegistry();
                module.Configure(room, registry);
                module.Enter(kernel.Pose);
                Assert.AreEqual(3, kernel.Q);
                // The PlanetKit tiles from Resources: their cairn replaces the pole beacon (8 on the procedural fallback: 9).
                Assert.IsTrue(space.View.UsesKit, "the PlanetKit tiles load from Resources");
                Assert.IsNull(space.View.BeaconRenderer);
                Assert.AreEqual(8, pool.Rented, "six tiles, the two lap markers");
                double R = space.Core.R;
                Assert.AreEqual((float)(0.5 * Math.PI * R), Atlas.Rendering.AtlasLook.State.FogStart, 1e-5f);
                Assert.AreEqual((float)(0.8 * Math.PI * R), Atlas.Rendering.AtlasLook.State.FogEnd, 1e-5f);

                var frame = new FrameContext { Time = 1.0, DeltaTime = 1f / 60f, Tracking = TrackingState.Normal };
                frame.Kernel = kernel.Step(double2.zero, true, frame.Time);
                frame.Walker = kernel.Pose;
                module.Tick(in frame);
                ((IUploadStep)module).Upload(in frame);
                Assert.IsTrue(space.View.Shown);
                Assert.AreEqual(Atlas.Rendering.WarpAnchor.ToMatrix(space.Core.U.Inverse()),
                                space.View.TileRenderers[0].sharedMaterial.GetMatrix(Atlas.Rendering.AtlasShaderIDs.Anchor));
                Assert.IsTrue(space.Markers.FlagRenderer.enabled, "the lap flag of the 3-corner walk");

                var state = StateMessage.Identity;
                module.WriteState(ref state);
                Assert.AreEqual(3f, state.P3);

                module.Exit();
                Assert.AreEqual(0, pool.Rented);
                Assert.IsFalse(registry.IsGroupActive(CurvedDialParams.Group));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(space.gameObject);
                if (oldPool != null) AtlasServices.Register(oldPool); else AtlasServices.Unregister(pool);
                if (oldKernel != null) AtlasServices.Register(oldKernel); else AtlasServices.Unregister<IDialKernel>(kernel);
                pool.Dispose();
                Atlas.Rendering.AtlasLook.ResetForSpace();
            }
        }

        // The component with an input service: the twin and its chip exist exactly while the space is entered, the space
        // reads DialRight and nothing it cannot offer by gaze, and a still walker finds the twin live.
        [Test, Category("Unity")]
        public void Module_RegistersTheDialTwin()
        {
            var pool = Atlas.Rendering.MaterialPool.CreateDefault(16);
            var kernel = new HolonomyWalkerKernel();
            var input = new CoreInput();
            input.PushContext(InputContext.Space);
            RoomModel room = Room(RoomFixtures.Sample3x3);
            kernel.Configure(KernelConfig.Flat(room.LapSide));
            Atlas.Rendering.MaterialPool oldPool = AtlasServices.Replace(pool);
            IDialKernel oldKernel = AtlasServices.Replace<IDialKernel>(kernel);
            IAtlasInput oldInput = AtlasServices.Replace<IAtlasInput>(input);
            ISpaceModule module = SmallPlanetSpace.Create();
            var space = (SmallPlanetSpace)module;
            try
            {
                module.Configure(room, new ParamRegistry());
                Assert.IsNull(space.Dial, "nothing before Enter");
                module.Enter(kernel.Pose);
                Assert.IsNotNull(space.Dial);
                Assert.IsTrue(space.Dial.Target.IsValid);
                Assert.AreEqual(space.View.UsesKit ? 9 : 10, pool.Rented, "six tiles, the beacon on the procedural fallback, the two lap markers, the dial chip");
                Assert.IsFalse(space.DialChip.activeSelf);

                var missing = new List<AtlasAction>();
                var eyes = new float3(0f, 1.6f, 0f);
                for (int i = 0; i < 120; i++)
                {
                    input.Tick((Vector3)eyes, Quaternion.identity, 1f / 60f);
                    var frame = new FrameContext
                    {
                        FrameIndex = i, Time = (i + 1) / 60.0, DeltaTime = 1f / 60f, Tracking = TrackingState.Normal,
                        HeadPosition = eyes, HeadRotation = quaternion.identity
                    };
                    frame.Kernel = kernel.Step(double2.zero, true, frame.Time);
                    frame.Walker = kernel.Pose;
                    module.Tick(in frame);
                    ((IUploadStep)module).Upload(in frame);
                    Assert.AreEqual(kernel.Still, space.Dial.Live, "frame " + i);
                    Assert.AreEqual(space.Dial.Live, space.DialChip.activeSelf, "frame " + i);
                    if (!space.Dial.Live) continue;
                    input.CopyActionsWithoutGazeTwin(missing);
                    Assert.That(missing, Is.Empty, "frame " + i);
                }
                Assert.IsTrue(space.Dial.Live, "the walker stood still");
                Assert.IsTrue(input.Core.Targets.HasEnabled(AtlasAction.DialRight, InputContext.Space));
                Vector3 centre = space.DialChip.transform.position;
                Assert.AreEqual(SmallPlanetDial.Chip.x, centre.x, 1e-5f);
                Assert.AreEqual(1.6f + SmallPlanetDial.Chip.y, centre.y, 1e-5f);
                Assert.AreEqual(SmallPlanetDial.Chip.z, centre.z, 1e-5f);

                module.Exit();
                Assert.IsNull(space.Dial);
                Assert.IsFalse(input.Core.Targets.HasEnabled(AtlasAction.DialRight, InputContext.Space), "unregistered at Exit");
                Assert.AreEqual(0, pool.Rented);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(space.gameObject);
                if (oldPool != null) AtlasServices.Register(oldPool); else AtlasServices.Unregister(pool);
                if (oldKernel != null) AtlasServices.Register(oldKernel); else AtlasServices.Unregister<IDialKernel>(kernel);
                if (oldInput != null) AtlasServices.Register(oldInput); else AtlasServices.Unregister<IAtlasInput>(input);
                pool.Dispose();
                Atlas.Rendering.AtlasLook.ResetForSpace();
            }
        }
#endif
    }
}

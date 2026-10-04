using System;
using System.Collections.Generic;
using Atlas.Blades.Playground.Exhibits;
using Atlas.Blades.Playground.Shell;
using Atlas.Input;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Spaces.Glued;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Blades.Playground.Tests.ShellFlow
{
    /// <summary>
    /// P2-C01 integration: the composed shell driven through beats 1 to 10 with fakes for the lane B ports, the
    /// operator's SceneCmd, and the six space entrances. This checks the wiring the component tests cannot: the
    /// director to prompts, captions, menu, exhibits, probe, comfort and SEQ.
    /// </summary>
    public class PlaygroundFlowTests
    {
        sealed class FlatRamp : ICurvatureRamp
        {
            public double Kappa { get { return 0.0; } }
            public void RampToFlat(float seconds) { }
            public bool IsFlat { get { return true; } }
        }

        sealed class OriginProjection : IFloorProjection
        {
            public bool TryProject(in WalkerPose pose, out double2 floorMetres, out double yawRadians)
            {
                floorMetres = double2.zero;
                yawRadians = 0.0;
                return true;
            }
        }

        sealed class FixedBearing : IHomeBearing
        {
            public double Az = 225.0;
            public void MarkStart() { }
            public bool TryGet(out double azimuthDeg, out double distanceMetres)
            {
                azimuthDeg = Az;
                distanceMetres = 3.0;
                return true;
            }
        }

        // The Glued exhibit's real core behind the link, so glued.* events are real. Completion is the walker's
        // tasks; the test marks it done when the walk through a cell edge has been made.
        sealed class GluedCoreLink : IExhibitLink
        {
            readonly GluedExhibitCore m_Core;
            public bool Finished;
            public GluedCoreLink(GluedExhibitCore core)
            {
                m_Core = core;
                m_Core.PromptChanged += s => { if (PromptChanged != null) PromptChanged(s); };
            }
            public GluedExhibitCore Core { get { return m_Core; } }
            public string Prompt { get { return m_Core.Prompt; } }
            public event Action<string> PromptChanged;
            public void Restart() { m_Core.Begin(); }
            public bool Completed { get { return Finished || m_Core.Task == GluedTask.Done; } }
        }

        sealed class Rig
        {
            public readonly RecordingEvents Log = new RecordingEvents();
            public readonly FakeHost Host = new FakeHost { CurrentNow = SpaceId.Home };
            public readonly ShellWalkFakes.FakeContent Content = new ShellWalkFakes.FakeContent();
            public readonly ShellWalkFakes.FakeDial Dial = new ShellWalkFakes.FakeDial();
            public readonly ShellWalkFakes.FakeWalk Walk = new ShellWalkFakes.FakeWalk();
            public readonly ShellWalkFakes.FakeMode Mode = new ShellWalkFakes.FakeMode();
            public readonly ShellWalkFakes.FakeBeacon Beacon = new ShellWalkFakes.FakeBeacon();
            public readonly ShellWalkFakes.FakeMap Map = new ShellWalkFakes.FakeMap();
            public readonly FixedBearing Bearing = new FixedBearing();
            public readonly ParamRegistry Params = new ParamRegistry();
            public readonly RoomModel Room = RoomFixtures.LoadFromProject(RoomFixtures.Sample4x5Table);
            public readonly PlaygroundShell Shell;
            public double Now;

            public Rig()
            {
                Shell = new PlaygroundShell(new PlaygroundParts
                {
                    Log = Log, Host = Host, Params = Params, Content = Content, Clock = () => Now,
                    Dial = Dial, Walk = Walk, Mode = Mode, Beacon = Beacon, MiniMap = Map,
                    HomeBearing = Bearing, Ramp = new FlatRamp(), Projection = new OriginProjection()
                });
            }

            public PlaygroundBeat Beat { get { return Shell.Director.Beat; } }

            /// <summary>One frame at 60 Hz with the head at (x, z).</summary>
            public void Frame(float x = 0f, float z = 0f, float yawDegrees = 0f)
            {
                Now += 1.0 / 60.0;
                Shell.Update(new PlaygroundFrame
                {
                    Time = Now, DeltaTime = 1f / 60f,
                    HeadPosition = new float3(x, 1.6f, z),
                    HeadRotation = quaternion.RotateY(math.radians(yawDegrees)),
                    Tracking = TrackingState.Normal,
                    Walker = WalkerPose.Start
                });
            }

            public void Frames(int n) { for (int i = 0; i < n; i++) Frame(); }

            /// <summary>Frames until the beat changes (at most n), so a beat is not run past by waiting.</summary>
            public void FramesWhileIn(PlaygroundBeat beat, int n)
            {
                for (int i = 0; i < n && Beat == beat; i++) Frame();
            }

            public void Press(AtlasAction action, InputSource source = InputSource.Gamepad, int tag = 0)
            {
                Shell.Map.OnActionFired(action, source, tag);
            }

            public int IndexOfBeat(int beat)
            {
                string want = "{\"beat\":" + beat + ",";
                for (int i = 0; i < Log.Events.Count; i++)
                    if (Log.Events[i].Ev == "pg.beat" && Log.Events[i].Json.StartsWith(want)) return i;
                return -1;
            }

            public int IndexOf(string ev, int from = 0)
            {
                for (int i = from; i < Log.Events.Count; i++) if (Log.Events[i].Ev == ev) return i;
                return -1;
            }
        }

        static float3 CellHead(in GluedLattice lattice, int col, int row)
        {
            double3 c = lattice.CellCentre(new GluedCell(col, row, 0));
            return new float3((float)c.x, 1.6f + (float)c.y, (float)c.z);
        }

        // Walks the run to the start of beat `target`, by the walker's own inputs.
        static void RunTo(Rig r, int target)
        {
            r.Shell.Begin();
            if (target <= 1) return;
            r.Frame();
            Assert.That(r.Shell.SetRoom(r.Room), Is.True);
            r.Frame(); r.Frame();
            if (target <= 2) return;
            r.Content.Build(r.Room);
            r.Shell.Scan.LocalNetworkAnswered = true;
            r.Press(AtlasAction.Confirm);
            r.Frame(); r.Frame();
            if (target <= 3) return;
            r.Shell.Scan.StereoOn = true;
            r.FramesWhileIn(PlaygroundBeat.HeadsetOn, 200);
            if (target <= 4) return;
            r.Frame(); r.Frame();
            if (target <= 5) return;
            r.Press(AtlasAction.DialRight);
            r.Frame(); r.Frame();
            if (target <= 6) return;
            WalkLoop(r, 4);
            r.Dial.Set(5);
            WalkLoop(r, 5);
            r.Frame();
            if (target <= 7) return;
            r.Press(AtlasAction.Primary);
            r.Frame(); r.Frame();
            if (target <= 8) return;
            r.Press(AtlasAction.SpaceMenu);
            r.Frame(); r.Frame();
        }

        // The walk of one q: the tracker replays the loop; at the third corner the probe is asked and answered.
        static void WalkLoop(Rig r, int q)
        {
            r.Walk.QueueLoop(q, 0.1, 90.0);
            for (int i = 0; i < q + 1; i++)
            {
                r.Frame();
                if (r.Shell.Probe.IsArmed) r.Shell.Probe.Capture(0.0, 0.0, 0.0, 1.0, r.Now);
            }
        }

        [Test]
        public void TheRunWalksAllTenBeatsByTheWalkersInputs()
        {
            var r = new Rig();
            var seen = new List<string>();
            RunTo(r, 1);
            seen.Add(r.Shell.Chip.Text);
            Assert.That(r.Beat, Is.EqualTo(PlaygroundBeat.Scan));
            r.Frame();
            Assert.That(r.Beat, Is.EqualTo(PlaygroundBeat.Scan), "no room yet");

            RunTo2(r, PlaygroundBeat.HomeAppears, seen, () =>
            {
                Assert.That(r.Shell.SetRoom(r.Room), Is.True);
                r.Frame(); r.Frame();
            });
            RunTo2(r, PlaygroundBeat.HeadsetOn, seen, () =>
            {
                r.Content.Build(r.Room);
                r.Shell.Scan.LocalNetworkAnswered = true;
                r.Press(AtlasAction.Confirm);
                r.Frame(); r.Frame();
            });
            RunTo2(r, PlaygroundBeat.Centre, seen, () =>
            {
                r.Shell.Scan.StereoOn = true;
                r.FramesWhileIn(PlaygroundBeat.HeadsetOn, 200);
            });
            RunTo2(r, PlaygroundBeat.Dial, seen, () => { r.Frame(); r.Frame(); });
            RunTo2(r, PlaygroundBeat.Walk345, seen, () =>
            {
                r.Press(AtlasAction.DialRight);
                r.Frame(); r.Frame();
            });
            Assert.That(r.Dial.Q, Is.EqualTo(5));
            Assert.That(r.Walk.Armed, Is.True, "the beat arms the loop");
            Assert.That(r.Shell.Comfort.IsRunning, Is.True, "the comfort check runs from beat 4");

            RunTo2(r, PlaygroundBeat.FreeCurvedWalk, seen, () =>
            {
                r.Dial.Set(4);
                WalkLoop(r, 4);
                r.Dial.Set(5);
                WalkLoop(r, 5);
                r.Frame();
            });
            Assert.That(r.Shell.Probe.Taken, Is.EqualTo(2), "the pointing probe at q = 4 and q = 5");
            Assert.That(r.Beacon.Enabled, Is.True);
            Assert.That(r.Map.Visible, Is.True, "the mini-map after lap 1");
            Assert.That(r.Shell.Caption.Text, Is.EqualTo(CaptionTexts.ModeSmooth));

            RunTo2(r, PlaygroundBeat.Compare, seen, () =>
            {
                r.Press(AtlasAction.Primary);
                r.Frame(); r.Frame();
            });
            Assert.That(r.Mode.Mode, Is.EqualTo(CurvedMode.AlignedCells));
            Assert.That(r.Shell.Caption.Text, Is.EqualTo(CaptionTexts.ModeCorners));

            RunTo2(r, PlaygroundBeat.ExhibitMenu, seen, () =>
            {
                r.Press(AtlasAction.SpaceMenu);
                r.Frame(); r.Frame();
            });
            Assert.That(r.Shell.Menu.IsOpen, Is.True);

            // The walker picks Glued; its task lines reach the prompt chip.
            var core = new GluedExhibitCore(r.Room, r.Log);
            var link = new GluedCoreLink(core);
            r.Shell.Exhibits.Register(ExhibitId.Glued, link);
            r.Press(AtlasAction.Confirm);
            Assert.That(r.Host.Switches, Has.Member(SpaceId.Glued));
            Assert.That(r.Shell.Exhibits.ActiveId, Is.EqualTo(ExhibitId.Glued));
            r.Host.Enter(SpaceId.Glued);
            r.Shell.OnExhibitReady();
            Assert.That(r.Shell.Chip.Text, Is.EqualTo(core.Prompt));
            Assert.That(r.Shell.Chip.Kind, Is.EqualTo(PromptKind.Exhibit));
            r.Frame();
            Assert.That(r.Beat, Is.EqualTo(PlaygroundBeat.ExhibitMenu), "the exhibit holds beat 9 until it is done");

            GluedLattice lattice = core.Lattice;
            core.Step(CellHead(in lattice, 0, 0), quaternion.identity, GluedLayout.Torus);
            core.Step(CellHead(in lattice, 1, 0), quaternion.identity, GluedLayout.Torus);
            Assert.That(r.Log.Count("glued.wrap"), Is.EqualTo(1));
            Assert.That(r.Shell.Chip.Text, Is.EqualTo(core.Prompt), "a changed task line replaces the chip text");
            link.Finished = true;
            r.Host.RoomNow = r.Room;

            RunTo2(r, PlaygroundBeat.ReturnHome, seen, () => r.Frame());
            r.Frame();
            r.Host.Enter(SpaceId.Home);
            r.Frame(); r.Frame();
            Assert.That(r.Shell.Director.Finished, Is.True);
            Assert.That(r.Shell.Return.State, Is.EqualTo(HomeReturnState.Done));
            Assert.That(r.Log.Count("pg.home"), Is.EqualTo(1));

            // SEQ is asked at the end, answered by the operator.
            Assert.That(r.Shell.Seq.Pending, Is.True);
            r.Shell.Enter(OperatorEntryKind.Seq, 6);
            Assert.That(r.Shell.Seq.Pending, Is.False, "the entry closes the question");
            Assert.That(r.Log.Count("seq"), Is.EqualTo(0), "the Hub writes seq; the phone must not log a second one");

            // FMS every 60 s once the headset is on.
            r.Now += 61.0;
            r.Frame();
            Assert.That(r.Shell.Comfort.AskCount, Is.EqualTo(0), "stopped at the end of the run");

            // The ten beats, once each, in order; the measures lie where the plan puts them.
            Assert.That(r.Log.Count("pg.beat"), Is.EqualTo(10));
            int prev = -1;
            for (int beat = 1; beat <= 10; beat++)
            {
                int at = r.IndexOfBeat(beat);
                Assert.That(at, Is.GreaterThan(prev), "beat " + beat + " in order");
                prev = at;
            }
            int b6 = r.IndexOfBeat(6), b7 = r.IndexOfBeat(7), b9 = r.IndexOfBeat(9), b10 = r.IndexOfBeat(10);
            Assert.That(r.IndexOf("walk.corner", b6), Is.GreaterThan(b6).And.LessThan(b7));
            Assert.That(r.IndexOf("walk.closed", b6), Is.LessThan(b7));
            Assert.That(r.IndexOf("probe", b6), Is.LessThan(b7));
            Assert.That(r.Log.Count("probe"), Is.EqualTo(2));
            Assert.That(r.IndexOf("glued.wrap", b9), Is.GreaterThan(b9).And.LessThan(b10));
            Assert.That(r.IndexOf("pg.home", b10), Is.GreaterThan(b10));
            Assert.That(r.Shell.Seq.Answers, Is.EqualTo(1), "SEQ asked after the walker is home and answered once");

            // One prompt per beat: the chip showed each beat's line once, on entry.
            var expected = new List<string>();
            for (int beat = 1; beat <= 10; beat++) expected.Add(BeatPrompts.TextFor(beat));
            Assert.That(seen, Is.EqualTo(expected));
            for (int beat = 1; beat <= 10; beat++) Assert.That(r.Shell.Prompts.ShownForBeat(beat), Is.EqualTo(1), "beat " + beat);
        }

        // Runs the action that satisfies the beat before `beat`, then checks that the next beat started and that its
        // line is the one on the chip.
        static void RunTo2(Rig r, PlaygroundBeat beat, List<string> seen, Action act)
        {
            act();
            Assert.That(r.Beat, Is.EqualTo(beat));
            seen.Add(r.Shell.Chip.Text);
        }

        [Test]
        public void FmsIsAskedEverySixtySecondsAndTheEntryIsNotLoggedByThePhone()
        {
            var r = new Rig();
            RunTo(r, 5);
            Assert.That(r.Shell.Comfort.IsRunning, Is.True);
            double start = r.Shell.Comfort.LastAskS;
            r.Now = r.Now + 60.0;
            r.Frame();
            Assert.That(r.Shell.Comfort.AskCount, Is.EqualTo(1));
            r.Shell.Enter(OperatorEntryKind.Fms, 3);
            Assert.That(r.Log.Count("fms"), Is.EqualTo(0), "the Hub writes fms; the phone must not log a second one");
            Assert.That(r.Shell.Comfort.LastScore, Is.EqualTo(3));
            r.Shell.Enter(OperatorEntryKind.Fms, 20);
            Assert.That(r.Log.Count("pg.fmsAlert"), Is.EqualTo(0), "the Hub writes pg.fmsAlert too");
            Assert.That(r.Shell.Comfort.AlertActive, Is.True, "the phone still shows the stop line");
            Assert.That(r.Params.Find(ComfortMonitor.StopParamName), Is.Not.Null, "pg.fmsStop is declared");
        }

        [Test]
        public void SceneCmdTapDrivesTheShellAndPassesTheRestOn()
        {
            var r = new Rig();
            r.Shell.Begin();
            var inner = new RecordingCommands();
            var tap = new OperatorCommandTap(inner, r.Shell);

            tap.SwitchSpace(SpaceId.Home, "next");
            Assert.That(r.Beat, Is.EqualTo(PlaygroundBeat.HomeAppears));
            tap.SwitchSpace(SpaceId.Home, "beat:6");
            Assert.That(r.Beat, Is.EqualTo(PlaygroundBeat.Walk345));
            Assert.That(r.Walk.Armed, Is.False, "no room yet: the walk is not armed, and the jump does not fail");
            r.Frame();
            r.Shell.SetRoom(r.Room);
            tap.SwitchSpace(SpaceId.Home, "beat:6");
            Assert.That(r.Walk.Armed, Is.True);
            Assert.That(inner.Switches, Is.Empty, "the director's commands are not forwarded");

            tap.SwitchSpace(SpaceId.Glued, "klein");
            tap.SwitchSpace(SpaceId.Home, null);
            tap.Home();
            Assert.That(inner.Switches, Is.EqualTo(new[] { "Glued:klein", "Home:" }));
            Assert.That(inner.Homes, Is.EqualTo(1));
        }

        sealed class RecordingCommands : IOperatorCommands
        {
            public readonly List<string> Switches = new List<string>();
            public int Homes;
            public void SwitchSpace(SpaceId space, string variant) { Switches.Add(space + ":" + variant); }
            public void Home() { Homes++; }
            public void Recentre() { }
            public void SetQuality(int tier) { }
            public void Cue() { }
            public void SetLock(bool on) { }
            public void Walk(float moveX, float moveY, float turnDegreesPerSecond, float speed) { }
        }

        [Test]
        public void TheChipIsShownAtEveryEntrance_OnePerSpace()
        {
            var r = new Rig();
            SpaceId[] all =
            {
                SpaceId.Home, SpaceId.Glued, SpaceId.CurvedWalk, SpaceId.AlignedCells, SpaceId.SmallPlanet, SpaceId.Portal,
                SpaceId.Slicer
            };
            foreach (SpaceId id in all)
            {
                r.Host.Exit(r.Host.CurrentNow);
                r.Host.Enter(id);
                Assert.That(r.Shell.Caption.Visible, Is.True, id + " shows its chip");
                Assert.That(r.Shell.Caption.Space, Is.EqualTo(id));
                Assert.That(r.Shell.Caption.Text, Is.EqualTo(id == SpaceId.CurvedWalk ? CaptionTexts.CurvedWalkForDial(5) : CaptionTexts.For(id)));
                Assert.That(r.Shell.Caption.ShownCount(id), Is.EqualTo(1));
            }
            Assert.That(r.Shell.Caption.EntrancesShown, Is.EqualTo(all.Length));
        }

        [Test]
        public void ExhibitsAreLinkedToThePromptQueueAndTheMenu()
        {
            var r = new Rig();
            RunTo(r, 9);
            Assert.That(r.Beat, Is.EqualTo(PlaygroundBeat.ExhibitMenu));
            var done = r.Shell.Menu;
            // Done ends the run's beat 9 without an exhibit.
            r.Press(AtlasAction.NavDown);
            r.Press(AtlasAction.NavDown);
            r.Press(AtlasAction.NavDown);
            Assert.That(done.Selected, Is.EqualTo((int)ExhibitId.Done));
            r.Press(AtlasAction.Confirm);
            r.Frame();
            Assert.That(r.Beat, Is.EqualTo(PlaygroundBeat.ReturnHome));
            Assert.That(r.Shell.Menu.IsOpen, Is.False);
        }

        [Test]
        public void ReturnHomeTwinInAnExhibitStartsBeat10()
        {
            var r = new Rig();
            RunTo(r, 9);
            r.Shell.Exhibits.Register(ExhibitId.Glued, new GluedCoreLink(new GluedExhibitCore(r.Room, null)));
            r.Press(AtlasAction.Confirm);
            r.Host.Enter(SpaceId.Glued);
            r.Frame();
            Assert.That(r.Beat, Is.EqualTo(PlaygroundBeat.ExhibitMenu));
            r.Press(AtlasAction.ReturnHome);
            r.Frame();
            Assert.That(r.Beat, Is.EqualTo(PlaygroundBeat.ReturnHome));
        }
    }
}

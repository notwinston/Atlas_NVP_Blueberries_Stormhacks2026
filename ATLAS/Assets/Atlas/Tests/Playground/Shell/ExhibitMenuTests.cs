using System.Collections.Generic;
using Atlas.Input;
using Atlas.Kernel;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Blades.Playground.Tests
{
    /// <summary>P2-C08: the exhibit menu, the slot for lane A's Slicer toybox, and the return Home.</summary>
    public class ExhibitMenuTests
    {
        sealed class Slot : IExhibitSlot
        {
            public bool Ready { get; set; } = true;
            public int Launched;
            public bool Launch() { Launched++; return true; }
        }

        sealed class Ramp : ICurvatureRamp
        {
            public double Kappa { get; set; } = -1.0;
            public int Ramps;
            public float Seconds;
            public bool IsFlat { get { return Kappa == 0.0; } }
            public void RampToFlat(float seconds) { Ramps++; Seconds = seconds; }
        }

        sealed class Projection : IFloorProjection
        {
            public double2 Floor;
            public double Yaw;
            public bool Ok = true;
            public bool TryProject(in WalkerPose pose, out double2 floorMetres, out double yawRadians)
            {
                floorMetres = Floor;
                yawRadians = Yaw;
                return Ok;
            }
        }

        sealed class LogSink : IEventLog
        {
            public readonly List<string> Lines = new List<string>();
            public void Log(string ev) { Lines.Add(ev); }
            public void Log(string ev, string key, double value) { Lines.Add(ev + " " + key + "=" + value); }
            public void Log(string ev, string key, string value) { Lines.Add(ev + " " + key + "=" + value); }
            public void Log(string ev, string key1, double value1, string key2, double value2) { Lines.Add(ev); }
            public void LogJson(string ev, string dataJson) { Lines.Add(ev + " " + dataJson); }
        }

        // ---- the menu ----------------------------------------------------------------------------------------

        [Test]
        public void TheRowsAreTheThreeExhibitsAndDone()
        {
            Assert.That(ExhibitMenu.RowCount, Is.EqualTo(5));
            Assert.That(new[] { ExhibitMenu.Label(0), ExhibitMenu.Label(1), ExhibitMenu.Label(2), ExhibitMenu.Label(3), ExhibitMenu.Label(4) },
                        Is.EqualTo(new[] { "Glued", "Impossible Rooms", "Slicer", "Network", "Done" }));
            Assert.That(ExhibitMenu.SpaceOf(ExhibitId.Glued), Is.EqualTo(SpaceId.Glued));
            Assert.That(ExhibitMenu.SpaceOf(ExhibitId.ImpossibleRooms), Is.EqualTo(SpaceId.Portal));
            Assert.That(ExhibitMenu.SpaceOf(ExhibitId.Slicer), Is.EqualTo(SpaceId.Slicer));
        }

        [Test]
        public void PickingAnExhibitAsksSpaceLabAndClosesTheMenu()
        {
            var host = new FakeHost();
            var menu = new ExhibitMenu(host, null, null);
            ExhibitId chosen = ExhibitId.Done;
            menu.Chosen += id => chosen = id;
            Assert.That(menu.Open(), Is.True);
            Assert.That(menu.Pick(1), Is.True);
            Assert.That(host.Switches, Is.EqualTo(new[] { SpaceId.Portal }));
            Assert.That(chosen, Is.EqualTo(ExhibitId.ImpossibleRooms));
            Assert.That(menu.IsOpen, Is.False);
        }

        sealed class HomeExhibit : IHomeExhibit
        {
            public bool Ready { get; set; } = true;
            public bool Active { get; private set; }
            public string Label { get { return "Network"; } }
            public bool Begin() { Active = true; return true; }
            public void End() { Active = false; }
        }

        [Test]
        public void NetworkRow_ReachableFromMenu()
        {
            var exhibit = new HomeExhibit();
            var host = new FakeHost();
            var menu = new ExhibitMenu(host, null, null);
            Assert.That(menu.IsReady(ExhibitId.Network), Is.False, "no exhibit published: not ready");
            AtlasServices.Register<IHomeExhibit>(exhibit);
            try
            {
                Assert.That(menu.IsReady(ExhibitId.Network), Is.True);
                ExhibitId chosen = ExhibitId.Done;
                menu.Chosen += id => chosen = id;
                menu.Open();
                menu.Step(1); menu.Step(1); menu.Step(1);
                Assert.That(menu.Selected, Is.EqualTo((int)ExhibitId.Network));
                Assert.That(menu.Pick(), Is.True);
                Assert.That(exhibit.Active, Is.True);
                Assert.That(chosen, Is.EqualTo(ExhibitId.Network));
                Assert.That(host.Switches, Is.Empty, "the network is shown in Home, not a space of its own");
                Assert.That(menu.IsOpen, Is.False);

                // Done hides it again, leaving Home as it was.
                menu.Open();
                Assert.That(menu.Pick((int)ExhibitId.Done), Is.True);
                Assert.That(exhibit.Active, Is.False);

                exhibit.Ready = false;
                menu.Open();
                Assert.That(menu.Pick((int)ExhibitId.Network), Is.False);
            }
            finally { AtlasServices.Unregister<IHomeExhibit>(exhibit); }
        }

        [Test]
        public void StepsWrapAndSkipRowsThatAreNotReady()
        {
            var host = new FakeHost();
            host.Available.Remove(SpaceId.Slicer);
            var menu = new ExhibitMenu(host, null, null);
            menu.Open();
            Assert.That(menu.Selected, Is.EqualTo(0));
            menu.Step(1);
            Assert.That(menu.Selected, Is.EqualTo(1));
            menu.Step(1);
            Assert.That(menu.Selected, Is.EqualTo(4), "the Slicer and Network rows are skipped");
            menu.Step(1);
            Assert.That(menu.Selected, Is.EqualTo(0));
            menu.Step(-1);
            Assert.That(menu.Selected, Is.EqualTo(4));
        }

        [Test]
        public void ARowThatIsNotReadyCannotBePicked()
        {
            var host = new FakeHost();
            host.Available.Remove(SpaceId.Glued);
            var menu = new ExhibitMenu(host, null, null);
            menu.Open();
            Assert.That(menu.Selected, Is.EqualTo(1), "opens on the first ready row");
            Assert.That(menu.Pick(0), Is.False);
            Assert.That(host.Switches, Is.Empty);
            Assert.That(menu.IsOpen, Is.True);
        }

        [Test]
        public void DoneClosesTheMenuAndReportsIt()
        {
            var host = new FakeHost();
            var menu = new ExhibitMenu(host, null, null);
            int done = 0;
            menu.DonePicked += () => done++;
            menu.Open();
            menu.Step(-1);
            Assert.That(menu.Selected, Is.EqualTo(4));
            Assert.That(menu.Pick(), Is.True);
            Assert.That(done, Is.EqualTo(1));
            Assert.That(menu.IsOpen, Is.False);
            Assert.That(host.Switches, Is.Empty);
        }

        [Test]
        public void LaneAsSlicerSlotDecidesTheSlicerRow()
        {
            var host = new FakeHost();
            host.Available.Remove(SpaceId.Slicer);
            var menu = new ExhibitMenu(host, null, null);
            Assert.That(menu.IsReady(ExhibitId.Slicer), Is.False, "no slot and no space: not ready");

            var slot = new Slot();
            menu.SlicerSlot = slot;
            Assert.That(menu.IsReady(ExhibitId.Slicer), Is.True);
            menu.Open();
            Assert.That(menu.Pick((int)ExhibitId.Slicer), Is.True);
            Assert.That(slot.Launched, Is.EqualTo(1));
            Assert.That(host.Switches, Is.Empty, "the slot launched it, not SpaceLab");

            slot.Ready = false;
            menu.Open();
            Assert.That(menu.Pick((int)ExhibitId.Slicer), Is.False);
        }

        [Test]
        public void TheTwinsOpenStepAndPick()
        {
            var host = new FakeHost();
            var map = new ActionMap();
            var menu = new ExhibitMenu(host, null, map);
            map.Fired += menu.OnAction;

            map.OnActionFired(AtlasAction.SpaceMenu, InputSource.Gamepad, 0);
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(map.MenuOpen, Is.True);
            map.OnActionFired(AtlasAction.NavDown, InputSource.Gamepad, 0);
            Assert.That(menu.Selected, Is.EqualTo(1));
            map.OnActionFired(AtlasAction.Confirm, InputSource.Gamepad, 0);
            Assert.That(host.Switches, Is.EqualTo(new[] { SpaceId.Portal }));
            Assert.That(map.MenuOpen, Is.False);
        }

        [Test]
        public void EnteringASpaceClosesTheMenu()
        {
            var host = new FakeHost();
            var menu = new ExhibitMenu(host, null, null);
            menu.Open();
            host.Enter(SpaceId.Glued);
            Assert.That(menu.IsOpen, Is.False);
        }

        [Test]
        public void TheMenuHoldsTheMenuContextWhileOpen()
        {
            var host = new FakeHost();
            var input = new CoreInput();
            var map = new ActionMap();
            var menu = new ExhibitMenu(host, input, map);
            menu.PlaceSigns(Rooms.Empty(), new PgGazeTargets(input));
            Assert.That(input.Context, Is.EqualTo(InputContext.Space));
            menu.Open();
            Assert.That(input.Context, Is.EqualTo(InputContext.Menu));
            menu.Close();
            Assert.That(input.Context, Is.EqualTo(InputContext.Space));
        }

        [Test]
        public void ADwellOnASignPicksThatSign()
        {
            var host = new FakeHost();
            var input = new CoreInput();
            var map = new ActionMap();
            var menu = new ExhibitMenu(host, input, map);
            map.Fired += menu.OnAction;
            input.ActionFired += map.OnActionFired;
            RoomModel room = Rooms.Empty();
            menu.PlaceSigns(room, new PgGazeTargets(input));
            menu.Open();

            // Look at sign 2 (the Slicer) from the middle of the arc.
            float3 c = ExhibitSignLayout.Centre(room, 2);
            input.Head = new Vector3(0f, c.y, 0f);
            input.Aim(new Vector3(c.x, 0f, c.z));
            for (int i = 0; i < 60; i++) input.Frame(1f / 60f);
            Assert.That(host.Switches, Is.EqualTo(new[] { SpaceId.Slicer }));
            Assert.That(menu.IsOpen, Is.False);
        }

        [Test]
        public void TheClosedBoardOpensTheMenuByDwell()
        {
            var host = new FakeHost();
            var input = new CoreInput();
            var map = new ActionMap();
            var menu = new ExhibitMenu(host, input, map);
            map.Fired += menu.OnAction;
            input.ActionFired += map.OnActionFired;
            RoomModel room = Rooms.Empty();
            menu.PlaceSigns(room, new PgGazeTargets(input));

            float3 c = ExhibitSignLayout.BoardCentre(room);
            input.Head = new Vector3(0f, c.y, 0f);
            input.Aim(new Vector3(c.x, 0f, c.z));
            for (int i = 0; i < 60; i++) input.Frame(1f / 60f);
            Assert.That(menu.IsOpen, Is.True);
        }

        [Test]
        public void EverySignStandsInsideTheFreeRectangleAtReadingHeight()
        {
            foreach (var (w, d) in new[] { (3f, 3f), (4f, 5f), (6f, 8f), (2.2f, 2.6f) })
            {
                RoomModel room = Rooms.Empty(w, d);
                float2 min = room.FreeRect.Min, max = room.FreeRect.Max;
                for (int k = 0; k < ExhibitSignLayout.Count; k++)
                {
                    float3 c = ExhibitSignLayout.Centre(room, k);
                    Assert.That(c.x, Is.InRange(min.x, max.x), w + "x" + d + " sign " + k);
                    Assert.That(c.z, Is.InRange(min.y, max.y), w + "x" + d + " sign " + k);
                    Assert.That(c.y, Is.EqualTo(1.5f));
                }
                float3 b = ExhibitSignLayout.BoardCentre(room);
                Assert.That(b.z, Is.InRange(min.y, max.y));
            }
        }

        [Test]
        public void SignsAreSeparatedSoTheyCanBeToldApartByGaze()
        {
            RoomModel room = Rooms.Empty();
            for (int k = 1; k < ExhibitSignLayout.Count; k++)
            {
                float gap = math.distance(ExhibitSignLayout.Centre(room, k), ExhibitSignLayout.Centre(room, k - 1));
                Assert.That(gap, Is.GreaterThan(2f * ExhibitSignLayout.HalfWidth), "gap " + k);
            }
        }

        // ---- the return Home ---------------------------------------------------------------------------------------

        static WalkerPose AtCell(IWalkerKernel kernel, double x, double z)
        {
            kernel.ResetToFloor(new double2(x, z));
            return kernel.Pose;
        }

        static HomeReturn NewReturn(FakeHost host, ICurvatureRamp ramp, IFloorProjection projection, LogSink log = null)
        {
            return new HomeReturn(host, ramp, projection, log);
        }

        [Test]
        public void KernelResetPutsHomeWithinFiveCentimetresOfTheRoomModel()
        {
            var kernel = new FlatWalkerKernel();
            RoomModel room = Rooms.Empty();
            kernel.Configure(KernelConfig.Flat(room.LapSide));
            var host = new FakeHost { RoomNow = room, CurrentNow = SpaceId.Home };
            var log = new LogSink();
            var home = NewReturn(host, new FlatCurvatureRamp(), new FlatFloorProjection(kernel), log);

            // The walker has walked about: the head is 2.2 m from where tile zero was.
            double2 head = new double2(1.3, -1.8);
            WalkerPose pose = AtCell(kernel, head.x, head.y);
            home.Begin();
            for (int i = 0; i < 4; i++) home.Tick(1f / 60f, in pose, head);
            Assert.That(host.ReturnHomeCalls, Is.EqualTo(1));
            Assert.That(home.State, Is.EqualTo(HomeReturnState.Done));
            Assert.That(home.WallDeviationM, Is.LessThan(0.05));
            Assert.That(home.WithinTolerance, Is.True);
            Assert.That(log.Lines.Count, Is.EqualTo(1));
            Assert.That(log.Lines[0], Does.StartWith("pg.home wall_dev_m="));
        }

        [Test]
        public void ARegistrationThatDriftedByMoreThanFiveCentimetresFails()
        {
            RoomModel room = Rooms.Empty();
            var host = new FakeHost { RoomNow = room, CurrentNow = SpaceId.Home };
            var projection = new Projection { Floor = new double2(0.4, 0.2) };
            var home = NewReturn(host, new FlatCurvatureRamp(), projection);
            var pose = WalkerPose.Start;
            home.Begin();
            for (int i = 0; i < 4; i++) home.Tick(1f / 60f, in pose, new double2(0.4, 0.2 - 0.06));
            Assert.That(home.State, Is.EqualTo(HomeReturnState.Failed));
            Assert.That(home.WallDeviationM, Is.EqualTo(0.06).Within(1e-9));
        }

        [Test]
        public void ATurnedRegistrationMovesTheFarWallsMost()
        {
            RoomModel room = Rooms.Empty();
            // A 1 degree error about the head moves the far corner, 3.2 m away, by 0.056 m: more than the 0.05 m bound.
            double dev = HomeCheck.MaxWallDeviation(room, new double2(0, 0), new double2(0, 0), System.Math.PI / 180.0);
            Assert.That(dev, Is.EqualTo(System.Math.Sqrt(2 * 2 + 2.5 * 2.5) * System.Math.PI / 180.0).Within(1e-4));
            Assert.That(HomeCheck.MaxWallDeviation(room, new double2(0, 0), new double2(0, 0), 0.0), Is.EqualTo(0.0).Within(1e-12));
        }

        [Test]
        public void KappaRampsToZeroOverFourSecondsBeforeTheReturn()
        {
            RoomModel room = Rooms.Empty();
            var host = new FakeHost { RoomNow = room, CurrentNow = SpaceId.CurvedWalk };
            var ramp = new Ramp();
            var projection = new Projection { Floor = new double2(0, 0) };
            var home = NewReturn(host, ramp, projection);
            var pose = WalkerPose.Start;

            home.Begin();
            Assert.That(home.State, Is.EqualTo(HomeReturnState.Ramping));
            Assert.That(ramp.Ramps, Is.EqualTo(1));
            Assert.That(ramp.Seconds, Is.EqualTo(4f));
            home.Tick(1f, in pose, double2.zero);
            Assert.That(host.ReturnHomeCalls, Is.EqualTo(0), "not before kappa is zero");

            ramp.Kappa = 0.0;
            home.Tick(1f / 60f, in pose, double2.zero);
            Assert.That(host.ReturnHomeCalls, Is.EqualTo(1));
            Assert.That(home.State, Is.EqualTo(HomeReturnState.Returning), "Home is not running yet");

            host.CurrentNow = SpaceId.Home;
            home.Tick(1f / 60f, in pose, double2.zero);
            Assert.That(home.State, Is.EqualTo(HomeReturnState.Done));
        }

        [Test]
        public void ASpaceLabThatNeverAnswersFailsTheReturn()
        {
            RoomModel room = Rooms.Empty();
            var host = new FakeHost { RoomNow = room, CurrentNow = SpaceId.CurvedWalk, StateNow = SpaceHostState.Loading };
            var home = NewReturn(host, new FlatCurvatureRamp(), new Projection());
            var pose = WalkerPose.Start;
            HomeReturnState finished = HomeReturnState.Idle;
            home.Finished += s => finished = s;
            home.Begin();
            for (int i = 0; i < 20 * 60; i++) home.Tick(1f / 60f, in pose, double2.zero);
            Assert.That(home.State, Is.EqualTo(HomeReturnState.Failed));
            Assert.That(finished, Is.EqualTo(HomeReturnState.Failed));
        }

        [Test]
        public void StartOpensAndClosesTheMenuWithSpaceActionsWiredToo()
        {
            var host = new FakeHost();
            var map = new ActionMap();
            var menu = new ExhibitMenu(host, null, map);
            map.Fired += menu.OnAction;
            var registry = new Atlas.Link.ParamRegistry();
            registry.DeclareBool(Atlas.App.CommandRouter.ParamDemoLock, "app", false);
            // The seam of the status file: during the Playground lane D's Spaces page is not wired, so only the
            // action map drives the menu.
            var actions = new Atlas.App.SpaceActions(host, registry) { OpenSpaceMenu = null };

            for (int round = 0; round < 2; round++)
            {
                // The input tick, then step 8b of the same frame.
                map.OnActionFired(AtlasAction.SpaceMenu, InputSource.Gamepad, 0);
                actions.Do(AtlasAction.SpaceMenu, menu.IsOpen ? InputContext.Menu : InputContext.Space);
                Assert.That(menu.IsOpen, Is.EqualTo(round == 0), "round " + round);
            }

            // Wired to Open, as the first status file said, Start could never close it.
            actions.OpenSpaceMenu = () => menu.Open();
            map.OnActionFired(AtlasAction.SpaceMenu, InputSource.Gamepad, 0);
            map.OnActionFired(AtlasAction.SpaceMenu, InputSource.Gamepad, 0);
            Assert.That(menu.IsOpen, Is.False);
            actions.Do(AtlasAction.SpaceMenu, InputContext.Space);
            Assert.That(menu.IsOpen, Is.True, "why the seam must stay empty");
        }

        [Test]
        public void AReturnSpaceActionsAlreadyDidInTheSameFrameStillCounts()
        {
            RoomModel room = Rooms.Empty();
            var host = new FakeHost { RoomNow = room, CurrentNow = SpaceId.Home, ReturnHomeResult = false };
            var ramp = new Ramp();
            var home = NewReturn(host, ramp, new Projection());
            var pose = WalkerPose.Start;
            home.Begin();
            ramp.Kappa = 0.0;
            home.Tick(1f / 60f, in pose, double2.zero);
            Assert.That(home.State, Is.EqualTo(HomeReturnState.Done));
        }

        [Test]
        public void TheTimeoutRunsFromTheMomentKappaIsFlatNotFromTheStart()
        {
            RoomModel room = Rooms.Empty();
            var host = new FakeHost { RoomNow = room, CurrentNow = SpaceId.CurvedWalk, StateNow = SpaceHostState.Loading };
            var ramp = new Ramp();
            var home = NewReturn(host, ramp, new Projection());
            var pose = WalkerPose.Start;
            home.Begin();
            // A walker who keeps moving: the ramp takes 20 s, longer than the old 14 s limit.
            for (int i = 0; i < 20 * 60; i++) home.Tick(1f / 60f, in pose, double2.zero);
            Assert.That(home.State, Is.EqualTo(HomeReturnState.Ramping));
            ramp.Kappa = 0.0;
            for (int i = 0; i < 9 * 60; i++) home.Tick(1f / 60f, in pose, double2.zero);
            Assert.That(home.State, Is.EqualTo(HomeReturnState.Returning));
            for (int i = 0; i < 2 * 60; i++) home.Tick(1f / 60f, in pose, double2.zero);
            Assert.That(home.State, Is.EqualTo(HomeReturnState.Failed));
        }

        [Test]
        public void TheReturnHomeTwinStartsIt()
        {
            RoomModel room = Rooms.Empty();
            var host = new FakeHost { RoomNow = room, CurrentNow = SpaceId.Home };
            var map = new ActionMap();
            var home = NewReturn(host, new FlatCurvatureRamp(), new Projection());
            map.Fired += home.OnAction;
            map.OnActionFired(AtlasAction.ReturnHome, InputSource.Gamepad, 0);
            Assert.That(home.State, Is.EqualTo(HomeReturnState.Returning));
            // A second press while it runs changes nothing.
            map.OnActionFired(AtlasAction.ReturnHome, InputSource.Gaze, 0);
            Assert.That(home.State, Is.EqualTo(HomeReturnState.Returning));
        }

        [Test]
        public void TheFlatProjectionHandlesEveryCellOfTheKernel()
        {
            var kernel = new FlatWalkerKernel();
            kernel.Configure(KernelConfig.Flat(2.4));
            var projection = new FlatFloorProjection(kernel);
            var rng = new System.Random(3);
            for (int i = 0; i < 200; i++)
            {
                double2 p = new double2((rng.NextDouble() - 0.5) * 12, (rng.NextDouble() - 0.5) * 12);
                WalkerPose pose = AtCell(kernel, p.x, p.y);
                Assert.That(projection.TryProject(in pose, out double2 back, out double yaw), Is.True);
                Assert.That(back.x, Is.EqualTo(p.x).Within(1e-9));
                Assert.That(back.y, Is.EqualTo(p.y).Within(1e-9));
                Assert.That(yaw, Is.EqualTo(0.0).Within(1e-12));
            }
        }
    }
}

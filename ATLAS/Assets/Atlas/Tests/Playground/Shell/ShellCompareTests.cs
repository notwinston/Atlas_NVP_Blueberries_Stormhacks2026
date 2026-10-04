using Atlas.Blades.Playground.Shell;
using Atlas.Kernel;
using NUnit.Framework;

namespace Atlas.Blades.Playground.Tests.ShellFlow
{
    using static Atlas.Blades.Playground.Tests.ShellFlow.ShellWalkFakes;
    /// <summary>P2-C05: the mode chip, the mini-map reveal and the home beacon.</summary>
    public class ShellCompareTests
    {
        sealed class Rig
        {
            public readonly FakeMode Mode = new FakeMode();
            public readonly FakeMap Map = new FakeMap();
            public readonly FakeBeacon Beacon = new FakeBeacon();
            public readonly FakeWalk Walk = new FakeWalk();
            public readonly FakeDial Dial = new FakeDial();
            public readonly HeadStillness Still = new HeadStillness();
            public readonly GuidedWalk Guided;
            public readonly ModeCompare Compare;

            public Rig()
            {
                Guided = new GuidedWalk(Walk, Dial, Still, null);
                Guided.SetRoom(Atlas.World.RoomFixtures.LoadFromProject(Atlas.World.RoomFixtures.Sample4x5Table));
                Stand(Still, 0.0, 1.0);
                Compare = new ModeCompare(new ModeChip(Mode), Map, new HomeBeaconLink(Beacon), Guided);
            }

            public void CloseALap()
            {
                Dial.Set(5);
                Guided.Enter();
                Walk.QueueLoop(5, 0.1, 90.0);
                for (int k = 0; k < 6; k++) Guided.Update();
            }
        }

        [Test]
        public void Chip_SwitchKeepsTheTileId()
        {
            var mode = new FakeMode();
            var chip = new ModeChip(mode);
            Assert.That(chip.Label, Is.EqualTo("Mode 1"));
            ModeSwitchResult r = chip.Toggle();
            Assert.That(r.Switched, Is.True);
            Assert.That(r.From, Is.EqualTo(CurvedMode.CurvedWalk));
            Assert.That(r.To, Is.EqualTo(CurvedMode.AlignedCells));
            Assert.That(r.TileKept, Is.True);
            Assert.That(r.TileAfter, Is.EqualTo(new TileId(1, 2, 3, 4, 5, 6)));
            Assert.That(chip.Label, Is.EqualTo("Mode 2"));

            Assert.That(chip.Toggle().To, Is.EqualTo(CurvedMode.CurvedWalk));
        }

        [Test]
        public void Chip_ReportsASwitchThatLostTheTile()
        {
            var chip = new ModeChip(new FakeMode { ChangeTileOnSwitch = true });
            ModeSwitchResult r = chip.Toggle();
            Assert.That(r.Switched, Is.True);
            Assert.That(r.TileKept, Is.False);
        }

        [Test]
        public void Chip_SwitchToTheShownModeIsNotASwitch()
        {
            var chip = new ModeChip(new FakeMode());
            ModeSwitchResult r = chip.SwitchTo(CurvedMode.CurvedWalk);
            Assert.That(r.Switched, Is.False);
            Assert.That(r.TileKept, Is.True);
        }

        [Test]
        public void FreeWalk_MiniMapAppearsAfterTheFirstLapAndTheBeaconIsOn()
        {
            var rig = new Rig();
            IBeatStep free = rig.Compare.Step(PlaygroundBeat.FreeCurvedWalk);
            rig.Map.Visible = true;                        // left over from an earlier run
            free.Enter();
            Assert.That(rig.Map.Visible, Is.False, "no lap closed yet");
            Assert.That(rig.Beacon.Enabled, Is.True);

            rig.CloseALap();
            rig.Compare.Update();
            Assert.That(rig.Map.Visible, Is.True);
            Assert.That(rig.Compare.MiniMapRevealed, Is.True);
        }

        [Test]
        public void FreeWalk_MiniMapIsThereAtOnceWhenTheLapWasWalkedInBeat6()
        {
            var rig = new Rig();
            rig.CloseALap();
            rig.Guided.Leave();
            rig.Compare.Step(PlaygroundBeat.FreeCurvedWalk).Enter();
            Assert.That(rig.Map.Visible, Is.True);
        }

        [Test]
        public void FreeWalk_EndsWhenTheWalkerOpensTheChip()
        {
            var rig = new Rig();
            IBeatStep free = rig.Compare.Step(PlaygroundBeat.FreeCurvedWalk);
            free.Enter();
            Assert.That(free.Complete, Is.False);
            ModeSwitchResult r = rig.Compare.OpenModeChip();
            Assert.That(r.Switched, Is.True);
            Assert.That(r.TileKept, Is.True);
            Assert.That(rig.Mode.Mode, Is.EqualTo(CurvedMode.AlignedCells));
            Assert.That(free.Complete, Is.True);
        }

        [Test]
        public void Compare_EndsWhenTheWalkerOpensTheMenu()
        {
            var rig = new Rig();
            rig.Compare.Step(PlaygroundBeat.FreeCurvedWalk).Enter();
            rig.Compare.OpenModeChip();
            IBeatStep compare = rig.Compare.Step(PlaygroundBeat.Compare);
            compare.Enter();
            Assert.That(compare.Complete, Is.False);
            rig.Compare.OpenMenu();
            Assert.That(compare.Complete, Is.True);
            Assert.That(rig.Beacon.Enabled, Is.True, "the beacon stays on through the compare");
        }

        [Test]
        public void Compare_MenuOutsideTheBeatDoesNothing()
        {
            var rig = new Rig();
            rig.Compare.OpenMenu();
            Assert.That(rig.Compare.CompareComplete, Is.False);
        }

        [Test]
        public void Leave_HidesMapAndBeacon()
        {
            var rig = new Rig();
            rig.CloseALap();
            rig.Compare.Step(PlaygroundBeat.FreeCurvedWalk).Enter();
            rig.Compare.Leave();
            Assert.That(rig.Map.Visible, Is.False);
            Assert.That(rig.Beacon.Enabled, Is.False);
        }

        [Test]
        public void Step_IsNullOutsideBeats7And8()
        {
            Assert.That(new Rig().Compare.Step(PlaygroundBeat.Dial), Is.Null);
        }

        [Test]
        public void Beacon_MatchesTheKernelWithinTenCentimetres()
        {
            var port = new FakeBeacon { Enabled = true };
            var link = new HomeBeaconLink(port);
            for (int k = 0; k < 50; k++)
            {
                port.KernelDistanceM = 0.2 * k;
                port.ShownDistanceM = 0.2 * k + 0.09 * ((k % 2) * 2 - 1);
                link.Update();
            }
            Assert.That(link.WithinTolerance, Is.True);
            Assert.That(link.MaxErrorM, Is.EqualTo(0.09).Within(1e-9));

            port.ShownDistanceM = port.KernelDistanceM + 0.11;
            link.Update();
            Assert.That(link.WithinTolerance, Is.False);
            link.Reset();
            Assert.That(link.WithinTolerance, Is.True);
        }

        [Test]
        public void Beacon_OffMeasuresNothingAndANumberlessBeaconFails()
        {
            var port = new FakeBeacon { Enabled = false, ShownDistanceM = 5, KernelDistanceM = 1 };
            var link = new HomeBeaconLink(port);
            link.Update();
            Assert.That(link.WithinTolerance, Is.True);
            Assert.That(double.IsNaN(link.ErrorM), Is.True);

            port.Enabled = true;
            port.ShownDistanceM = double.NaN;
            link.Update();
            Assert.That(link.WithinTolerance, Is.False);
        }

        [Test]
        public void Ports_RejectNull()
        {
            Assert.Throws<System.ArgumentNullException>(() => new ModeChip(null));
            Assert.Throws<System.ArgumentNullException>(() => new HomeBeaconLink(null));
            Assert.Throws<System.ArgumentNullException>(() => new DialHandOff(null, new HeadStillness()));
            Assert.Throws<System.ArgumentNullException>(() => new ScanToHome(null, new HeadStillness()));
        }
    }
}

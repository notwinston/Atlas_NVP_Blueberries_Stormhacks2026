using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
#if !ATLAS_HEADLESS
using Atlas.Rendering;
using UnityEngine;
#endif

namespace Atlas.Spaces.AlignedCells.Tests
{
    public class AlignedCellsModuleTests
    {
        static readonly string[] CurvedModes = { "Mode 1 Curved Walk", "Mode 2 Aligned Cells" };

        // curved.mode as the app declares it (AppParams.DeclareUnownedKillSwitches).
        static ParamEntry DeclareMode(ParamRegistry registry)
            => registry.DeclareEnum(AlignedCellsCore.ParamMode, "curved", CurvedModes, 0, null, true);

        // Criterion: lane A contract §5.1: the space announces itself; its parameters are in group "cells" (lane D contract §1.5)
        [Test]
        public void Mode2_Registration_PutsAlignedCellsInTheRegistry()
        {
            AlignedCellsRegistration.Register();
            Assert.IsTrue(SpaceRegistry.IsRegistered(SpaceId.AlignedCells));
            AlignedCellsRegistration.Register();     // safe to run twice: no domain reload in the Editor
            Assert.IsTrue(SpaceRegistry.IsRegistered(SpaceId.AlignedCells));

            var registry = new ParamRegistry();
            SpaceRegistry.DeclareAllParams(registry);
            Assert.IsTrue(registry.TryGet(AlignedCellsCore.ParamViewDepth, out ParamEntry depth));
            Assert.AreEqual("cells", depth.Group);
            Assert.AreEqual(SpaceIds.ParamGroup(SpaceId.AlignedCells), depth.Group);
            Assert.AreEqual(ParamKind.Int, depth.Kind);
            Assert.AreEqual(SightLines.ViewRange, depth.Int);
            Assert.AreEqual(0f, depth.Min); Assert.AreEqual((float)SightLines.ViewRange, depth.Max);
            Assert.IsNull(registry.Find(AlignedCellsCore.ParamMode), "curved.mode is the app's to declare");
        }

        // Decision 7: declared once; Configure declares again and keeps the value
        [Test]
        public void Mode2_Configure_DeclaresAgainAndKeepsTheValue()
        {
            var registry = new ParamRegistry();
            AlignedCellsCore.DeclareParams(registry);
            int declared = registry.Count;
            registry.Find(AlignedCellsCore.ParamViewDepth).Set(1);
            var core = new AlignedCellsCore();
            core.Configure(Fixtures.Room(RoomFixtures.Sample3x3), registry);
            Assert.AreEqual(declared, registry.Count);
            Assert.AreEqual(1, core.ViewDepth);
        }

        // Criterion: G3 "switch on one tile graph with no tile-id change"; Master Plan §4.8. SpaceLab hands Mode 1's pose over.
        [Test]
        public void Mode2_Module_EnterKeepsTileId()
        {
            var tile = TileId.Zero.Step(1).Step(2);
            var rig = new Rig(Fixtures.Room(RoomFixtures.Sample3x3), start: new WalkerPose { Tile = tile, Local = Iso2.Boost(new double2(0.1, 0.05), -1) });
            Assert.IsTrue(rig.Core.EnteredFromCurvedWalk);
            Assert.AreEqual(tile, rig.Core.Walker.Frame.Tile);
            var st = StateMessage.Identity;
            rig.Core.WriteState(ref st);
            Assert.AreEqual(tile, new TileId(st.Tile0, st.Tile1, st.Tile2, st.Tile3, st.Tile4, st.Tile5), "State.tileId is Mode 1's");
        }

        // The kill switch cells.viewDepth: the instance list follows the parameter on the next frame.
        [Test]
        public void Mode2_Module_ViewDepthParameter()
        {
            var rig = new Rig(Fixtures.Room(RoomFixtures.BudgetRoom), new int2(1, 1));
            int full = rig.Core.Instances.Count;
            rig.Registry.Find(AlignedCellsCore.ParamViewDepth).Set(0);
            Assert.IsTrue(rig.Core.Tick(new double2(rig.X, rig.Z), false));
            Assert.AreEqual(1, rig.Core.Instances.Count);
            Assert.IsFalse(rig.Core.Tick(new double2(rig.X, rig.Z), false));
            rig.Registry.Find(AlignedCellsCore.ParamViewDepth).Set(2);
            Assert.IsTrue(rig.Core.Tick(new double2(rig.X, rig.Z), false));
            Assert.AreEqual(full, rig.Core.Instances.Count);
        }

        // Master Plan §12.2 risk 5, lane D contract §1.5: curved.mode switches between the two modes. S3 holds it at Mode 2 while
        // entered, asks for Curved Walk when the operator moves it to Mode 1, and never leaves a stale Mode 2 behind.
        [Test]
        public void Mode2_Module_CurvedModeParameter()
        {
            var registry = new ParamRegistry();
            ParamEntry mode = DeclareMode(registry);
            var core = new AlignedCellsCore();
            core.Configure(Fixtures.Room(RoomFixtures.Sample3x3), registry);
            Assert.IsFalse(core.PollModeSwitch(), "not entered");
            Assert.AreEqual(0, mode.Int);
            core.Enter(WalkerPose.Start, double2.zero);
            Assert.IsFalse(core.PollModeSwitch());
            Assert.AreEqual(AlignedCellsCore.ModeAlignedCells, mode.Int, "entered by the menu: the parameter follows");
            Assert.IsFalse(core.PollModeSwitch());

            mode.Set(AlignedCellsCore.ModeCurvedWalk);
            Assert.IsTrue(core.PollModeSwitch(), "the operator asks for Mode 1");
            Assert.IsFalse(core.PollModeSwitch(), "asked once: the space ticks on through the fade");
            core.RefuseModeSwitch();
            Assert.AreEqual(AlignedCellsCore.ModeAlignedCells, mode.Int, "refused by the host: back to what is shown");
            Assert.IsFalse(core.PollModeSwitch());

            core.Exit();
            Assert.AreEqual(AlignedCellsCore.ModeCurvedWalk, mode.Int, "left for another space: Curved Walk finds Mode 1");

            core.Configure(Fixtures.Room(RoomFixtures.Sample3x3), registry);
            core.Enter(WalkerPose.Start, double2.zero);
            core.PollModeSwitch();
            mode.Set(AlignedCellsCore.ModeCurvedWalk);
            Assert.IsTrue(core.PollModeSwitch());
            core.Exit();
            Assert.AreEqual(AlignedCellsCore.ModeCurvedWalk, mode.Int);

            // No such parameter (the Station before the manifest arrives): nothing to poll.
            var bare = new AlignedCellsCore();
            bare.Configure(Fixtures.Room(RoomFixtures.Sample3x3), new ParamRegistry());
            bare.Enter(WalkerPose.Start, double2.zero);
            Assert.IsFalse(bare.PollModeSwitch());
        }

        // Criterion: Master Plan App. B.1, lane D contract §2.3 (State: tileId, sheet, xform; the rest is the frame loop's)
        [Test]
        public void Mode2_Module_WriteState()
        {
            var rig = new Rig(Fixtures.Room(RoomFixtures.BudgetRoom), new int2(1, 2));
            var st = StateMessage.Identity;
            st.SpaceId = (byte)SpaceId.AlignedCells; st.P0 = 1f; st.P1 = 2f; st.P2 = 3f; st.WorldVer = 7;
            rig.Core.WriteState(ref st);
            Assert.AreEqual(3, st.SpaceId); Assert.AreEqual(1f, st.P0); Assert.AreEqual(2f, st.P1); Assert.AreEqual(3f, st.P2);
            Assert.AreEqual(7, st.WorldVer);
            Assert.AreEqual(1, st.Tile4, "tile zero"); Assert.AreEqual(0, st.Tile0 | st.Tile1 | st.Tile2 | st.Tile3 | st.Tile5);
            Assert.AreEqual(2 * rig.Grid.CountX + 1, st.Sheet);
            for (int i = 0; i < 9; i++) Assert.AreEqual(i % 4 == 0 ? 1.0 : 0.0, st.GetXform(i), "identity at turn 0");
            Assert.AreEqual(2f, st.P3); Assert.AreEqual(3f, st.P4); Assert.AreEqual(4f, st.P5); Assert.AreEqual(0.8f, st.P6, 1e-6f);

            // Not entered: nothing is written.
            var idle = new AlignedCellsCore();
            idle.Configure(Fixtures.Room(RoomFixtures.BudgetRoom), new ParamRegistry());
            var blank = StateMessage.Identity;
            idle.WriteState(ref blank);
            Assert.AreEqual(0, blank.Tile4); Assert.AreEqual(0, blank.Sheet);
        }

        // Master Plan §7.3.1, lane D contract §2.3: the Station's copy redraws the phone's frame from State alone: the same cell,
        // the same tile and turn, the same instance list, through the 202 bytes of the wire.
        [Test]
        public void Mode2_Module_StateRoundTrip()
        {
            RoomModel room = Fixtures.Room(RoomFixtures.BudgetRoom);
            var phone = new Rig(room, new int2(1, 1));
            var station = new AlignedCellsCore();
            station.Configure(room, new ParamRegistry());
            station.Enter(WalkerPose.Start, double2.zero);
            var wire = new byte[StateMessage.Size];
            int turnsSeen = 0;
            void Compare()
            {
                var st = StateMessage.Identity;
                st.SpaceId = (byte)SpaceId.AlignedCells;
                phone.Core.WriteState(ref st);
                st.WriteTo(wire, 0);
                var received = new StateMessage();
                Assert.IsTrue(received.ReadFrom(wire, 0, wire.Length));
                station.ReadState(in received);
                station.Tick(new double2(double.NaN, double.NaN), false);     // the Station's own head plays no part
                Assert.IsTrue(station.Remote);
                Assert.AreEqual(phone.Core.Walker.Frame, station.Walker.Frame);
                Assert.AreEqual(phone.Core.Walker.Cell, station.Walker.Cell);
                Assert.AreEqual(phone.Core.Instances.Count, station.Instances.Count);
                for (int i = 0; i < station.Instances.Count; i++)
                {
                    Assert.AreEqual(phone.Core.Instances[i].Cell, station.Instances[i].Cell);
                    Assert.AreEqual(phone.Core.Instances[i].Frame, station.Instances[i].Frame);
                    Assert.AreEqual(phone.Core.Instances[i].GateCount, station.Instances[i].GateCount);
                }
                turnsSeen |= 1 << phone.Core.Walker.Frame.Turn;
                // xform is a rotation of the hyperbolic plane's isometry group: the quarter-turn of the walker's tile
                Iso2 u = Iso2.Rot(-phone.Core.Walker.Frame.Turn * System.Math.PI / 2, -1);
                Assert.AreEqual(u.M.c0.x, received.Xform0, 1e-12); Assert.AreEqual(u.M.c1.x, received.Xform1, 1e-12);
                Assert.AreEqual(u.M.c0.y, received.Xform3, 1e-12); Assert.AreEqual(u.M.c1.y, received.Xform4, 1e-12);
                Assert.AreEqual(1.0, received.Xform8);
            }
            Compare();
            // twice round the post between cells (1,1), (2,1), (2,2), (1,2), then on: every turn comes up
            foreach (var c in new[] { new int2(2, 1), new int2(2, 2), new int2(1, 2), new int2(1, 1), new int2(2, 1), new int2(2, 2), new int2(1, 2),
                                      new int2(1, 1), new int2(0, 1), new int2(0, 0), new int2(1, 0), new int2(1, 1), new int2(1, 3) })
            {
                phone.GoCell(c.x, c.y);
                Compare();
            }
            Assert.AreEqual(0xF, turnsSeen, "all four quarter-turns were sent");
            phone.Registry.Find(AlignedCellsCore.ParamViewDepth).Set(1);
            phone.Step();
            Compare();

            // A State of another space, or one with no tile, is not taken.
            var other = StateMessage.Identity;
            other.SpaceId = (byte)SpaceId.Glued; other.SetTile(5, 0, 0, 0, 1, 0);
            CellFrame before = station.Walker.Frame;
            station.ReadState(in other); station.Tick(double2.zero, false);
            Assert.AreEqual(before, station.Walker.Frame);
            var none = StateMessage.Identity;
            none.SpaceId = (byte)SpaceId.AlignedCells;
            station.ReadState(in none); station.Tick(double2.zero, false);
            Assert.AreEqual(before, station.Walker.Frame);
        }

#if !ATLAS_HEADLESS
        // Criterion: Master Plan §6.5, §4.9 (S3 registered); lane A contract §2 (a MonoBehaviour made by its factory)
        [Test, Category("Unity")]
        public void Mode2_Component_IdRegisteredAndFactory()
        {
            using (var stage = new UnityStage(Fixtures.Room(RoomFixtures.Sample3x3)))
            {
                Assert.AreEqual(SpaceId.AlignedCells, stage.Space.Id);
                Assert.IsTrue(stage.Space.Registered);
                Assert.AreEqual(SpaceIds.SceneName(SpaceId.AlignedCells), stage.Space.gameObject.name);
                Assert.IsInstanceOf<IUploadStep>(stage.Space);
                Assert.IsInstanceOf<ISpaceStateReader>(stage.Space);
                Assert.IsNotNull(stage.Space.View);
                Assert.IsNull(stage.Space.RoomView, "the room is taken at Enter");
            }
        }

        // Criterion: [P1-B12 D6] the room is taken when the room content is built, also after Enter
        [Test, Category("Unity")]
        public void Mode2_Component_RoomArrivesLate()
        {
            RoomModel room = Fixtures.Room(RoomFixtures.BudgetRoom);
            using (var stage = new UnityStage(room, buildRoom: false))
            {
                stage.Enter(WalkerPose.Start, new int2(0, 0));
                Assert.IsNull(stage.Space.RoomView);
                stage.Content.Build(room);
                stage.Step();
                Assert.IsNotNull(stage.Space.RoomView);
                var v = stage.Space.RoomView; var list = stage.Space.Core.Instances;
                for (int i = 0; i < v.ObjectCount; i++)
                {
                    bool any = false;
                    for (int k = 0; k < list.Count; k++) any |= v.ObjectCells(i).Contains(list[k].Cell);
                    Assert.AreEqual(any, stage.Content.GetObject(i).Renderer.enabled);
                }
            }
        }

        // Criterion: S3 content disappears on leaving the space (§6.5 Exit); every rented material goes back to the pool
        [Test, Category("Unity")]
        public void Mode2_Component_ExitReleases()
        {
            using (var stage = new UnityStage(Fixtures.Room(RoomFixtures.BudgetRoom)))
            {
                int roomRented = stage.Pool.Rented - stage.Space.View.MaterialsRented;
                stage.Enter(WalkerPose.Start, new int2(1, 1));
                Assert.AreEqual(stage.Space.Core.Instances.Count + 1, stage.Space.View.MaterialsRented, "one per instance drawn, one for the posts");
                Assert.AreEqual(roomRented + stage.Space.View.MaterialsRented, stage.Pool.Rented);
                stage.Space.Exit();
                Assert.IsNull(stage.Space.View);
                Assert.AreEqual(roomRented, stage.Pool.Rented);
                Assert.AreEqual(0, stage.Root.GetComponentsInChildren<MeshRenderer>(false).Length);
            }
        }

        // Criterion: Master Plan §4.7 gates per eye in world positions; [P1-B12 D11]
        [Test, Category("Unity")]
        public void Mode2_Component_ReuploadsGatesWhenAnchorMoves()
        {
            using (var stage = new UnityStage(Fixtures.Room(RoomFixtures.Sample3x3)))
            {
                stage.Enter(WalkerPose.Start, new int2(0, 0));
                var list = stage.Space.Core.Instances;
                int k = -1;
                for (int i = 0; i < list.Count && k < 0; i++) if (list[i].GateCount > 0) k = i;
                Assert.GreaterOrEqual(k, 0);
                Material mat = stage.Space.View.InstanceMaterial(k);
                float before = mat.GetVector(AtlasShaderIDs.Gate0).x;
                stage.Root.position += new Vector3(0.5f, 0, 0);
                stage.Step();
                float after = mat.GetVector(AtlasShaderIDs.Gate0).x;
                Assert.AreEqual(0.5f, after - before, 1e-5);
            }
        }

        // Criterion: Master Plan §4.8: from Mode 1 the tile id is kept; from anywhere else the walker starts in tile zero
        [Test, Category("Unity")]
        public void Mode2_Component_EnterKeepsTileId()
        {
            var tile = TileId.Zero.Step(1).Step(2);
            using (var stage = new UnityStage(Fixtures.Room(RoomFixtures.Sample3x3)))
            {
                stage.Enter(new WalkerPose { Tile = tile, Local = Iso2.Boost(new double2(0.1, 0.05), -1) }, new int2(1, 1));
                Assert.AreEqual(tile, stage.Space.Core.Walker.Frame.Tile);
                var st = StateMessage.Identity;
                stage.Space.WriteState(ref st);
                Assert.AreEqual(tile, new TileId(st.Tile0, st.Tile1, st.Tile2, st.Tile3, st.Tile4, st.Tile5));
            }
            using (var stage = new UnityStage(Fixtures.Room(RoomFixtures.Sample3x3)))
            {
                stage.Enter(WalkerPose.Start, new int2(1, 1));
                Assert.AreEqual(TileId.Zero, stage.Space.Core.Walker.Frame.Tile);
            }
        }
#endif
    }
}

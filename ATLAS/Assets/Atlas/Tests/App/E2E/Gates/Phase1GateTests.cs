using Atlas.App;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Safety;
using Atlas.Scan;
using Atlas.Spaces.AlignedCells;
using Atlas.Spaces.CurvedWalk;
using Atlas.Spaces.SmallPlanet;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.E2E.Tests
{
    /// <summary>
    /// The conditions of the Phase 1 gate checklists (Master Plan section 11.3) that need the spaces of all
    /// lanes in the real SpaceLab and frame loop, and no Editor: G3 for the curved spaces on the three sample
    /// rooms, and the standing room of G5 in them. tools/e2e/gates.py lists what each gate has beyond these.
    /// </summary>
    public class Phase1GateTests
    {
        static readonly string[] SampleRooms = { RoomFixtures.Sample3x3, RoomFixtures.SampleL3x4, RoomFixtures.Sample4x5Table };
        static readonly SpaceId[] Curved = { SpaceId.CurvedWalk, SpaceId.AlignedCells, SpaceId.SmallPlanet };

        /// <summary>A rig with no Station, the walker standing at the room's centre, Home entered.</summary>
        static RehearsalRig Enter(RoomModel room)
        {
            var rig = new RehearsalRig();
            rig.StandAt(float2.zero);
            rig.Steps(30);
            Assert.That(rig.ActivateRoom(room), Is.GreaterThanOrEqualTo(0), "Home was never entered");
            Assert.That(rig.Lab.Current, Is.EqualTo(SpaceId.Home));
            return rig;
        }

        /// <summary>Asks for a space as the on-device menu does and waits until it runs.</summary>
        static void Switch(RehearsalRig rig, SpaceId space)
        {
            rig.Commands.SwitchSpace(space, null);
            Assert.That(rig.StepUntil(() => rig.Lab.State == SpaceHostState.Running && rig.Lab.HasSpace && rig.Lab.Current == space, 120),
                Is.GreaterThanOrEqualTo(0), SpaceIds.Code(space) + " was never entered");
        }

        static void AssertExposed(RehearsalRig rig, string name)
        {
            ParamEntry entry;
            Assert.That(rig.Registry.TryGet(name, out entry), Is.True, name + " is not declared");
            Assert.That(rig.Registry.IsGroupActive(entry.Group), Is.True, name + " is folded away while its space runs");
        }

        static void AssertHealthy(RehearsalRig rig)
        {
            Assert.That(rig.Warnings, Is.Empty, "frame loop warnings");
            Assert.That(rig.HasEvent("space.error"), Is.False, "a space threw");
            Assert.That(rig.HasEvent("space.loadFailed"), Is.False);
            Assert.That(rig.HasEvent("space.refused"), Is.False);
        }

        // G3: "S1-S6 each load in SpaceLab on the three sample rooms, and expose their parameters". The flat
        // spaces are E2E_Flow_EveryFixture; these are the curved ones, each on the app's one kernel.
        [TestCaseSource(nameof(SampleRooms))]
        public void G3_CurvedSpaces_LoadInSpaceLabOnEverySampleRoom_AndExposeTheirParameters(string fixture)
        {
            RoomModel room = RoomFixtures.LoadFromProject(fixture);
            Assert.That(room.Standing, Is.False);
            using (RehearsalRig rig = Enter(room))
            {
                // S2: {4,5} at the room's loop-fit L, the room as tile zero, the generated world, the ramp.
                ushort version = rig.Lab.WorldVersion;
                Switch(rig, SpaceId.CurvedWalk);
                var s2 = rig.Module<CurvedWalkModule>();
                double l5 = room.Curvature.ForDial(5, false);
                Assert.That(rig.Lab.WorldVersion, Is.Not.EqualTo(version));
                Assert.That(rig.Kernel.Q, Is.EqualTo(5));
                Assert.That(rig.Kernel.ConfiguredMetresPerUnit, Is.EqualTo(l5).Within(1e-12));
                Assert.That(l5, Is.GreaterThanOrEqualTo(LoopFit.FloorL), "the comfort floor of section 4.4");
                Assert.That(rig.Kernel.Pose.Tile, Is.EqualTo(TileId.Zero));
                Assert.That(s2.Core.World, Is.Not.Null);
                Assert.That(s2.Core.Footprint.L, Is.EqualTo(l5));
                Assert.That(s2.ContentHash.Length, Is.EqualTo(16));
                Assert.That(rig.StepUntil(() => rig.Kernel.CurvatureNow != 0 && !rig.Kernel.Ramping, 420), Is.GreaterThanOrEqualTo(0), "the curvature never arrived");
                Assert.That(rig.Kernel.CurvatureNow, Is.EqualTo(-1.0 / (l5 * l5)).Within(1e-12));
                rig.Steps(2);
                Assert.That(s2.Core.Walk.Armed, Is.True, "the 3-4-5 walk is offered in a room one can walk");
                Assert.That(s2.Core.SideM, Is.EqualTo(room.LapSide));
                AssertExposed(rig, CurvedWalkCore.ParamQ);
                AssertExposed(rig, CurvedWalkCore.ParamStrong);
                AssertExposed(rig, CurvedWalkCore.ParamMode);
                Assert.That(rig.Registry.GetInt(AppParams.Space), Is.EqualTo((int)SpaceId.CurvedWalk));
                Assert.That(rig.Loop.LastState.SpaceId, Is.EqualTo((byte)SpaceId.CurvedWalk));
                Assert.That(rig.Loop.LastState.GetParam(0), Is.EqualTo((float)l5), "p[0] is L");

                // S3: the cell grid of the room, the kernel flat and the walker registered.
                Switch(rig, SpaceId.AlignedCells);
                var s3 = rig.Module<AlignedCellsModule>();
                Assert.That(s3.Core.Configured, Is.True);
                Assert.That(s3.Core.Grid.CountX * s3.Core.Grid.CountZ, Is.GreaterThanOrEqualTo(1));
                Assert.That(s3.Core.Grid.Side, Is.GreaterThanOrEqualTo(CellGrid.MinSide));
                Assert.That(s3.Core.Instances.Count, Is.GreaterThan(0));
                Assert.That(rig.Kernel.Q, Is.EqualTo(4));
                Assert.That(rig.Lab.Module.Registered, Is.True);
                rig.Steps(5);
                AssertExposed(rig, AlignedCellsCore.ParamViewDepth);
                Assert.That(rig.Registry.GetInt(AppParams.CurvedMode), Is.EqualTo(AlignedCellsCore.ModeAlignedCells));
                Assert.That(rig.Loop.LastState.SpaceId, Is.EqualTo((byte)SpaceId.AlignedCells));

                // S4: the sphere at the room's R, or at planet.R when that is larger (its default is the PlanetKit tiles' R0).
                Switch(rig, SpaceId.SmallPlanet);
                var s4 = rig.Module<SmallPlanetModule>();
                double r = SmallPlanetCore.RadiusFor(room, false, rig.Registry.GetFloat(SmallPlanetCore.ParamRadius));
                Assert.That(r, Is.GreaterThanOrEqualTo(LoopFit.FloorR));
                Assert.That(rig.Kernel.Q, Is.EqualTo(3));
                Assert.That(rig.Kernel.Kappa, Is.EqualTo(Curvature.Spherical));
                Assert.That(rig.Kernel.MetresPerUnit, Is.EqualTo(r).Within(1e-12));
                rig.Steps(5);
                Assert.That(s4.Core.Placed, Is.True);
                AssertExposed(rig, SmallPlanetCore.ParamFogRho);
                AssertExposed(rig, SmallPlanetCore.ParamRadius);
                AssertExposed(rig, CurvedWalkCore.ParamQ);
                Assert.That(rig.Loop.LastState.SpaceId, Is.EqualTo((byte)SpaceId.SmallPlanet));
                Assert.That(rig.Loop.LastState.GetParam(0), Is.EqualTo((float)r), "p[0] is R");

                // Home again: the flat kernel of this room.
                rig.Commands.Home();
                Assert.That(rig.StepUntil(() => rig.Lab.State == SpaceHostState.Running && rig.Lab.Current == SpaceId.Home, 120), Is.GreaterThanOrEqualTo(0));
                rig.Step();
                Assert.That(rig.Kernel.Q, Is.EqualTo(4));
                Assert.That(((FlatTileGraph)rig.Kernel.Graph).CentreSpacing * rig.Kernel.MetresPerUnit, Is.EqualTo(room.LapSide).Within(1e-9));
                Assert.That(rig.Guard.Armed, Is.True);
                AssertHealthy(rig);
            }
        }

        // G3: "Mode 1 and Mode 2 switch on one tile graph with no tile-id change"; G7: "both walked from the
        // same tile zero". The walker leaves tile zero through its +Z edge in Mode 1 and stands still.
        [TestCaseSource(nameof(SampleRooms))]
        public void G3_Mode1ToMode2_OnOneTileGraph_KeepsTheTileId_OnEverySampleRoom(string fixture)
        {
            RoomModel room = RoomFixtures.LoadFromProject(fixture);
            using (RehearsalRig rig = Enter(room))
            {
                Switch(rig, SpaceId.CurvedWalk);
                Assert.That(rig.Kernel.Pose.Tile, Is.EqualTo(TileId.Zero), "Mode 1 begins in tile zero");
                Assert.That(rig.StepUntil(() => rig.Kernel.CurvatureNow != 0 && !rig.Kernel.Ramping, 420), Is.GreaterThanOrEqualTo(0));

                // Tile zero's edge is half a centre spacing from the room's centre: s/2 at the loop-fit L, further
                // when the comfort floor lifted L. The free rectangle ends 0.2 m beyond s/2 (section 4.4).
                float edge = 0.5f * (float)(LoopFit.CentreSpacing(5) * room.Curvature.ForDial(5, false));
                var beyond = new float2(0f, 0.5f * room.FreeRect.Df - 0.02f);
                Assert.That(beyond.y, Is.GreaterThan(edge + 0.05f), fixture + " cannot be left through +Z inside its free rectangle");
                PoseLog walk = RehearsalRig.Line(room.Id, float2.zero, beyond, 0.5f);
                rig.Play(walk);
                for (int k = 0; k < walk.Samples.Length; k++) rig.Step();
                rig.StandAt(in walk.Samples[walk.Samples.Length - 1]);
                rig.Steps(30);
                WalkerPose mode1 = rig.Kernel.Pose;
                Assert.That(mode1.Tile.Equals(TileId.Zero), Is.False, "the walk never left tile zero");
                TileId neighbour;
                bool isNeighbour = false;
                for (int e = 0; e < 4 && !isNeighbour; e++)
                {
                    neighbour = rig.Kernel.Graph.Neighbour(TileId.Zero, e);
                    isNeighbour = neighbour.Equals(mode1.Tile);
                }
                Assert.That(isNeighbour, Is.True, "one edge was crossed");

                Switch(rig, SpaceId.AlignedCells);
                var s3 = rig.Module<AlignedCellsModule>();
                Assert.That(s3.Start.Tile, Is.EqualTo(mode1.Tile), "SpaceLab handed over the pose Mode 1 left");
                Assert.That(s3.Core.EnteredFromCurvedWalk, Is.True);
                Assert.That(s3.Core.Graph.Q, Is.EqualTo(5), "Mode 2 walks the tile graph of Mode 1");
                Assert.That(s3.Core.Walker.Frame.Tile, Is.EqualTo(mode1.Tile), "the same tile id in Mode 2");
                rig.Steps(5);
                for (int i = 0; i < StateMessage.TileLength; i++)
                    Assert.That(rig.Loop.LastState.GetTile(i), Is.EqualTo(mode1.Tile[i]), "State.tile word " + i);
                // Standing still in Mode 2 changes nothing.
                Assert.That(s3.Core.Crossings, Is.EqualTo(0));
                Assert.That(s3.Core.Walker.Frame.Tile, Is.EqualTo(mode1.Tile));
                AssertHealthy(rig);
            }
        }

        // G5: "a RoomModel with a 1.4 x 1.4 m free floor selects standing mode". In such a room the curved
        // spaces still load, offer no walk, and leave Bump Guard armed with the room's map.
        [Test]
        public void G5_StandingRoom_TheCurvedSpacesLoadWithNoWalk_AndBumpGuardStaysArmed()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.TinyStanding);
            Assert.That(room.Standing, Is.True);
            using (RehearsalRig rig = Enter(room))
            {
                Assert.That(rig.Lab.Standing, Is.True);
                for (int i = 0; i < Curved.Length; i++)
                {
                    Switch(rig, Curved[i]);
                    rig.Steps(300);     // past the ramp
                    Assert.That(rig.Lab.Current, Is.EqualTo(Curved[i]), SpaceIds.Code(Curved[i]) + " fell back");
                    Assert.That(rig.Guard.Armed, Is.True);
                    Assert.That(rig.Guard.Core.HasRoom, Is.True);
                    Assert.That(rig.Loop.HasGuard, Is.True);
                    Assert.That(rig.Guard.Level, Is.Not.EqualTo(BumpLevel.TrackingLost));
                }
                Assert.That(rig.HasEvent("walk.started"), Is.False, "no lap is offered in a standing room");
                AssertHealthy(rig);
            }
        }
    }
}

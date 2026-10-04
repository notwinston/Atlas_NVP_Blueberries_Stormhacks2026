using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Telemetry.Tests
{
    /// <summary>
    /// The flat-twin builder on the room fixtures (Master Plan rule 6, task P1-D07): the primitives each built
    /// space is drawn with, the walker inside the outline and inside the view, the tile sequence of a replayed
    /// State log, and the cost of a rebuild.
    /// </summary>
    public class FlatTwinTests
    {
        static readonly string[] s_rooms = RoomFixtures.All;

        static RoomModel Load(string name) { return RoomFixtures.LoadFromProject(name); }

        static StateMessage StateAt(byte space, float x, float z, float yaw = 0f)
        {
            StateMessage s = StateMessage.Identity;
            s.SpaceId = space;
            s.SceneId = space;
            s.HeadPosX = x;
            s.HeadPosY = 1.6f;
            s.HeadPosZ = z;
            s.HeadRotY = (float)Math.Sin(0.5 * yaw);
            s.HeadRotW = (float)Math.Cos(0.5 * yaw);
            return s;
        }

        static int Objects(RoomModel room)
        {
            int n = 0;
            for (int i = 0; i < room.Objects.Length; i++) if (room.Objects[i] != null) n++;
            return n;
        }

        static void AssertRoomInView(FlatTwin twin, RoomModel room)
        {
            for (int i = 0; i < room.Outline.Length; i++)
                Assert.That(twin.InView(room.Outline[i].x, room.Outline[i].y), Is.True, "outline corner " + i + " of " + room.Id);
        }

        [Test]
        public void Home_is_the_floor_plan_of_every_fixture()
        {
            var twin = new FlatTwin();
            for (int k = 0; k < s_rooms.Length; k++)
            {
                RoomModel room = Load(s_rooms[k]);
                StateMessage state = StateAt((byte)SpaceId.Home, 0.2f, -0.1f, 0.5f);
                FlatTwinBuilder.Build(twin, room, new FlatWalkerKernel(), in state, null);

                bool lap = !room.Standing && room.LapSide > 0f;
                Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Outline), Is.EqualTo(room.Outline.Length), room.Id);
                Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Grid), Is.EqualTo(lap ? 8 : 4), room.Id + ": free rectangle and lap square");
                Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Object), Is.EqualTo(4 * Objects(room)), room.Id);
                Assert.That(twin.CountOf(FlatTwinShape.Arrow, FlatTwinInk.Walker), Is.EqualTo(1), room.Id);

                FlatTwinItem walker;
                Assert.That(twin.TryFind(FlatTwinShape.Arrow, FlatTwinInk.Walker, out walker), Is.True);
                Assert.That(walker.A, Is.EqualTo(0.2f));
                Assert.That(walker.B, Is.EqualTo(-0.1f));
                Assert.That(walker.C, Is.EqualTo(0.5f).Within(1e-4f), "heading from the head rotation");
                Assert.That(WalkablePolygon.RingContains(room.Outline, new float2(walker.A, walker.B)), Is.True, "walker inside the outline");
                Assert.That(twin.InView(walker.A, walker.B), Is.True);
                AssertRoomInView(twin, room);
                // The fit is tight: the room fills at least two thirds of the panel along its longer side.
                float minX, minZ, maxX, maxZ;
                twin.TryGetBounds(out minX, out minZ, out maxX, out maxZ);
                Assert.That(twin.HalfExtent, Is.LessThan(0.75f * Math.Max(maxX - minX, maxZ - minZ)), room.Id);
            }
        }

        [Test]
        public void Without_a_space_the_room_is_drawn_from_the_outline_message()
        {
            RoomModel room = Load(RoomFixtures.BudgetRoom);
            RoomOutlineMessage outline = SpectatorTestRooms.OutlineOf(room);
            var fromModel = new FlatTwin();
            var fromMessage = new FlatTwin();
            StateMessage state = StateAt(SpaceIds.NoSpace, 0f, 0f);
            FlatTwinBuilder.Build(fromModel, room, null, null, in state, null);
            FlatTwinBuilder.Build(fromMessage, null, outline, null, in state, null);

            Assert.That(fromMessage.CountOf(FlatTwinShape.Line, FlatTwinInk.Outline), Is.EqualTo(room.Outline.Length));
            Assert.That(fromMessage.CountOf(FlatTwinShape.Line, FlatTwinInk.Object), Is.EqualTo(4 * Objects(room)));
            Assert.That(fromMessage.CountOf(FlatTwinShape.Line, FlatTwinInk.Grid), Is.EqualTo(4), "the free rectangle; an outline message has no lap square");
            Assert.That(fromMessage.CentreX, Is.EqualTo(fromModel.CentreX).Within(1e-4f));
            Assert.That(fromMessage.HalfExtent, Is.EqualTo(fromModel.HalfExtent).Within(1e-4f));

            // Nothing at all is known: a placeholder square and a label, and the walker still shows.
            var empty = new FlatTwin();
            FlatTwinBuilder.Build(empty, null, null, null, in state, null);
            Assert.That(empty.LabelCount, Is.EqualTo(1));
            Assert.That(empty.CountOf(FlatTwinShape.Arrow, FlatTwinInk.Walker), Is.EqualTo(1));
        }

        [Test]
        public void Glued_is_the_lattice_round_the_walkers_cell()
        {
            var twin = new FlatTwin();
            for (int k = 0; k < s_rooms.Length; k++)
            {
                RoomModel room = Load(s_rooms[k]);
                for (int mode = 0; mode <= 2; mode++)
                {
                    int copies = mode == 2 ? 3 : 5;
                    float sx = (float)room.Glued.SizeX, sz = (float)room.Glued.SizeZ;
                    // The walker a little off the centre of cell (0, 0), as the module reports it.
                    float hx = -0.5f * room.FreeRect.Wf + 0.6f * sx, hz = -0.5f * room.FreeRect.Df + 0.4f * sz;
                    StateMessage state = StateAt((byte)SpaceId.Glued, hx, hz);
                    state.SetTile(0, 0, 0, 0, 0, 0);
                    state.P3 = mode;
                    state.P4 = copies;
                    state.P5 = sx;
                    state.P6 = sz;
                    FlatTwinBuilder.Build(twin, room, null, in state, null);

                    string what = room.Id + " mode " + mode;
                    int mirroredRows = mode == 1 ? copies / 2 + (((copies / 2) & 1) == 1 ? 1 : 0) : 0;
                    Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Cell), Is.EqualTo(4 * copies * copies), what);
                    Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Highlight), Is.EqualTo(4), what + ": the fundamental cell");
                    Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Outline), Is.EqualTo(4), what + ": the real room");
                    Assert.That(twin.CountOf(FlatTwinShape.Dot, FlatTwinInk.Walker), Is.EqualTo(copies * copies - 1), what + ": the copies");
                    Assert.That(twin.CountOf(FlatTwinShape.Arrow, FlatTwinInk.Walker), Is.EqualTo(1), what);
                    Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Portal), Is.EqualTo(mirroredRows * copies), what + ": mirrored rows");
                    Assert.That(twin.LabelCount, Is.EqualTo(1), what);
                    Assert.That(twin.InView(hx, hz), Is.True, what);
                    Assert.That(room.FreeRect.Contains(new float2(hx, hz)), Is.True);
                    // Centred on the walker's cell, n cells across.
                    Assert.That(twin.CentreX, Is.EqualTo(-0.5f * room.FreeRect.Wf + 0.5f * sx).Within(1e-4f), what);
                    Assert.That(twin.HalfExtent, Is.EqualTo(0.5f * copies * Math.Max(sx, sz) * 1.05f).Within(1e-3f), what);
                }
            }
        }

        [Test]
        public void Glued_klein_mirrors_the_copies_of_odd_rows()
        {
            RoomModel room = Load(RoomFixtures.Sample3x3);
            float sx = (float)room.Glued.SizeX, sz = (float)room.Glued.SizeZ;
            float cx = -0.5f * room.FreeRect.Wf + 0.5f * sx, cz = -0.5f * room.FreeRect.Df + 0.5f * sz;
            StateMessage state = StateAt((byte)SpaceId.Glued, cx + 0.3f, cz + 0.1f);
            state.P3 = FlatTwinBuilder.GluedKlein;
            state.P4 = 3;
            state.P5 = sx;
            state.P6 = sz;
            var twin = new FlatTwin();
            FlatTwinBuilder.Build(twin, room, null, in state, null);

            bool sameRow = false, nextRow = false;
            for (int i = 0; i < twin.Count; i++)
            {
                FlatTwinItem item = twin[i];
                if (item.Shape != FlatTwinShape.Dot) continue;
                if (Math.Abs(item.A - (cx + sx + 0.3f)) < 1e-4f && Math.Abs(item.B - (cz + 0.1f)) < 1e-4f) sameRow = true;
                if (Math.Abs(item.A - (cx - 0.3f)) < 1e-4f && Math.Abs(item.B - (cz + sz + 0.1f)) < 1e-4f) nextRow = true;
            }
            Assert.That(sameRow, Is.True, "a copy in the same row is a translate");
            Assert.That(nextRow, Is.True, "a copy one row up is mirrored in X");
        }

        [Test]
        public void Impossible_rooms_show_room_k_of_n_and_the_room_graph()
        {
            RoomModel room = Load(RoomFixtures.Sample4x5Table);
            StateMessage state = StateAt((byte)SpaceId.Portal, 0f, 0f);
            state.Tile0 = 2;
            state.Sheet = 2;
            state.P3 = FlatTwinBuilder.PortalLoop;
            state.P4 = 4;
            var twin = new FlatTwin();
            FlatTwinBuilder.Build(twin, room, null, in state, null);

            Assert.That(twin.CountOf(FlatTwinShape.Dot, FlatTwinInk.Portal), Is.EqualTo(3));
            Assert.That(twin.CountOf(FlatTwinShape.Dot, FlatTwinInk.Highlight), Is.EqualTo(1), "the current room");
            Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Portal), Is.EqualTo(3 + 3), "three doorways in a row and the way back");
            float x, z;
            Assert.That(twin.GetLabel(0, out x, out z), Is.EqualTo("room 3 of 4"));
            Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Outline), Is.EqualTo(room.Outline.Length), "over the floor plan");
            AssertRoomInView(twin, room);
            // The strip lies above the plan and inside the view.
            FlatTwinItem node;
            twin.TryFind(FlatTwinShape.Dot, FlatTwinInk.Highlight, out node);
            Assert.That(twin.InView(node.A, node.B), Is.True);
            float maxZ = float.MinValue;
            for (int i = 0; i < room.Outline.Length; i++) maxZ = Math.Max(maxZ, room.Outline[i].y);
            Assert.That(node.B, Is.GreaterThan(maxZ));

            // A booth with a hall is two rooms and no way back.
            state.P3 = 0;
            state.P4 = 2;
            state.Tile0 = 0;
            FlatTwinBuilder.Build(twin, room, null, in state, null);
            Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Portal), Is.EqualTo(1));
            Assert.That(twin.GetLabel(0, out x, out z), Is.EqualTo("room 1 of 2"));
        }

        [Test]
        public void Slicer_shows_the_cut_on_its_range_and_three_dials()
        {
            RoomModel room = Load(RoomFixtures.Sample3x3);
            var twin = new FlatTwin();
            float[] cuts = { -FlatTwinBuilder.SlicerCutRange, 0f, 0.55f, 5f };
            float previous = float.MinValue;
            for (int k = 0; k < cuts.Length; k++)
            {
                StateMessage state = StateAt((byte)SpaceId.Slicer, 0.3f, 0.3f);
                state.P3 = cuts[k];
                state.P4 = 0.5f;
                state.P5 = 1.0f;
                state.P6 = -2.0f;
                FlatTwinBuilder.Build(twin, room, null, in state, null);

                Assert.That(twin.CountOf(FlatTwinShape.Dot, FlatTwinInk.Highlight), Is.EqualTo(1), "the cut");
                Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Cell), Is.EqualTo(3 * 8), "three dials");
                Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Highlight), Is.EqualTo(3), "three hands");
                FlatTwinItem cut;
                twin.TryFind(FlatTwinShape.Dot, FlatTwinInk.Highlight, out cut);
                Assert.That(twin.InView(cut.A, cut.B), Is.True, "a cut beyond the range stays on the bar");
                Assert.That(cut.A, Is.GreaterThanOrEqualTo(previous), "the marker moves right with w");
                previous = cut.A;
                AssertRoomInView(twin, room);
                Assert.That(twin.CountOf(FlatTwinShape.Arrow, FlatTwinInk.Walker), Is.EqualTo(1));
            }
        }

        [Test]
        public void A_space_that_draws_its_own_twin_replaces_the_built_in_one()
        {
            var twin = new FlatTwin();
            twin.Line(0f, 0f, 1f, 1f, FlatTwinInk.Outline);
            FlatTwinBuilder.Build(twin, new OwnTwin());
            Assert.That(twin.Count, Is.EqualTo(1));
            Assert.That(twin.CountOf(FlatTwinShape.Dot, FlatTwinInk.Portal), Is.EqualTo(1));
            Assert.That(twin.HalfExtent, Is.EqualTo(3f));
        }

        sealed class OwnTwin : IFlatTwinSource
        {
            public void WriteFlatTwin(FlatTwin twin)
            {
                twin.Dot(1f, 2f, 0.1f, FlatTwinInk.Portal);
                twin.SetView(1f, 2f, 3f, false);
            }
        }

        // ---- the tile graph: the flat stub ----------------------------------------------------------------------

        // A walk over the flat grid, written as the Hub writes state.bin: 16 bytes of header, then records of
        // the Hub's receive time and the 202 bytes of the State (lane D contract, section 2.12).
        static byte[] RecordWalk(FlatWalkerKernel kernel, out List<TileId> sequence)
        {
            const int RecordSize = 8 + StateMessage.Size;
            sequence = new List<TileId>();
            var log = new List<byte>();
            log.AddRange(System.Text.Encoding.ASCII.GetBytes("ATLSTATE"));
            log.AddRange(new byte[] { 1, 0, (byte)RecordSize, 0, 0, 0, 0, 0 });
            var record = new byte[RecordSize];
            double x = 0.0, z = 0.0;
            kernel.ResetToFloor(new double2(x, z));
            long t = 0;
            // East over three tiles, north over two, back west, at walking pace and 60 Hz.
            double[] legs = { 6.4, 0.0, 0.0, 4.2, -9.0, 0.0, 0.0, -2.6 };
            for (int leg = 0; leg < legs.Length; leg += 2)
            {
                int steps = (int)(Math.Max(Math.Abs(legs[leg]), Math.Abs(legs[leg + 1])) / 0.02);
                for (int i = 0; i < steps; i++)
                {
                    x += legs[leg] / steps;
                    z += legs[leg + 1] / steps;
                    kernel.Step(new double2(x, z), true);
                    WalkerPose pose = kernel.Pose;
                    StateMessage s = StateMessage.Identity;
                    s.SpaceId = (byte)SpaceId.CurvedWalk;
                    s.TimeUs = t;
                    s.HeadPosX = (float)x;
                    s.HeadPosZ = (float)z;
                    s.SetTile(pose.Tile.Xu, pose.Tile.Xv, pose.Tile.Yu, pose.Tile.Yv, pose.Tile.Tu, pose.Tile.Tv);
                    s.SetXform(0, pose.Local.M00); s.SetXform(1, pose.Local.M01); s.SetXform(2, pose.Local.M02);
                    s.SetXform(3, pose.Local.M10); s.SetXform(4, pose.Local.M11); s.SetXform(5, pose.Local.M12);
                    s.SetXform(6, pose.Local.M20); s.SetXform(7, pose.Local.M21); s.SetXform(8, pose.Local.M22);
                    s.P0 = (float)kernel.MetresPerUnit;
                    s.P2 = (float)kernel.CullRho;
                    if (sequence.Count == 0 || sequence[sequence.Count - 1] != pose.Tile) sequence.Add(pose.Tile);
                    BitConverter.GetBytes(t).CopyTo(record, 0);
                    s.WriteTo(record, 8);
                    log.AddRange(record);
                    t += 16667;
                }
            }
            return log.ToArray();
        }

        [Test]
        public void FlatTwin_shows_the_tile_id_sequence_of_a_replayed_State_log()
        {
            var walkKernel = new FlatWalkerKernel();
            List<TileId> walked;
            byte[] log = RecordWalk(walkKernel, out walked);
            Assert.That(walked.Count, Is.GreaterThan(8), "the walk crosses tiles");

            // Replay: every record goes to the trail, as the Spectator does with every State it receives.
            const int RecordSize = 8 + StateMessage.Size;
            var kernel = new FlatWalkerKernel();
            var trail = new FlatTwinTrail();
            var twin = new FlatTwin();
            var state = new StateMessage();
            int records = 0;
            for (int offset = 16; offset + RecordSize <= log.Length; offset += RecordSize)
            {
                Assert.That(state.ReadFrom(log, offset + 8, StateMessage.Size), Is.True);
                trail.Add(in state);
                records++;
            }
            Assert.That(records, Is.GreaterThan(600));

            Assert.That(trail.TileCount, Is.EqualTo(walked.Count));
            Assert.That(trail.TilesSeen, Is.EqualTo(walked.Count));
            for (int i = 0; i < walked.Count; i++)
                Assert.That(trail.GetTile(i), Is.EqualTo(walked[i]), "tile " + i + " of the sequence");

            // The picture of the last record: the kernel placed where the State says, as in the Spectator.
            var pose = new WalkerPose
            {
                Tile = new TileId(state.Tile0, state.Tile1, state.Tile2, state.Tile3, state.Tile4, state.Tile5),
                Local = new Iso2(state.Xform0, state.Xform1, state.Xform2, state.Xform3, state.Xform4, state.Xform5,
                                 state.Xform6, state.Xform7, state.Xform8, 0.0)
            };
            kernel.Reset(in pose);
            RoomModel room = Load(RoomFixtures.Sample3x3);
            FlatTwinBuilder.Build(twin, room, kernel, in state, trail);

            Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Trail), Is.EqualTo(walked.Count - 1), "one stroke per tile change");
            Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Cell), Is.EqualTo(4 * kernel.Candidates.Count));
            Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Highlight), Is.EqualTo(4), "the walker's tile");
            Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Outline), Is.EqualTo(room.Outline.Length), "the room over tile zero");

            // The strokes join the centres of the tiles in the order they were walked.
            float side = (float)(kernel.Graph.CentreSpacing * kernel.MetresPerUnit);
            int stroke = 0;
            for (int i = 0; i < twin.Count; i++)
            {
                FlatTwinItem item = twin[i];
                if (item.Shape != FlatTwinShape.Line || item.Ink != FlatTwinInk.Trail) continue;
                long ai, aj, bi, bj;
                FlatTileGraph.TryGetCell(walked[stroke], out ai, out aj);
                FlatTileGraph.TryGetCell(walked[stroke + 1], out bi, out bj);
                Assert.That(item.A, Is.EqualTo(ai * side).Within(1e-4f));
                Assert.That(item.B, Is.EqualTo(aj * side).Within(1e-4f));
                Assert.That(item.C, Is.EqualTo(bi * side).Within(1e-4f));
                Assert.That(item.D, Is.EqualTo(bj * side).Within(1e-4f));
                stroke++;
            }

            // The walker stands in the highlighted tile, where the log's head position is.
            FlatTwinItem walker;
            Assert.That(twin.TryFind(FlatTwinShape.Arrow, FlatTwinInk.Walker, out walker), Is.True);
            Assert.That(walker.A, Is.EqualTo(state.HeadPosX).Within(1e-3f));
            Assert.That(walker.B, Is.EqualTo(state.HeadPosZ).Within(1e-3f));
            Assert.That(twin.InView(walker.A, walker.B), Is.True);
            long wi, wj;
            FlatTileGraph.TryGetCell(walked[walked.Count - 1], out wi, out wj);
            Assert.That(Math.Abs(walker.A - wi * side), Is.LessThanOrEqualTo(0.5f * side + 0.05f));
            Assert.That(Math.Abs(walker.B - wj * side), Is.LessThanOrEqualTo(0.5f * side + 0.05f));
        }

        [Test]
        public void FlatTwinTrail_keeps_ten_positions_per_second_and_the_last_64_tiles()
        {
            var trail = new FlatTwinTrail();
            StateMessage s = StateAt((byte)SpaceId.Home, 0f, 0f);
            // Two seconds at 60 Hz and 0.8 m/s.
            for (int i = 0; i < 120; i++)
            {
                s.TimeUs = 1000000 + i * 16667L;
                s.HeadPosX = 0.8f * i / 60f;
                trail.Add(in s);
            }
            Assert.That(trail.PositionCount, Is.InRange(19, 21));
            Assert.That(trail.TileCount, Is.EqualTo(1), "one tile, never repeated");

            // Standing still adds nothing.
            for (int i = 0; i < 120; i++)
            {
                s.TimeUs += 16667;
                trail.Add(in s);
            }
            Assert.That(trail.PositionCount, Is.InRange(19, 21));

            // The rings hold the newest 600 positions and 64 tiles.
            for (int i = 0; i < 1000; i++)
            {
                s.TimeUs += 100000;
                s.HeadPosX += 0.1f;
                s.SetTile(i + 1, 0, 0, 0, 1, 0);
                trail.Add(in s);
            }
            Assert.That(trail.PositionCount, Is.EqualTo(FlatTwinTrail.MaxPositions));
            Assert.That(trail.TileCount, Is.EqualTo(FlatTwinTrail.MaxTiles));
            Assert.That(trail.GetTile(FlatTwinTrail.MaxTiles - 1).Xu, Is.EqualTo(1000));
            Assert.That(trail.GetTile(0).Xu, Is.EqualTo(1000 - FlatTwinTrail.MaxTiles + 1));
            float x, z;
            trail.GetPosition(FlatTwinTrail.MaxPositions - 1, out x, out z);
            Assert.That(x, Is.EqualTo(s.HeadPosX));

            // Another space starts both again.
            s.SpaceId = (byte)SpaceId.Glued;
            trail.Add(in s);
            Assert.That(trail.PositionCount, Is.EqualTo(1));
            Assert.That(trail.TileCount, Is.EqualTo(1));
        }

        // ---- the mesh ---------------------------------------------------------------------------------------------

        [Test]
        public void FlatTwinMesher_clips_to_the_view_and_stays_on_the_panel()
        {
            var twin = new FlatTwin();
            var data = new MeshData(64, 128);
            twin.Line(-0.5f, 0f, 0.5f, 0f, FlatTwinInk.Outline);        // inside
            twin.Line(-5f, 0.5f, 5f, 0.5f, FlatTwinInk.Grid);           // crosses the view
            twin.Line(3f, 3f, 4f, 4f, FlatTwinInk.Grid);                // outside
            twin.Dot(0f, 0f, 0.1f, FlatTwinInk.Walker);
            twin.Dot(9f, 9f, 0.1f, FlatTwinInk.Walker);                 // outside
            twin.Arrow(0.2f, 0.2f, 1f, FlatTwinInk.Walker);
            twin.Bar(-0.5f, -0.5f, 0.5f, -0.4f, FlatTwinInk.Good);
            twin.SetView(0f, 0f, 1f, false);
            FlatTwinMesher.Write(twin, data, 0.125f, true);

            // Background 4, two lines 8, one dot 9, the dart 4, the bar 4.
            Assert.That(data.VertexCount, Is.EqualTo(4 + 8 + 9 + 4 + 4));
            Assert.That(data.TriangleCount, Is.EqualTo(2 + 4 + 8 + 2 + 2));
            float reach = 0.125f * (1f + FlatTwinMesher.ArrowLength);
            for (int i = 0; i < data.VertexCount; i++)
            {
                Assert.That(Math.Abs(data.Vertices[i].Position.x), Is.LessThanOrEqualTo(reach));
                Assert.That(Math.Abs(data.Vertices[i].Position.y), Is.LessThanOrEqualTo(reach));
                Assert.That(data.Vertices[i].Position.z, Is.EqualTo(0f));
            }
            // The crossing line was cut at the panel's edge.
            Assert.That(data.Vertices[8].Position.x, Is.EqualTo(-0.125f).Within(1e-5f));
            Assert.That(data.Vertices[8].Color, Is.EqualTo(FlatTwinMesher.Colour(FlatTwinInk.Grid)));

            // A disc view cuts at the unit circle.
            twin.Clear();
            twin.Line(-5f, 0f, 5f, 0f, FlatTwinInk.Grid);
            twin.SetView(0f, 0f, 1f, true);
            FlatTwinMesher.Write(twin, data, 1f, false);
            Assert.That(data.VertexCount, Is.EqualTo(4));
            Assert.That(data.BoundsMax.x, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void FlatTwin_rebuild_allocates_nothing_and_stays_small()
        {
            // The worst flat case of this build: budget_room with twelve objects and a full trail.
            RoomModel room = Load(RoomFixtures.BudgetRoom);
            var kernel = new FlatWalkerKernel();
            var twin = new FlatTwin();
            var data = new MeshData(4096, 8192);
            var trail = new FlatTwinTrail();
            StateMessage state = StateAt((byte)SpaceId.Glued, 0.1f, 0.2f);
            for (int i = 0; i < 700; i++)
            {
                state.TimeUs = 1 + i * 100000L;
                state.HeadPosX = 0.9f * (float)Math.Sin(i * 0.1);
                state.HeadPosZ = 1.2f * (float)Math.Cos(i * 0.07);
                trail.Add(in state);
            }
            Assert.That(trail.PositionCount, Is.EqualTo(FlatTwinTrail.MaxPositions));

            byte[] spaces = { (byte)SpaceId.Home, (byte)SpaceId.Glued, (byte)SpaceId.CurvedWalk, (byte)SpaceId.Portal, (byte)SpaceId.Slicer, SpaceIds.NoSpace };
            // Warm-up: the list and the mesh reach their working size.
            for (int k = 0; k < spaces.Length; k++) Rebuild(twin, data, room, kernel, trail, spaces[k]);

            long before = GC.GetAllocatedBytesForCurrentThread();
            int worst = 0;
            for (int round = 0; round < 10; round++)
            {
                for (int k = 0; k < spaces.Length; k++)
                {
                    Rebuild(twin, data, room, kernel, trail, spaces[k]);
                    if (data.TriangleCount > worst) worst = data.TriangleCount;
                }
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0), "a rebuild allocates nothing");
            Assert.That(worst, Is.LessThan(3000), "the map is a few thousand triangles at most");
            Assert.That(data.VertexCount, Is.LessThan(FlatTwinMesher.MaxVertices));
        }

        static void Rebuild(FlatTwin twin, MeshData data, RoomModel room, IWalkerKernel kernel, FlatTwinTrail trail, byte space)
        {
            StateMessage state = StateAt(space, 0.3f, -0.2f, 1f);
            state.SetTile(space == (byte)SpaceId.Glued ? 0 : 1, 0, 0, 0, space == (byte)SpaceId.Glued ? 0 : 1, 0);
            state.P3 = 1f;
            state.P4 = 7f;
            state.P5 = (float)room.Glued.SizeX;
            state.P6 = (float)room.Glued.SizeZ;
            FlatTwinBuilder.Build(twin, room, kernel, in state, trail);
            FlatTwinMesher.Write(twin, data, 0.125f, true);
        }
    }
}

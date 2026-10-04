using System;
using System.Collections.Generic;
using System.Diagnostics;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Telemetry.Tests
{
    /// <summary>
    /// The Poincaré-disc twin of S2 and S3 on lane B's kernel (Master Plan rule 6, task P1-D07): the walker's
    /// tile in the middle, every point inside the unit disc, the walker's marker at the walked hyperbolic
    /// distance from the room, q tiles round every corner, and the same list for the same walk.
    /// </summary>
    public class FlatTwinDiscTests
    {
        const double L = 1.885;             // metres per kernel unit of the 3 x 3 m sample room
        const double StepMetres = 0.05;

        static HolonomyWalkerKernel Kernel(int q)
        {
            var kernel = new HolonomyWalkerKernel();
            kernel.Configure(KernelConfig.Curved(q, L));
            kernel.ResetToFloor(double2.zero);
            return kernel;
        }

        // Walks the head in a straight line on the floor, in steps the kernel accepts. Returns the end point.
        static double2 Walk(HolonomyWalkerKernel kernel, double2 from, double2 to)
        {
            int steps = (int)Math.Ceiling(math.length(to - from) / StepMetres);
            for (int i = 1; i <= steps; i++) kernel.Step(math.lerp(from, to, (double)i / steps), true);
            return to;
        }

        static StateMessage StateOf(IWalkerKernel kernel, byte space, double2 head, float yaw)
        {
            StateMessage s = StateMessage.Identity;
            s.SpaceId = space;
            s.SceneId = space;
            s.HeadPosX = (float)head.x;
            s.HeadPosY = 1.6f;
            s.HeadPosZ = (float)head.y;
            s.HeadRotY = (float)Math.Sin(0.5 * yaw);
            s.HeadRotW = (float)Math.Cos(0.5 * yaw);
            s.P0 = (float)kernel.MetresPerUnit;
            WalkerPose pose = kernel.Pose;
            for (int i = 0; i < 6; i++) s.SetTile(i, pose.Tile[i]);
            Span<double> m = stackalloc double[9];
            pose.Local.CopyTo(m);
            for (int i = 0; i < 9; i++) s.SetXform(i, m[i]);
            return s;
        }

        // Hyperbolic distance between two points of the unit disc.
        static double DiscDistance(double ax, double az, double bx, double bz)
        {
            double d2 = (ax - bx) * (ax - bx) + (az - bz) * (az - bz);
            return Math.Acosh(1.0 + 2.0 * d2 / ((1.0 - ax * ax - az * az) * (1.0 - bx * bx - bz * bz)));
        }

        static int FirstOf(FlatTwin twin, FlatTwinShape shape, FlatTwinInk ink)
        {
            for (int i = 0; i < twin.Count; i++)
                if (twin[i].Shape == shape && twin[i].Ink == ink) return i;
            return -1;
        }

        // A tile is LinesPerTile lines in a row; corner k is where edge k starts.
        static float2 Corner(FlatTwin twin, int firstLine, int k)
        {
            FlatTwinItem line = twin[firstLine + (k & 3) * FlatTwinDisc.EdgeSegments];
            return new float2(line.A, line.B);
        }

        static void AssertInsideDisc(FlatTwin twin, string what)
        {
            for (int i = 0; i < twin.Count; i++)
            {
                FlatTwinItem item = twin[i];
                Assert.That(item.A * item.A + item.B * item.B, Is.LessThan(1f), what + ": entry " + i);
                if (item.Shape == FlatTwinShape.Line)
                    Assert.That(item.C * item.C + item.D * item.D, Is.LessThan(1f), what + ": end of line " + i);
            }
        }

        static void AssertSame(FlatTwin a, FlatTwin b, string what)
        {
            Assert.That(b.Count, Is.EqualTo(a.Count), what);
            for (int i = 0; i < a.Count; i++)
            {
                FlatTwinItem x = a[i], y = b[i];
                bool same = x.Shape == y.Shape && x.Ink == y.Ink && x.A == y.A && x.B == y.B && x.C == y.C && x.D == y.D;
                Assert.That(same, Is.True, what + ": entry " + i);
            }
        }

        static int OnMap(IWalkerKernel kernel)
        {
            int n = 0;
            for (int k = 0; k < kernel.Candidates.Count; k++)
                if (kernel.Candidates[k].F.Position.z <= Math.Cosh(FlatTwinDisc.MapRho) + 1e-9) n++;
            return n;
        }

        [TestCase(5)]
        [TestCase(6)]
        public void Tile_zero_is_centred_while_the_walker_is_on_it(int q)
        {
            HolonomyWalkerKernel kernel = Kernel(q);
            var head = new double2(0.3, -0.2);
            Walk(kernel, double2.zero, head);
            Assert.That(kernel.Pose.Tile, Is.EqualTo(TileId.Zero));

            var twin = new FlatTwin();
            Assert.That(FlatTwinDisc.Build(twin, null, kernel, null, 0f), Is.True);
            Assert.That(twin.ClipToDisc, Is.True);
            Assert.That(twin.CentreX, Is.EqualTo(0f));
            Assert.That(twin.CentreZ, Is.EqualTo(0f));
            Assert.That(twin.HalfExtent, Is.EqualTo(FlatTwinDisc.ViewHalfExtent));

            int onMap = OnMap(kernel);
            if (q == 5) Assert.That(onMap, Is.EqualTo(45), "kernel contract, section 4.6: 45 tile centres within rho = 3");
            Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Cell), Is.EqualTo(FlatTwinDisc.LinesPerTile * onMap));
            Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Highlight), Is.EqualTo(FlatTwinDisc.LinesPerTile));
            Assert.That(twin.CountOf(FlatTwinShape.Arrow, FlatTwinInk.Walker), Is.EqualTo(1));

            // The walker's tile: four corners at the circumradius round the origin, corner 0 at -45 degrees.
            int own = FirstOf(twin, FlatTwinShape.Line, FlatTwinInk.Highlight);
            float corner = (float)Math.Tanh(0.5 * kernel.Graph.CircumRadius);
            float2 sum = float2.zero;
            for (int k = 0; k < 4; k++)
            {
                float2 c = Corner(twin, own, k);
                double angle = (-0.25 + 0.5 * k) * Math.PI;
                Assert.That(c.x, Is.EqualTo(corner * (float)Math.Cos(angle)).Within(1e-6f), "corner " + k);
                Assert.That(c.y, Is.EqualTo(corner * (float)Math.Sin(angle)).Within(1e-6f), "corner " + k);
                sum += c;
            }
            Assert.That(math.length(sum), Is.LessThan(1e-6f));

            // The room's anchor is tile zero's centre: the middle of the disc.
            FlatTwinItem home;
            Assert.That(twin.TryFind(FlatTwinShape.Dot, FlatTwinInk.Outline, out home), Is.True);
            Assert.That(home.A, Is.EqualTo(0f).Within(1e-7f));
            Assert.That(home.B, Is.EqualTo(0f).Within(1e-7f));

            // The walker inside it, at tanh(d / 2) along the way it walked, the arrow along the room's +Z.
            FlatTwinItem walker;
            Assert.That(twin.TryFind(FlatTwinShape.Arrow, FlatTwinInk.Walker, out walker), Is.True);
            double radius = Math.Tanh(0.5 * math.length(head) / L);
            Assert.That(walker.A, Is.EqualTo(radius * head.x / math.length(head)).Within(1e-6));
            Assert.That(walker.B, Is.EqualTo(radius * head.y / math.length(head)).Within(1e-6));
            AssertInsideDisc(twin, "q = " + q);
        }

        [TestCase(5)]
        [TestCase(6)]
        public void Every_point_lies_inside_the_unit_disc_along_a_walk(int q)
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            HolonomyWalkerKernel kernel = Kernel(q);
            var twin = new FlatTwin();
            var trail = new FlatTwinTrail();
            // Round a 2 m square five times: the walk the curved space is made for, and it leaves tile zero.
            double2[] lap = { new double2(-1, -1), new double2(1, -1), new double2(1, 1), new double2(-1, 1) };
            double2 at = Walk(kernel, double2.zero, lap[0]);
            var tiles = new HashSet<TileId>();
            for (int leg = 0; leg < 20; leg++)
            {
                double2 to = lap[(leg + 1) % 4];
                for (int part = 1; part <= 4; part++)
                {
                    double2 next = math.lerp(lap[leg % 4], to, part / 4.0);
                    at = Walk(kernel, at, next);
                    StateMessage state = StateOf(kernel, (byte)SpaceId.CurvedWalk, at, 0.3f * leg);
                    trail.Add(in state);
                    Assert.That(FlatTwinDisc.Build(twin, room, kernel, in state, trail), Is.True, "leg " + leg);
                    AssertInsideDisc(twin, "q = " + q + ", leg " + leg);
                    Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Highlight), Is.EqualTo(FlatTwinDisc.LinesPerTile));
                    tiles.Add(kernel.Pose.Tile);
                }
            }
            Assert.That(tiles.Count, Is.GreaterThan(2), "the walk went through several tiles");
        }

        [TestCase(5, 0)]
        [TestCase(5, 1)]
        [TestCase(6, 0)]
        [TestCase(6, 3)]
        public void A_step_across_an_edge_moves_the_marker_by_the_walked_distance(int q, int edge)
        {
            HolonomyWalkerKernel kernel = Kernel(q);
            var twin = new FlatTwin();
            var direction = new double2(Math.Cos(0.5 * Math.PI * edge), Math.Sin(0.5 * Math.PI * edge));
            float yaw = (float)Math.Atan2(direction.x, direction.y);        // the head looks where it walks
            double inRadiusMetres = 0.5 * kernel.Graph.CentreSpacing * L;
            int crossings = 0;
            double2 at = double2.zero;
            for (int i = 1; i * StepMetres < 1.5 * inRadiusMetres; i++)
            {
                at = i * StepMetres * direction;
                KernelStep step = kernel.Step(at, true);
                crossings += step.Crossings;
                StateMessage state = StateOf(kernel, (byte)SpaceId.CurvedWalk, at, yaw);
                Assert.That(FlatTwinDisc.Build(twin, null, kernel, in state, null), Is.True);

                FlatTwinItem walker, home;
                Assert.That(twin.TryFind(FlatTwinShape.Arrow, FlatTwinInk.Walker, out walker), Is.True);
                Assert.That(twin.TryFind(FlatTwinShape.Dot, FlatTwinInk.Outline, out home), Is.True);
                // The marker is as far from the room's anchor, in the disc's own metric, as the walker walked.
                Assert.That(DiscDistance(walker.A, walker.B, home.A, home.B), Is.EqualTo(i * StepMetres / L).Within(1e-5), "step " + i);
                // And it points away from the room.
                double fx = Math.Sin(walker.C), fz = Math.Cos(walker.C);
                Assert.That(fx * (walker.A - home.A) + fz * (walker.B - home.B), Is.GreaterThan(0.0), "step " + i);

                if (crossings == 0)
                {
                    Assert.That(Math.Abs(home.A) + Math.Abs(home.B), Is.LessThan(1e-7f), "tile zero in the middle");
                    Assert.That(Math.Cos(walker.C - yaw), Is.GreaterThan(1.0 - 1e-9), "heading on the axis");
                }
                else
                {
                    // The disc is centred on the new tile, and tile zero is one centre spacing away.
                    Assert.That(kernel.Pose.Tile, Is.Not.EqualTo(TileId.Zero));
                    Assert.That(DiscDistance(0, 0, home.A, home.B), Is.EqualTo(kernel.Graph.CentreSpacing).Within(1e-5));
                    Assert.That(DiscDistance(0, 0, walker.A, walker.B), Is.LessThan(0.5 * kernel.Graph.CentreSpacing));
                }
            }
            Assert.That(crossings, Is.EqualTo(1), "the walk crossed one edge");
        }

        [TestCase(5)]
        [TestCase(6)]
        public void Tiles_are_regular_and_q_of_them_meet_at_every_corner(int q)
        {
            HolonomyWalkerKernel kernel = Kernel(q);
            var twin = new FlatTwin();
            Assert.That(FlatTwinDisc.Build(twin, null, kernel, null, 0f), Is.True);
            ITileGraph graph = kernel.Graph;

            int first = FirstOf(twin, FlatTwinShape.Line, FlatTwinInk.Cell);
            int tiles = twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Cell) / FlatTwinDisc.LinesPerTile;
            double rc = graph.CircumRadius;
            double side = Geometry2.Distance(
                Geometry2.Exp(rc * new double2(Math.Cos(0.25 * Math.PI), -Math.Sin(0.25 * Math.PI)), -1),
                Geometry2.Exp(rc * new double2(Math.Cos(0.25 * Math.PI), Math.Sin(0.25 * Math.PI)), -1), -1);

            // Every tile on the map is the same square: four equal sides, two diagonals of twice the circumradius.
            for (int t = 0; t < tiles; t++)
            {
                int at = first + t * FlatTwinDisc.LinesPerTile;
                for (int k = 0; k < 4; k++)
                {
                    float2 a = Corner(twin, at, k), b = Corner(twin, at, k + 1), c = Corner(twin, at, k + 2);
                    Assert.That(DiscDistance(a.x, a.y, b.x, b.y), Is.EqualTo(side).Within(2e-3), "tile " + t + " side " + k);
                    Assert.That(DiscDistance(a.x, a.y, c.x, c.y), Is.EqualTo(2.0 * rc).Within(2e-3), "tile " + t + " diagonal " + k);
                }
                // The outline is closed: each line starts where the one before it ended.
                for (int i = 0; i < FlatTwinDisc.LinesPerTile; i++)
                {
                    FlatTwinItem line = twin[at + i], next = twin[at + (i + 1) % FlatTwinDisc.LinesPerTile];
                    Assert.That(line.C == next.A && line.D == next.B, Is.True, "tile " + t + " line " + i);
                }
            }

            // The middle tile's edge e faces e * 90 degrees, as the kernel numbers its edges, and the lines to two
            // neighbouring edge midpoints meet at a right angle: the centres of the five tiles round a corner of
            // {4,5} are the corners of a right-angled pentagon.
            int own = FirstOf(twin, FlatTwinShape.Line, FlatTwinInk.Highlight);
            float mid = (float)Math.Tanh(0.25 * graph.CentreSpacing);
            for (int e = 0; e < 4; e++)
            {
                FlatTwinItem line = twin[own + e * FlatTwinDisc.EdgeSegments + FlatTwinDisc.EdgeSegments / 2];
                Assert.That(line.A, Is.EqualTo(mid * (float)Math.Cos(0.5 * Math.PI * e)).Within(1e-6f), "edge " + e);
                Assert.That(line.B, Is.EqualTo(mid * (float)Math.Sin(0.5 * Math.PI * e)).Within(1e-6f), "edge " + e);
            }

            // The vertex figure: q tiles share every corner that is far enough from the rim for all of them to be drawn.
            int checkedCorners = 0;
            for (int t = 0; t < tiles; t++)
            {
                for (int k = 0; k < 4; k++)
                {
                    float2 v = Corner(twin, first + t * FlatTwinDisc.LinesPerTile, k);
                    if (math.length(v) > 0.6f) continue;
                    int meeting = 0;
                    for (int u = 0; u < tiles; u++)
                        for (int j = 0; j < 4; j++)
                            if (math.distance(Corner(twin, first + u * FlatTwinDisc.LinesPerTile, j), v) < 1e-5f) meeting++;
                    Assert.That(meeting, Is.EqualTo(q), "tile " + t + " corner " + k);
                    checkedCorners++;
                }
            }
            Assert.That(checkedCorners, Is.GreaterThanOrEqualTo(4 * q), "at least the four corners of the middle tile");
        }

        [TestCase(5)]
        [TestCase(6)]
        public void The_same_walk_gives_the_same_list(int q)
        {
            HolonomyWalkerKernel a = Kernel(q), b = Kernel(q);
            var twinA = new FlatTwin();
            var twinB = new FlatTwin();
            var fromPose = new FlatTwin();
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            double2[] path = { new double2(1.2, 0.4), new double2(1.3, -1.1), new double2(-0.6, -1.4), new double2(-1.4, 0.9), new double2(0.2, 1.3) };
            double2 at = double2.zero;
            for (int leg = 0; leg < 3 * path.Length; leg++)
            {
                double2 to = path[leg % path.Length];
                Walk(a, at, to);
                Walk(b, at, to);
                at = to;
                StateMessage stateA = StateOf(a, (byte)SpaceId.CurvedWalk, at, 0.7f);
                StateMessage stateB = StateOf(b, (byte)SpaceId.CurvedWalk, at, 0.7f);
                Assert.That(FlatTwinDisc.Build(twinA, room, a, in stateA, null), Is.True);
                Assert.That(FlatTwinDisc.Build(twinB, room, b, in stateB, null), Is.True);
                AssertSame(twinA, twinB, "two kernels, leg " + leg);
                // A second build from the same State changes nothing.
                Assert.That(FlatTwinDisc.Build(twinB, room, a, in stateA, null), Is.True);
                AssertSame(twinA, twinB, "rebuilt, leg " + leg);
                // The State carries the kernel's pose without loss, so both entry points draw the same disc.
                Assert.That(FlatTwinDisc.Build(fromPose, room, a, null, FlatTwinBuilder.Heading(in stateA)), Is.True);
                AssertSame(twinA, fromPose, "from the pose, leg " + leg);
            }
            Assert.That(a.Pose.Tile, Is.EqualTo(b.Pose.Tile));
        }

        [Test]
        public void The_room_is_drawn_on_tile_zero()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            HolonomyWalkerKernel kernel = Kernel(5);
            var twin = new FlatTwin();
            Assert.That(FlatTwinDisc.Build(twin, room, kernel, null, 0f), Is.True);

            int sides = room.Outline.Length;
            Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Outline), Is.EqualTo(sides * FlatTwinDisc.RoomSegments));
            Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Grid), Is.EqualTo(4 * FlatTwinDisc.RoomSegments), "free rectangle");
            // Each side starts on its corner: the corner's metres on tile zero's chart, at tanh(d / 2).
            int first = FirstOf(twin, FlatTwinShape.Line, FlatTwinInk.Outline);
            for (int i = 0; i < sides; i++)
            {
                FlatTwinItem line = twin[first + i * FlatTwinDisc.RoomSegments];
                float2 p = room.Outline[i];
                double metres = math.length(p), radius = Math.Tanh(0.5 * metres / L);
                Assert.That(line.A, Is.EqualTo(radius * p.x / metres).Within(1e-6), "corner " + i);
                Assert.That(line.B, Is.EqualTo(radius * p.y / metres).Within(1e-6), "corner " + i);
            }
            AssertInsideDisc(twin, "room");

            // Without a usable length scale the tiles are drawn and the room is left out.
            Assert.That(FlatTwinDisc.Draw(new FlatTwin(), kernel.Graph, kernel.Pose, kernel.Candidates, 0.0, room, null, 0f), Is.True);
            twin.Clear();
            WalkerPose pose = kernel.Pose;
            FlatTwinDisc.Draw(twin, kernel.Graph, in pose, kernel.Candidates, double.NaN, room, null, 0f);
            Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Outline), Is.EqualTo(0));
            Assert.That(twin.CountOf(FlatTwinShape.Dot, FlatTwinInk.Outline), Is.EqualTo(1));
        }

        [Test]
        public void The_trail_joins_the_centres_of_the_walked_tiles()
        {
            HolonomyWalkerKernel kernel = Kernel(5);
            var twin = new FlatTwin();
            var trail = new FlatTwinTrail();
            double2 at = double2.zero;
            StateMessage state = StateOf(kernel, (byte)SpaceId.CurvedWalk, at, 0f);
            trail.Add(in state);
            double2[] path = { new double2(1.4, 0), new double2(1.4, 1.4) };
            for (int leg = 0; leg < path.Length; leg++)
            {
                int steps = (int)Math.Ceiling(math.length(path[leg] - at) / StepMetres);
                for (int i = 1; i <= steps; i++)
                {
                    double2 p = math.lerp(at, path[leg], (double)i / steps);
                    kernel.Step(p, true);
                    state = StateOf(kernel, (byte)SpaceId.CurvedWalk, p, 0f);
                    trail.Add(in state);
                }
                at = path[leg];
            }
            Assert.That(trail.TileCount, Is.GreaterThanOrEqualTo(3));
            Assert.That(trail.GetTile(trail.TileCount - 1), Is.EqualTo(kernel.Pose.Tile));

            Assert.That(FlatTwinDisc.Build(twin, null, kernel, in state, trail), Is.True);
            Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Trail), Is.EqualTo(trail.TileCount - 1));
            int first = FirstOf(twin, FlatTwinShape.Line, FlatTwinInk.Trail);
            for (int i = 0; i < trail.TileCount - 1; i++)
            {
                FlatTwinItem line = twin[first + i];
                // Tiles walked one after the other are neighbours: their centres are one centre spacing apart.
                Assert.That(DiscDistance(line.A, line.B, line.C, line.D), Is.EqualTo(kernel.Graph.CentreSpacing).Within(1e-5), "leg " + i);
            }
            // The trail ends on the walker's tile, the middle of the disc.
            FlatTwinItem last = twin[first + trail.TileCount - 2];
            Assert.That(Math.Abs(last.C) + Math.Abs(last.D), Is.LessThan(1e-6f));
        }

        [Test]
        public void A_State_far_from_the_local_kernel_is_drawn_from_the_graph()
        {
            // The Station's kernel stands on tile zero; the phone has walked 12 m away.
            HolonomyWalkerKernel phone = Kernel(5), station = Kernel(5);
            double2 at = Walk(phone, double2.zero, new double2(12.0, 0.0));
            StateMessage state = StateOf(phone, (byte)SpaceId.CurvedWalk, at, 0f);
            int held = station.Candidates.Count;
            bool near = false;
            for (int k = 0; k < held; k++) near |= station.Candidates[k].Id == phone.Pose.Tile;
            Assert.That(near, Is.False, "the walker's tile is not among the Station's candidates");

            var twin = new FlatTwin();
            var expected = new FlatTwin();
            Assert.That(FlatTwinDisc.Build(twin, null, station, in state, null), Is.True);
            Assert.That(FlatTwinDisc.Build(expected, null, phone, in state, null), Is.True);
            Assert.That(twin.CountOf(FlatTwinShape.Line, FlatTwinInk.Cell), Is.EqualTo(expected.CountOf(FlatTwinShape.Line, FlatTwinInk.Cell)));
            Assert.That(twin.CountOf(FlatTwinShape.Dot, FlatTwinInk.Outline), Is.EqualTo(0), "tile zero is off the map");
            FlatTwinItem a, b;
            Assert.That(twin.TryFind(FlatTwinShape.Arrow, FlatTwinInk.Walker, out a), Is.True);
            Assert.That(expected.TryFind(FlatTwinShape.Arrow, FlatTwinInk.Walker, out b), Is.True);
            Assert.That(a.A == b.A && a.B == b.B && a.C == b.C, Is.True);
            AssertInsideDisc(twin, "far State");
            // The kernel's own list was only read.
            Assert.That(station.Candidates.Count, Is.EqualTo(held));
            Assert.That(station.Candidates[0].Id, Is.EqualTo(TileId.Zero));
        }

        [Test]
        public void What_cannot_be_drawn_is_refused_and_leaves_the_twin_empty()
        {
            var twin = new FlatTwin();
            HolonomyWalkerKernel kernel = Kernel(5);
            StateMessage state = StateOf(kernel, (byte)SpaceId.CurvedWalk, double2.zero, 0f);

            // The flat kernel (q = 4) and no kernel at all: the square plan of FlatTwinBuilder is the map.
            Assert.That(FlatTwinDisc.Build(twin, null, new FlatWalkerKernel(), in state, null), Is.False);
            Assert.That(twin.Count, Is.EqualTo(0));
            Assert.That(FlatTwinDisc.Build(twin, null, null, in state, null), Is.False);
            Assert.That(FlatTwinDisc.Supports(new FlatWalkerKernel().Graph), Is.False);
            Assert.That(FlatTwinDisc.Supports(kernel.Graph), Is.True);

            // "No tile", and six words that are no tile of {4,5}.
            StateMessage none = state;
            none.SetTile(0, 0, 0, 0, 0, 0);
            Assert.That(FlatTwinDisc.Build(twin, null, kernel, in none, null), Is.False);
            StateMessage wrong = state;
            wrong.SetTile(7, 3, -2, 5, 1, 9);
            Assert.That(FlatTwinDisc.Build(twin, null, kernel, in wrong, null), Is.False);
            Assert.That(twin.Count, Is.EqualTo(0));
            Assert.That(twin.LabelCount, Is.EqualTo(0));

            // A transform that is no isometry (S3 sends the identity; a broken one is treated the same way):
            // the walker is put on the centre of its tile.
            StateMessage broken = state;
            broken.SetXform(2, double.NaN);
            Assert.That(FlatTwinDisc.Build(twin, null, kernel, in broken, null), Is.True);
            FlatTwinItem walker;
            Assert.That(twin.TryFind(FlatTwinShape.Arrow, FlatTwinInk.Walker, out walker), Is.True);
            Assert.That(walker.A, Is.EqualTo(0f));
            Assert.That(walker.B, Is.EqualTo(0f));
            Assert.Throws<ArgumentNullException>(() => FlatTwinDisc.Build(null, null, kernel, null, 0f));
        }

        [TestCase(5)]
        [TestCase(6)]
        public void A_rebuild_allocates_nothing_and_fits_the_panel_mesh(int q)
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.BudgetRoom);
            HolonomyWalkerKernel kernel = Kernel(q);
            var twin = new FlatTwin();
            var data = new MeshData(4096, 8192);
            var trail = new FlatTwinTrail();
            double2 at = double2.zero;
            StateMessage state = StateOf(kernel, (byte)SpaceId.CurvedWalk, at, 0.4f);
            double2[] lap = { new double2(1, -1), new double2(1, 1), new double2(-1, 1), new double2(-1, -1) };
            for (int leg = 0; leg < 8; leg++)
            {
                at = Walk(kernel, at, lap[leg % 4]);
                state = StateOf(kernel, (byte)SpaceId.CurvedWalk, at, 0.4f);
                trail.Add(in state);
            }

            // Warm-up: the list and the mesh reach their working size.
            for (int i = 0; i < 3; i++)
            {
                FlatTwinDisc.Build(twin, room, kernel, in state, trail);
                FlatTwinMesher.Write(twin, data, 0.125f, true);
            }

            const int Rounds = 50;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < Rounds; i++)
            {
                FlatTwinDisc.Build(twin, room, kernel, in state, trail);
                FlatTwinMesher.Write(twin, data, 0.125f, true);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0), "a rebuild allocates nothing");

            Stopwatch watch = Stopwatch.StartNew();
            for (int i = 0; i < Rounds; i++) FlatTwinDisc.Build(twin, room, kernel, in state, trail);
            double ms = watch.Elapsed.TotalMilliseconds / Rounds;
            TestContext.WriteLine("q = " + q + ": " + twin.Count + " entries, " + data.TriangleCount + " triangles, " + ms.ToString("0.000") + " ms per rebuild on this host");

            Assert.That(data.VertexCount, Is.LessThan(FlatTwinMesher.MaxVertices));
            Assert.That(data.TriangleCount, Is.LessThan(3000), "the map is a few thousand triangles at most");
            // At most ten rebuilds per second: a generous bound for a host under load.
            Assert.That(ms, Is.LessThan(5.0));
            AssertInsideDisc(twin, "q = " + q);
        }
    }
}

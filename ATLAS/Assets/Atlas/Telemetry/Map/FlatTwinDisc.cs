using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Telemetry
{
    /// <summary>
    /// The flat twin of the curved spaces S2 and S3 (Master Plan rule 6, task P1-D07): the {4,5} or {4,6} tiling
    /// round the walker as a Poincaré disc, in the same draw list as the other twins. The disc is centred on the
    /// walker's tile and keeps that tile's axes, so the picture stands still while the walker stays on a tile.
    /// Tile frames, edge steps, distances and the exponential map are lane B's kernel; the one formula of this
    /// file is the projection of a model point onto the disc. Engine-free. Main thread, as the tile graph is.
    /// A rebuild allocates nothing once the draw list has grown.
    /// </summary>
    public static class FlatTwinDisc
    {
        /// <summary>Tiles whose centre lies further than this from the centre of the walker's tile are left out:
        /// they are smaller than a line's width. Kernel units.</summary>
        public const double MapRho = 3.0;
        /// <summary>Lines per tile edge. An edge is a geodesic, drawn as this many chords.</summary>
        public const int EdgeSegments = 4;
        /// <summary>Lines per tile: four edges, edge 0 first, each from its corner at -45 degrees to the next.</summary>
        public const int LinesPerTile = 4 * EdgeSegments;
        /// <summary>Lines per straight side of the room's outline and free rectangle.</summary>
        public const int RoomSegments = 4;
        /// <summary>Half side of the view: the unit disc with a small margin.</summary>
        public const float ViewHalfExtent = 1.03f;
        /// <summary>No point is placed further out than this, so every point lies inside the unit disc.</summary>
        public const float RimLimit = 0.9995f;

        const string Label45 = "{4,5}", Label46 = "{4,6}";

        // Used only when the caller's tile list does not hold the walker's tile.
        static readonly List<VisibleTile> s_scratch = new List<VisibleTile>(128);

        /// <summary>True for the tilings this map draws: {4,5} and {4,6}.</summary>
        public static bool Supports(ITileGraph graph)
        {
            return graph != null && (graph.Q == 5 || graph.Q == 6) && graph.Kappa < 0.0;
        }

        /// <summary>
        /// Clears the twin and draws the disc from a State: its tile id, its tile-local transform and the heading
        /// of its head rotation. This is the call of the Spectator. False, with the twin left empty, when the
        /// kernel's graph is not a {4,5} or {4,6} tiling or the State's tile id is no tile of it.
        /// </summary>
        public static bool Build(FlatTwin twin, RoomModel room, IWalkerKernel kernel, in StateMessage state, FlatTwinTrail trail)
        {
            if (twin == null) throw new ArgumentNullException("twin");
            twin.Clear();
            ITileGraph graph = kernel != null ? kernel.Graph : null;
            if (!Supports(graph)) return false;

            WalkerPose pose;
            pose.Tile = new TileId(state.Tile0, state.Tile1, state.Tile2, state.Tile3, state.Tile4, state.Tile5);
            pose.Local = new Iso2(state.Xform0, state.Xform1, state.Xform2, state.Xform3, state.Xform4, state.Xform5,
                                  state.Xform6, state.Xform7, state.Xform8, Curvature.Hyperbolic);
            return Draw(twin, graph, in pose, kernel.Candidates, kernel.MetresPerUnit, room, trail, FlatTwinBuilder.Heading(in state));
        }

        /// <summary>
        /// Clears the twin and draws the disc from the kernel's own pose: the call of the phone's mini-map, which
        /// holds the kernel that is stepped. heading is the head's, in the room frame, radians from +Z towards +X.
        /// </summary>
        public static bool Build(FlatTwin twin, RoomModel room, IWalkerKernel kernel, FlatTwinTrail trail, float heading)
        {
            if (twin == null) throw new ArgumentNullException("twin");
            twin.Clear();
            ITileGraph graph = kernel != null ? kernel.Graph : null;
            if (!Supports(graph)) return false;

            WalkerPose pose = kernel.Pose;
            return Draw(twin, graph, in pose, kernel.Candidates, kernel.MetresPerUnit, room, trail, heading);
        }

        /// <summary>
        /// Adds the disc to the twin without clearing it, and sets the view to the unit disc. tiles is a visible
        /// set of the graph round any tile (the kernel's Candidates); it is read, never changed. When it is null
        /// or does not hold the walker's tile, the graph is asked for the set round that tile. room and trail
        /// may be null. False, with nothing added, when the graph or the pose cannot be drawn.
        /// </summary>
        /// <remarks>
        /// The order of the list is fixed. First every tile within <see cref="MapRho"/>, in the order of tiles,
        /// <see cref="LinesPerTile"/> lines each in Cell. Then, when tile zero is among them, a dot in Outline on
        /// its centre (the room's anchor) and the room's outline and free rectangle. Then the walked tile
        /// sequence in Trail, the walker's tile again in Highlight, and the walker's arrow.
        /// </remarks>
        public static bool Draw(FlatTwin twin, ITileGraph graph, in WalkerPose pose, List<VisibleTile> tiles,
                                double metresPerUnit, RoomModel room, FlatTwinTrail trail, float heading)
        {
            if (twin == null) throw new ArgumentNullException("twin");
            if (!Supports(graph) || pose.Tile.IsNone) return false;

            int centre = IndexOf(tiles, pose.Tile);
            if (centre < 0)
            {
                // A State may name a tile the local kernel is not standing near (the Station, or a late State).
                try { graph.VisibleSet(pose.Tile, MapRho, s_scratch); }
                catch (ArgumentException) { return false; }         // not a tile of this graph
                catch (OverflowException) { return false; }         // beyond the range of the tile ids
                catch (InvalidOperationException) { return false; } // q = 6 beyond its precision
                tiles = s_scratch;
                centre = IndexOf(tiles, pose.Tile);
                if (centre < 0) return false;
            }

            // The frame of the list's own centre tile -> the frame of the walker's tile, which is the disc's.
            Iso2 toView = tiles[centre].F.Inverse();

            // U. A State that carries no usable transform (S3 sends the identity) puts the walker on the tile's centre.
            Iso2 local = new Iso2(pose.Local.M, Curvature.Hyperbolic);
            if (!(local.Defect() < 1e-6)) local = Iso2.IdentityFor(Curvature.Hyperbolic);

            // One tile's outline in its own frame: edge e is the geodesic at the in-radius along e * 90 degrees,
            // at right angles to that axis, between the corners at the circumradius.
            Span<double3> outline = stackalloc double3[LinesPerTile];
            double inRadius = 0.5 * graph.CentreSpacing, circumRadius = graph.CircumRadius;
            double halfSide = 0.5 * Geometry2.Distance(
                Geometry2.Exp(circumRadius * Direction(-0.25 * Math.PI), Curvature.Hyperbolic),
                Geometry2.Exp(circumRadius * Direction(0.25 * Math.PI), Curvature.Hyperbolic), Curvature.Hyperbolic);
            for (int e = 0; e < 4; e++)
            {
                double2 axis = Direction(0.5 * Math.PI * e), along = new double2(-axis.y, axis.x);
                Iso2 toEdge = Iso2.Boost(inRadius * axis, Curvature.Hyperbolic);
                for (int s = 0; s < EdgeSegments; s++)
                {
                    double t = halfSide * (2.0 * s / EdgeSegments - 1.0);
                    outline[e * EdgeSegments + s] = toEdge.Apply(Geometry2.Exp(t * along, Curvature.Hyperbolic));
                }
            }

            double keep = Math.Cosh(MapRho) + KernelConstants.RadiusEpsilon;      // w of a centre is cosh of its distance
            Iso2 zeroView = default(Iso2);
            bool hasZero = false;
            int count = tiles.Count;
            for (int k = 0; k < count; k++)
            {
                VisibleTile tile = tiles[k];
                Iso2 view = toView * tile.F;
                if (!(view.Position.z <= keep)) continue;
                Tile(twin, in view, outline, FlatTwinInk.Cell);
                if (tile.Id == TileId.Zero) { zeroView = view; hasZero = true; }
            }

            // The room sits on tile zero: its centre is the tile's centre and its metres are the tile's chart.
            if (hasZero)
            {
                float hx, hz;
                Project(zeroView.Position, out hx, out hz);
                twin.Dot(hx, hz, FlatTwinBuilder.DotShare * ViewHalfExtent, FlatTwinInk.Outline);
                if (room != null && metresPerUnit > 0.0 && !double.IsInfinity(metresPerUnit))
                {
                    double perMetre = 1.0 / metresPerUnit;
                    int n = room.Outline.Length;
                    for (int i = 0; i < n; i++)
                    {
                        int j = (i + 1) % n;
                        RoomSide(twin, in zeroView, perMetre, room.Outline[i].x, room.Outline[i].y,
                                 room.Outline[j].x, room.Outline[j].y, FlatTwinInk.Outline);
                    }
                    float hw = 0.5f * room.FreeRect.Wf, hd = 0.5f * room.FreeRect.Df;
                    if (hw > 0f && hd > 0f)
                    {
                        RoomSide(twin, in zeroView, perMetre, -hw, -hd, hw, -hd, FlatTwinInk.Grid);
                        RoomSide(twin, in zeroView, perMetre, hw, -hd, hw, hd, FlatTwinInk.Grid);
                        RoomSide(twin, in zeroView, perMetre, hw, hd, -hw, hd, FlatTwinInk.Grid);
                        RoomSide(twin, in zeroView, perMetre, -hw, hd, -hw, -hd, FlatTwinInk.Grid);
                    }
                }
            }

            // The walked tile sequence, oldest first, as a line through the tile centres that are on the map.
            if (trail != null)
            {
                bool have = false;
                float px = 0f, pz = 0f;
                for (int k = 0; k < trail.TileCount; k++)
                {
                    int at = IndexOf(tiles, trail.GetTile(k));
                    if (at < 0) { have = false; continue; }
                    double3 c = (toView * tiles[at].F).Position;
                    if (!(c.z <= keep)) { have = false; continue; }
                    float x, z;
                    Project(c, out x, out z);
                    if (have) twin.Line(px, pz, x, z, FlatTwinInk.Trail);
                    px = x;
                    pz = z;
                    have = true;
                }
            }

            Iso2 own = toView * tiles[centre].F;
            Tile(twin, in own, outline, FlatTwinInk.Highlight);

            // The walker: U's position, and the room-frame heading carried by U's two axes. The walker frame has
            // the room's x and z axes, not the head's yaw (kernel contract, section 4.5).
            double3 p = local.Position;
            double sin = Math.Sin(heading), cos = Math.Cos(heading);
            double3 forward = sin * local.M.c0 + cos * local.M.c1;
            // The projection's derivative along forward, up to a positive factor.
            double dx = forward.x * (1.0 + p.z) - p.x * forward.z, dz = forward.y * (1.0 + p.z) - p.y * forward.z;
            float wx, wz;
            Project(p, out wx, out wz);
            twin.Arrow(wx, wz, dx * dx + dz * dz > 1e-24 ? (float)Math.Atan2(dx, dz) : heading, FlatTwinInk.Walker);

            twin.SetView(0f, 0f, ViewHalfExtent, true);
            twin.Label(0f, -0.92f, graph.Q == 5 ? Label45 : Label46);
            return true;
        }

        /// <summary>
        /// A point of the hyperboloid model, (x, z, w) as the kernel stores it, on the Poincaré disc:
        /// (x, z) / (1 + w). A point at distance d from the base point lands at radius tanh(d / 2).
        /// </summary>
        public static void Project(double3 modelPoint, out float x, out float z)
        {
            double d = 1.0 + modelPoint.z;
            double px = modelPoint.x / d, pz = modelPoint.y / d;
            double r2 = px * px + pz * pz;
            if (!(r2 <= (double)RimLimit * RimLimit))
            {
                // Rounding at the rim, or a point that is not on the model at all.
                double r = Math.Sqrt(r2);
                if (!(r > 0.0) || double.IsInfinity(r)) { px = 0.0; pz = 0.0; }
                else { px *= RimLimit / r; pz *= RimLimit / r; }
            }
            x = (float)px;
            z = (float)pz;
        }

        static void Tile(FlatTwin twin, in Iso2 view, ReadOnlySpan<double3> outline, FlatTwinInk ink)
        {
            float fx, fz;
            Project(view.Apply(outline[0]), out fx, out fz);
            float px = fx, pz = fz;
            for (int i = 1; i < outline.Length; i++)
            {
                float x, z;
                Project(view.Apply(outline[i]), out x, out z);
                twin.Line(px, pz, x, z, ink);
                px = x;
                pz = z;
            }
            twin.Line(px, pz, fx, fz, ink);
        }

        // A straight side of the room, given in metres on tile zero's chart, bent by the exponential map.
        static void RoomSide(FlatTwin twin, in Iso2 zeroView, double perMetre, float ax, float az, float bx, float bz, FlatTwinInk ink)
        {
            float px = 0f, pz = 0f;
            for (int s = 0; s <= RoomSegments; s++)
            {
                double t = (double)s / RoomSegments;
                var chart = new double2(ax + (bx - ax) * t, az + (bz - az) * t) * perMetre;
                float x, z;
                Project(zeroView.Apply(Geometry2.Exp(chart, Curvature.Hyperbolic)), out x, out z);
                if (s > 0) twin.Line(px, pz, x, z, ink);
                px = x;
                pz = z;
            }
        }

        static int IndexOf(List<VisibleTile> tiles, TileId id)
        {
            if (tiles == null) return -1;
            int count = tiles.Count;
            for (int k = 0; k < count; k++)
                if (tiles[k].Id == id) return k;
            return -1;
        }

        static double2 Direction(double angle) { return new double2(Math.Cos(angle), Math.Sin(angle)); }
    }
}

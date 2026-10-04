using System;
using Atlas.Rendering;
using Atlas.Rendering.Room;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.Glued
{
    /// <summary>Where the walker's copy stands in another cell. Engine-free.</summary>
    public static class GluedAvatarPose
    {
        /// <summary>
        /// The heading of a head rotation about +Y (Unity's sense, radians): where the walker faces on the
        /// floor. Looking straight up or down the top of the head takes over, so the heading never spins.
        /// Returns `fallback` when the rotation gives no direction at all.
        /// </summary>
        public static float Heading(quaternion head, float fallback = 0f)
        {
            if (!(math.lengthsq(head.value) > 1e-6f)) return fallback;      // a pose that was never filled in
            float3 forward = math.mul(head, new float3(0f, 0f, 1f));
            float3 up = math.mul(head, new float3(0f, 1f, 0f));
            float2 flat = forward.xz - forward.y * up.xz;
            if (!(math.lengthsq(flat) > 1e-8f)) return fallback;
            return math.atan2(flat.x, flat.y);
        }

        /// <summary>
        /// The copy of the walker in a cell: the image of the head position under the gluing, the heading
        /// (negated in a mirrored copy) and whether the copy is mirrored in X.
        /// </summary>
        public static void InCell(in GluedLattice lattice, GluedCell walker, GluedCell cell, float3 headPosition,
                                  float heading, out float3 position, out float copyHeading, out bool mirrored)
        {
            position = lattice.ImageOf(walker, cell, headPosition, out mirrored);
            copyHeading = mirrored ? -heading : heading;
        }
    }

    /// <summary>
    /// The wrap-walk prompt (Master Plan task P1-A15): chevrons on the nearest cell edge the walker can really
    /// cross, shown until the first crossing. In standing mode the space runs in place and never prompts.
    /// Engine-free.
    /// </summary>
    public static class GluedPrompt
    {
        /// <summary>The chevrons stay this far inside the free rectangle, metres.</summary>
        public const float Margin = 0.3f;

        /// <summary>True when the room has a cell edge inside its free floor and the walker is not standing.</summary>
        public static bool Available(in GluedLattice lattice, bool standing)
        {
            return !standing && (lattice.Nx > 1 || lattice.Nz > 1);
        }

        /// <summary>True while the prompt is to be drawn.</summary>
        public static bool Shown(in GluedLattice lattice, bool standing, int crossings)
        {
            return crossings == 0 && Available(in lattice, standing);
        }

        /// <summary>
        /// Where the chevrons lie: on the cell edge inside the free rectangle that is nearest the walker,
        /// level with the walker, pointing away from the walker's side. False when the room has no such edge.
        /// </summary>
        /// <param name="heading">About +Y, Unity's sense: 0 points along +Z.</param>
        public static bool Place(in GluedLattice lattice, float2 walker, out float2 position, out float heading)
        {
            position = float2.zero;
            heading = 0f;
            double bestX = NearestInner(walker.x, lattice.Wf, lattice.SizeX, lattice.Nx, out double lineX);
            double bestZ = NearestInner(walker.y, lattice.Df, lattice.SizeZ, lattice.Nz, out double lineZ);
            if (double.IsPositiveInfinity(bestX) && double.IsPositiveInfinity(bestZ)) return false;

            float limitX = math.max(0f, (float)(0.5 * lattice.Wf) - Margin);
            float limitZ = math.max(0f, (float)(0.5 * lattice.Df) - Margin);
            if (bestX <= bestZ)
            {
                position = new float2((float)lineX, math.clamp(walker.y, -limitZ, limitZ));
                heading = lineX >= walker.x ? 0.5f * math.PI : -0.5f * math.PI;
            }
            else
            {
                position = new float2(math.clamp(walker.x, -limitX, limitX), (float)lineZ);
                heading = lineZ >= walker.y ? 0f : math.PI;
            }
            return true;
        }

        // Distance to the nearest of the n - 1 cell edges strictly inside a side of the free rectangle.
        static double NearestInner(double value, double side, double size, int n, out double line)
        {
            line = 0.0;
            if (n < 2) return double.PositiveInfinity;
            int k = (int)Math.Floor((value + 0.5 * side) / size + 0.5);
            k = Math.Min(Math.Max(k, 1), n - 1);
            line = -0.5 * side + k * size;
            return Math.Abs(value - line);
        }
    }

    /// <summary>What S1 costs to draw. Both eyes draw the same content, so a frame is twice an eye.</summary>
    public struct GluedDrawCost
    {
        public int DrawCallsPerEye;
        public int TrianglesPerEye;

        public int DrawCallsPerFrame { get { return 2 * DrawCallsPerEye; } }
        public int TrianglesPerFrame { get { return 2 * TrianglesPerEye; } }
    }

    /// <summary>
    /// Draw-call and triangle accounting of S1 (Master Plan section 8.5 and task P1-A09). Engine-free: it
    /// counts what GluedSpace builds, with the same builders.
    /// </summary>
    /// <remarks>
    /// The batching: every cell copy is one mesh and one draw call per eye, and so is every avatar; all cells
    /// share one pooled material and all avatars another, so the lattice costs two set-passes. No frustum
    /// culling is assumed: the numbers are the worst case with every copy in view.
    /// </remarks>
    public static class GluedBudget
    {
        /// <summary>Cells and avatars alone at 5 x 5, per frame, both eyes.</summary>
        public const int LatticeDrawCalls = 110, LatticeTriangles = 110000;
        /// <summary>Cells and avatars alone for the 3-torus, per frame, both eyes.</summary>
        public const int Torus3DrawCalls = 120, Torus3Triangles = 120000;
        /// <summary>The S1 rows of the per-space table: with the room.</summary>
        public const int SpaceDrawCalls = 150, SpaceTriangles = 250000;
        public const int SpaceTorus3DrawCalls = 160, SpaceTorus3Triangles = 250000;

        /// <summary>
        /// The info card and the gluing sign at their worst, per eye: the plate with the three gluing buttons, the plan
        /// with the walker's dot, three blocks of text, the button words (one text), the "seen it" button, one gaze ring
        /// and one dwell bar. The triangles are counted generously for the text (two per glyph, about 320 glyphs).
        /// </summary>
        public const int CardDrawCallsPerEye = 9, CardTrianglesPerEye = 1500;

        /// <summary>Triangles of the cell mesh for this cell size, with the kit landmark when one is given.</summary>
        public static int CellTriangles(float sizeX, float sizeZ, MeshData landmark = null)
        {
            var scratch = new AtlasMeshBuilder(1024, 4096);
            GluedCellMesh.Build(sizeX, sizeZ, scratch, landmark);
            return scratch.TriangleCount;
        }

        /// <summary>Triangles of the avatar mesh, the kit avatar when one is given.</summary>
        public static int AvatarTriangles(MeshData kit = null)
        {
            var scratch = new AtlasMeshBuilder(512, 2048);
            GluedAvatarMesh.Build(scratch, kit);
            return scratch.TriangleCount;
        }

        public static int PromptTriangles()
        {
            var scratch = new AtlasMeshBuilder(64, 128);
            GluedPromptMesh.Build(scratch);
            return scratch.TriangleCount;
        }

        /// <summary>The lattice from counts: one draw call per cell, one per avatar (every cell but the walker's own) and one for the prompt.</summary>
        public static GluedDrawCost Lattice(GluedLayout layout, int copies, int cellTriangles, int avatarTriangles,
                                            bool avatars = true, int promptTriangles = 0)
        {
            int cells = GluedRules.CellCount(layout, copies);
            int others = avatars ? cells - 1 : 0;
            var cost = new GluedDrawCost
            {
                DrawCallsPerEye = cells + others,
                TrianglesPerEye = cells * cellTriangles + others * avatarTriangles,
            };
            if (promptTriangles > 0)
            {
                cost.DrawCallsPerEye++;
                cost.TrianglesPerEye += promptTriangles;
            }
            return cost;
        }

        /// <summary>The lattice of a room as GluedSpace draws it: cells, avatars and, where the room has one, the prompt.</summary>
        public static GluedDrawCost Lattice(RoomModel room, GluedLayout layout, int copies, bool avatars = true)
        {
            var lattice = new GluedLattice(room, layout);
            bool prompt = GluedPrompt.Available(in lattice, room.Standing);
            return Lattice(layout, copies, CellTriangles((float)lattice.SizeX, (float)lattice.SizeZ), AvatarTriangles(),
                           avatars, prompt ? PromptTriangles() : 0);
        }

        /// <summary>
        /// The whole space: the lattice plus the room drawn once, walls as kerbs, its own floor hidden, and the info card
        /// with the gluing sign.
        /// </summary>
        public static GluedDrawCost Space(RoomModel room, GluedLayout layout, int copies, bool avatars = true)
        {
            GluedDrawCost cost = Lattice(room, layout, copies, avatars);
            RoomDrawCost shell = RoomBudget.Estimate(room, WallMode.Kerbs, false, true);
            cost.DrawCallsPerEye += shell.DrawCallsPerEye + CardDrawCallsPerEye;
            cost.TrianglesPerEye += shell.TrianglesPerEye + CardTrianglesPerEye;
            return cost;
        }

        /// <summary>True when cells and avatars alone are inside the figures of task P1-A09 for the layout.</summary>
        public static bool LatticeFits(GluedLayout layout, in GluedDrawCost cost)
        {
            return layout == GluedLayout.Torus3
                ? cost.DrawCallsPerFrame <= Torus3DrawCalls && cost.TrianglesPerFrame <= Torus3Triangles
                : cost.DrawCallsPerFrame <= LatticeDrawCalls && cost.TrianglesPerFrame <= LatticeTriangles;
        }

        /// <summary>True when the space with the room is inside its row of the per-space table.</summary>
        public static bool SpaceFits(GluedLayout layout, in GluedDrawCost cost)
        {
            return layout == GluedLayout.Torus3
                ? cost.DrawCallsPerFrame <= SpaceTorus3DrawCalls && cost.TrianglesPerFrame <= SpaceTorus3Triangles
                : cost.DrawCallsPerFrame <= SpaceDrawCalls && cost.TrianglesPerFrame <= SpaceTriangles;
        }
    }
}

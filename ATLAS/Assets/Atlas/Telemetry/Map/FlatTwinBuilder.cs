using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;

namespace Atlas.Telemetry
{
    /// <summary>
    /// The built-in flat twins (Master Plan rule 6, task P1-D07; lane D contract, section 5.9). Each needs only
    /// the RoomModel, the kernel and the newest State, so the headset and the Station draw the same picture.
    /// Engine-free; a rebuild allocates nothing.
    /// </summary>
    public static class FlatTwinBuilder
    {
        /// <summary>Radius of a dot that stands for a copy of the walker, as a share of the view's half side.</summary>
        public const float DotShare = 0.03f;
        /// <summary>The Slicer's cut runs over plus and minus this much, model units (lane A contract, section 14.3).</summary>
        public const float SlicerCutRange = 1.1f;
        /// <summary>Glued copies per side are 3, 5 or 7 (lane A contract, section 14.1).</summary>
        public const int GluedMinCopies = 3, GluedMaxCopies = 7;
        /// <summary>Layout values of State.p[3] in Glued and in Impossible Rooms.</summary>
        public const int GluedKlein = 1, GluedTorus3 = 2, PortalLoop = 1;
        public const int MaxPortalRooms = 16;

        const string LabelTorus = "torus", LabelKlein = "Klein bottle", LabelTorus3 = "3-torus";
        const string LabelCut = "w", LabelNoRoom = "no room", LabelNoGraph = "no tile graph";
        static readonly string[] s_roomOf = BuildRoomLabels();

        /// <summary>Faces of the Small Planet's cube; State.tileId's first word names the walker's face, 0 to 5.</summary>
        public const int CubeFaces = 6;
        // The unfolded cube as a cross of unit squares: face 0 (the room's face) in the middle, faces 1 to 4
        // round it on +X, +Z, -X and -Z, face 5 (the far side) below face 4. Column and row of each face.
        static readonly sbyte[] s_netColumn = { 0, 1, 0, -1, 0, 0 };
        static readonly sbyte[] s_netRow = { 0, 0, 1, 0, -1, -2 };
        static readonly string[] s_faceOf = { "face 1 of 6", "face 2 of 6", "face 3 of 6", "face 4 of 6", "face 5 of 6", "face 6 of 6" };

        /// <summary>
        /// Clears the twin and draws the one of the State's space. room and kernel may be null: what needs them
        /// is then left out. trail may be null.
        /// </summary>
        public static void Build(FlatTwin twin, RoomModel room, IWalkerKernel kernel, in StateMessage state, FlatTwinTrail trail)
        {
            Build(twin, room, null, kernel, in state, trail);
        }

        /// <summary>
        /// As above. While no RoomModel is held, the room is drawn from the phone's <c>RoomOutline</c> message,
        /// which the Station gets before it has fetched the room.
        /// </summary>
        public static void Build(FlatTwin twin, RoomModel room, RoomOutlineMessage outline, IWalkerKernel kernel,
                                 in StateMessage state, FlatTwinTrail trail)
        {
            if (twin == null) throw new ArgumentNullException("twin");
            twin.Clear();
            switch (state.SpaceId)
            {
                case (byte)SpaceId.Glued:
                    Glued(twin, room, outline, in state);
                    break;
                case (byte)SpaceId.CurvedWalk:
                    // Until the curvature is on, Curved Walk is the room as it is.
                    if (kernel != null && FlatTwinDisc.Supports(kernel.Graph) && !(state.P4 < 0f))
                        FloorPlan(twin, room, outline, in state, trail, 0.08f);
                    else Tiles(twin, room, kernel, in state, trail);
                    break;
                case (byte)SpaceId.AlignedCells:
                    Cells(twin, in state, trail);
                    break;
                case (byte)SpaceId.SmallPlanet:
                    CubeNet(twin, in state, trail);
                    break;
                case (byte)SpaceId.Portal:
                    FloorPlan(twin, room, outline, in state, trail, 0.30f);
                    PortalStrip(twin, in state);
                    break;
                case (byte)SpaceId.Slicer:
                    FloorPlan(twin, room, outline, in state, trail, 0.30f);
                    SlicerStrip(twin, in state);
                    break;
                default:
                    // Home, and no space at all (onboarding, scan): the room as it is.
                    FloorPlan(twin, room, outline, in state, trail, 0.08f);
                    break;
            }
        }

        /// <summary>Clears the twin and lets a space draw its own (lane D contract, section 5.9, last row of the table).</summary>
        public static void Build(FlatTwin twin, IFlatTwinSource source)
        {
            if (twin == null) throw new ArgumentNullException("twin");
            twin.Clear();
            if (source != null) source.WriteFlatTwin(twin);
        }

        /// <summary>The heading of a head rotation on the floor: radians from +Z towards +X.</summary>
        public static float Heading(in StateMessage state)
        {
            float x = state.HeadRotX, y = state.HeadRotY, z = state.HeadRotZ, w = state.HeadRotW;
            // The forward axis (0, 0, 1) turned by the quaternion, projected on the floor.
            float fx = 2f * (x * z + w * y);
            float fz = 1f - 2f * (x * x + y * y);
            if (fx * fx + fz * fz < 1e-8f) return 0f;      // looking straight up or down
            return (float)Math.Atan2(fx, fz);
        }

        // ---- S0, S5, S6 and no space: the floor plan ---------------------------------------------------------

        static void FloorPlan(FlatTwin twin, RoomModel room, RoomOutlineMessage outline, in StateMessage state,
                              FlatTwinTrail trail, float topStrip)
        {
            bool drawn = DrawRoom(twin, room, outline, true);
            if (!drawn)
            {
                // Nothing is known about the room yet: a metre square round the walker keeps the view sane.
                twin.Rect(-1f, -1f, 1f, 1f, FlatTwinInk.Grid);
                twin.Label(0f, -0.8f, LabelNoRoom);
            }
            float minX, minZ, maxX, maxZ;
            twin.TryGetBounds(out minX, out minZ, out maxX, out maxZ);
            float half = 0.5f * Math.Max(maxX - minX, maxZ - minZ);
            float cx = 0.5f * (minX + maxX), cz = 0.5f * (minZ + maxZ);
            // A space with a strip gets room for it above the plan.
            twin.SetView(cx, cz + half * topStrip, half * (1.08f + topStrip), false);

            DrawTrail(twin, trail);
            twin.Arrow(state.HeadPosX, state.HeadPosZ, Heading(in state), FlatTwinInk.Walker);
        }

        // Outline, free rectangle, lap square and object boxes. False when neither source holds a room.
        static bool DrawRoom(FlatTwin twin, RoomModel room, RoomOutlineMessage outline, bool withLap)
        {
            if (room != null)
            {
                int n = room.Outline.Length;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    twin.Line(room.Outline[i].x, room.Outline[i].y, room.Outline[j].x, room.Outline[j].y, FlatTwinInk.Outline);
                }
                float hw = 0.5f * room.FreeRect.Wf, hd = 0.5f * room.FreeRect.Df;
                twin.Rect(-hw, -hd, hw, hd, FlatTwinInk.Grid);
                if (withLap && !room.Standing && room.LapSide > 0f)
                {
                    float hs = 0.5f * room.LapSide;
                    twin.Rect(-hs, -hs, hs, hs, FlatTwinInk.Grid);
                }
                for (int i = 0; i < room.Objects.Length; i++)
                {
                    RoomObject o = room.Objects[i];
                    if (o != null) twin.Box(o.Base.x, o.Base.z, o.Size.x, o.Size.z, o.Yaw, FlatTwinInk.Object);
                }
                return n > 0 || room.FreeRect.Wf > 0f;
            }
            if (outline != null && outline.PointCount > 0)
            {
                int n = outline.PointCount;
                float[] p = outline.PointsXZ;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    twin.Line(p[2 * i], p[2 * i + 1], p[2 * j], p[2 * j + 1], FlatTwinInk.Outline);
                }
                if (outline.RectW > 0f && outline.RectD > 0f)
                    twin.Box(outline.RectCx, outline.RectCz, outline.RectW, outline.RectD, outline.RectYaw, FlatTwinInk.Grid);
                for (int i = 0; i < outline.BoxCount; i++)
                {
                    RoomOutlineBox b = outline.Boxes[i];
                    twin.Box(b.Cx, b.Cz, b.W, b.D, b.Yaw, FlatTwinInk.Object);
                }
                return true;
            }
            return false;
        }

        static void DrawTrail(FlatTwin twin, FlatTwinTrail trail)
        {
            if (trail == null) return;
            int n = trail.PositionCount;
            float px = 0f, pz = 0f;
            for (int i = 0; i < n; i++)
            {
                float x, z;
                trail.GetPosition(i, out x, out z);
                if (i > 0) twin.Line(px, pz, x, z, FlatTwinInk.Trail);
                px = x;
                pz = z;
            }
        }

        // ---- S5: "room k of n" and the room graph ------------------------------------------------------------

        static void PortalStrip(FlatTwin twin, in StateMessage state)
        {
            int count = Clamp((int)Math.Round(Finite(state.P4)), 0, MaxPortalRooms);
            if (count <= 0) return;
            int current = Clamp((int)state.Tile0, 0, count - 1);
            bool loop = (int)Math.Round(Finite(state.P3)) == PortalLoop;

            float half = twin.HalfExtent, cx = twin.CentreX;
            float z = twin.CentreZ + half * 0.80f;
            float span = half * 1.4f;
            float step = count > 1 ? span / (count - 1) : 0f;
            float x0 = cx - 0.5f * step * (count - 1);
            // Rooms as nodes, doorways as edges. A loop comes back to its first room.
            for (int k = 0; k + 1 < count; k++) twin.Line(x0 + k * step, z, x0 + (k + 1) * step, z, FlatTwinInk.Portal);
            if (loop && count > 2)
            {
                float lift = half * 0.07f;
                twin.Line(x0, z, x0, z + lift, FlatTwinInk.Portal);
                twin.Line(x0, z + lift, x0 + (count - 1) * step, z + lift, FlatTwinInk.Portal);
                twin.Line(x0 + (count - 1) * step, z + lift, x0 + (count - 1) * step, z, FlatTwinInk.Portal);
            }
            for (int k = 0; k < count; k++)
                twin.Dot(x0 + k * step, z, half * DotShare, k == current ? FlatTwinInk.Highlight : FlatTwinInk.Portal);
            twin.Label(cx, twin.CentreZ + half * 0.62f, s_roomOf[current * (MaxPortalRooms + 1) + count]);
        }

        static string[] BuildRoomLabels()
        {
            // Every "room k of n" up front, so the map never builds a string while it is shown.
            var labels = new string[MaxPortalRooms * (MaxPortalRooms + 1)];
            for (int k = 0; k < MaxPortalRooms; k++)
                for (int n = 0; n <= MaxPortalRooms; n++)
                    labels[k * (MaxPortalRooms + 1) + n] = "room " + (k + 1) + " of " + n;
            return labels;
        }

        // ---- S6: the cut on its range and the three angles ---------------------------------------------------

        static void SlicerStrip(FlatTwin twin, in StateMessage state)
        {
            float half = twin.HalfExtent, cx = twin.CentreX;
            float z = twin.CentreZ + half * 0.82f;
            float reach = half * 0.42f;
            float left = cx - half * 0.80f, right = left + 2f * reach;
            float tick = half * 0.04f;
            twin.Line(left, z, right, z, FlatTwinInk.Grid);
            twin.Line(left, z - tick, left, z + tick, FlatTwinInk.Grid);
            twin.Line(right, z - tick, right, z + tick, FlatTwinInk.Grid);
            twin.Line(left + reach, z - tick, left + reach, z + tick, FlatTwinInk.Grid);
            float w = Math.Max(-SlicerCutRange, Math.Min(SlicerCutRange, Finite(state.P3)));
            twin.Dot(left + reach + reach * w / SlicerCutRange, z, half * DotShare, FlatTwinInk.Highlight);
            twin.Label(left + reach, twin.CentreZ + half * 0.66f, LabelCut);

            // XW, YW and ZW as three small dials: a hand turned by the angle, from straight up.
            float dial = half * 0.09f;
            Dial(twin, cx + half * 0.28f, z, dial, Finite(state.P4));
            Dial(twin, cx + half * 0.54f, z, dial, Finite(state.P5));
            Dial(twin, cx + half * 0.80f, z, dial, Finite(state.P6));
        }

        static void Dial(FlatTwin twin, float x, float z, float radius, float angle)
        {
            const int Sides = 8;
            float px = x, pz = z + radius;
            for (int i = 1; i <= Sides; i++)
            {
                double a = 2.0 * Math.PI * i / Sides;
                float qx = x + radius * (float)Math.Sin(a), qz = z + radius * (float)Math.Cos(a);
                twin.Line(px, pz, qx, qz, FlatTwinInk.Cell);
                px = qx;
                pz = qz;
            }
            twin.Line(x, z, x + radius * (float)Math.Sin(angle), z + radius * (float)Math.Cos(angle), FlatTwinInk.Highlight);
        }

        // ---- S1: the lattice plan ----------------------------------------------------------------------------

        static void Glued(FlatTwin twin, RoomModel room, RoomOutlineMessage outline, in StateMessage state)
        {
            float wf, df;
            if (room != null) { wf = room.FreeRect.Wf; df = room.FreeRect.Df; }
            else if (outline != null && outline.RectW > 0f) { wf = outline.RectW; df = outline.RectD; }
            else { wf = 0f; df = 0f; }

            // The cell size the phone drew with; before the first State of the space, the room's own grid.
            float sx = Finite(state.P5), sz = Finite(state.P6);
            if (!(sx > 0.05f) || !(sz > 0.05f))
            {
                if (room != null && room.Glued.SizeX > 0.0 && room.Glued.SizeZ > 0.0)
                {
                    sx = (float)room.Glued.SizeX;
                    sz = (float)room.Glued.SizeZ;
                }
                else if (wf > 0f && df > 0f) { sx = wf; sz = df; }
                else { sx = 1.5f; sz = 1.5f; }
            }
            if (!(wf > 0f) || !(df > 0f)) { wf = sx; df = sz; }

            int mode = (int)Math.Round(Finite(state.P3));
            int n = Clamp((int)Math.Round(Finite(state.P4)), GluedMinCopies, GluedMaxCopies);
            if ((n & 1) == 0) n--;
            if (mode == GluedTorus3) n = GluedMinCopies;
            int reach = n / 2;

            // The lattice is fixed to the floor: cell (0, 0) has its low corner at the free rectangle's.
            float originX = -0.5f * wf, originZ = -0.5f * df;
            long col = ClampCell(state.Tile0), row = ClampCell(state.Tile1);
            int nx = Math.Max(1, (int)Math.Round(wf / sx)), nz = Math.Max(1, (int)Math.Round(df / sz));

            for (int dr = -reach; dr <= reach; dr++)
            {
                for (int dc = -reach; dc <= reach; dc++)
                {
                    float x0 = originX + (col + dc) * sx, z0 = originZ + (row + dr) * sz;
                    twin.Rect(x0, z0, x0 + sx, z0 + sz, FlatTwinInk.Cell);
                    // A mirrored row of the Klein bottle: a stroke across each of its cells.
                    if (mode == GluedKlein && ((row + dr) & 1) != 0)
                        twin.Line(x0 + 0.2f * sx, z0 + 0.2f * sz, x0 + 0.8f * sx, z0 + 0.8f * sz, FlatTwinInk.Portal);
                }
            }

            // The real room: the cells the walker can stand in, and the fundamental cell among them.
            twin.Rect(originX, originZ, originX + nx * sx, originZ + nz * sz, FlatTwinInk.Outline);
            twin.Rect(originX, originZ, originX + sx, originZ + sz, FlatTwinInk.Highlight);

            float hx = state.HeadPosX, hz = state.HeadPosZ;
            float cellCentreX = originX + (col + 0.5f) * sx, cellCentreZ = originZ + (row + 0.5f) * sz;
            float half = 0.5f * n * Math.Max(sx, sz) * 1.05f;
            twin.SetView(cellCentreX, cellCentreZ, half, false);

            // The walker's copies: one per drawn cell, mirrored in X where the row parity differs (Klein).
            float ux = hx - cellCentreX, uz = hz - cellCentreZ;
            for (int dr = -reach; dr <= reach; dr++)
            {
                for (int dc = -reach; dc <= reach; dc++)
                {
                    if (dc == 0 && dr == 0) continue;
                    bool flip = mode == GluedKlein && (dr & 1) != 0;
                    twin.Dot(cellCentreX + dc * sx + (flip ? -ux : ux), cellCentreZ + dr * sz + uz, half * DotShare, FlatTwinInk.Walker);
                }
            }
            twin.Arrow(hx, hz, Heading(in state), FlatTwinInk.Walker);
            twin.Label(cellCentreX, cellCentreZ - half * 0.90f,
                mode == GluedKlein ? LabelKlein : mode == GluedTorus3 ? LabelTorus3 : LabelTorus);
        }

        // ---- S4: the unfolded cube net -----------------------------------------------------------------------

        /// <summary>The centre of a face of the net, in the net's own units (a face is one unit across).</summary>
        public static void CubeNetCentre(int face, out float x, out float z)
        {
            int f = Clamp(face, 0, CubeFaces - 1);
            x = s_netColumn[f];
            z = s_netRow[f];
        }

        // Six faces, the walker's highlighted with a dot in it, and the visited faces joined in the order they
        // were walked. Only the face index is read: where the walker stands inside a face is the kernel's
        // business (lane B), and nothing curved is computed here.
        static void CubeNet(FlatTwin twin, in StateMessage state, FlatTwinTrail trail)
        {
            for (int f = 0; f < CubeFaces; f++)
                twin.Rect(s_netColumn[f] - 0.5f, s_netRow[f] - 0.5f, s_netColumn[f] + 0.5f, s_netRow[f] + 0.5f, FlatTwinInk.Cell);
            // The cross spans three faces across and four down.
            const float half = 2.15f;
            twin.SetView(0f, -0.5f, half, false);

            if (trail != null)
            {
                bool have = false;
                float px = 0f, pz = 0f;
                for (int k = 0; k < trail.TileCount; k++)
                {
                    long face = trail.GetTile(k).Xu;
                    if (face < 0 || face >= CubeFaces) { have = false; continue; }
                    float x = s_netColumn[face], z = s_netRow[face];
                    if (have) twin.Line(px, pz, x, z, FlatTwinInk.Trail);
                    px = x;
                    pz = z;
                    have = true;
                }
            }

            long walker = state.Tile0;
            if (walker < 0 || walker >= CubeFaces) return;      // a face this build does not know: the bare net
            float cx = s_netColumn[walker], cz = s_netRow[walker];
            twin.Rect(cx - 0.5f, cz - 0.5f, cx + 0.5f, cz + 0.5f, FlatTwinInk.Highlight);
            twin.Dot(cx, cz, half * DotShare, FlatTwinInk.Walker);
            twin.Label(1.2f, -1.9f, s_faceOf[walker]);
        }

        // ---- S2, S3: the tile graph ----------------------------------------------------------------------

        // S3 keeps the frame loop's kernel flat and walks a {4,5} graph of its own, so the map holds one too:
        // the tile ids of two graphs of the same tiling are the same ids. Made on the first S3 map.
        static TileGraph s_cellGraph;

        // Aligned Cells: the disc round the tile of the walker's cell. The State's transform is that tile's
        // quarter-turn, so the marker stands on the tile's centre. The room is not drawn: in S3 it is the real
        // room round every cell, not the content of tile zero.
        static void Cells(FlatTwin twin, in StateMessage state, FlatTwinTrail trail)
        {
            if (s_cellGraph == null) s_cellGraph = new TileGraph(5);
            WalkerPose pose;
            pose.Tile = new TileId(state.Tile0, state.Tile1, state.Tile2, state.Tile3, state.Tile4, state.Tile5);
            pose.Local = new Iso2(state.Xform0, state.Xform1, state.Xform2, state.Xform3, state.Xform4, state.Xform5,
                                  state.Xform6, state.Xform7, state.Xform8, Curvature.Hyperbolic);
            if (!FlatTwinDisc.Draw(twin, s_cellGraph, in pose, null, 0.0, null, trail, Heading(in state))) NoGraph(twin);
        }

        static void NoGraph(FlatTwin twin)
        {
            twin.Rect(-1f, -1f, 1f, 1f, FlatTwinInk.Grid);
            twin.Label(0f, 0f, LabelNoGraph);
            twin.SetView(0f, 0f, 1.1f, false);
        }

        // Curved Walk on the kernel's own tiling: the Poincare disc for {4,5} and {4,6}, a square-grid plan in
        // metres for a kernel that stands on the flat tiling.
        static void Tiles(FlatTwin twin, RoomModel room, IWalkerKernel kernel, in StateMessage state, FlatTwinTrail trail)
        {
            ITileGraph graph = kernel != null ? kernel.Graph : null;
            if (FlatTwinDisc.Supports(graph))
            {
                WalkerPose pose;
                pose.Tile = new TileId(state.Tile0, state.Tile1, state.Tile2, state.Tile3, state.Tile4, state.Tile5);
                pose.Local = new Iso2(state.Xform0, state.Xform1, state.Xform2, state.Xform3, state.Xform4, state.Xform5,
                                      state.Xform6, state.Xform7, state.Xform8, Curvature.Hyperbolic);
                // The scale the phone drew with, which moves through a ramp; the kernel's when the State has none.
                double metres = state.P0 > 0f && !float.IsInfinity(state.P0) ? state.P0 : kernel.MetresPerUnit;
                if (!FlatTwinDisc.Draw(twin, graph, in pose, kernel.Candidates, metres, room, trail, Heading(in state))) NoGraph(twin);
                return;
            }
            if (graph == null || graph.Q != 4)
            {
                NoGraph(twin);
                return;
            }

            float mpu = (float)kernel.MetresPerUnit;
            float side = (float)graph.CentreSpacing * mpu;
            long wi = 0, wj = 0;
            var walkerTile = new TileId(state.Tile0, state.Tile1, state.Tile2, state.Tile3, state.Tile4, state.Tile5);
            bool known = FlatTileGraph.TryGetCell(walkerTile, out wi, out wj);

            List<VisibleTile> candidates = kernel.Candidates;
            int count = candidates != null ? candidates.Count : 0;
            for (int k = 0; k < count; k++)
            {
                long i, j;
                if (!FlatTileGraph.TryGetCell(candidates[k].Tile, out i, out j)) continue;
                CellSquare(twin, i, j, side, FlatTwinInk.Cell);
            }

            // The room sits over tile zero, where the walk began.
            DrawRoom(twin, room, null, false);

            // The walked tile sequence, oldest first, as a line through the tile centres.
            if (trail != null)
            {
                bool have = false;
                float px = 0f, pz = 0f;
                for (int k = 0; k < trail.TileCount; k++)
                {
                    long i, j;
                    if (!FlatTileGraph.TryGetCell(trail.GetTile(k), out i, out j)) { have = false; continue; }
                    float x = i * side, z = j * side;
                    if (have) twin.Line(px, pz, x, z, FlatTwinInk.Trail);
                    px = x;
                    pz = z;
                    have = true;
                }
            }

            float cx = wi * side, cz = wj * side;
            if (known) CellSquare(twin, wi, wj, side, FlatTwinInk.Highlight);
            // The walker inside its tile: the translation of the tile-local transform, and its turn.
            float lx = (float)state.Xform2 * mpu, lz = (float)state.Xform5 * mpu;
            float heading = (float)Math.Atan2(state.Xform1, state.Xform4);
            if (!known || float.IsNaN(lx) || float.IsNaN(lz)) { lx = 0f; lz = 0f; heading = Heading(in state); }
            twin.Arrow(cx + lx, cz + lz, heading, FlatTwinInk.Walker);

            float rho = (float)kernel.CullRho * mpu;
            twin.SetView(cx, cz, Math.Max(rho, side) + 0.5f * side, false);
        }

        static void CellSquare(FlatTwin twin, long i, long j, float side, FlatTwinInk ink)
        {
            float x = i * side, z = j * side, h = 0.5f * side;
            twin.Rect(x - h, z - h, x + h, z + h, ink);
        }

        // ---- small things ------------------------------------------------------------------------------------

        static float Finite(float v) { return float.IsNaN(v) || float.IsInfinity(v) ? 0f : v; }

        static int Clamp(int v, int min, int max) { return v < min ? min : v > max ? max : v; }

        // A cell index from the wire is trusted only as far as a float can place it.
        static long ClampCell(long v) { return v < -100000 ? -100000 : v > 100000 ? 100000 : v; }
    }
}

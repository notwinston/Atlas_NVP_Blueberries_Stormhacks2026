using System;
using Atlas.Kernel;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Rendering
{
    // P1-B11: the room content placed in curved space by the exp map about the room centre (Master Plan §4.6): the room centre is
    // tile zero's centre and room metres are tile zero's chart metres. The view owns no geometry: it drives the room content
    // (IRoomContent: the shell and one view per object, each on its own pooled material instance, §8.3) and gives the warped
    // renderers 1000 m local bounds. Anchors are computed in double and cast last (WarpAnchor.ToMatrix). The room root sits at
    // the identity transform (assumption A2: world = anchor frame).
    //   kappa = 0: exactly the room (warp mode 0): floor and full walls, every object at its own transform.
    //   kappa != 0: walls sink to a 0.9 m kerb [D10] and the room floor is hidden (tile zero's own floor is drawn by the tile
    //   renderer); the kerb is anchored U^-1 F0; furniture is rigid: anchor U^-1 F0 Object(base, yaw) so each object is a rigid copy
    //   about its base, at the height of its base (the anchor's [1][3]).
    public sealed class CurvedRoomView : IDisposable
    {
        readonly IRoomContent room;
        bool warped, disposed;

        public CurvedRoomView(IRoomContent room)
        {
            this.room = room ?? throw new ArgumentNullException(nameof(room));
        }

        public IRoomContent Room => room;
        /// <summary>The warp mode at kappa > 0: spherical (2), or S3 (3) for the Small Planet's planet.view3d.</summary>
        public float SphericalMode { get; set; } = AtlasWarpCpu.ModeSpherical;
        public int ObjectCount => room.ObjectCount;
        public Renderer FloorRenderer => room.FloorRenderer;
        /// <summary>The full walls at kappa = 0, the kerbs in curved space.</summary>
        public Renderer WallRenderer => room.WallRenderer;
        public Renderer ObjectRenderer(int i) => room.GetObject(i).Renderer;

        /// <summary>
        /// Once per frame (MP §6.3 step 6). walker is U in the current tile, tileZeroInCurrent is F of tile zero seen from it.
        /// drawRoom = the room can be in view (the caller's distance test, [B09 D12]); false hides everything.
        /// </summary>
        public void Upload(double kappa, double lengthScaleM, in Iso2 walker, in Iso2 tileZeroInCurrent, bool drawRoom)
        {
            if (disposed || !room.IsBuilt) return;
            room.ObjectsVisible = drawRoom;
            if (kappa == 0)
            {
                Flatten();
                room.FloorVisible = drawRoom;
                SetWalls(drawRoom ? WallMode.Full : WallMode.Hidden);
                return;
            }
            float mode = kappa < 0 ? AtlasWarpCpu.ModeHyperbolic : SphericalMode;
            warped = true;
            room.FloorVisible = false;                                          // [D10]
            room.KerbHeight = CurvedRoomConstants.KerbHeightM;
            SetWalls(drawRoom ? WallMode.Kerbs : WallMode.Hidden);
            Iso2 a0 = walker.Inverse() * tileZeroInCurrent;                     // double
            Material shell = room.ShellMaterial;
            shell.SetFloat(AtlasShaderIDs.WarpMode, mode);
            shell.SetMatrix(AtlasShaderIDs.Anchor, WarpAnchor.ToMatrix(a0));
            AtlasWarpBounds.Apply(room.WallRenderer);
            for (int i = 0; i < room.ObjectCount; i++)
            {
                IRoomObjectView view = room.GetObject(i);
                RoomObject o = view.Data;
                Material material = view.Material;
                material.SetFloat(AtlasShaderIDs.WarpMode, mode);
                material.SetMatrix(AtlasShaderIDs.Anchor, ObjectAnchor(a0, o, kappa, lengthScaleM));
                AtlasWarpBounds.Apply(view.Renderer);
            }
        }

        /// <summary>
        /// The anchor uploaded for one object: shellAnchor (U^-1 F0) times its placement in tile zero (base x, z and yaw), with
        /// the height of its base in the entry the warp adds to the height (a wall-mounted object has Base.y above 0).
        /// </summary>
        public static Matrix4x4 ObjectAnchor(in Iso2 shellAnchor, RoomObject o, double kappa, double lengthScaleM)
        {
            Iso2 a = shellAnchor * TileZeroPlacement.Object(new double2(o.Base.x, o.Base.z), o.Yaw, kappa, lengthScaleM);
            return WarpAnchor.ToMatrix(a, o.Base.y);
        }

        // The walls change at once: while they sink the room content scales them through the Transform, which the warp ignores.
        void SetWalls(WallMode mode)
        {
            if (room.Walls != mode) room.SetWallMode(mode, 0f);
        }

        // Back to warp mode 0 and the renderers' own bounds. The wall mode, the kerb height and the floor stay as they are.
        void Flatten()
        {
            if (!warped) return;
            warped = false;
            Material shell = room.ShellMaterial;
            if (shell != null)
            {
                shell.SetFloat(AtlasShaderIDs.WarpMode, AtlasWarpCpu.ModeNone);
                shell.SetMatrix(AtlasShaderIDs.Anchor, Matrix4x4.identity);
            }
            if (room.WallRenderer != null) room.WallRenderer.ResetLocalBounds();
            for (int i = 0; i < room.ObjectCount; i++)
            {
                IRoomObjectView view = room.GetObject(i);
                view.Material.SetFloat(AtlasShaderIDs.WarpMode, AtlasWarpCpu.ModeNone);
                view.Material.SetMatrix(AtlasShaderIDs.Anchor, Matrix4x4.identity);
                view.Renderer.ResetLocalBounds();
            }
        }

        /// <summary>Takes the warp off the room content. The host resets the rest of the presentation (IRoomContent.ResetPresentation).</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (room.IsBuilt) Flatten();
        }
    }
}

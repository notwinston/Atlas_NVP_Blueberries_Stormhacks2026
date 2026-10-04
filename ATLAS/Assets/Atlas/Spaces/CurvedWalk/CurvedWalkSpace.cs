// P1-B09 / P1-B11: S2 as an ISpaceModule (Master Plan §6.5) driven by the frame loop (§6.3 steps 5 and 6); no Update().
using Atlas.Input;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.CurvedWalk
{
    /// <summary>
    /// S2 Curved Walk (Master Plan tasks P1-B09 and P1-B11): the walker stands in the scanned room, which is tile zero of a
    /// {4,5} or {4,6} world, turns the curvature dial and walks the 3-4-5 loop. Not registered: once the curvature is on, a
    /// virtual wall is no longer the real wall, and Bump Guard, which works in the room frame, is what keeps the walker safe.
    /// </summary>
    /// <remarks>
    /// A thin adapter. The logic is <see cref="CurvedWalkCore"/>, which drives the app's one walker kernel (IDialKernel);
    /// the views are lane B's tile renderer, the generated world's renderer and the lap markers, and the room is the room
    /// content's own renderers warped through their pooled materials (CurvedRoomView), never a copy. SpaceLab puts the
    /// kernel back to flat and resets the room's presentation after Exit.
    /// </remarks>
    public sealed class CurvedWalkSpace : MonoBehaviour, ISpaceModule, IUploadStep, ISpaceWorld
    {
        readonly CurvedWalkCore core = new CurvedWalkCore();
        MaterialPool pool;
        IRoomContent roomContent;
        ISpaceHost host;
        IAtlasInput input;
        IDialKernel kernel;

        GameObject tilesObject;
        TileRenderer tiles;
        WorldContentRenderer content;
        CurvedRoomView roomView;
        LapMarkerView markers;
        CurvedWalkHud hud;
        int tilesRevision = -1;
        double tilesL;
        bool roomFades, fogTinted;
        int fogRegion = -1;
        float3 enterFogColor;

        /// <summary>The factory given to SpaceRegistry: a new object named like the scene.</summary>
        public static ISpaceModule Create()
        {
            return new GameObject(SpaceIds.SceneName(SpaceId.CurvedWalk)).AddComponent<CurvedWalkSpace>();
        }

        /// <summary>The dial's parameters of group "curved": curved.q and curved.strong. curved.cullRho and curved.mode
        /// are the app's (AppParams). Safe to call again.</summary>
        public static void DeclareParams(ParamRegistry registry)
        {
            CurvedWalkCore.DeclareParams(registry);
        }

        public SpaceId Id { get { return SpaceId.CurvedWalk; } }
        public bool Registered { get { return false; } }   // MP §4.9: S2 starts at the room's registration but is not a registered space

        public long WorldSeed { get { return CurvedWalkCore.WorldSeed; } }
        public int GeneratorVersion { get { return CurvedWalkCore.GeneratorVersion; } }
        public string ContentHash { get { return core.ContentHash; } }

        public CurvedWalkCore Core { get { return core; } }
        public TileRenderer Tiles { get { return tiles; } }
        public WorldContentRenderer Content { get { return content; } }
        public CurvedRoomView RoomView { get { return roomView; } }
        public LapMarkerView Markers { get { return markers; } }

        public void Configure(RoomModel room, ParamRegistry registry)
        {
            AtlasServices.TryGet(out IEventLog log);
            core.Configure(room, registry, log);
            AtlasServices.TryGet(out roomContent);
            AtlasServices.TryGet(out host);
            AtlasServices.TryGet(out input);
            AtlasServices.TryGet(out pool);     // no look in this build: nothing to draw with
            AtlasServices.TryGet(out kernel);   // none on the Station: its kernel cannot follow a curved walker yet
            if (host != null)
            {
                core.RequestSwitch = host.RequestSwitch;
                core.WorldChanged = host.BumpWorldVersion;
            }
        }

        public void Enter(in WalkerPose start)
        {
            if (kernel == null) return;
            // The root sits at the identity transform: world = the room frame = tile zero's chart [B09 D18, A2].
            Transform root = transform;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            core.Enter(kernel);
            // The walker starts where the head is, at K = 0: the room is exactly Home until the ramp begins.
            if (AtlasServices.TryGet(out IFrameLoop loop)) core.Register(loop.Current.HeadPosition.xz);

            enterFogColor = AtlasLook.State.FogColorTarget;
            if (pool != null)
            {
                tilesObject = new GameObject("Tiles") { hideFlags = HideFlags.DontSave };
                tilesObject.transform.SetParent(root, false);
                tiles = tilesObject.AddComponent<TileRenderer>();
                content = tilesObject.AddComponent<WorldContentRenderer>();
                ConfigureTiles();
                markers = new LapMarkerView(root, pool);
                markers.Visible = false;
            }
            if (roomContent != null) roomView = new CurvedRoomView(roomContent);
            AtlasServices.TryGet(out IStereoRig rig);
            hud = new CurvedWalkHud(rig != null ? rig.Hud : null, root, input);
        }

        // {4,5}: the generated world, one merged mesh per tile (the tile renderer keeps the selection, the anchors and the
        // fog). {4,6}: no generator, so the tile renderer draws its own shells.
        void ConfigureTiles()
        {
            double l = core.LengthScale(kernel.Q);
            WorldGenerator world = core.World;
            content.Release();
            tiles.DrawShells = world == null;
            tiles.Configure(world != null ? world.Graph : kernel.Graph, l, pool);
            if (world != null) content.Configure(world, l, pool);
            else
                for (int i = 0; i < tiles.Slots.Count; i++)   // MP §3.5: solid things fade within 0.3 m of the head
                    MaterialPool.SetSurface(tiles.Slots[i].sharedMaterial, nearFadeMetres: (float)CurvedRoomConstants.NearFadeRadiusM);
            tilesRevision = core.WorldRevision; tilesL = l;
        }

        public void Tick(in FrameContext frame)
        {
            if (kernel == null || !core.Entered) return;
            CurvedWalkInput asked = default;
            if (input != null)
            {
                // Read every frame, so the input service knows these actions are live and checks their gaze twins.
                asked.DialLeft = input.Pressed(AtlasAction.DialLeft);
                asked.DialRight = input.Pressed(AtlasAction.DialRight);
                asked.ModeToggle = input.Pressed(AtlasAction.Primary);
            }
            core.Tick(in frame, in asked);

            // B06's draw-rule selection with the kernel's pose and the (ramping) L.
            if (tiles == null) return;
            if (tilesRevision != core.WorldRevision) ConfigureTiles();
            if (kernel.CurvatureNow != 0)
            {
                double l = kernel.MetresPerUnit;
                if (l != tilesL)
                {
                    tiles.SetLengthScale(l);   // L changes every frame in a ramp; the meshes are rebuilt
                    if (content.Configured) content.SetLengthScale(l);
                    tilesL = l;
                }
                tiles.CullRho = kernel.CullRho;
                WalkerPose pose = kernel.Pose;
                tiles.KernelStep(in pose, in frame);
            }
        }

        // Step 6
        public void Upload(in FrameContext frame)
        {
            if (kernel == null || !core.Entered) return;
            WalkerPose pose = kernel.Pose;
            Iso2 u = pose.Local;
            double kappa = kernel.Kappa, l = kappa == 0 ? 1.0 : kernel.MetresPerUnit;
            bool curved = kernel.CurvatureNow != 0;
            if (tiles != null)
            {
                if (tilesObject.activeSelf != curved) tilesObject.SetActive(curved);   // hidden at K = 0 (OnDisable restores the fog)
                if (curved)
                {
                    tiles.Upload(in frame);
                    if (content.Configured) content.Draw(tiles.Selection.Drawn, tiles.Selection.DrawnCount);
                }
                else if (content.Configured) content.Hide();
                FogHue(curved && content.Configured, pose.Tile);
            }
            if (roomView != null)
            {
                bool draw = core.TileZeroFound;
                if (draw && curved)
                    draw = Geometry2.Distance(u.M.c2, core.TileZeroInCurrent.M.c2, kappa) <= kernel.CullRho + core.RoomRadiusM / l;   // [B09 D12]
                roomView.Upload(kappa, l, u, core.TileZeroInCurrent, draw);
                RoomNearFade(curved);
            }
            if (markers != null)
            {
                markers.Visible = core.Walk.Armed;
                markers.Upload(core.Walk, core.TileZeroInCurrent, u, kappa, l, (Vector3)frame.HeadPosition);
            }
            if (hud != null) hud.Upload(frame.HeadPosition, frame.HeadRotation, core.SelectedQ, core.Walk);
        }

        // The fog takes the hue of the region the walker stands in (MP §8.5); at K = 0 it is the look's own again.
        void FogHue(bool tinted, TileId tile)
        {
            if (!tinted)
            {
                if (fogTinted) { AtlasLook.SetFogColor(enterFogColor); fogTinted = false; fogRegion = -1; }
                return;
            }
            int region = Regions.RegionOf(tile);
            if (region == fogRegion) return;
            fogRegion = region; fogTinted = true;
            AtlasLook.SetFogColor(WorldRegionTint.FogLinear(region));
        }

        // Kerbs, corner posts and furniture fade within 0.3 m of the head while the curvature is on (MP §3.5, §4.6): the
        // walker can walk through what is no longer where the real thing is. Written when the curvature turns on or off.
        void RoomNearFade(bool curved)
        {
            if (curved == roomFades || !roomContent.IsBuilt) return;
            roomFades = curved;
            float near = curved ? (float)CurvedRoomConstants.NearFadeRadiusM : 0f;
            if (roomContent.ShellMaterial != null) MaterialPool.SetSurface(roomContent.ShellMaterial, nearFadeMetres: near);
            bool scaled = false;
            for (int i = 0; i < roomContent.ObjectCount; i++)
            {
                IRoomObjectView view = roomContent.GetObject(i);
                MaterialPool.SetSurface(view.Material, nearFadeMetres: near);
                // The warp reads the mesh in metres and ignores Transforms: a fitting scale on the mesh's own transform
                // would be lost. The asset client bakes it into the mesh; say so if a caller ever does not.
                if (curved && view.Renderer.transform.localScale != Vector3.one) scaled = true;
            }
            if (scaled && AtlasServices.TryGet(out IEventLog log)) log.Log("curved.objectScale");
        }

        public void Exit()
        {
            core.Exit();
            if (hud != null) hud.Dispose();
            hud = null;
            if (markers != null) markers.Dispose();
            markers = null;
            if (roomView != null) roomView.Dispose();   // takes the warp off; SpaceLab resets the rest of the presentation
            roomView = null;
            roomFades = false;
            if (fogTinted) AtlasLook.SetFogColor(enterFogColor, true);
            fogTinted = false; fogRegion = -1;
            if (tilesObject != null)
            {
                content.Release();
                tiles.Release();
                if (Application.isPlaying) Destroy(tilesObject); else DestroyImmediate(tilesObject);
            }
            tilesObject = null; tiles = null; content = null; tilesRevision = -1;
        }

        public void WriteState(ref StateMessage state)
        {
            core.WriteState(ref state);
        }
    }
}

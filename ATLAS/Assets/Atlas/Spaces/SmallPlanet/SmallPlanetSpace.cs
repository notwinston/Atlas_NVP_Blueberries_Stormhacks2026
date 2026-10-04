using Atlas.Input;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.SmallPlanet
{
    /// <summary>
    /// S4 Small Planet (Master Plan §3.2, §6.5, §10.1.3; tasks P1-B08 and P1-B09): the {4,3} tiling, six tiles
    /// that are the faces of a cube blown onto a sphere of radius R, all always drawn, and the 3-corner walk. The
    /// tiles are the PlanetKit ones modelled in Blender (SmallPlanetView; the cairn on face 5 is the landmark), under
    /// a daytime sky (SmallPlanetSky). The room is tile zero: its kerbs and furniture are the shared room content, warped through
    /// their own materials. Registered only until the first step (§4.9), so the module reports false.
    /// </summary>
    /// <remarks>
    /// A thin adapter over <see cref="SmallPlanetCore"/>. The walker is the app's kernel, set to Curved(3, R) on
    /// Enter; SpaceLab puts it back to flat after Exit. `_HypL` = R and `_HeadPosWS` are the look's upload, from
    /// that kernel; the 2.6 rad discard is the shared shader's (AtlasWarpClip). Driven by the frame loop; no Update().
    /// </remarks>
    public sealed class SmallPlanetSpace : MonoBehaviour, ISpaceModule, IUploadStep, ISpaceStateReader
    {
        readonly SmallPlanetCore core = new SmallPlanetCore();
        readonly Iso2[] anchors = new Iso2[CubeTiling.FaceCount];

        MaterialPool pool;
        IRoomContent room;
        IAtlasInput input;
        ISpaceHost host;
        IEventLog log;
        IDialKernel kernel;
        SmallPlanetView view;
        SmallPlanetSky sky;
        LapMarkerView markers;
        CurvedRoomView roomView;
        SmallPlanetDial dial;
        GameObject dialChip;
        Mesh dialChipMesh;
        Material dialChipMaterial;
        float dialChipScale = 1f;
        float fogStart = -1f, fogEnd = -1f;

        /// <summary>The factory given to SpaceRegistry: a new object named like the scene.</summary>
        public static ISpaceModule Create()
        {
            return new GameObject(SpaceIds.SceneName(SpaceId.SmallPlanet)).AddComponent<SmallPlanetSpace>();
        }

        /// <summary>The parameters of group "planet", and the dial of group "curved". Safe to call again.</summary>
        public static void DeclareParams(ParamRegistry registry)
        {
            SmallPlanetCore.DeclareParams(registry);
        }

        public SpaceId Id => SpaceId.SmallPlanet;
        public bool Registered => false;   // MP §4.9
        /// <summary>The logic behind this component.</summary>
        public SmallPlanetCore Core => core;
        public SmallPlanetView View => view;
        /// <summary>The sun and clouds while the space is entered with a look, or null.</summary>
        public SmallPlanetSky Sky => sky;
        public LapMarkerView Markers => markers;
        /// <summary>The gaze twin of the dial while the space is entered with an input service, or null.</summary>
        internal SmallPlanetDial Dial => dial;
        /// <summary>What marks the twin for the eyes, or null with no look in this build.</summary>
        internal GameObject DialChip => dialChip;

        public void Configure(RoomModel roomModel, ParamRegistry registry)
        {
            core.Configure(roomModel, registry);
            AtlasServices.TryGet(out room);
            AtlasServices.TryGet(out input);
            AtlasServices.TryGet(out host);
            AtlasServices.TryGet(out log);
            AtlasServices.TryGet(out kernel);
            AtlasServices.TryGet(out pool);     // no look in this build: nothing to draw with
        }

        public void Enter(in WalkerPose start)
        {
            // The module's root is the room frame; the warp places everything round the head.
            Transform root = transform;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            // SpaceLab does not place the walker of a space that is not registered: face 0 over the floor point under the head.
            double2 head = double2.zero;
            if (AtlasServices.TryGet(out IFrameLoop loop))
            {
                float3 p = loop.Current.HeadPosition;
                if (math.all(math.isfinite(p))) head = new double2(p.x, p.z);
            }
            core.Enter(kernel, head);

            ReleaseSky();
            if (pool != null)
            {
                view = gameObject.AddComponent<SmallPlanetView>();
                view.Build(core.R, pool);   // without the kit (one error logged) nothing is drawn; the walk goes on
                view.Shown = core.Placed;
                AtlasServices.TryGet(out IStereoRig rig);
                sky = new SmallPlanetSky(transform, pool, rig, core.View3d);
                markers = new LapMarkerView(transform, pool);
                markers.Visible = false;
            }
            else SmallPlanetSky.ApplyLook(core.View3d);
            if (room != null && room.IsBuilt) roomView = new CurvedRoomView(room);
            ApplyView();
            // Rule 5: the dial is read in Tick, so its gaze twin lives as long as the space is entered.
            ReleaseDial();
            if (input != null)
            {
                dial = new SmallPlanetDial(input);
                if (pool != null) MakeDialChip();
            }
            fogStart = fogEnd = -1f;
            UploadFog();
        }

        public void ReadState(in StateMessage state)
        {
            core.ReadState(in state);
        }

        public void Tick(in FrameContext frame)
        {
            if (!core.Entered) return;
            bool still = kernel != null && kernel.Still;
            // q = 3 is the dial's lowest snap: DialLeft can do nothing here, so it is not read and needs no twin.
            bool right = input != null && input.Pressed(AtlasAction.DialRight);
            LapEvent ev = core.Tick(frame.Walker, still, false, right);
            WalkEvents.Emit(log, core.Walk, ev);

            if (core.RadiusChanged)
            {
                if (view != null) view.SetRadius(core.R);
                // A planet of another size is another world: the hash and the Station follow.
                if (host != null) host.BumpWorldVersion();
            }
            if (core.ViewChanged) ApplyView();
            if (core.SwitchWanted && (host == null || !host.RequestSwitch(SpaceId.CurvedWalk))) core.SwitchRefused();
        }

        public void Upload(in FrameContext frame)
        {
            if (!core.Entered) return;
            UploadFog();
            if (sky != null) sky.Upload(frame.HeadPosition, frame.Time);
            if (dial != null)
            {
                dial.Place(frame.HeadPosition, frame.HeadRotation);
                dial.SetLive(core.Driving && kernel.Still);
                if (dialChip != null)
                {
                    if (dialChip.activeSelf != dial.Live) dialChip.SetActive(dial.Live);
                    Vector3 centre = (Vector3)dial.Centre;
                    dialChip.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(centre - (Vector3)frame.HeadPosition));
                    // Within reach of the hands the plate is as large as its gaze target: its size times the layout's scale.
                    if (dial.Scale != dialChipScale)
                    {
                        dialChipScale = dial.Scale;
                        dialChip.transform.localScale = new Vector3(dialChipScale, dialChipScale, dialChipScale);
                    }
                }
            }
            bool placed = core.Placed;
            if (view != null)
            {
                view.Shown = placed;
                if (placed)
                {
                    core.ComputeAnchors(anchors);
                    view.Upload(anchors);
                }
                markers.Visible = placed && core.Walk.Armed;
                markers.Upload(core.Walk, core.TileZero, core.U, Curvature.Spherical, core.R, frame.HeadPosition);
            }
            // The room is tile zero's content: kerbs and furniture, each object rigid about its own base (lane C contract, section 9.2).
            if (roomView != null) roomView.Upload(Curvature.Spherical, core.R, core.U, core.TileZero, placed);
        }

        public void Exit()
        {
            core.Exit();
            ReleaseDial();
            ReleaseSky();
            if (roomView != null) roomView.Dispose();
            roomView = null;
            if (markers != null) markers.Dispose();
            markers = null;
            if (view != null) view.Release();
        }

        public void WriteState(ref StateMessage state)
        {
            core.WriteState(ref state);
        }

        void OnDestroy()
        {
            ReleaseDial();   // Exit has done this already; this only covers a root destroyed without Exit
            ReleaseSky();
        }

        // A small plate where the twin is, facing the walker, in the room frame: not warped, not fogged.
        void MakeDialChip()
        {
            var data = new AtlasMeshBuilder(32, 96);
            float side = 2f * SmallPlanetDial.Radius;
            data.AddBox(new float3(0f, -0.5f * side, 0f), new float3(side, side, 0.01f), 0f, AtlasPalette.Uv(AtlasPalette.Beacon), true);
            dialChipMesh = AtlasMesh.Create(data, "PlanetDialChip");
            dialChip = new GameObject("DialChip", typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave };
            dialChip.transform.SetParent(transform, false);
            dialChip.GetComponent<MeshFilter>().sharedMesh = dialChipMesh;
            var mr = dialChip.GetComponent<MeshRenderer>();
            dialChipMaterial = pool.Rent();
            MaterialPool.SetSurface(dialChipMaterial, 1f, 0f);
            mr.sharedMaterial = dialChipMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            dialChipScale = 1f;
            dialChip.SetActive(false);
        }

        // planet.view3d: warp mode 3 (S3) or 2 for everything of the planet that is warped (tiles, room, lap markers),
        // and the sky to match. Off is exactly the comfortable view: mode 2 and the daytime sky.
        void ApplyView()
        {
            bool on = core.View3d;
            float mode = on ? AtlasWarpCpu.ModeS3 : AtlasWarpCpu.ModeSpherical;
            if (view != null) view.SetWarpMode(mode);
            if (markers != null) markers.SphericalMode = mode;
            if (roomView != null) roomView.SphericalMode = mode;
            if (sky != null) sky.View3d = on;
            else if (pool == null) SmallPlanetSky.ApplyLook(on);
        }

        void ReleaseDial()
        {
            if (dial != null) dial.Dispose();
            dial = null;
            Kill(dialChip); Kill(dialChipMesh);
            dialChip = null; dialChipMesh = null;
            if (dialChipMaterial != null && pool != null && pool.Owns(dialChipMaterial)) pool.Return(dialChipMaterial);
            dialChipMaterial = null;
        }

        void ReleaseSky()
        {
            if (sky != null) sky.Dispose();
            sky = null;
        }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        // §9.5.6, §10.2: fog from 0.5 pi R, opaque at 0.8 pi R (planet.fogRho moves the opaque end), metres from the head.
        void UploadFog()
        {
            float start = (float)(core.FogStartRad * core.R), end = (float)(core.FogOpaqueRad * core.R);
            if (start == fogStart && end == fogEnd) return;
            fogStart = start; fogEnd = end;
            AtlasLook.SetFog(start, end);
        }
    }
}

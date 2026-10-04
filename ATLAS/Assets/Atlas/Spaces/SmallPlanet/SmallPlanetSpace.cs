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
    /// that are the faces of a cube blown onto a sphere of radius R, all always drawn, with a pole beacon and the
    /// 3-corner walk. The room is tile zero: its kerbs and furniture are the shared room content, warped through
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
        LapMarkerView markers;
        CurvedRoomView roomView;
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
        public LapMarkerView Markers => markers;

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

            if (pool != null)
            {
                view = gameObject.AddComponent<SmallPlanetView>();
                view.Build(core.R, pool);
                view.Shown = core.Placed;
                markers = new LapMarkerView(transform, pool);
                markers.Visible = false;
            }
            if (room != null && room.IsBuilt) roomView = new CurvedRoomView(room);
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
            bool left = input != null && input.Pressed(AtlasAction.DialLeft);
            bool right = input != null && input.Pressed(AtlasAction.DialRight);
            LapEvent ev = core.Tick(frame.Walker, still, left, right);
            WalkEvents.Emit(log, core.Walk, ev);

            if (core.RadiusChanged)
            {
                if (view != null) view.SetRadius(core.R);
                // A planet of another size is another world: the hash and the Station follow.
                if (host != null) host.BumpWorldVersion();
            }
            if (core.SwitchWanted && (host == null || !host.RequestSwitch(SpaceId.CurvedWalk))) core.SwitchRefused();
        }

        public void Upload(in FrameContext frame)
        {
            if (!core.Entered) return;
            UploadFog();
            bool placed = core.Placed;
            if (view != null)
            {
                view.Shown = placed;
                if (placed)
                {
                    core.ComputeAnchors(anchors, out Iso2 beacon);
                    view.Upload(anchors, beacon);
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

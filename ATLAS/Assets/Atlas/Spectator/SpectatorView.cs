using System.Collections.Generic;
using System.Text;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Spectator
{
    /// <summary>
    /// What the Station shows (Master Plan section 7.3.1, task P1-D05; lane D contract, section 5.8): the mirror,
    /// one mono camera at the walker's head pose, on the left; beside it the flat twin, the Bump Guard level as a
    /// coloured bar, the link state and State rate, and the banner. The two cameras draw side by side and never
    /// over each other. This component is also the Spectator's frame driver: Update runs steps 1 to 6 from the
    /// newest State, the camera is placed in LateUpdate, and the link is polled after the frame is rendered.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class SpectatorView : MonoBehaviour
    {
        /// <summary>Layers "AtlasHud" and "AtlasSpectator" (lane A contract, section 12.1).</summary>
        public const int HudLayer = 7, SpectatorLayer = 23;
        /// <summary>The mirror's share of the window's width.</summary>
        public const float MirrorShare = 0.72f;
        public const float FieldOfView = 60f, NearPlane = 0.05f, FarPlane = 60f;
        public const float MapSeconds = 0.1f, StatusSeconds = 0.5f;

        static readonly string[] s_bump = { "bump guard: clear", "bump guard: warn", "bump guard: STRONG", "bump guard: tracking lost" };

        SpectatorApp _app;
        Camera _mirror, _panels;
        Transform _panelRoot;
        FlatTwinPanel _map, _status;
        readonly StringBuilder _text = new StringBuilder(96);
        string _lineLink = string.Empty, _lineState = string.Empty, _lineRoom = string.Empty;
        float _sinceMap = MapSeconds, _sinceStatus = StatusSeconds;
        float _aspect = -1f;

        // The room outline on the mirror's floor while no room is held.
        GameObject _outlineObject;
        MeshFilter _outlineFilter;
        Mesh _outlineMesh;
        Material _outlineMaterial;
        int _outlineShown = -1;

        /// <summary>The mirror camera's transform: the head of the Spectator.</summary>
        public Transform Head { get { return _mirror != null ? _mirror.transform : null; } }
        public Camera Mirror { get { return _mirror; } }
        public FlatTwin Map { get { return _map != null ? _map.Twin : null; } }

        /// <summary>Creates the component, disabled, with its two cameras and two panels under parent.</summary>
        public static SpectatorView Create(Transform parent, SpectatorApp app)
        {
            var host = new GameObject("SpectatorView");
            host.transform.SetParent(parent, false);
            var view = host.AddComponent<SpectatorView>();
            view.enabled = false;
            view._app = app;
            view.Build(parent.parent != null ? parent.parent : parent);
            return view;
        }

        void Build(Transform root)
        {
            var head = new GameObject("Atlas Head");
            head.transform.SetParent(root, false);
            _mirror = head.AddComponent<Camera>();
            _mirror.fieldOfView = FieldOfView;
            _mirror.nearClipPlane = NearPlane;
            _mirror.farClipPlane = FarPlane;
            // The look's gradient is the scene's skybox.
            _mirror.clearFlags = CameraClearFlags.Skybox;
            _mirror.allowHDR = false;
            _mirror.allowMSAA = true;
            _mirror.depth = 0f;
            _mirror.rect = new Rect(0f, 0f, MirrorShare, 1f);
            _mirror.cullingMask = ~((1 << HudLayer) | (1 << SpectatorLayer));
            head.AddComponent<AudioListener>();

            var panelCamera = new GameObject("Spectator Panels");
            panelCamera.layer = SpectatorLayer;
            panelCamera.transform.SetParent(root, false);
            // Far from the world, so nothing of it is ever near the panels.
            panelCamera.transform.localPosition = new Vector3(0f, -500f, 0f);
            _panels = panelCamera.AddComponent<Camera>();
            _panels.orthographic = true;
            _panels.orthographicSize = 1f;
            _panels.nearClipPlane = 0.1f;
            _panels.farClipPlane = 10f;
            _panels.clearFlags = CameraClearFlags.SolidColor;
            _panels.backgroundColor = new Color(0.035f, 0.04f, 0.05f, 1f);
            _panels.allowHDR = false;
            _panels.allowMSAA = true;
            _panels.depth = 1f;
            _panels.rect = new Rect(MirrorShare, 0f, 1f - MirrorShare, 1f);
            _panels.cullingMask = 1 << SpectatorLayer;

            var panelRoot = new GameObject("Panels");
            panelRoot.layer = SpectatorLayer;
            panelRoot.transform.SetParent(panelCamera.transform, false);
            panelRoot.transform.localPosition = new Vector3(0f, 0f, 1f);
            _panelRoot = panelRoot.transform;

            // Both panels are one unit across and are scaled to the window by Layout.
            _map = new FlatTwinPanel(_panelRoot, "Flat Twin", 1f, false, 0.045f);
            _status = new FlatTwinPanel(_panelRoot, "Status", 1f, false, 0.05f);
            _status.SetLabelAnchor(TextAnchor.MiddleLeft, TextAlignment.Left);
        }

        void OnEnable()
        {
            RenderPipelineManager.endContextRendering += OnEndContextRendering;
        }

        void OnDisable()
        {
            RenderPipelineManager.endContextRendering -= OnEndContextRendering;
        }

        // Steps 1 to 6 from the newest State.
        void Update()
        {
            if (_app == null) return;
            _app.Loop.LinkUp = _app.TelemetrySystems.LinkState != LinkState.Offline;
            _app.Loop.Run(Time.unscaledTimeAsDouble, Time.unscaledDeltaTime);
        }

        void LateUpdate()
        {
            if (_app == null) return;
            ref readonly FrameContext frame = ref _app.Loop.Current;
            _mirror.transform.SetPositionAndRotation(frame.RawHeadPosition, frame.RawHeadRotation);

            if (_panels.aspect != _aspect) Layout(_panels.aspect);
            ShowOutline();

            _sinceMap += frame.DeltaTime;
            if (_sinceMap >= MapSeconds)
            {
                _sinceMap = 0f;
                RebuildMap();
            }
            _sinceStatus += frame.DeltaTime;
            if (_sinceStatus >= StatusSeconds)
            {
                _sinceStatus = 0f;
                RebuildStatus();
            }
        }

        // Step 8b, once per rendered frame: the probe and the link, which delivers the next State.
        void OnEndContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (_app != null) _app.Loop.RunAfterFrame();
        }

        // The panel column is two units high and 2 * aspect wide: the map on top, the status under it.
        void Layout(float aspect)
        {
            _aspect = aspect;
            float width = 2f * Mathf.Max(aspect, 0.05f);
            float side = Mathf.Min(width * 0.94f, 1.15f);
            Place(_map, side, 1f - 0.03f - 0.5f * side);
            float rest = 2f - side - 0.09f;
            float statusSide = Mathf.Min(width * 0.94f, rest);
            Place(_status, statusSide, 1f - 0.06f - side - 0.5f * statusSide);
        }

        static void Place(FlatTwinPanel panel, float side, float y)
        {
            Transform t = panel.Transform;
            if (t == null) return;
            t.localPosition = new Vector3(0f, y, 0f);
            t.localScale = new Vector3(side, side, 1f);
        }

        void RebuildMap()
        {
            SpectatorCore core = _app.Core;
            var source = core.Host.Module as IFlatTwinSource;
            if (source != null) FlatTwinBuilder.Build(_map.Twin, source);
            else FlatTwinBuilder.Build(_map.Twin, core.Room, core.Outline, _app.WalkerKernel, in core.State, core.Trail);
            _map.Apply();
        }

        // Link, State rate and space; the room; the Bump Guard level as a bar; the banner.
        void RebuildStatus()
        {
            SpectatorCore core = _app.Core;
            LinkClient link = _app.TelemetrySystems.Link;
            LinkState state = _app.TelemetrySystems.LinkState;
            ref readonly StateMessage s = ref core.State;
            FlatTwin twin = _status.Twin;
            twin.Clear();
            twin.SetView(0f, 0f, 1f, false);

            _text.Length = 0;
            _text.Append("link ").Append(LinkPump.StateName(state));
            if (state != LinkState.Offline) _text.Append("  rtt ").Append(link.RttMs).Append(" ms");
            _lineLink = Keep(_lineLink);

            _text.Length = 0;
            _text.Append("state ").Append((int)math.round(core.StateHz)).Append(" Hz  ");
            if (core.HasState && s.SpaceId < SpaceIds.Count)
                _text.Append(SpaceIds.Code((SpaceId)s.SpaceId)).Append(' ').Append(SpaceIds.DisplayName((SpaceId)s.SpaceId));
            else _text.Append("no space");
            _lineState = Keep(_lineState);

            _text.Length = 0;
            _text.Append("room ").Append(string.IsNullOrEmpty(core.RoomId) ? "-" : core.RoomId);
            if (core.Room != null) _text.Append(core.WorldVerified ? "  world ok" : "  world unchecked");
            _lineRoom = Keep(_lineRoom);

            twin.Dot(-0.88f, 0.84f, 0.035f,
                state == LinkState.Connected ? FlatTwinInk.Good : state == LinkState.Offline ? FlatTwinInk.Muted : FlatTwinInk.Warn);
            twin.Label(-0.78f, 0.84f, _lineLink);
            twin.Label(-0.92f, 0.62f, _lineState);
            twin.Label(-0.92f, 0.40f, _lineRoom);

            // Bump Guard: 0 clear, 1 warn, 2 strong, 3 tracking lost.
            int level = core.HasState ? math.min((int)s.BumpLevel, 3) : 0;
            twin.Bar(-0.92f, 0.02f, 0.92f, 0.16f, FlatTwinInk.Muted);
            float reach = level == 0 ? 0.12f : level == 1 ? 0.5f : level == 2 ? 0.8f : 1f;
            twin.Bar(-0.92f, 0.02f, -0.92f + 1.84f * reach, 0.16f,
                level == 0 ? FlatTwinInk.Good : level == 1 ? FlatTwinInk.Warn : FlatTwinInk.Alert);
            twin.Label(-0.92f, -0.10f, s_bump[level]);

            string banner = SpectatorCore.BannerText(core.Banner);
            if (banner.Length > 0)
            {
                twin.Rect(-0.92f, -0.56f, 0.92f, -0.30f, core.Banner == SpectatorBanner.WorldMismatch ? FlatTwinInk.Alert : FlatTwinInk.Warn);
                twin.Label(-0.84f, -0.43f, banner);
            }
            _status.Apply();
        }

        // The panel compares labels by reference: a line that reads the same keeps its string.
        string Keep(string previous)
        {
            if (previous.Length == _text.Length)
            {
                bool same = true;
                for (int i = 0; i < previous.Length && same; i++) same = previous[i] == _text[i];
                if (same) return previous;
            }
            return _text.ToString();
        }

        // While no room is held, the phone's outline is laid on the mirror's floor: the walker's camera then
        // moves over something the phone itself reported, and nothing is made up.
        void ShowOutline()
        {
            SpectatorCore core = _app.Core;
            RoomOutlineMessage outline = core.Room == null ? core.Outline : null;
            if (outline == null)
            {
                if (_outlineObject != null && _outlineObject.activeSelf) _outlineObject.SetActive(false);
                return;
            }
            if (_outlineObject == null)
            {
                _outlineObject = new GameObject("Phone Outline");
                _outlineObject.transform.SetParent(_app.Boot.RoomRoot, false);
                _outlineFilter = _outlineObject.AddComponent<MeshFilter>();
                var renderer = _outlineObject.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _outlineMaterial = _app.Pool.Rent();
                MaterialPool.SetCull(_outlineMaterial, CullMode.Off);
                renderer.sharedMaterial = _outlineMaterial;
            }
            if (!_outlineObject.activeSelf) _outlineObject.SetActive(true);
            if (_outlineShown == core.OutlineRevision) return;
            _outlineShown = core.OutlineRevision;

            const float Width = 0.04f, Lift = 0.004f;
            var data = new AtlasMeshBuilder(outline.PointCount * 4 + 16, outline.PointCount * 6 + 24);
            float2 colour = AtlasPalette.Uv(AtlasPalette.LapMarker);
            for (int i = 0; i < outline.PointCount; i++)
            {
                int j = (i + 1) % outline.PointCount;
                var a = new float3(outline.PointsXZ[2 * i], Lift, outline.PointsXZ[2 * i + 1]);
                var b = new float3(outline.PointsXZ[2 * j], Lift, outline.PointsXZ[2 * j + 1]);
                float3 along = b - a;
                float length = math.length(along);
                if (!(length > 1e-4f)) continue;
                float3 side = math.cross(new float3(0f, 1f, 0f), along / length) * Width;
                data.AddQuad(a - 0.5f * side, along, side, colour);
            }
            if (_outlineMesh == null)
            {
                _outlineMesh = AtlasMesh.Create(data, "Phone Outline");
                _outlineFilter.sharedMesh = _outlineMesh;
            }
            else AtlasMesh.Apply(data, _outlineMesh);
        }

        /// <summary>Releases the panels, the outline mesh and its pooled material. The objects go with the root.</summary>
        public void Release()
        {
            if (_map != null) _map.Dispose();
            if (_status != null) _status.Dispose();
            if (_outlineMaterial != null && _app != null && _app.Pool != null) _app.Pool.Return(_outlineMaterial);
            _outlineMaterial = null;
            SpectatorApp.Kill(_outlineMesh);
            _outlineMesh = null;
        }
    }
}

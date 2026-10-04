using System;
using System.Collections.Generic;
using Atlas.Rendering;
using Atlas.Safety;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Atlas.App
{
    /// <summary>
    /// The hexagon reveal between two spaces (<see cref="ISpaceTransition"/>). On Cover it takes a still of the
    /// space on show, six 512-pixel faces from the head, and puts up a dome of <see cref="HexDome"/> tiles that
    /// shows it, fixed in the room where the walker stood. The old space goes and the new one loads behind it.
    /// On Reveal it takes a still of the new space for the backs of the tiles, and the tiles turn over in a
    /// ripple: the one the walker looks at first, then its neighbours, then theirs, each dropped once turned,
    /// onto the live space.
    /// <para>The cost is the two stills: twelve camera renders, once each per switch; then one draw call of
    /// about 550 triangles while the dome is up, with the world behind it failing its depth test. The stills
    /// are pooled render textures, released when the dome goes down.</para>
    /// <para>The warning cue draws over the dome, so a Warn or Strong cue shows on it. With tracking lost the
    /// stills would stand in the wrong place: the dome does not go up then, and comes down at once if tracking
    /// is lost under it. Only in the headset: in the hand the switch fades as before. Each refusal and drop is
    /// in the event log as space.transition.</para>
    /// </summary>
    public sealed class HexTransition : ISpaceTransition, IDisposable
    {
        public const string ShaderResource = "AtlasHexFlip";
        /// <summary>Pixels on a side of each face of a still: about 5.7 per degree, for a second or two.</summary>
        public const int FaceSize = 512;

        static readonly int s_OldStill = Shader.PropertyToID("_OldStill");
        static readonly int s_NewStill = Shader.PropertyToID("_NewStill");
        static readonly int s_OldCentre = Shader.PropertyToID("_OldCentre");
        static readonly int s_NewCentre = Shader.PropertyToID("_NewCentre");
        static readonly int s_TileDelays = Shader.PropertyToID("_TileDelays");
        static readonly int s_TileAxes = Shader.PropertyToID("_TileAxes");
        static readonly int s_RevealTime = Shader.PropertyToID("_RevealTime");

        // The faces of a still in the order the shader reads them: +x, -x, +y, -y, +z, -z.
        static readonly Quaternion[] s_Faces =
        {
            Quaternion.LookRotation(Vector3.right, Vector3.up),
            Quaternion.LookRotation(Vector3.left, Vector3.up),
            Quaternion.LookRotation(Vector3.up, Vector3.back),
            Quaternion.LookRotation(Vector3.down, Vector3.forward),
            Quaternion.LookRotation(Vector3.forward, Vector3.up),
            Quaternion.LookRotation(Vector3.back, Vector3.up),
        };

        readonly IStereoRig _rig;
        readonly GameObject _dome;
        readonly MeshRenderer _renderer;
        readonly Mesh _mesh;
        readonly Material _material;
        readonly Camera _camera;
        readonly int _hudLayer;
        readonly HexDome _tiles = new HexDome();
        readonly int[] _rings = new int[HexDome.TileCount];
        readonly float[] _delays = new float[HexDome.TileCount];
        readonly float3[] _waveAxes = new float3[HexDome.TileCount];
        readonly Vector4[] _axes = new Vector4[HexDome.TileCount];

        RenderTexture _old, _new;
        bool _covered;

        HexTransition(IStereoRig rig, Shader shader)
        {
            _rig = rig;
            _hudLayer = rig.Hud.gameObject.layer;

            // On the head-locked layer, so the tracked camera never draws it and a still never holds it, but
            // in the room: it stays where it was put while the head moves inside it.
            _dome = new GameObject("Atlas Hex Dome") { layer = _hudLayer };
            _mesh = BuildMesh(_tiles);
            _material = new Material(shader) { name = "Atlas Hex Dome", hideFlags = HideFlags.DontSave };
            _dome.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _dome.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _renderer.enabled = false;

            var still = new GameObject("Atlas Still Camera");
            _camera = still.AddComponent<Camera>();
            _camera.enabled = false;                       // renders only when asked
            _camera.fieldOfView = 90f;
            _camera.aspect = 1f;
            _camera.nearClipPlane = StereoRig.NearPlane;
            _camera.farClipPlane = StereoRig.FarPlane;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            _camera.allowDynamicResolution = false;
            _camera.useOcclusionCulling = false;
            UniversalAdditionalCameraData data = _camera.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresDepthTexture = false;
            data.requiresColorTexture = false;
            data.renderShadows = false;
            data.allowXRRendering = false;
            data.SetRenderer(0);                           // the eyes' renderer
        }

        /// <summary>Null when the shader is not in Resources: SpaceLab then fades as before.</summary>
        public static HexTransition Create(IStereoRig rig)
        {
            if (rig == null || rig.Hud == null) return null;
            Shader shader = Resources.Load<Shader>(ShaderResource);
            if (shader == null)
            {
                Debug.LogWarning("Atlas: shader '" + ShaderResource + "' is not in Resources; spaces fade.");
                return null;
            }
            return new HexTransition(rig, shader);
        }

        /// <summary>Where space.transition goes; null logs nothing.</summary>
        public IEventLog Events { get; set; }

        /// <summary>
        /// Called with true just before the six renders of a still and false right after (also if a render
        /// throws). The tracked hands go through it: they stay live through a switch, so a still must not hold a
        /// frozen copy of them under the dome. Null when there are no hands.
        /// </summary>
        public Action<bool> HideFromStill { get; set; }

        // Runs render for each face with the hands hidden from the still, and always shows them again.
        internal static void RenderFaces(Action<bool> hide, int faces, Action<int> render)
        {
            if (hide != null) hide(true);
            try
            {
                for (int i = 0; i < faces; i++) render(i);
            }
            finally
            {
                if (hide != null) hide(false);
            }
        }

        public bool Cover()
        {
            Uncover();
            string refusal = Refusal();
            if (refusal != null)
            {
                Log("refused", refusal);
                return false;
            }

            Transform head = _rig.Head;
            Vector3 centre = head.position;
            Vector3 forward = head.forward;
            _old = Still();
            Capture(centre, _old);

            _dome.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(forward, Vector3.up));
            _material.SetTexture(s_OldStill, _old);
            _material.SetTexture(s_NewStill, _old);
            _material.SetVector(s_OldCentre, centre);
            _material.SetVector(s_NewCentre, centre);
            for (int i = 0; i < _delays.Length; i++)
            {
                _delays[i] = 0f;
                _axes[i] = (Vector3)_tiles.Axes[i];
            }
            _material.SetFloatArray(s_TileDelays, _delays);
            _material.SetVectorArray(s_TileAxes, _axes);
            _material.SetFloat(s_RevealTime, -1f);
            _renderer.enabled = true;
            _covered = true;
            Log("cover", null);
            return true;
        }

        public float Reveal()
        {
            if (!_covered) return 0f;
            Transform head = _rig.Head;
            Vector3 centre = head.position;
            _new = Still();
            Capture(centre, _new);
            _material.SetTexture(s_NewStill, _new);
            _material.SetVector(s_NewCentre, centre);
            // No outline shows before its tile's turn, so the dome can be turned about its centre unseen (the
            // stills are read by direction from it): the level hexagon straight ahead goes where the walker
            // looks now, wherever they turned while the space loaded. It turns first and sets off the ripple.
            _dome.transform.rotation = Quaternion.LookRotation(head.forward, Vector3.up);
            int first = _tiles.TileAt(new float3(0f, 0f, 1f));
            int outer = _tiles.Rings(first, _rings);
            _tiles.WaveAxes(first, _waveAxes);
            for (int i = 0; i < _delays.Length; i++)
            {
                _delays[i] = HexDome.Delay(_rings[i]);
                _axes[i] = (Vector3)_waveAxes[i];
            }
            _material.SetFloatArray(s_TileDelays, _delays);
            _material.SetVectorArray(s_TileAxes, _axes);
            _material.SetFloat(s_RevealTime, 0f);
            Log("reveal", null);
            return HexDome.RevealSeconds(outer);
        }

        public bool Show(float revealSeconds)
        {
            if (!_covered) return false;
            string refusal = Refusal();
            if (refusal != null)
            {
                Log("dropped", refusal);
                Uncover();
                return false;
            }
            _material.SetFloat(s_RevealTime, revealSeconds);
            return true;
        }

        public void Uncover()
        {
            _covered = false;
            if (_renderer != null) _renderer.enabled = false;
            if (_material != null)
            {
                _material.SetTexture(s_OldStill, null);
                _material.SetTexture(s_NewStill, null);
            }
            Release(ref _old);
            Release(ref _new);
        }

        public void Dispose()
        {
            Uncover();
            if (_camera != null) UnityEngine.Object.Destroy(_camera.gameObject);
            if (_dome != null) UnityEngine.Object.Destroy(_dome);
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_material != null) UnityEngine.Object.Destroy(_material);
        }

        // Why the dome cannot stand now, or null.
        string Refusal()
        {
            if (_rig.Mode != DisplayMode.Headset) return "hand";
            if (BumpCue.Shown == BumpLevel.TrackingLost) return "trackingLost";
            return null;
        }

        void Log(string state, string reason)
        {
            IEventLog events = Events;
            if (events == null) return;
            if (reason == null) events.Log("space.transition", "state", state);
            else events.LogJson("space.transition", "{\"state\":\"" + state + "\",\"reason\":\"" + reason + "\"}");
        }

        // Six faces of 90 degrees from centre, world-aligned, into the slices of target. What the eyes draw but
        // the head-locked layer: no menu, reticle or cue in a still.
        void Capture(Vector3 centre, RenderTexture target)
        {
            Camera eye = _rig.GetEyeCamera(Eye.Left);
            _camera.cullingMask = eye.cullingMask & ~(1 << _hudLayer);
            _camera.clearFlags = eye.clearFlags;
            _camera.backgroundColor = eye.backgroundColor;
            RenderTexture face = RenderTexture.GetTemporary(FaceDescriptor(24));
            _camera.targetTexture = face;
            _camera.aspect = 1f;
            _camera.ResetProjectionMatrix();
            Transform t = _camera.transform;
            try
            {
                RenderFaces(HideFromStill, s_Faces.Length, i =>
                {
                    t.SetPositionAndRotation(centre, s_Faces[i]);
                    _camera.Render();
                    Graphics.CopyTexture(face, 0, 0, target, i, 0);
                });
            }
            finally
            {
                _camera.targetTexture = null;
                RenderTexture.ReleaseTemporary(face);
            }
        }

        static RenderTexture Still()
        {
            RenderTextureDescriptor desc = FaceDescriptor(0);
            desc.dimension = TextureDimension.Tex2DArray;
            desc.volumeDepth = s_Faces.Length;
            RenderTexture still = RenderTexture.GetTemporary(desc);
            still.filterMode = FilterMode.Bilinear;
            still.wrapMode = TextureWrapMode.Clamp;
            return still;
        }

        static RenderTextureDescriptor FaceDescriptor(int depthBits)
        {
            var desc = new RenderTextureDescriptor(FaceSize, FaceSize, RenderTextureFormat.ARGB32, depthBits)
            {
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false,
                sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear,
            };
            return desc;
        }

        static void Release(ref RenderTexture texture)
        {
            if (texture == null) return;
            RenderTexture.ReleaseTemporary(texture);
            texture = null;
        }

        static Mesh BuildMesh(HexDome dome)
        {
            int count = dome.Positions.Count;
            var positions = new List<Vector3>(count);
            var axes = new List<Vector3>(count);
            var centres = new List<Vector4>(count);
            var rims = new List<Vector2>(count);
            for (int i = 0; i < count; i++)
            {
                positions.Add(dome.Positions[i]);
                axes.Add(dome.VertexAxes[i]);
                centres.Add(dome.VertexCentres[i]);
                rims.Add(new Vector2(dome.Rims[i], 0f));
            }
            var mesh = new Mesh { name = "Atlas Hex Dome", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(positions);
            mesh.SetNormals(axes);
            mesh.SetUVs(0, centres);
            mesh.SetUVs(1, rims);
            mesh.SetTriangles(dome.Triangles, 0, false);
            // The tiles move in the vertex shader, up to a tile's width off the sphere.
            float reach = HexDome.Radius + 1f;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (2f * reach));
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}

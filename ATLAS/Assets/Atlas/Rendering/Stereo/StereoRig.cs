using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Atlas.Rendering
{
    /// <summary>
    /// Our own two-camera stereo rig (Master Plan D7, task P1-A04), built in code under the tracked camera:
    /// two eye cameras at -/+ IPD / 2, each drawing with a full viewport into its own eye texture through the
    /// one shared renderer, with the projection of the lens model. No camera image, no
    /// post-processing. The AR camera only carries the pose.
    /// </summary>
    public sealed class StereoRig : MonoBehaviour, IStereoRig
    {
        public const float DefaultIpd = 0.064f;
        public const float MinIpd = 0.055f;
        public const float MaxIpd = 0.075f;
        /// <summary>Near and far planes of the eye cameras. Metres. The fog is opaque long before the far plane.</summary>
        public const float NearPlane = 0.05f;
        public const float FarPlane = 60f;

        // Layer numbers of the layer table (lane A contract section 12.1).
        const int HudLayer = 7;
        const int SpectatorLayer = 23;
        const int XrSimulationLayer = 30;
        const int EyeCullingMask = ~((1 << SpectatorLayer) | (1 << XrSimulationLayer));

        Transform _eyes;
        Transform _hud;
        Transform _leftTransform;
        Transform _rightTransform;
        Camera _left;
        Camera _right;
        Camera _trackedCamera;
        Behaviour _handBackground;
        LensModel _lens;
        int _lensVersion = -1;
        float _ipd = DefaultIpd;
        DisplayMode _mode = DisplayMode.Hand;
        // The eye-target override (FieldFit's wide source) and the targets the eye textures gave the cameras.
        RenderTexture _overrideLeft, _overrideRight, _normalLeft, _normalRight;
        float _overrideFov;
        bool _overridden;

        /// <summary>
        /// Builds the rig under the tracked camera: "Atlas Head" with this component, "Atlas Eyes", the two eye
        /// cameras and "Hud". The rig starts in <see cref="DisplayMode.Hand"/>. Nothing is searched in the scene:
        /// the tracked camera's own Camera and AR background component are the only things looked up, on the
        /// transform that was handed in.
        /// </summary>
        public static StereoRig Create(Transform trackedCamera, LensModel lens)
        {
            if (trackedCamera == null) throw new ArgumentNullException(nameof(trackedCamera));
            if (lens == null) throw new ArgumentNullException(nameof(lens));

            var head = new GameObject("Atlas Head");
            head.transform.SetParent(trackedCamera, false);
            var rig = head.AddComponent<StereoRig>();
            rig.Build(trackedCamera, lens);
            return rig;
        }

        void Build(Transform trackedCamera, LensModel lens)
        {
            _lens = lens;
            _trackedCamera = trackedCamera.GetComponent<Camera>();
            // AR Foundation is not referenced from this assembly, so the background component is found by name.
            _handBackground = trackedCamera.GetComponent("ARCameraBackground") as Behaviour;

            _eyes = NewChild("Atlas Eyes", transform, 0);
            _leftTransform = NewChild("Eye Left", _eyes, 0);
            _rightTransform = NewChild("Eye Right", _eyes, 0);
            _hud = NewChild("Hud", _eyes, HudLayer);

            _left = AddEyeCamera(_leftTransform, 0);
            _right = AddEyeCamera(_rightTransform, 1);

            PlaceEyes();
            ApplyProjection();
            ApplyMode();
        }

        static Transform NewChild(string name, Transform parent, int layer)
        {
            var child = new GameObject(name) { layer = layer };
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        static Camera AddEyeCamera(Transform eye, int order)
        {
            var camera = eye.gameObject.AddComponent<Camera>();
            camera.depth = order;                         // URP sorts cameras by (int)depth: whole numbers only
            camera.rect = new Rect(0f, 0f, 1f, 1f);       // any other rect costs an intermediate texture
            camera.nearClipPlane = NearPlane;
            camera.farClipPlane = FarPlane;
            camera.cullingMask = EyeCullingMask;
            camera.clearFlags = CameraClearFlags.Skybox;  // the look's background; black when none is set
            camera.backgroundColor = Color.black;
            camera.allowHDR = false;
            camera.allowMSAA = true;                      // the eye texture's own sample count applies
            camera.allowDynamicResolution = false;
            camera.useOcclusionCulling = false;

            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresDepthTexture = false;
            data.requiresColorTexture = false;
            data.renderShadows = false;
            data.allowXRRendering = false;
            data.SetRenderer(0);                          // one shared renderer for both eyes
            return camera;
        }

        void OnEnable()
        {
            RenderPipelineManager.beginContextRendering += OnBeginContext;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContext;
        }

        // Once per frame, before any camera is culled: pick up live edits of the lens model, and keep an
        // override in place when the quality ladder has swapped the eye textures under it.
        void OnBeginContext(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (_lens != null && _lens.Version != _lensVersion)
                ApplyProjection();
            if (_overridden)
                AssignOverrideTargets();
        }

        void ApplyProjection()
        {
            _lensVersion = _lens.Version;
            if (_overridden)
            {
                _left.projectionMatrix = OverrideProjection(_overrideLeft);
                _right.projectionMatrix = OverrideProjection(_overrideRight);
                return;
            }
            _left.projectionMatrix = _lens.GetProjection(Eye.Left, NearPlane, FarPlane);
            _right.projectionMatrix = _lens.GetProjection(Eye.Right, NearPlane, FarPlane);
        }

        Matrix4x4 OverrideProjection(RenderTexture target)
        {
            return Matrix4x4.Perspective(_overrideFov, (float)target.width / target.height, NearPlane, FarPlane);
        }

        // A target that is not the override was put there by the eye textures: it is what Clear gives back.
        void AssignOverrideTargets()
        {
            if (_left.targetTexture != _overrideLeft)
            {
                _normalLeft = _left.targetTexture;
                _left.targetTexture = _overrideLeft;
            }
            if (_right.targetTexture != _overrideRight)
            {
                _normalRight = _right.targetTexture;
                _right.targetTexture = _overrideRight;
            }
        }

        void PlaceEyes()
        {
            _leftTransform.localPosition = new Vector3(-0.5f * _ipd, 0f, 0f);
            _rightTransform.localPosition = new Vector3(0.5f * _ipd, 0f, 0f);
        }

        void ApplyMode()
        {
            bool headset = _mode == DisplayMode.Headset;
            _left.enabled = headset;
            _right.enabled = headset;
            // The tracked camera keeps its pose driver and its AR camera manager; only its drawing stops.
            if (_trackedCamera != null) _trackedCamera.enabled = !headset;
            if (_handBackground != null) _handBackground.enabled = !headset;
        }

        /// <summary>The lens model the eye projections come from.</summary>
        public LensModel Lens => _lens;

        public DisplayMode Mode => _mode;

        /// <summary>Raised by <see cref="SetMode"/> after the cameras have been switched.</summary>
        public event Action<DisplayMode> ModeChanged;

        public void SetMode(DisplayMode mode)
        {
            if (mode == _mode)
                return;
            _mode = mode;
            ApplyMode();
            ModeChanged?.Invoke(mode);
        }

        public float Ipd
        {
            get => _ipd;
            set
            {
                float clamped = math.clamp(value, MinIpd, MaxIpd);
                if (clamped == _ipd)
                    return;
                _ipd = clamped;
                PlaceEyes();
            }
        }

        public float3 HeadOffset
        {
            get => transform.localPosition;
            set => transform.localPosition = value;
        }

        public quaternion PredictionOffset
        {
            get => _eyes.localRotation;
            set => _eyes.localRotation = value;
        }

        public Transform Head => transform;
        public Transform Hud => _hud;

        /// <summary>True between <see cref="OverrideEyeTargets"/> and <see cref="ClearEyeTargetOverride"/>.</summary>
        public bool EyeTargetsOverridden => _overridden;
        /// <summary>The overriding target of an eye; null when there is no override.</summary>
        public RenderTexture GetEyeTargetOverride(Eye eye) => _overridden ? (eye == Eye.Left ? _overrideLeft : _overrideRight) : null;

        /// <summary>
        /// The eye cameras draw into these textures instead of the eye textures, with a symmetric frustum of
        /// the given vertical field of view and each texture's own aspect (FieldFit's wide source, Master Plan
        /// section 10.4.3). The override holds while the quality ladder swaps the eye textures; the lens blit
        /// then samples these textures. Calling it again replaces the textures and the field of view.
        /// </summary>
        public void OverrideEyeTargets(RenderTexture left, RenderTexture right, float verticalFovDegrees)
        {
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right == null) throw new ArgumentNullException(nameof(right));
            if (_overridden)
                AssignOverrideTargets();   // take note of a swap made since the last frame
            else
            {
                _normalLeft = _left.targetTexture;
                _normalRight = _right.targetTexture;
            }
            _overridden = true;
            _overrideLeft = left;
            _overrideRight = right;
            _overrideFov = verticalFovDegrees;
            _left.targetTexture = left;
            _right.targetTexture = right;
            ApplyProjection();
        }

        /// <summary>Gives the eye cameras back the eye textures and the projection of the lens model.</summary>
        public void ClearEyeTargetOverride()
        {
            if (!_overridden)
                return;
            AssignOverrideTargets();
            _overridden = false;
            _left.targetTexture = _normalLeft;
            _right.targetTexture = _normalRight;
            _overrideLeft = _overrideRight = _normalLeft = _normalRight = null;
            ApplyProjection();
        }

        public Camera GetEyeCamera(Eye eye) => eye == Eye.Left ? _left : _right;

        public float3 GetEyePosition(Eye eye) => (eye == Eye.Left ? _leftTransform : _rightTransform).position;

        /// <summary>The frustum of an eye as tangents, from the lens model.</summary>
        public EyeFrustum GetEyeFrustum(Eye eye) => _lens.GetFrustum(eye);

        public void GetEyeFrustumPlanes(Eye eye, Plane[] planes)
        {
            Camera camera = GetEyeCamera(eye);
            GeometryUtility.CalculateFrustumPlanes(camera.projectionMatrix * camera.worldToCameraMatrix, planes);
        }
    }
}

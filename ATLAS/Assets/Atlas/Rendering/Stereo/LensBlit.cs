using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Atlas.Rendering
{
    /// <summary>
    /// The Screen camera and its one full-screen pass (Master Plan D7, section 8.2, task P1-A05): both eye
    /// textures are sampled with the per-eye barrel pre-distortion of the lens model, combined with the upscale
    /// to the screen. Switchable to a plain side-by-side copy. The pass is a render-graph raster pass enqueued
    /// for this camera only; under URP 17 only a camera that targets the screen can write it.
    /// </summary>
    public sealed class LensBlit : MonoBehaviour
    {
        /// <summary>Name of the shader file under a Resources folder, so a player build always includes it.</summary>
        public const string ShaderResource = "AtlasLensBlit";
        public const string ShaderName = "Atlas/LensBlit";
        /// <summary>Camera.depth of the Screen camera: after both eye cameras (0 and 1).</summary>
        public const int ScreenCameraOrder = 10;

        static readonly int s_LensScreen = Shader.PropertyToID("_LensScreen");
        static readonly int s_LensK = Shader.PropertyToID("_LensK");
        static readonly int s_LensFrustumL = Shader.PropertyToID("_LensFrustumL");
        static readonly int s_LensFrustumR = Shader.PropertyToID("_LensFrustumR");

        Camera _camera;
        UniversalAdditionalCameraData _cameraData;
        Material _material;
        LensBlitPass _pass;
        EyeTextureSet _eyes;
        LensModel _lens;
        StereoRig _rig;
        bool _distortion;
        bool _constantsValid;
        int _lensVersion;

        /// <summary>
        /// Creates the "Screen" object under parent with the Screen camera and the lens pass. The camera is
        /// enabled only while the rig is in <see cref="DisplayMode.Headset"/>.
        /// </summary>
        public static LensBlit Create(Transform parent, StereoRig rig, EyeTextureSet eyes, LensModel lens)
        {
            if (rig == null) throw new ArgumentNullException(nameof(rig));
            if (eyes == null) throw new ArgumentNullException(nameof(eyes));
            if (lens == null) throw new ArgumentNullException(nameof(lens));

            var screen = new GameObject("Screen");
            screen.transform.SetParent(parent, false);
            var blit = screen.AddComponent<LensBlit>();
            blit.Build(rig, eyes, lens);
            return blit;
        }

        void Build(StereoRig rig, EyeTextureSet eyes, LensModel lens)
        {
            _rig = rig;
            _eyes = eyes;
            _lens = lens;
            // Through the lenses on the phone; a plain copy in the Editor, where there are no lenses.
            _distortion = Application.isMobilePlatform;

            _camera = gameObject.AddComponent<Camera>();
            _camera.targetTexture = null;
            _camera.depth = ScreenCameraOrder;
            _camera.cullingMask = 0;                             // draws no object: the pass is all it does
            _camera.clearFlags = CameraClearFlags.SolidColor;    // black if the pass is ever missing
            _camera.backgroundColor = Color.black;
            _camera.allowMSAA = false;                           // the screen is never multisampled
            _camera.allowHDR = false;
            _camera.allowDynamicResolution = false;
            _camera.useOcclusionCulling = false;

            _cameraData = _camera.GetUniversalAdditionalCameraData();
            _cameraData.renderType = CameraRenderType.Base;
            _cameraData.renderPostProcessing = false;
            _cameraData.antialiasing = AntialiasingMode.None;
            _cameraData.requiresDepthTexture = false;
            _cameraData.requiresColorTexture = false;
            _cameraData.renderShadows = false;
            _cameraData.allowXRRendering = false;
            _cameraData.SetRenderer(0);

            Shader shader = Resources.Load<Shader>(ShaderResource);
            if (shader == null)
                shader = Shader.Find(ShaderName);
            if (shader != null)
                _material = new Material(shader) { name = "Atlas Lens Blit", hideFlags = HideFlags.DontSave };
            else
                Debug.LogError("Atlas: shader '" + ShaderName + "' not found in Resources; the headset view stays black.");

            _pass = new LensBlitPass();

            _rig.ModeChanged += OnModeChanged;
            _camera.enabled = _rig.Mode == DisplayMode.Headset;

            // Facts only a device run can show (formats, on-tile resolve preconditions): logged once.
            Debug.Log("Atlas: eye textures " + eyes.Left.width + " x " + eyes.Left.height
                + " " + eyes.Left.graphicsFormat + " / " + eyes.Left.depthStencilFormat
                + ", memoryless " + SystemInfo.supportsMemorylessTextures
                + ", store-and-resolve " + SystemInfo.supportsStoreAndResolveAction
                + ", sRGB blit to back buffer " + Display.main.requiresSrgbBlitToBackbuffer
                + ", watermark visible " + Watermark.IsVisible()
                + ", screen " + Screen.width + " x " + Screen.height + " at " + Screen.dpi + " dpi");
        }

        /// <summary>False: plain side by side, no distortion, each eye texture stretched over its half.</summary>
        public bool Distortion
        {
            get => _distortion;
            set
            {
                if (_distortion == value)
                    return;
                _distortion = value;
                _constantsValid = false;
            }
        }

        /// <summary>The five lens numbers. Setting them re-shapes the eye frusta and the blit together.</summary>
        public LensParams Lens
        {
            get => _lens.Params;
            set => _lens.Params = value;
        }

        /// <summary>The lens model shared with the stereo rig.</summary>
        public LensModel Model => _lens;

        /// <summary>Physical size of the screen in landscape. Metres.</summary>
        public float2 ScreenSizeMetres => new float2(_lens.ScreenWidth, _lens.ScreenHeight);

        /// <summary>The Screen camera: the only camera that draws to the display in headset mode.</summary>
        public Camera ScreenCamera => _camera;

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        }

        void OnDestroy()
        {
            if (_rig != null) _rig.ModeChanged -= OnModeChanged;
            CoreUtils.Destroy(_material);
        }

        void OnModeChanged(DisplayMode mode)
        {
            if (_camera != null) _camera.enabled = mode == DisplayMode.Headset;
        }

        // Runs just before URP sets this camera up, which is when a pass can be queued for it alone.
        void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _camera || _material == null)
                return;
            ScriptableRenderer renderer = _cameraData.scriptableRenderer;
            if (renderer == null)
                return;

            if (!_constantsValid || _lensVersion != _lens.Version)
                UploadConstants();

            _pass.Setup(_material, _eyes.LeftHandle, _eyes.RightHandle);
            renderer.EnqueuePass(_pass);
        }

        void UploadConstants()
        {
            _lensVersion = _lens.Version;
            _constantsValid = true;
            _lens.GetBlitConstants(out float4 screen, out float4 k, out float4 frustumLeft, out float4 frustumRight);
            // z: distortion on. w: the back buffer wants sRGB values written by hand (the eye textures are
            // sRGB, so sampling gives linear values).
            k.z = _distortion ? 1f : 0f;
            k.w = Display.main.requiresSrgbBlitToBackbuffer ? 1f : 0f;
            _material.SetVector(s_LensScreen, screen);
            _material.SetVector(s_LensK, k);
            _material.SetVector(s_LensFrustumL, frustumLeft);
            _material.SetVector(s_LensFrustumR, frustumRight);
        }
    }
}

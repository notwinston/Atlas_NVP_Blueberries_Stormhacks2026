using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

namespace Atlas.Scan
{
    /// <summary>
    /// Works around the ARKit provider on iOS 27 handing the camera background a portrait display transform
    /// in a landscape app: Screen.orientation and the projection are landscape, but the display matrix swaps
    /// the image axes, so the camera picture is turned 90 degrees. Just before the tracked camera draws, an
    /// axis-swapped matrix is replaced by the landscape one built from the image and screen sizes. A provider
    /// that sends a landscape matrix is left alone.
    /// </summary>
    sealed class ArBackgroundOrientation
    {
        static readonly int DisplayTransformId = Shader.PropertyToID("_UnityDisplayTransform");

        readonly ARCameraManager _manager;
        readonly Camera _camera;
        ARCameraBackground _background;
        Matrix4x4 _reported;
        bool _hasReported;
        float _imageAspect;
        bool _logged;

        ArBackgroundOrientation(ARCameraManager manager, Camera camera)
        {
            _manager = manager;
            _camera = camera;
            _manager.frameReceived += OnFrame;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        public static void Attach(ARCameraManager manager, Camera camera)
        {
            if (manager == null || camera == null) return;
            new ArBackgroundOrientation(manager, camera);
        }

        void OnFrame(ARCameraFrameEventArgs args)
        {
            if (args.displayMatrix.HasValue)
            {
                _reported = args.displayMatrix.Value;
                _hasReported = true;
            }
            if (args.textures != null && args.textures.Count > 0 && args.textures[0] != null && args.textures[0].height > 0)
                _imageAspect = (float)args.textures[0].width / args.textures[0].height;
        }

        void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (_camera == null)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
                return;
            }
            if (camera != _camera || !_hasReported || _imageAspect <= 0f) return;

            ScreenOrientation orientation = Screen.orientation;
            bool landscape = orientation == ScreenOrientation.LandscapeLeft || orientation == ScreenOrientation.LandscapeRight;
            // A landscape transform keeps u on the screen's x: m00 is about +-1. A portrait one swaps them: m00 is 0.
            if (!landscape || Mathf.Abs(_reported.m00) > 0.5f) return;

            if (_background == null) _background = _camera.GetComponent<ARCameraBackground>();
            Material material = _background != null ? _background.material : null;
            if (material == null) return;

            material.SetMatrix(DisplayTransformId, Landscape(orientation, _imageAspect, (float)Screen.width / Screen.height));
            if (!_logged)
            {
                _logged = true;
                Debug.Log("Atlas: AR background display transform corrected to " + orientation + " (the provider sent a portrait one)");
            }
        }

        /// <summary>
        /// Screen uv to camera-image uv for the shader's mul(float4(uv, 1, 1), M), the image cropped to fill
        /// the screen. LandscapeLeft is the sensor's own orientation; LandscapeRight is the same turned 180.
        /// </summary>
        static Matrix4x4 Landscape(ScreenOrientation orientation, float imageAspect, float screenAspect)
        {
            float ku = 1f, kv = 1f;
            if (screenAspect >= imageAspect) kv = imageAspect / screenAspect;   // wider screen: crop top and bottom
            else ku = screenAspect / imageAspect;                               // narrower: crop the sides

            // LandscapeLeft: u = 0.5 + ku (x - 0.5), v = 0.5 - kv (y - 0.5).
            float su = ku, sv = -kv;
            if (orientation == ScreenOrientation.LandscapeRight) { su = -ku; sv = kv; }

            Matrix4x4 m = Matrix4x4.zero;
            m.m00 = su;                     // u from x
            m.m11 = sv;                     // v from y
            m.m20 = 0.5f - 0.5f * su;       // offsets
            m.m21 = 0.5f - 0.5f * sv;
            m.m22 = 1f;
            m.m33 = 1f;
            return m;
        }
    }
}

using Atlas.World;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Atlas.Scan
{
    /// <summary>
    /// LiDAR depth for Bump Guard (Master Plan sections 4.2 and 4.10, Appendix C.4): the environment depth
    /// CPU image of AR Foundation, copied into one reused float buffer in metres, with the intrinsics of the
    /// image and the world pose of the tracked camera in the frame the image was read.
    /// </summary>
    public sealed class ArDepthSource : IDepthSource
    {
        readonly AROcclusionManager _occlusion;
        readonly ARCameraManager _camera;
        readonly Transform _trackedCamera;

        float[] _buffer;
        int _width, _height;
        int _sequence;
        double _lastTimestamp = double.NegativeInfinity;
        bool _sizeLogged;
        bool _missingLogged;
        double _firstAsked = double.NaN;

        /// <summary>Seconds without a first image before a warning is logged.</summary>
        public const double MissingAfterSeconds = 5.0;

        public ArDepthSource(AROcclusionManager occlusion, ARCameraManager camera, Transform trackedCamera)
        {
            _occlusion = occlusion;
            _camera = camera;
            _trackedCamera = trackedCamera;
        }

        /// <summary>True on a device whose occlusion subsystem delivers an environment depth image (LiDAR).</summary>
        public bool Available
        {
            get
            {
                if (_occlusion == null || _camera == null || _trackedCamera == null) return false;
                XROcclusionSubsystemDescriptor descriptor = _occlusion.descriptor;
                return descriptor != null && descriptor.environmentDepthImageSupported == Supported.Supported;
            }
        }

        /// <summary>Images delivered since start.</summary>
        public int Sequence { get { return _sequence; } }
        /// <summary>Size of the last image; 0 before the first.</summary>
        public int Width { get { return _width; } }
        public int Height { get { return _height; } }
        /// <summary>How many pixels of the last image were returns between 0.3 and 5 m. For the start-up check (gate G1).</summary>
        public unsafe int CountReturns()
        {
            if (_buffer == null) return 0;
            fixed (float* p = _buffer) return DepthKernel.CountReturns(p, _width * _height);
        }

        // Gate G1 needs a depth image at the chosen video format: say so once when none arrives.
        void NoteMissing(double time)
        {
            if (_sequence > 0 || _missingLogged) return;
            if (double.IsNaN(_firstAsked)) _firstAsked = time;
            if (time - _firstAsked < MissingAfterSeconds) return;
            _missingLogged = true;
            Debug.LogWarning("Atlas: ARKit depth: no image " + MissingAfterSeconds + " s after the first request" +
                             (Available ? "" : " (the device reports no environment depth)"));
        }

        public unsafe bool TryRead(double time, out DepthFrame frame)
        {
            frame = default(DepthFrame);
            if (_occlusion == null || _camera == null || _trackedCamera == null) return false;
            if (!_occlusion.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage image))
            {
                NoteMissing(time);
                return false;
            }
            using (image)       // the native image is released on every path
            {
                if (image.format != XRCpuImage.Format.DepthFloat32 || image.planeCount < 1) return false;
                int width = image.width, height = image.height;
                if (width <= 0 || height <= 0) return false;
                // ARKit hands out the same image until the next camera frame.
                if (image.timestamp == _lastTimestamp) return false;

                if (!_camera.TryGetIntrinsics(out XRCameraIntrinsics intrinsics) || intrinsics.resolution.x <= 0 ||
                    intrinsics.resolution.y <= 0)
                    return false;

                XRCpuImage.Plane plane = image.GetPlane(0);
                if (plane.rowStride < width * plane.pixelStride || plane.pixelStride < sizeof(float) ||
                    plane.data.Length < (height - 1) * plane.rowStride + width * plane.pixelStride)
                    return false;

                if (_buffer == null || width != _width || height != _height)
                {
                    // Once, or when the video format changes. Never on a steady frame.
                    _buffer = new float[width * height];
                    _width = width;
                    _height = height;
                    if (!_sizeLogged)
                    {
                        _sizeLogged = true;
                        if (width != DepthFrame.NominalWidth || height != DepthFrame.NominalHeight)
                            Debug.LogWarning("Atlas: ARKit depth " + width + "x" + height + " (expected " +
                                             DepthFrame.NominalWidth + "x" + DepthFrame.NominalHeight + ")");
                        else
                            Debug.Log("Atlas: ARKit depth " + width + "x" + height);
                    }
                }

                byte* source = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(plane.data);
                fixed (float* destination = _buffer)
                    DepthKernel.CopyRows(source, plane.rowStride, plane.pixelStride, width, height, destination);

                _lastTimestamp = image.timestamp;
                _sequence++;

                // The depth image covers the camera image's field of view at its own resolution.
                float sx = width / (float)intrinsics.resolution.x;
                float sy = height / (float)intrinsics.resolution.y;
                frame.Depth = _buffer;
                frame.Width = width;
                frame.Height = height;
                frame.Fx = intrinsics.focalLength.x * sx;
                frame.Fy = intrinsics.focalLength.y * sy;
                frame.Cx = intrinsics.principalPoint.x * sx;
                frame.Cy = intrinsics.principalPoint.y * sy;
                _trackedCamera.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
                frame.Position = position;
                frame.Rotation = rotation;
                frame.Time = time;
                frame.Sequence = _sequence;
                return true;
            }
        }
    }
}

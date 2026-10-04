using System.Threading;
using Atlas.World;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Atlas.Scan
{
    /// <summary>
    /// Photos of the room's furniture for the Forge (Master Plan sections 4.5 and 7.4, task P1-C08; contract
    /// lane C section 11). During the sweep it keeps a ring of camera images with their poses; when the room
    /// is final it crops each object out of its best views and hands the JPEGs to the photo sink. The thin
    /// engine side of PhotoCaptureCore: it only reads the AR camera's CPU image. With no AR camera (the Editor,
    /// the Station) no key frame is taken, and every object is reported as having no photo, so it takes its
    /// built-in mesh. No camera image is ever written to disk, and none outlives the hand-over.
    /// </summary>
    public sealed class ObjectPhotoCapture : IObjectPhotoCapture
    {
        const string LogPrefix = "Atlas: ";

        static readonly WaitCallback MeasureSharpness = state => ((KeyFrame)state).MeasureSharpness();

        readonly ArSessionRig _rig;
        readonly PhotoCaptureCore _core;
        bool _formatLogged;
        bool _wasBusy;
        RoomModel _captured;      // the room whose capture was started since the last sweep or cancel

        /// <summary>rig may be null (tests, the Station): no key frame is then taken.</summary>
        public ObjectPhotoCapture(ArSessionRig rig)
        {
            _rig = rig;
            _core = new PhotoCaptureCore(new UnityJpegEncoder());
        }

        /// <summary>Where the photos go; null means a sink that only counts.</summary>
        public IObjectPhotoSink Sink
        {
            get { return _core.Sink; }
            set { _core.Sink = value; }
        }

        public int KeyFrameCount { get { return _core.KeyFrameCount; } }
        /// <summary>Selection, cropping or delivery is under way.</summary>
        public bool Busy { get { return _core.Busy; } }

        /// <summary>The sweep starts: the ring is emptied and key frames are collected from now on.</summary>
        public void BeginSweep()
        {
            _core.BeginSweep();
            _wasBusy = false;
            _captured = null;
        }

        /// <summary>
        /// Once per frame during the sweep, with the raw pose and this frame's depth (may be invalid). Costs a
        /// few comparisons on most frames and one image copy on a key frame.
        /// </summary>
        public void OnFrame(in HeadPose raw, in DepthFrame depth)
        {
            if (!_core.Sweeping || _rig == null) return;
            if (!_core.Ring.Wants(in raw)) return;

            ARCameraManager camera = _rig.Camera;
            if (camera == null || !camera.enabled || camera.subsystem == null) return;
            // XR Simulation answers with an all-zero struct.
            if (!camera.TryGetIntrinsics(out XRCameraIntrinsics intrinsics) || intrinsics.resolution.x <= 0 ||
                intrinsics.resolution.y <= 0 || !(intrinsics.focalLength.x > 0f) || !(intrinsics.focalLength.y > 0f))
                return;
            if (!camera.TryAcquireLatestCpuImage(out XRCpuImage image)) return;
            using (image)       // released at once on every path: holding camera images stalls ARKit
            {
                Store(in image, in intrinsics, in raw, in depth);
            }
        }

        /// <summary>
        /// Starts selection and cropping for a final room; the sink is called as crops finish. The key frames
        /// were taken in the AR session frame, in which the room's anchor pose is given.
        /// </summary>
        public void BeginCapture(RoomModel room)
        {
            if (room == null) return;
            // The scan session starts the capture the moment the room is final. A second call for the same
            // room (the activation sequence of contract section 14.2 names one) must not drop the crops
            // that are under way.
            if (ReferenceEquals(room, _captured)) return;
            _captured = room;
            int frames = _core.KeyFrameCount;
            bool swept = _core.Sweeping;
            _core.BeginCapture(room, room.Anchor.Position, room.Anchor.Yaw);
            _wasBusy = _core.Busy;
            if (swept)
                Debug.Log(LogPrefix + "object photos: choosing from " + frames + " key frames for " + room.MeshedObjectCount + " objects");
        }

        /// <summary>Delivers finished crops, one object per frame.</summary>
        public void Tick()
        {
            if (!_wasBusy) return;
            _core.Tick();
            if (_core.Busy) return;
            _wasBusy = false;
            Debug.Log(LogPrefix + "object photos: " + _core.Submitted + " objects sent, " + _core.NoPhoto +
                      " take the built-in mesh; key frames released");
            if (_core.Errors > 0)
                Debug.LogWarning(LogPrefix + "object photos: " + _core.Errors + " objects failed to crop (" + _core.FirstError + ")");
        }

        /// <summary>Clears the ring and drops pending crops.</summary>
        public void Cancel()
        {
            _core.Cancel();
            _wasBusy = false;
            _captured = null;
        }

        unsafe void Store(in XRCpuImage image, in XRCameraIntrinsics intrinsics, in HeadPose raw, in DepthFrame depth)
        {
            KeyFrameFormat format;
            switch (image.format)
            {
                case XRCpuImage.Format.IosYpCbCr420_8BiPlanarFullRange: format = KeyFrameFormat.YCbCr420; break;
                case XRCpuImage.Format.RGBA32: format = KeyFrameFormat.Rgba32; break;
                case XRCpuImage.Format.BGRA32: format = KeyFrameFormat.Bgra32; break;
                case XRCpuImage.Format.ARGB32: format = KeyFrameFormat.Argb32; break;
                case XRCpuImage.Format.RGB24: format = KeyFrameFormat.Rgb24; break;
                default:
                    if (!_formatLogged)
                    {
                        _formatLogged = true;
                        Debug.LogWarning(LogPrefix + "object photos: camera image format " + image.format + " is not handled; furniture takes the built-in meshes");
                    }
                    return;
            }
            bool biplanar = format == KeyFrameFormat.YCbCr420;
            if (image.planeCount < (biplanar ? 2 : 1)) return;

            int step = KeyFrameRing.StepFor(image.width);
            int width = (image.width / step) & ~1, height = (image.height / step) & ~1;
            if (width < 16 || height < 16) return;
            int bytesPerPixel = KeyFrame.BytesPerPixelOf(format);

            XRCpuImage.Plane plane0 = image.GetPlane(0);
            if (!Covers(in plane0, width, height, step, bytesPerPixel)) return;
            XRCpuImage.Plane plane1 = default(XRCpuImage.Plane);
            if (biplanar)
            {
                plane1 = image.GetPlane(1);
                if (!Covers(in plane1, width / 2, height / 2, step, 2)) return;
            }

            // Everything is known to fit: only now does the ring give up its oldest frame.
            KeyFrame frame = _core.Ring.Add(in raw);
            frame.EnsureImage(format, width, height);
            fixed (byte* into = frame.Plane0)
                PhotoKernel.CopyPlane((byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(plane0.data), plane0.rowStride,
                    plane0.pixelStride, bytesPerPixel, step, width, height, into);
            if (biplanar)
            {
                fixed (byte* into = frame.Plane1)
                    PhotoKernel.CopyPlane((byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(plane1.data), plane1.rowStride,
                        plane1.pixelStride, 2, step, width / 2, height / 2, into);
            }

            // The intrinsics are given for intrinsics.resolution; the image may be delivered at another size.
            float sx = image.width / (float)intrinsics.resolution.x / step;
            float sy = image.height / (float)intrinsics.resolution.y / step;
            frame.Intrinsics = new float4(intrinsics.focalLength.x * sx, intrinsics.focalLength.y * sy,
                intrinsics.principalPoint.x * sx, intrinsics.principalPoint.y * sy);

            // The camera itself, in the session frame: the room's anchor is given there, wherever the XR
            // Origin has been moved to.
            XROrigin origin = _rig.Origin;
            Camera tracked = origin != null ? origin.Camera : null;
            if (tracked != null)
            {
                Transform cameraTransform = tracked.transform;
                Transform space = origin.TrackablesParent;
                cameraTransform.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
                if (space != null)
                {
                    position = space.InverseTransformPoint(position);
                    rotation = Quaternion.Inverse(space.rotation) * rotation;
                }
                frame.Position = position;
                frame.Rotation = rotation;
            }

            if (depth.IsValid && depth.Depth.Length >= depth.Width * depth.Height)
            {
                frame.EnsureDepth(depth.Width, depth.Height);
                fixed (float* metres = depth.Depth)
                fixed (ushort* millimetres = frame.Depth)
                    PhotoKernel.DepthToMillimetres(metres, depth.Width * depth.Height, millimetres);
            }

            // Sharpness off the frame path; the frame is usable (with sharpness 0) until it lands.
            ThreadPool.UnsafeQueueUserWorkItem(MeasureSharpness, frame);
        }

        // The plane holds every byte the copy will read.
        static bool Covers(in XRCpuImage.Plane plane, int outWidth, int outHeight, int step, int bytesPerPixel)
        {
            if (plane.pixelStride < bytesPerPixel || plane.rowStride <= 0) return false;
            long last = (long)(outHeight - 1) * step * plane.rowStride + (long)(outWidth - 1) * step * plane.pixelStride + bytesPerPixel;
            return plane.data.IsCreated && plane.data.Length >= last;
        }

        /// <summary>JPEG through the engine's thread-safe array encoder (Unity API notes, input-net-assets section 7).</summary>
        sealed class UnityJpegEncoder : IPhotoEncoder
        {
            // Unity's image data starts with the bottom row.
            public bool RowsBottomUp { get { return true; } }

            public byte[] EncodeJpeg(byte[] rgba, int width, int height, int quality)
            {
                return ImageConversion.EncodeArrayToJPG(rgba, GraphicsFormat.R8G8B8A8_SRGB, (uint)width, (uint)height,
                    (uint)(width * 4), quality);
            }
        }
    }
}

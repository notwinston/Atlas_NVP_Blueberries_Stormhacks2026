using System;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Scan
{
    /// <summary>How the pixels of a key frame are stored.</summary>
    public enum KeyFrameFormat : byte
    {
        /// <summary>ARKit's camera image: Plane0 is luma, Plane1 is half-size Cb, Cr pairs, full range.</summary>
        YCbCr420 = 0,
        Rgba32 = 1,
        Bgra32 = 2,
        Argb32 = 3,
        Rgb24 = 4
    }

    /// <summary>Why a frame that was due to become a key frame did not (counted by the ring and the engine adapter).</summary>
    public enum KeyFrameRefusal : byte
    {
        NotTracking = 0,
        TurningFast = 1,
        /// <summary>No AR camera, or it is off.</summary>
        NoCamera = 2,
        NoIntrinsics = 3,
        /// <summary>The camera gave no CPU image.</summary>
        NoImage = 4,
        /// <summary>The CPU image format is not handled.</summary>
        Format = 5,
        /// <summary>The image is too small or its planes are shorter than they should be.</summary>
        ImageSize = 6
    }

    /// <summary>
    /// One camera image kept during the sweep, with the pose and intrinsics it was taken with and the depth
    /// image of the same frame (contract lane C section 11.1). It lives in memory only and is never written
    /// anywhere (rule 10 of Master Plan section 0.5).
    /// </summary>
    public sealed class KeyFrame
    {
        public KeyFrameFormat Format;
        /// <summary>Pixels of the stored image.</summary>
        public int Width, Height;
        /// <summary>Luma, or the packed colour pixels.</summary>
        public byte[] Plane0;
        /// <summary>Cb, Cr pairs at half size for YCbCr420; unused otherwise.</summary>
        public byte[] Plane1;
        /// <summary>Camera pose in the frame the scan works in (the AR session frame).</summary>
        public float3 Position;
        public quaternion Rotation = quaternion.identity;
        /// <summary>fx, fy, cx, cy in pixels of the stored image, cx and cy from the top-left corner.</summary>
        public float4 Intrinsics;
        /// <summary>Seconds, app clock.</summary>
        public double Time;
        /// <summary>Millimetres along the camera's forward axis, row 0 at the top; 0 is no return. Covers the same view as the image.</summary>
        public ushort[] Depth;
        /// <summary>0 x 0 when the frame came with no depth image.</summary>
        public int DepthWidth, DepthHeight;

        volatile float _sharpness;
        volatile bool _sharpnessReady;
        volatile int _generation;

        /// <summary>Variance of the Laplacian of the reduced luma image; 0 until measured.</summary>
        public float Sharpness { get { return _sharpness; } }
        public bool SharpnessReady { get { return _sharpnessReady; } }

        /// <summary>Bytes of one pixel of Plane0.</summary>
        public int BytesPerPixel { get { return BytesPerPixelOf(Format); } }

        public static int BytesPerPixelOf(KeyFrameFormat format)
        {
            switch (format)
            {
                case KeyFrameFormat.YCbCr420: return 1;
                case KeyFrameFormat.Rgb24: return 3;
                default: return 4;
            }
        }

        /// <summary>Byte offsets of red, green and blue inside a packed pixel.</summary>
        public static void ChannelOffsets(KeyFrameFormat format, out int red, out int green, out int blue)
        {
            switch (format)
            {
                case KeyFrameFormat.Bgra32: red = 2; green = 1; blue = 0; break;
                case KeyFrameFormat.Argb32: red = 1; green = 2; blue = 3; break;
                default: red = 0; green = 1; blue = 2; break;
            }
        }

        /// <summary>Sizes the pixel buffers. They are kept when the size is unchanged, so a reused slot allocates nothing.</summary>
        public void EnsureImage(KeyFrameFormat format, int width, int height)
        {
            int plane0 = width * height * BytesPerPixelOf(format);
            int plane1 = format == KeyFrameFormat.YCbCr420 ? (width / 2) * (height / 2) * 2 : 0;
            if (Plane0 == null || Plane0.Length != plane0) Plane0 = new byte[plane0];
            if (plane1 == 0) Plane1 = null;
            else if (Plane1 == null || Plane1.Length != plane1) Plane1 = new byte[plane1];
            Format = format;
            Width = width;
            Height = height;
        }

        /// <summary>Sizes the depth buffer; 0 x 0 marks the frame as having no depth.</summary>
        public void EnsureDepth(int width, int height)
        {
            int count = width * height;
            if (count > 0 && (Depth == null || Depth.Length != count)) Depth = new ushort[count];
            DepthWidth = count > 0 ? width : 0;
            DepthHeight = count > 0 ? height : 0;
        }

        /// <summary>Stored depth under an image pixel, metres; 0 when there is none.</summary>
        public float DepthAt(float2 imagePixel)
        {
            ushort[] depth = Depth;
            if (depth == null || DepthWidth <= 0 || DepthHeight <= 0 || Width <= 0 || Height <= 0) return 0f;
            int u = (int)(imagePixel.x * DepthWidth / Width), v = (int)(imagePixel.y * DepthHeight / Height);
            if (u < 0 || v < 0 || u >= DepthWidth || v >= DepthHeight) return 0f;
            return depth[v * DepthWidth + u] * 0.001f;
        }

        /// <summary>The camera of this frame for PhotoScore.</summary>
        public PhotoCamera Camera
        {
            get
            {
                return new PhotoCamera { Position = Position, Rotation = Rotation, Intrinsics = Intrinsics, Width = Width, Height = Height };
            }
        }

        // The slot is being refilled: a sharpness still being measured for the old image must not land.
        internal void Reuse()
        {
            _generation = _generation + 1;
            _sharpnessReady = false;
            _sharpness = 0f;
        }

        /// <summary>
        /// Measures the sharpness of the stored image. Safe on a worker thread: when the slot is refilled
        /// meanwhile the result is dropped.
        /// </summary>
        public unsafe void MeasureSharpness()
        {
            int generation = _generation;
            byte[] plane = Plane0;
            int width = Width, height = Height;
            KeyFrameFormat format = Format;
            int bytesPerPixel = BytesPerPixelOf(format);
            if (plane == null || width <= 0 || height <= 0 || plane.Length < (long)width * height * bytesPerPixel) return;
            // Green stands in for luma in a packed colour image.
            int red, green, blue;
            ChannelOffsets(format, out red, out green, out blue);
            float sharpness;
            fixed (byte* p = plane)
                sharpness = PhotoKernel.Sharpness(p, width, height, bytesPerPixel, format == KeyFrameFormat.YCbCr420 ? 0 : green);
            if (generation != _generation) return;
            _sharpness = sharpness;
            _sharpnessReady = true;
        }
    }

    /// <summary>
    /// The key frames of one sweep (contract lane C section 11.1): a new one when the camera has moved 0.25 m
    /// or turned 15 degrees since the last and is turning slower than 30 degrees a second; at most 96, the
    /// oldest dropped first. Engine-free; the adapter fills the pixels.
    /// </summary>
    public sealed class KeyFrameRing
    {
        public const int DefaultCapacity = 96;
        /// <summary>Metres the camera moves before the next key frame.</summary>
        public const float MoveMetres = 0.25f;
        /// <summary>Degrees the camera turns before the next key frame.</summary>
        public const float TurnDegrees = 15f;
        /// <summary>Degrees per second: a faster turn smears the image, so no key frame is taken.</summary>
        public const float MaxTurnRateDeg = 30f;
        /// <summary>Widest image stored. A wider camera image keeps every n-th pixel.</summary>
        public const int MaxStoredWidth = 1280;
        /// <summary>Number of KeyFrameRefusal values.</summary>
        public const int RefusalKinds = 7;

        readonly KeyFrame[] _slots;
        int _oldest, _count;

        bool _hasPrevious, _hasKey;
        quaternion _previousRotation, _keyRotation;
        double _previousTime;
        float3 _keyPosition;
        float _turnRateDeg;
        readonly int[] _refused = new int[RefusalKinds];

        public KeyFrameRing(int capacity = DefaultCapacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _slots = new KeyFrame[capacity];
        }

        public int Capacity { get { return _slots.Length; } }
        public int Count { get { return _count; } }
        /// <summary>Key frames dropped because the ring was full, since the last Clear.</summary>
        public int Evicted { get; private set; }

        /// <summary>
        /// Frames that were due for a key frame and were refused for this reason, since the last ResetCounts.
        /// Clear does not reset it: the summary is printed after the frames have been taken.
        /// </summary>
        public int Refused(KeyFrameRefusal reason)
        {
            return (uint)reason < (uint)RefusalKinds ? _refused[(int)reason] : 0;
        }

        /// <summary>Counts one refusal the adapter met itself (no camera, no image, a format it cannot read).</summary>
        public void NoteRefused(KeyFrameRefusal reason)
        {
            if ((uint)reason < (uint)RefusalKinds) _refused[(int)reason]++;
        }

        /// <summary>Zeroes the refusal counts.</summary>
        public void ResetCounts()
        {
            Array.Clear(_refused, 0, _refused.Length);
        }

        /// <summary>Key frame i, 0 being the oldest.</summary>
        public KeyFrame this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
                return _slots[(_oldest + index) % _slots.Length];
            }
        }

        /// <summary>Keeps one pixel in step of a camera image so that the stored width is at most 1280.</summary>
        public static int StepFor(int imageWidth)
        {
            return imageWidth <= MaxStoredWidth ? 1 : (imageWidth + MaxStoredWidth - 1) / MaxStoredWidth;
        }

        /// <summary>The angle between two rotations, degrees, 0..180.</summary>
        public static float AngleDeg(quaternion a, quaternion b)
        {
            float d = math.min(math.abs(math.dot(a.value, b.value)), 1f);
            return math.degrees(2f * math.acos(d));
        }

        /// <summary>
        /// Call once per frame of the sweep with the raw pose. True when this frame should become a key
        /// frame; the answer stays true on the following frames until Add is called. A frame that is due
        /// (no key yet, or moved or turned enough) but refused for tracking or turn rate is counted.
        /// </summary>
        public bool Wants(in HeadPose raw)
        {
            if (_hasPrevious)
            {
                double dt = raw.Time - _previousTime;
                if (dt > 1e-4) _turnRateDeg = AngleDeg(_previousRotation, raw.Rotation) / (float)dt;
            }
            _previousRotation = raw.Rotation;
            _previousTime = raw.Time;
            _hasPrevious = true;

            bool due = !_hasKey || math.distance(raw.Position, _keyPosition) >= MoveMetres ||
                       AngleDeg(_keyRotation, raw.Rotation) >= TurnDegrees;
            if (raw.Tracking == TrackingState.NotTracking)
            {
                if (due) _refused[(int)KeyFrameRefusal.NotTracking]++;
                return false;
            }
            if (_turnRateDeg >= MaxTurnRateDeg)
            {
                if (due) _refused[(int)KeyFrameRefusal.TurningFast]++;
                return false;
            }
            return due;
        }

        /// <summary>
        /// The slot of a new key frame taken at this pose. When the ring is full the oldest frame is dropped
        /// and its buffers are reused. The caller fills the image, the depth and the intrinsics.
        /// </summary>
        public KeyFrame Add(in HeadPose raw)
        {
            int capacity = _slots.Length;
            int slot;
            if (_count < capacity)
            {
                slot = (_oldest + _count) % capacity;
                _count++;
            }
            else
            {
                slot = _oldest;
                _oldest = (_oldest + 1) % capacity;
                Evicted++;
            }
            KeyFrame frame = _slots[slot];
            if (frame == null) _slots[slot] = frame = new KeyFrame();
            frame.Reuse();
            frame.Position = raw.Position;
            frame.Rotation = raw.Rotation;
            frame.Time = raw.Time;
            frame.DepthWidth = 0;
            frame.DepthHeight = 0;

            _keyPosition = raw.Position;
            _keyRotation = raw.Rotation;
            _hasKey = true;
            return frame;
        }

        /// <summary>Copies the frames, oldest first, into a new array and empties the ring: the caller now owns them.</summary>
        public KeyFrame[] TakeAll()
        {
            var frames = new KeyFrame[_count];
            for (int i = 0; i < frames.Length; i++) frames[i] = this[i];
            Clear();
            return frames;
        }

        /// <summary>Drops every frame and its buffers, and forgets the last pose.</summary>
        public void Clear()
        {
            Array.Clear(_slots, 0, _slots.Length);
            _oldest = 0;
            _count = 0;
            Evicted = 0;
            _hasPrevious = false;
            _hasKey = false;
            _turnRateDeg = 0f;
        }
    }
}

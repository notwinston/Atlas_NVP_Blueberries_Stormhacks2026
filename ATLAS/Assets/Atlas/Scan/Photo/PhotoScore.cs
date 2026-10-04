using System;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Scan
{
    /// <summary>
    /// A pinhole camera for photo selection: Unity's camera space (+X right, +Y up, +Z forward) and pixel
    /// coordinates measured from the top-left corner of the image.
    /// </summary>
    public struct PhotoCamera
    {
        public float3 Position;
        public quaternion Rotation;
        /// <summary>fx, fy, cx, cy in pixels of the image.</summary>
        public float4 Intrinsics;
        public int Width, Height;
    }

    /// <summary>An object box in the frame the key frames were taken in: centre of the base, (w, h, d), yaw about +Y.</summary>
    public struct PhotoBox
    {
        public float3 Base;
        public float3 Size;
        public float Yaw;

        public float3 Centre { get { return Base + new float3(0f, 0.5f * Size.y, 0f); } }
        public quaternion Rotation { get { return quaternion.RotateY(Yaw); } }
    }

    /// <summary>A box seen by one camera.</summary>
    public struct BoxProjection
    {
        /// <summary>Bounding box of the eight projected corners, pixels.</summary>
        public float2 Min, Max;
        /// <summary>The projected centre of the box, pixels.</summary>
        public float2 Centre;
        /// <summary>Metres from the camera to the centre of the box.</summary>
        public float Distance;
        /// <summary>Projected bounding box over the image, 0 and up.</summary>
        public float Area;
        /// <summary>1 at the image centre, 0 at a corner.</summary>
        public float Centredness;
        /// <summary>Metres along the camera's forward axis to where the ray through the box centre enters the box.</summary>
        public float FaceDepth;
    }

    /// <summary>A pixel rectangle inside an image; (X, Y) is its top-left corner.</summary>
    public struct CropRect
    {
        public int X, Y, Width, Height;
    }

    /// <summary>
    /// Which key frames show an object best (Master Plan sections 4.5 and 7.4, task P1-C08; contract lane C
    /// section 11.2 and 11.3). Engine-free: projection of an oriented box, the candidate test, the score, the
    /// choice of one to three views and the crop rectangle.
    /// </summary>
    public static class PhotoScore
    {
        /// <summary>Most views of one object.</summary>
        public const int MaxViews = 3;
        /// <summary>Share of the image width and height the projected corners must stay clear of at each edge.</summary>
        public const float EdgeMargin = 0.05f;
        public const float MinArea = 0.10f;
        public const float MaxArea = 0.70f;
        /// <summary>The area at which the area term is full.</summary>
        public const float FullArea = 0.35f;
        /// <summary>Metres from the camera to the box centre.</summary>
        public const float MinDistance = 0.6f;
        public const float MaxDistance = 3.0f;
        /// <summary>Metres: a stored depth further than this from the box face means something is in the way.</summary>
        public const float OcclusionTolerance = 0.15f;
        /// <summary>Degrees of azimuth between any two views of one object.</summary>
        public const float MinViewSpreadDeg = 30f;
        /// <summary>Share of the projected box added on every side of the crop.</summary>
        public const float CropGrow = 0.12f;
        /// <summary>Largest side of a delivered crop, pixels.</summary>
        public const int MaxCropSide = 768;

        // A corner nearer than this to the camera plane has no usable projection.
        const float NearPlane = 0.05f;

        /// <summary>An object of the room carried into the frame of the key frames: the room frame sits at
        /// roomOrigin, turned by roomYaw about +Y (RoomAnchor.Position and RoomAnchor.Yaw).</summary>
        public static PhotoBox ToCaptureFrame(RoomObject obj, float3 roomOrigin, float roomYaw)
        {
            return new PhotoBox
            {
                Base = roomOrigin + math.mul(quaternion.RotateY(roomYaw), obj.Base),
                Size = obj.Size,
                Yaw = obj.Yaw + roomYaw
            };
        }

        /// <summary>Camera-space point to pixels. z must be positive.</summary>
        public static float2 ToPixel(in PhotoCamera camera, float3 cameraPoint)
        {
            float4 k = camera.Intrinsics;
            return new float2(k.z + k.x * cameraPoint.x / cameraPoint.z, k.w - k.y * cameraPoint.y / cameraPoint.z);
        }

        /// <summary>
        /// Projects the eight corners and the centre of a box. False when the camera is unusable or any part
        /// of the box is at or behind the camera plane.
        /// </summary>
        public static bool Project(in PhotoCamera camera, in PhotoBox box, out BoxProjection projection)
        {
            projection = default(BoxProjection);
            float4 k = camera.Intrinsics;
            if (camera.Width <= 0 || camera.Height <= 0 || !(k.x > 0f) || !(k.y > 0f)) return false;

            quaternion toCamera = math.inverse(camera.Rotation);
            quaternion boxRotation = box.Rotation;
            float hx = 0.5f * box.Size.x, hz = 0.5f * box.Size.z;
            float2 min = new float2(float.MaxValue, float.MaxValue);
            float2 max = new float2(float.MinValue, float.MinValue);
            for (int corner = 0; corner < 8; corner++)
            {
                var local = new float3((corner & 1) != 0 ? hx : -hx, (corner & 2) != 0 ? box.Size.y : 0f, (corner & 4) != 0 ? hz : -hz);
                float3 c = math.mul(toCamera, box.Base + math.mul(boxRotation, local) - camera.Position);
                if (!(c.z > NearPlane)) return false;
                float2 pixel = ToPixel(in camera, c);
                min = math.min(min, pixel);
                max = math.max(max, pixel);
            }

            float3 centre = math.mul(toCamera, box.Centre - camera.Position);
            if (!(centre.z > NearPlane)) return false;
            float distance = math.length(centre);

            projection.Min = min;
            projection.Max = max;
            projection.Centre = ToPixel(in camera, centre);
            projection.Distance = distance;
            projection.Area = (max.x - min.x) * (max.y - min.y) / ((float)camera.Width * camera.Height);
            var imageCentre = new float2(0.5f * camera.Width, 0.5f * camera.Height);
            projection.Centredness = math.saturate(1f - math.distance(projection.Centre, imageCentre) / math.length(imageCentre));

            // Where the ray through the box centre enters the box, by the slab test in the box's own frame.
            float3 direction = centre / distance;
            quaternion toBox = math.inverse(boxRotation);
            float3 origin = math.mul(toBox, camera.Position - box.Base);
            float3 ray = math.mul(toBox, math.mul(camera.Rotation, direction));
            var lo = new float3(-hx, 0f, -hz);
            var hi = new float3(hx, box.Size.y, hz);
            float enter = 0f;
            for (int axis = 0; axis < 3; axis++)
            {
                float d = ray[axis];
                if (math.abs(d) < 1e-6f) continue;
                float a = (lo[axis] - origin[axis]) / d, b = (hi[axis] - origin[axis]) / d;
                enter = math.max(enter, math.min(a, b));
            }
            projection.FaceDepth = math.min(enter, distance) * direction.z;
            return true;
        }

        /// <summary>
        /// The geometric part of the candidate test: every corner inside the image with a 5 % margin, the
        /// projected box covering 10 % to 70 % of the image, the camera 0.6 m to 3.0 m from the box centre.
        /// </summary>
        public static bool IsCandidate(in PhotoCamera camera, in BoxProjection projection)
        {
            float mx = EdgeMargin * camera.Width, my = EdgeMargin * camera.Height;
            if (projection.Min.x < mx || projection.Max.x > camera.Width - mx) return false;
            if (projection.Min.y < my || projection.Max.y > camera.Height - my) return false;
            if (!(projection.Area >= MinArea) || projection.Area > MaxArea) return false;
            return projection.Distance >= MinDistance && projection.Distance <= MaxDistance;
        }

        /// <summary>
        /// True when the depth stored at the projected box centre says something else is there. A value that
        /// is no return (no depth image, out of range) cannot tell, and does not reject the frame.
        /// </summary>
        public static bool IsOccluded(float storedDepth, float faceDepth)
        {
            if (!DepthFrame.IsReturn(storedDepth)) return false;
            return math.abs(storedDepth - faceDepth) > OcclusionTolerance;
        }

        /// <summary>0.4 area + 0.3 centredness + 0.3 sharpness against the ring's median: 0..1.</summary>
        public static float Score(in BoxProjection projection, float sharpness, float medianSharpness)
        {
            float area = math.min(projection.Area / FullArea, 1f);
            float sharp = medianSharpness > 0f ? math.saturate(sharpness / medianSharpness) : 1f;
            return 0.4f * area + 0.3f * projection.Centredness + 0.3f * sharp;
        }

        /// <summary>
        /// The score of one frame for one box, with no depth test: 0 when the box is behind the camera, leaves
        /// the image, is too small, too large, too near or too far.
        /// </summary>
        public static float Evaluate(in PhotoCamera camera, in PhotoBox box, float sharpness, float medianSharpness,
            out BoxProjection projection)
        {
            if (!Project(in camera, in box, out projection)) return 0f;
            if (!IsCandidate(in camera, in projection)) return 0f;
            return Score(in projection, sharpness, medianSharpness);
        }

        /// <summary>Direction from the box centre to the camera in the box's local frame, atan2(x, z), degrees in (-180, 180].</summary>
        public static float AzimuthDeg(float3 cameraPosition, in PhotoBox box)
        {
            float3 local = math.mul(math.inverse(box.Rotation), cameraPosition - box.Centre);
            if (local.x * local.x + local.z * local.z < 1e-12f) return 0f;
            return math.degrees(math.atan2(local.x, local.z));
        }

        /// <summary>The angle between two azimuths, degrees, 0..180.</summary>
        public static float AzimuthGapDeg(float a, float b)
        {
            float d = math.abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }

        /// <summary>
        /// Chooses the views of one object: the best candidate, then up to two more, each the best remaining
        /// whose azimuth differs by at least 30 degrees from every view taken. A score of 0 is not a
        /// candidate. picked needs room for MaxViews indices; the number of views (0 to 3) is returned.
        /// </summary>
        public static int Select(float[] scores, float[] azimuthsDeg, int count, int[] picked)
        {
            int taken = 0;
            while (taken < MaxViews)
            {
                int best = -1;
                float bestScore = 0f;
                for (int i = 0; i < count; i++)
                {
                    float s = scores[i];
                    if (!(s > bestScore)) continue;
                    bool free = true;
                    for (int t = 0; t < taken && free; t++)
                        free = picked[t] != i && AzimuthGapDeg(azimuthsDeg[i], azimuthsDeg[picked[t]]) >= MinViewSpreadDeg;
                    if (!free) continue;
                    best = i;
                    bestScore = s;
                }
                if (best < 0) break;
                picked[taken++] = best;
            }
            return taken;
        }

        /// <summary>
        /// The crop of a projected box: grown by 12 % on every side, made square about its centre, clamped to
        /// the image (so it may end up narrower than it is tall, or the reverse). Never empty.
        /// </summary>
        public static CropRect Crop(float2 min, float2 max, int imageWidth, int imageHeight)
        {
            float2 size = (max - min) * (1f + 2f * CropGrow);
            float half = 0.5f * math.max(size.x, size.y);
            float2 centre = 0.5f * (min + max);
            int x0 = math.clamp((int)math.floor(centre.x - half), 0, imageWidth - 1);
            int y0 = math.clamp((int)math.floor(centre.y - half), 0, imageHeight - 1);
            int x1 = math.clamp((int)math.ceil(centre.x + half), x0 + 1, imageWidth);
            int y1 = math.clamp((int)math.ceil(centre.y + half), y0 + 1, imageHeight);
            return new CropRect { X = x0, Y = y0, Width = x1 - x0, Height = y1 - y0 };
        }

        /// <summary>The delivered size of a crop: scaled down so that neither side exceeds 768, never up.</summary>
        public static void OutputSize(in CropRect crop, out int width, out int height)
        {
            int longest = math.max(crop.Width, crop.Height);
            if (longest <= MaxCropSide)
            {
                width = crop.Width;
                height = crop.Height;
                return;
            }
            float scale = MaxCropSide / (float)longest;
            width = math.clamp((int)math.round(crop.Width * scale), 1, MaxCropSide);
            height = math.clamp((int)math.round(crop.Height * scale), 1, MaxCropSide);
        }

        /// <summary>fx, fy, cx, cy of a crop delivered at outWidth x outHeight, in its own pixels.</summary>
        public static float4 CropIntrinsics(float4 intrinsics, in CropRect crop, int outWidth, int outHeight)
        {
            float sx = outWidth / (float)crop.Width, sy = outHeight / (float)crop.Height;
            return new float4(intrinsics.x * sx, intrinsics.y * sy, (intrinsics.z - crop.X) * sx, (intrinsics.w - crop.Y) * sy);
        }

        /// <summary>Camera frame to the box's local frame, whose origin is the centre of the base.</summary>
        public static float4x4 CamToBox(float3 cameraPosition, quaternion cameraRotation, in PhotoBox box)
        {
            quaternion toBox = math.inverse(box.Rotation);
            return new float4x4(math.mul(toBox, cameraRotation), math.mul(toBox, cameraPosition - box.Base));
        }

        /// <summary>The median of the first count values; scratch (at least count long) is overwritten. 0 when count is 0.</summary>
        public static float Median(float[] values, int count, float[] scratch)
        {
            if (count <= 0) return 0f;
            Array.Copy(values, scratch, count);
            Array.Sort(scratch, 0, count);
            return (count & 1) != 0 ? scratch[count / 2] : 0.5f * (scratch[count / 2 - 1] + scratch[count / 2]);
        }
    }
}

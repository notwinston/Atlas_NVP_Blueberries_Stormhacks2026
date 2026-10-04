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

    /// <summary>Why a view of a box is refused, in the order the tests are made (None: it is accepted).</summary>
    public enum ViewRejection : byte
    {
        None = 0,
        BehindCamera = 1,
        OutsideImage = 2,
        TooSmall = 3,
        TooLarge = 4,
        TooNear = 5,
        TooFar = 6,
        Occluded = 7
    }

    /// <summary>A pixel rectangle inside an image; (X, Y) is its top-left corner.</summary>
    public struct CropRect
    {
        public int X, Y, Width, Height;
    }

    /// <summary>
    /// Which key frames show an object best (Master Plan sections 4.5 and 7.4, task P1-C08; contract lane C
    /// section 11.2 and 11.3; mesh design section 5.1). Engine-free: projection of an oriented box, the
    /// candidate test, the score, the preference for views from the front, the choice of one to three views
    /// and the crop rectangle. There are two tiers of candidate: the strict one (the whole box inside the
    /// image) and a relaxed one for a partial view, whose score is lower so a whole view always wins. The
    /// relaxed tier exists because furniture seen from a phone held at head height rarely fits the image.
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
        /// <summary>Degrees of azimuth from the front (local +Z) beyond which the camera stands in the back half of the object.</summary>
        public const float FrontHalfDeg = 90f;
        /// <summary>Degrees above the horizontal, seen from the box centre, beyond which the camera looks down on the object.</summary>
        public const float MaxElevationDeg = 30f;
        /// <summary>What the score of a view from the back half or from too high is multiplied by. Not 0: it is still a candidate.</summary>
        public const float OffViewFactor = 0.5f;
        /// <summary>Share of the projected box added on every side of the crop.</summary>
        public const float CropGrow = 0.12f;
        /// <summary>Largest side of a delivered crop, pixels.</summary>
        public const int MaxCropSide = 768;
        /// <summary>The number of ViewRejection values, for tables of counts.</summary>
        public const int RejectionKinds = 8;
        /// <summary>What the score of a partial view is multiplied by, so that any whole view ranks above it.</summary>
        public const float RelaxedFactor = 0.35f;
        /// <summary>Share of the projected bounding box that must lie inside the image for a partial view.</summary>
        public const float MinVisibleShare = 0.6f;
        /// <summary>Smallest and largest share of the image the visible (clipped) box may cover in a partial view.</summary>
        public const float RelaxedMinArea = 0.05f, RelaxedMaxArea = 0.90f;
        /// <summary>Metres from the camera to the box centre in a partial view.</summary>
        public const float RelaxedMinDistance = 0.4f, RelaxedMaxDistance = 4.0f;

        // A corner nearer than this to the camera plane has no usable projection.
        const float NearPlane = 0.05f;
        // Degrees a camera may stand past either limit before it counts: one exactly at the side of the box, or
        // exactly 30 degrees up, is not put behind or above it by rounding.
        const float AngleSlackDeg = 0.01f;

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
            return CheckProjection(in camera, in projection) == ViewRejection.None;
        }

        /// <summary>
        /// The strict geometric test with no depth, and the first reason it fails: the box is behind the
        /// camera (or the camera is unusable), leaves the image, is too small, too large, too near or too far.
        /// </summary>
        public static ViewRejection Check(in PhotoCamera camera, in PhotoBox box, out BoxProjection projection)
        {
            if (!Project(in camera, in box, out projection)) return ViewRejection.BehindCamera;
            return CheckProjection(in camera, in projection);
        }

        static ViewRejection CheckProjection(in PhotoCamera camera, in BoxProjection projection)
        {
            float mx = EdgeMargin * camera.Width, my = EdgeMargin * camera.Height;
            if (projection.Min.x < mx || projection.Max.x > camera.Width - mx) return ViewRejection.OutsideImage;
            if (projection.Min.y < my || projection.Max.y > camera.Height - my) return ViewRejection.OutsideImage;
            if (!(projection.Area >= MinArea)) return ViewRejection.TooSmall;
            if (projection.Area > MaxArea) return ViewRejection.TooLarge;
            if (projection.Distance < MinDistance) return ViewRejection.TooNear;
            if (projection.Distance > MaxDistance) return ViewRejection.TooFar;
            return ViewRejection.None;
        }

        /// <summary>The share of the projected bounding box that lies inside the image, 0 to 1; 0 for an empty box.</summary>
        public static float VisibleShare(in PhotoCamera camera, in BoxProjection projection)
        {
            float2 size = projection.Max - projection.Min;
            float whole = size.x * size.y;
            if (!(whole > 0f)) return 0f;
            float2 lo, hi;
            Clip(in camera, in projection, out lo, out hi);
            return math.saturate((hi.x - lo.x) * (hi.y - lo.y) / whole);
        }

        /// <summary>The projected bounding box clamped to the image, pixels.</summary>
        public static void Clip(in PhotoCamera camera, in BoxProjection projection, out float2 min, out float2 max)
        {
            var image = new float2(camera.Width, camera.Height);
            min = math.clamp(projection.Min, float2.zero, image);
            max = math.clamp(projection.Max, float2.zero, image);
        }

        /// <summary>
        /// The relaxed candidate test, for a box that does not fit the image: its projected centre is inside
        /// the image, at least 60 % of its bounding box is, that visible part covers 5 % to 90 % of the image,
        /// and the camera is 0.4 m to 4.0 m from the box centre.
        /// </summary>
        public static bool IsPartialCandidate(in PhotoCamera camera, in BoxProjection projection)
        {
            float2 c = projection.Centre;
            if (!(c.x >= 0f && c.x <= camera.Width && c.y >= 0f && c.y <= camera.Height)) return false;
            if (!(VisibleShare(in camera, in projection) >= MinVisibleShare)) return false;
            float2 lo, hi;
            Clip(in camera, in projection, out lo, out hi);
            float area = (hi.x - lo.x) * (hi.y - lo.y) / ((float)camera.Width * camera.Height);
            if (!(area >= RelaxedMinArea) || area > RelaxedMaxArea) return false;
            return projection.Distance >= RelaxedMinDistance && projection.Distance <= RelaxedMaxDistance;
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
        /// the image, is too small, too large, too near or too far; otherwise Score, halved for a camera in
        /// the back half of the object or more than 30 degrees above its centre (ViewFactor).
        /// </summary>
        public static float Evaluate(in PhotoCamera camera, in PhotoBox box, float sharpness, float medianSharpness,
            out BoxProjection projection)
        {
            if (Check(in camera, in box, out projection) != ViewRejection.None) return 0f;
            return Score(in projection, sharpness, medianSharpness) *
                   ViewFactor(AzimuthDeg(camera.Position, in box), ElevationDeg(camera.Position, in box));
        }

        /// <summary>
        /// The score of a partial view, with no depth test: 0 when any corner is behind the camera or the view
        /// is not a partial candidate; otherwise Score with the area of the visible, clipped box, times
        /// ViewFactor, times RelaxedFactor. The projection is the unclipped one.
        /// </summary>
        public static float EvaluatePartial(in PhotoCamera camera, in PhotoBox box, float sharpness, float medianSharpness,
            out BoxProjection projection)
        {
            if (!Project(in camera, in box, out projection)) return 0f;
            if (!IsPartialCandidate(in camera, in projection)) return 0f;
            float2 lo, hi;
            Clip(in camera, in projection, out lo, out hi);
            BoxProjection clipped = projection;
            clipped.Area = (hi.x - lo.x) * (hi.y - lo.y) / ((float)camera.Width * camera.Height);
            return Score(in clipped, sharpness, medianSharpness) *
                   ViewFactor(AzimuthDeg(camera.Position, in box), ElevationDeg(camera.Position, in box)) * RelaxedFactor;
        }

        /// <summary>Direction from the box centre to the camera in the box's local frame, atan2(x, z), degrees in (-180, 180].</summary>
        public static float AzimuthDeg(float3 cameraPosition, in PhotoBox box)
        {
            float3 local = math.mul(math.inverse(box.Rotation), cameraPosition - box.Centre);
            if (local.x * local.x + local.z * local.z < 1e-12f) return 0f;
            return math.degrees(math.atan2(local.x, local.z));
        }

        /// <summary>The angle above the horizontal of the direction from the box centre to the camera, degrees in [-90, 90].</summary>
        public static float ElevationDeg(float3 cameraPosition, in PhotoBox box)
        {
            float3 away = cameraPosition - box.Centre;
            float flat = math.sqrt(away.x * away.x + away.z * away.z);
            if (flat * flat + away.y * away.y < 1e-12f) return 0f;
            return math.degrees(math.atan2(away.y, flat));
        }

        /// <summary>
        /// What a view's score is multiplied by (mesh design section 5.1): the generator was trained on views
        /// from the front half of the object and from no more than 30 degrees above it. 0.5 when the azimuth,
        /// which is measured from the front once facing is decided, is more than 90 degrees from 0, or the
        /// elevation is above 30 degrees; 1 otherwise. Never 0: a view from behind is taken when there is no other.
        /// </summary>
        public static float ViewFactor(float azimuthDeg, float elevationDeg)
        {
            bool behind = math.abs(azimuthDeg) > FrontHalfDeg + AngleSlackDeg;
            bool above = elevationDeg > MaxElevationDeg + AngleSlackDeg;
            return behind || above ? OffViewFactor : 1f;
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

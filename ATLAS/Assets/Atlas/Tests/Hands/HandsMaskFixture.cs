using System;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Hands.Tests
{
    /// <summary>
    /// Scenes for the depth mask tests (documents/ATLAS_Hands_Design.md section 7.1): a depth image with a pose that
    /// is not the identity, open hands built in the camera's own space and carried into the room with that pose (so
    /// the expected pixels never come from the code under test), and a small renderer that paints a hand and its
    /// forearm into the depth image the way LiDAR would see them.
    /// </summary>
    internal static class HandsMaskFixture
    {
        public const int Width = 256, Height = 192;

        /// <summary>Focal length of the nominal camera: 61.7 degrees across 256 pixels.</summary>
        public const float NominalFx = 215.6f;

        /// <summary>Depth the room's far wall is painted at, metres: beyond every gate.</summary>
        public const float FarWall = 3f;

        /// <summary>
        /// A depth frame whose pose is neither the origin nor the identity rotation, so a builder that forgets the
        /// world-to-camera step projects the hands somewhere else. Every sample is the far wall.
        /// </summary>
        public static DepthFrame Frame(float fx = NominalFx, int width = Width, int height = Height, int sequence = 1,
                                       float wall = FarWall)
        {
            var depth = new float[width * height];
            for (int i = 0; i < depth.Length; i++) depth[i] = wall;
            return new DepthFrame
            {
                Depth = depth,
                Width = width,
                Height = height,
                Fx = fx,
                Fy = fx,
                Cx = width * 0.5f,
                Cy = height * 0.5f,
                Position = new float3(0.4f, 1.5f, -0.7f),
                Rotation = math.mul(quaternion.RotateY(math.radians(25f)), quaternion.RotateX(math.radians(12f))),
                Time = 10.0,
                Sequence = sequence
            };
        }

        /// <summary>Sets every sample of the image to one depth.</summary>
        public static void Fill(in DepthFrame frame, float depth)
        {
            for (int i = 0; i < frame.Depth.Length; i++) frame.Depth[i] = depth;
        }

        // ---- Hands ----

        /// <summary>
        /// An open hand in camera space (+X right, +Y up, +Z forward), its wrist at <paramref name="wrist"/>. With
        /// no roll and no tilt the palm faces the camera and the fingers point up the picture. Roll turns the hand
        /// about the viewing axis (90 degrees points the fingers to the left); tilt turns the fingers away from the
        /// camera.
        /// </summary>
        public static JointSet Hand(HandSide side, float3 wrist, float rollDegrees = 0f, float tiltDegrees = 0f, float scale = 1f)
        {
            // The palm frame has +Z from the wrist to the middle knuckle and +Y out of the palm. A turn of -90 degrees
            // about X puts +Z up the picture and +Y back at the camera.
            quaternion facing = quaternion.RotateX(math.radians(-90f));
            quaternion orient = math.mul(quaternion.RotateZ(math.radians(rollDegrees)),
                                         math.mul(quaternion.RotateX(math.radians(tiltDegrees)), facing));
            return HandModel.RestPose(side, scale).Transformed(orient, wrist);
        }

        /// <summary>The hand as the filter hands it over: Tracked, in the room, with the frame's pose undone.</summary>
        public static TrackedHand Tracked(in DepthFrame frame, in JointSet cameraJoints, float confidence = 0.9f,
                                          bool fresh = true, float lostSeconds = 0f, float ageSeconds = 0.05f,
                                          float3 cameraVelocity = default(float3))
        {
            JointSet world = cameraJoints.Transformed(frame.Rotation, frame.Position);
            var hand = new TrackedHand
            {
                Tracked = true,
                Fresh = fresh,
                Confidence = confidence,
                LostSeconds = lostSeconds,
                AgeSeconds = ageSeconds,
                World = world,
                PalmCentre = PalmCentre(world),
                PalmVelocity = math.mul(frame.Rotation, cameraVelocity),
                TrackedSeconds = 1f,
                GoodDetections = 5
            };
            return hand;
        }

        /// <summary>The mean of the wrist and the four knuckles.</summary>
        public static float3 PalmCentre(in JointSet joints)
        {
            return (joints[HandJoint.Wrist] + joints[HandJoint.IndexMcp] + joints[HandJoint.MiddleMcp]
                    + joints[HandJoint.RingMcp] + joints[HandJoint.LittleMcp]) / 5f;
        }

        // ---- Where things are in the picture ----

        /// <summary>
        /// The sample index of a camera-space point: the inverse of <see cref="DepthFrame.Unproject"/>, whose pixel
        /// (u, v) is centred on (u + 0.5, v + 0.5).
        /// </summary>
        public static float2 Pixel(in DepthFrame frame, float3 camera)
        {
            return new float2(frame.Cx + camera.x / camera.z * frame.Fx - 0.5f,
                              frame.Cy - camera.y / camera.z * frame.Fy - 0.5f);
        }

        /// <summary>The box of the projected joints.</summary>
        public static void Bounds(in DepthFrame frame, in JointSet cameraJoints,
                                  out float minU, out float minV, out float maxU, out float maxV)
        {
            minU = minV = float.MaxValue;
            maxU = maxV = float.MinValue;
            for (int joint = 0; joint < HandJoints.Count; joint++)
            {
                float2 p = Pixel(frame, cameraJoints[joint]);
                minU = math.min(minU, p.x);
                maxU = math.max(maxU, p.x);
                minV = math.min(minV, p.y);
                maxV = math.max(maxV, p.y);
            }
        }

        /// <summary>The palm's depth along the camera's axis: the mean of the wrist and the four knuckles.</summary>
        public static float PalmDepth(in JointSet cameraJoints)
        {
            return PalmCentre(cameraJoints).z;
        }

        /// <summary>The palm's width in depth pixels at the palm's depth.</summary>
        public static float PalmWidthPixels(in DepthFrame frame, in JointSet cameraJoints)
        {
            float metres = math.distance(cameraJoints[HandJoint.IndexMcp], cameraJoints[HandJoint.LittleMcp]);
            return metres * frame.Fx / PalmDepth(cameraJoints);
        }

        // ---- A small renderer ----

        /// <summary>
        /// Paints a sphere (camera space) into the depth image: the nearest surface along each ray wins, and the
        /// depth is measured along the camera's z axis, as LiDAR reports it.
        /// </summary>
        public static void Sphere(in DepthFrame frame, float3 centre, float radius)
        {
            if (centre.z - radius <= 0.02f) return;
            float2 c = Pixel(frame, centre);
            float reach = radius / (centre.z - radius) * frame.Fx + 2f;
            int u0 = math.max(0, (int)math.floor(c.x - reach)), u1 = math.min(frame.Width - 1, (int)math.ceil(c.x + reach));
            int v0 = math.max(0, (int)math.floor(c.y - reach)), v1 = math.min(frame.Height - 1, (int)math.ceil(c.y + reach));
            float cc = math.dot(centre, centre) - radius * radius;
            for (int v = v0; v <= v1; v++)
            {
                for (int u = u0; u <= u1; u++)
                {
                    // The ray through the sample's centre, scaled so that its z is 1: t is the depth.
                    float3 dir = new float3((u + 0.5f - frame.Cx) / frame.Fx, -(v + 0.5f - frame.Cy) / frame.Fy, 1f);
                    float a = math.dot(dir, dir), b = math.dot(dir, centre);
                    float disc = b * b - a * cc;
                    if (disc < 0f) continue;
                    float t = (b - math.sqrt(disc)) / a;
                    int index = v * frame.Width + u;
                    if (t > 0f && t < frame.Depth[index]) frame.Depth[index] = t;
                }
            }
        }

        /// <summary>
        /// Paints one hand: a tube along every bone, a plate for the palm and, if asked, a forearm that runs from the
        /// wrist away from the fingers, down the picture and back towards the camera.
        /// </summary>
        public static void PaintHand(in DepthFrame frame, in JointSet joints, bool forearm)
        {
            for (int joint = 1; joint < HandJoints.Count; joint++)
            {
                int parent = HandJoints.Parent(joint);
                float3 a = joints[parent], b = joints[joint];
                float ra = HandModel.Radius(parent), rb = HandModel.Radius(joint);
                int steps = Math.Max(2, (int)(math.distance(a, b) / 0.004f));
                for (int s = 0; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    Sphere(frame, math.lerp(a, b, t), math.lerp(ra, rb, t));
                }
            }

            int[] ring =
            {
                (int)HandJoint.ThumbCmc, (int)HandJoint.IndexMcp, (int)HandJoint.MiddleMcp,
                (int)HandJoint.RingMcp, (int)HandJoint.LittleMcp
            };
            float3 wrist = joints[HandJoint.Wrist];
            for (int k = 0; k + 1 < ring.Length; k++)
            {
                float3 p = joints[ring[k]], q = joints[ring[k + 1]];
                for (int i = 0; i <= 10; i++)
                    for (int j = 0; i + j <= 10; j++)
                        Sphere(frame, wrist + (p - wrist) * (i / 10f) + (q - wrist) * (j / 10f), 0.012f);
            }

            if (!forearm) return;
            float3 along = math.normalize(wrist - joints[HandJoint.MiddleMcp]);
            float3 axis = math.normalize(along + new float3(0f, 0f, -0.8f));
            for (int s = 0; s <= 75; s++)
            {
                float length = s * 0.004f;
                Sphere(frame, wrist + axis * length, 0.032f + 0.01f * length / 0.3f);
            }
        }

        /// <summary>LiDAR gives nothing nearer than its minimum range: those samples become "no return" (0).</summary>
        public static void Lidar(in DepthFrame frame)
        {
            for (int i = 0; i < frame.Depth.Length; i++)
                if (frame.Depth[i] < DepthFrame.MinRange) frame.Depth[i] = 0f;
        }

        /// <summary>
        /// Samples whose depth says "something near the hand" (a return nearer than 2 m) that the mask does not
        /// contain. Zero when the mask hides every sample the painted hand put there.
        /// </summary>
        public static int NearSamplesMissed(in DepthFrame frame, in DepthMask mask, out int nearSamples)
        {
            int missed = 0;
            nearSamples = 0;
            for (int v = 0; v < frame.Height; v++)
            {
                for (int u = 0; u < frame.Width; u++)
                {
                    float d = frame.Depth[v * frame.Width + u];
                    if (!DepthFrame.IsReturn(d) || d > 2f) continue;
                    nearSamples++;
                    if (!mask.Contains(u, v, d)) missed++;
                }
            }
            return missed;
        }

        /// <summary>
        /// The share of Bump Guard's samples (every fourth pixel from the second, as its kernel takes them) that
        /// are returns the mask contains. Counted here with the mask's own Contains, not with the builder's code.
        /// </summary>
        public static float MaskedShare(in DepthFrame frame, in DepthMask mask)
        {
            int masked = 0, total = 0;
            for (int v = 2; v < frame.Height; v += 4)
            {
                for (int u = 2; u < frame.Width; u += 4)
                {
                    total++;
                    float d = frame.Depth[v * frame.Width + u];
                    if (DepthFrame.IsReturn(d) && mask.Contains(u, v, d)) masked++;
                }
            }
            return total == 0 ? 0f : masked / (float)total;
        }
    }
}

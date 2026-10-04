using System;
using Atlas.World;
using Unity.Mathematics;
using Random = System.Random;   // not Unity.Mathematics.Random

namespace Atlas.Hands.Tests
{
    /// <summary>The four hand shapes the lift is judged on (documents/ATLAS_Hands_Design.md section 9.1).</summary>
    internal enum HandsLiftShape { Open, Pointing, Pinch, Fist }

    /// <summary>
    /// What the lift's tests are built from: a template hand in a shape, put in front of a 1280 x 720 camera,
    /// written as a detection by <see cref="HandsSynth"/> and then spoilt with noise from a seeded generator,
    /// so that every run sees the same numbers.
    /// </summary>
    internal static class HandsLiftFixture
    {
        public const int ImageWidth = 1280, ImageHeight = 720, DepthWidth = 256, DepthHeight = 144;
        /// <summary>A 72 degree horizontal field in a 1280 pixel picture, as the wide camera gives.</summary>
        public const float Focal = 880f, Cx = 640f, Cy = 360f;
        /// <summary>Standard deviation of a joint in the picture, per axis, pixels; and of every LiDAR depth, metres.</summary>
        public const float PixelNoise = 2f, DepthNoise = 0.01f;

        /// <summary>Back of the hand to the camera, fingers up: the palm frame's X, Y and Z axes in camera space.</summary>
        public static readonly float3x3 BackToCamera =
            new float3x3(new float3(-1f, 0f, 0f), new float3(0f, 0f, 1f), new float3(0f, 1f, 0f));
        /// <summary>Palm to the camera, fingers up.</summary>
        public static readonly float3x3 PalmToCamera =
            new float3x3(new float3(1f, 0f, 0f), new float3(0f, 0f, -1f), new float3(0f, 1f, 0f));

        /// <summary>The template hand in a shape, in its own palm frame.</summary>
        public static JointSet Shape(HandsLiftShape shape, HandSide side, float scale = 1f)
        {
            switch (shape)
            {
                case HandsLiftShape.Pointing: return HandModel.Pose(side, new float4(0f, 1f, 1f, 1f), 0.6f, scale);
                case HandsLiftShape.Pinch: return HandModel.Pose(side, new float4(0.5f, 0.15f, 0.15f, 0.15f), 0.8f, scale);
                case HandsLiftShape.Fist: return HandModel.Pose(side, new float4(1f, 1f, 1f, 1f), 1f, scale);
                default: return HandModel.Pose(side, float4.zero, 0f, scale);
            }
        }

        /// <summary>The palm's centre as the lift reports it: halfway between the wrist and the middle of the four knuckles.</summary>
        public static float3 PalmCentre(in JointSet joints)
        {
            return 0.5f * joints[HandJoint.Wrist]
                 + 0.125f * (joints[HandJoint.IndexMcp] + joints[HandJoint.MiddleMcp] + joints[HandJoint.RingMcp] + joints[HandJoint.LittleMcp]);
        }

        /// <summary>
        /// A base orientation turned about the camera's own axes: pitched (positive tips the top of the hand away
        /// from the camera), then yawed, then rolled in the picture. Degrees. Fingers that point up to begin
        /// with end up pointing away by the pitch, less what the yaw takes off.
        /// </summary>
        public static float3x3 Turned(float3x3 orientation, float yawDegrees, float pitchDegrees, float rollDegrees)
        {
            float3x3 turn = math.mul(float3x3.RotateZ(math.radians(rollDegrees)),
                            math.mul(float3x3.RotateY(math.radians(yawDegrees)), float3x3.RotateX(math.radians(pitchDegrees))));
            return math.mul(turn, orientation);
        }

        /// <summary>The hand in camera space, turned about its palm centre, with that centre at the given point.</summary>
        public static JointSet Place(in JointSet palmSpace, float3x3 orientation, float3 palmCentre)
        {
            float3 centre = PalmCentre(in palmSpace);
            JointSet camera = default(JointSet);
            for (int joint = 0; joint < HandJoints.Count; joint++)
                camera[joint] = palmCentre + math.mul(orientation, palmSpace[joint] - centre);
            return camera;
        }

        /// <summary>An empty detection of the test camera, the camera at the session's origin.</summary>
        public static void Begin(ref HandsDetection detection, int sequence, double captureTime)
        {
            Begin(ref detection, sequence, captureTime, float4x4.identity);
        }

        /// <summary>An empty detection of the test camera at the given pose in the AR session.</summary>
        public static void Begin(ref HandsDetection detection, int sequence, double captureTime, in float4x4 cameraToSession)
        {
            HandsSynth.Begin(ref detection, sequence, ImageWidth, ImageHeight, Focal, Focal, Cx, Cy, DepthWidth, DepthHeight,
                             captureTime, captureTime + 0.03, in cameraToSession);
        }

        /// <summary>
        /// Adds one hand as the detector and LiDAR would report it, then spoils it: every joint moved in the
        /// picture, every depth (the joints' and the palm's samples) moved along its ray. palmDepth false takes
        /// the palm's depth samples away, as when the palm is nearer than LiDAR's range.
        /// </summary>
        public static void AddHand(ref HandsDetection detection, in JointSet cameraJoints, HandSide side, Random random,
                                   float pixelNoise = PixelNoise, float depthNoise = DepthNoise, bool palmDepth = true,
                                   bool jointDepth = true)
        {
            int index = detection.HandCount;
            HandsSynth.AddHand(ref detection, in cameraJoints, (int)side, 0.95f, palmDepth || jointDepth);
            if (index == 0) Spoil(ref detection.Hand0, random, pixelNoise, depthNoise, palmDepth, jointDepth);
            else if (index == 1) Spoil(ref detection.Hand1, random, pixelNoise, depthNoise, palmDepth, jointDepth);
        }

        /// <summary>One detection holding one hand.</summary>
        public static void Detect(ref HandsDetection detection, int sequence, double captureTime, in JointSet cameraJoints,
                                  HandSide side, Random random, float pixelNoise = PixelNoise, float depthNoise = DepthNoise,
                                  bool palmDepth = true, bool jointDepth = true)
        {
            Begin(ref detection, sequence, captureTime);
            AddHand(ref detection, in cameraJoints, side, random, pixelNoise, depthNoise, palmDepth, jointDepth);
        }

        static void Spoil(ref DetectedHand hand, Random random, float pixelNoise, float depthNoise, bool palmDepth, bool jointDepth)
        {
            for (int joint = 0; joint < HandJoints.Count; joint++)
            {
                JointSample sample = hand.GetJoint(joint);
                sample.Pixel += new float2(Gaussian(random), Gaussian(random)) * pixelNoise;
                if (!jointDepth)
                {
                    sample.Depth = 0f;
                    sample.DepthSpread = 0f;
                    sample.DepthConfidence = -1;
                }
                else if (sample.Depth > 0f) sample.Depth += Gaussian(random) * depthNoise;
                hand.SetJoint(joint, in sample);
            }

            int count = hand.PalmCount;
            hand.PalmCount = 0;
            if (!palmDepth) return;
            for (int i = 0; i < count; i++)
            {
                // Each sample is read before the slot it is written back to is touched: i never trails PalmCount.
                float3 sample = hand.GetPalmSample(i);
                hand.AddPalmSample(sample.x, sample.y, sample.z + Gaussian(random) * depthNoise);
            }
        }

        /// <summary>A standard normal deviate (Box and Muller).</summary>
        public static float Gaussian(Random random)
        {
            double u = 1.0 - random.NextDouble(), v = random.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v));
        }

        public static float Uniform(Random random, float from, float to)
        {
            return from + (to - from) * (float)random.NextDouble();
        }

        /// <summary>The value below which the given share of the samples lie. Sorts the array.</summary>
        public static float Percentile(float[] values, int count, float share)
        {
            Array.Sort(values, 0, count);
            int index = math.clamp((int)math.ceil(share * count) - 1, 0, count - 1);
            return values[index];
        }
    }
}

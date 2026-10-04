using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>
    /// Writes a detection as a perfect detector and a perfect LiDAR would report it, from joints given in the
    /// camera's own space. The Editor's mouse-and-keys source and the test fixtures are built on it, so that
    /// both run through the real lift (documents/ATLAS_Hands_Design.md section 4.5). Noise is the caller's to add.
    /// </summary>
    public static class HandsSynth
    {
        /// <summary>Palm depth samples per side of the triangle wrist, index knuckle, little-finger knuckle.</summary>
        const int PalmGrid = 9;

        /// <summary>An empty detection for one camera frame.</summary>
        public static void Begin(ref HandsDetection detection, int sequence, int imageWidth, int imageHeight,
                                 float fx, float fy, float cx, float cy, int depthWidth, int depthHeight,
                                 double captureTime, double pollTime, in float4x4 cameraToSession)
        {
            detection = default(HandsDetection);
            detection.Version = HandsDetection.CurrentVersion;
            detection.Sequence = sequence;
            detection.ImageWidth = imageWidth;
            detection.ImageHeight = imageHeight;
            detection.DepthWidth = depthWidth;
            detection.DepthHeight = depthHeight;
            detection.Fx = fx;
            detection.Fy = fy;
            detection.Cx = cx;
            detection.Cy = cy;
            detection.CaptureTime = captureTime;
            detection.PollTime = pollTime;
            detection.CameraToSession = cameraToSession;
        }

        /// <summary>
        /// Adds one hand from its 21 joints in camera space (Unity's convention: +X right, +Y up, +Z forward,
        /// metres). side is 0 left, 1 right, -1 unknown. With withDepth false the hand carries no LiDAR depth, as
        /// when it is nearer than the sensor's range. False when the detection already holds two hands.
        /// </summary>
        public static bool AddHand(ref HandsDetection detection, in JointSet cameraJoints, int side,
                                   float confidence = 0.95f, bool withDepth = true)
        {
            if (detection.HandCount >= HandsDetection.MaxHands) return false;
            DetectedHand hand = default(DetectedHand);
            hand.Side = side;
            hand.Confidence = confidence;
            for (int joint = 0; joint < HandJoints.Count; joint++)
            {
                float3 p = cameraJoints[joint];
                JointSample sample;
                sample.Pixel = detection.Project(p);
                sample.Confidence = confidence;
                bool seen = withDepth && p.z >= DepthFrame.MinRange && p.z <= DepthFrame.MaxRange;
                sample.Depth = seen ? p.z : 0f;
                sample.DepthSpread = seen ? 0.005f : 0f;
                sample.DepthConfidence = seen ? 2 : -1;
                hand.SetJoint(joint, in sample);
            }

            if (withDepth)
            {
                float3 wrist = cameraJoints[HandJoint.Wrist];
                float3 index = cameraJoints[HandJoint.IndexMcp];
                float3 little = cameraJoints[HandJoint.LittleMcp];
                for (int i = 1; i < PalmGrid; i++)
                {
                    for (int j = 1; i + j < PalmGrid; j++)
                    {
                        float u = i / (float)PalmGrid, v = j / (float)PalmGrid;
                        float3 p = wrist + u * (index - wrist) + v * (little - wrist);
                        if (p.z < DepthFrame.MinRange || p.z > DepthFrame.MaxRange) continue;
                        float2 pixel = detection.Project(p);
                        hand.AddPalmSample(pixel.x, pixel.y, p.z);
                    }
                }
            }

            if (detection.HandCount == 0) detection.Hand0 = hand;
            else detection.Hand1 = hand;
            detection.HandCount++;
            return true;
        }
    }
}

using System.Runtime.InteropServices;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>One joint as the detector saw it: where in the picture, how sure, and the LiDAR depth under it.</summary>
    public struct JointSample
    {
        /// <summary>Pixels of the captured image, origin at the top left.</summary>
        public float2 Pixel;
        /// <summary>0 to 1.</summary>
        public float Confidence;
        /// <summary>Metres: median of the 3 x 3 depth pixels at the joint. 0 when there is none.</summary>
        public float Depth;
        /// <summary>Metres: largest minus smallest of those pixels. Large at a silhouette.</summary>
        public float DepthSpread;
        /// <summary>0 low, 1 medium, 2 high; -1 no sample.</summary>
        public int DepthConfidence;
    }

    /// <summary>
    /// One detected hand. The layout is the plugin's (documents/ATLAS_Hands_Design.md Appendix C,
    /// AtlasHandsHand in AtlasHands.swift): do not reorder. 1,288 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct DetectedHand
    {
        public const int MaxPalmSamples = 64;

        /// <summary>0 left, 1 right, -1 unknown.</summary>
        public int Side;
        /// <summary>The detector's own confidence for the hand, 0 to 1.</summary>
        public float Confidence;
        /// <summary>Palm depth samples in use, 0 to 64.</summary>
        public int PalmCount;
        int _reserved;
        fixed float _palmU[MaxPalmSamples];
        fixed float _palmV[MaxPalmSamples];
        fixed float _palmDepth[MaxPalmSamples];
        fixed float _jointX[HandJoints.Count];
        fixed float _jointY[HandJoints.Count];
        fixed float _jointConfidence[HandJoints.Count];
        fixed float _jointDepth[HandJoints.Count];
        fixed float _jointDepthSpread[HandJoints.Count];
        fixed int _jointDepthConfidence[HandJoints.Count];

        public JointSample GetJoint(int joint)
        {
            JointSample s = default(JointSample);
            if ((uint)joint >= HandJoints.Count) { s.DepthConfidence = -1; return s; }
            fixed (float* x = _jointX, y = _jointY, c = _jointConfidence, d = _jointDepth, sp = _jointDepthSpread)
            fixed (int* dc = _jointDepthConfidence)
            {
                s.Pixel = new float2(x[joint], y[joint]);
                s.Confidence = c[joint];
                s.Depth = d[joint];
                s.DepthSpread = sp[joint];
                s.DepthConfidence = dc[joint];
            }
            return s;
        }

        public void SetJoint(int joint, in JointSample sample)
        {
            if ((uint)joint >= HandJoints.Count) return;
            fixed (float* x = _jointX, y = _jointY, c = _jointConfidence, d = _jointDepth, sp = _jointDepthSpread)
            fixed (int* dc = _jointDepthConfidence)
            {
                x[joint] = sample.Pixel.x;
                y[joint] = sample.Pixel.y;
                c[joint] = sample.Confidence;
                d[joint] = sample.Depth;
                sp[joint] = sample.DepthSpread;
                dc[joint] = sample.DepthConfidence;
            }
        }

        /// <summary>A palm depth sample: x, y the picture pixel, z the depth in metres.</summary>
        public float3 GetPalmSample(int index)
        {
            if ((uint)index >= MaxPalmSamples) return float3.zero;
            fixed (float* u = _palmU, v = _palmV, d = _palmDepth) return new float3(u[index], v[index], d[index]);
        }

        /// <summary>Appends a palm depth sample. False when all 64 are in use.</summary>
        public bool AddPalmSample(float pixelU, float pixelV, float depth)
        {
            if (PalmCount < 0) PalmCount = 0;
            if (PalmCount >= MaxPalmSamples) return false;
            fixed (float* u = _palmU, v = _palmV, d = _palmDepth)
            {
                u[PalmCount] = pixelU;
                v[PalmCount] = pixelV;
                d[PalmCount] = depth;
            }
            PalmCount++;
            return true;
        }
    }

    /// <summary>
    /// One result of the hand detector: what it found in one camera frame, with that frame's camera pose,
    /// intrinsics and time. The layout is the plugin's (Appendix C, AtlasHandsResult): do not reorder.
    /// 2,712 bytes; pass it with in, ref or out, never by value on the frame path.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct HandsDetection
    {
        public const int CurrentVersion = 1;
        public const int MaxHands = 2;
        /// <summary>sizeof(AtlasHandsResult) on the native side; checked at start.</summary>
        public const int NativeSize = 2712;

        /// <summary>The camera frame's timestamp, seconds, on the detector's own clock.</summary>
        public double CaptureTime;
        /// <summary>The same clock, when this result was handed over.</summary>
        public double PollTime;
        public int Version;
        /// <summary>Rises by one with every result.</summary>
        public int Sequence;
        /// <summary>0, 1 or 2.</summary>
        public int HandCount;
        public int ImageWidth, ImageHeight;
        /// <summary>0 when the frame had no depth.</summary>
        public int DepthWidth, DepthHeight;
        /// <summary>Timer ticks the detector skipped since its previous result.</summary>
        public int Skipped;
        /// <summary>Pinhole intrinsics of the captured image, pixels; Cx, Cy from the top-left corner.</summary>
        public float Fx, Fy, Cx, Cy;
        /// <summary>How long the request took, milliseconds.</summary>
        public float RequestMs;
        float _reserved;
        /// <summary>
        /// Camera pose of the frame in the AR session's space, already in Unity's convention (left-handed, +X
        /// right, +Y up, +Z forward): column-major, a point transform from camera space to session space.
        /// </summary>
        fixed float _cameraToSession[16];
        public DetectedHand Hand0;
        public DetectedHand Hand1;

        /// <summary>Seconds between capture and hand-over, never negative.</summary>
        public float AgeSeconds { get { return (float)math.max(0.0, PollTime - CaptureTime); } }

        public float4x4 CameraToSession
        {
            get
            {
                fixed (float* m = _cameraToSession)
                    return new float4x4(
                        new float4(m[0], m[1], m[2], m[3]),
                        new float4(m[4], m[5], m[6], m[7]),
                        new float4(m[8], m[9], m[10], m[11]),
                        new float4(m[12], m[13], m[14], m[15]));
            }
            set
            {
                fixed (float* m = _cameraToSession)
                {
                    m[0] = value.c0.x; m[1] = value.c0.y; m[2] = value.c0.z; m[3] = value.c0.w;
                    m[4] = value.c1.x; m[5] = value.c1.y; m[6] = value.c1.z; m[7] = value.c1.w;
                    m[8] = value.c2.x; m[9] = value.c2.y; m[10] = value.c2.z; m[11] = value.c2.w;
                    m[12] = value.c3.x; m[13] = value.c3.y; m[14] = value.c3.z; m[15] = value.c3.w;
                }
            }
        }

        /// <summary>Camera-space point (Unity's convention) of a picture pixel at a depth along the camera's forward axis.</summary>
        public float3 Unproject(float2 pixel, float depth)
        {
            return new float3((pixel.x - Cx) / Fx * depth, -(pixel.y - Cy) / Fy * depth, depth);
        }

        /// <summary>The picture pixel of a camera-space point. Meaningful for points in front of the camera.</summary>
        public float2 Project(float3 cameraPoint)
        {
            float z = math.max(cameraPoint.z, 1e-4f);
            return new float2(Cx + cameraPoint.x / z * Fx, Cy - cameraPoint.y / z * Fy);
        }
    }
}

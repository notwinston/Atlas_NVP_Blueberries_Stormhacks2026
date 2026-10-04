using System;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Hands.Tests
{
    /// <summary>
    /// A small deterministic random source (xorshift and Box-Muller): the same numbers in the Editor and in the
    /// headless run, whatever the runtime's own generator does, so a tolerance that holds here holds there.
    /// </summary>
    internal struct HandsFilterRng
    {
        uint _state;
        bool _hasSpare;
        float _spare;

        public HandsFilterRng(uint seed)
        {
            _state = seed == 0u ? 0x9E3779B9u : seed;
            _hasSpare = false;
            _spare = 0f;
        }

        uint Next()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public float Uniform()
        {
            return (Next() >> 8) * (1f / 16777216f);
        }

        /// <summary>Standard normal.</summary>
        public float Gaussian()
        {
            if (_hasSpare)
            {
                _hasSpare = false;
                return _spare;
            }
            float u1 = math.max(Uniform(), 1e-7f);
            float u2 = Uniform();
            float radius = math.sqrt(-2f * math.log(u1));
            float angle = 2f * math.PI * u2;
            _spare = radius * math.sin(angle);
            _hasSpare = true;
            return radius * math.cos(angle);
        }

        /// <summary>A vector with the given standard deviation on every axis.</summary>
        public float3 Gaussian3(float sigma)
        {
            return new float3(Gaussian(), Gaussian(), Gaussian()) * sigma;
        }
    }

    /// <summary>Observed hands as the lift would hand them to the filter, built from a known truth.</summary>
    internal static class HandsFilterFixtures
    {
        /// <summary>Where the palm's centre sits in the palm frame (wrist origin), metres along Z.</summary>
        public const float PalmCentreZ = 0.055f;

        /// <summary>
        /// An open or posed hand at a wrist position and orientation, in world space. hint is the detector's
        /// label (0 left, 1 right, -1 unknown); pixelX the palm's picture column (the view is 1280 wide).
        /// </summary>
        public static ObservedHand Hand(HandSide side, float3 wrist, quaternion rotation, int hint,
                                        float confidence = 0.9f, float pixelX = -1f)
        {
            return Hand(HandModel.RestPose(side), wrist, rotation, hint, confidence, pixelX);
        }

        public static ObservedHand Hand(in JointSet palmFrame, float3 wrist, quaternion rotation, int hint,
                                        float confidence = 0.9f, float pixelX = -1f)
        {
            ObservedHand hand = default(ObservedHand);
            JointSet world = default(JointSet);
            for (int joint = 0; joint < HandJoints.Count; joint++)
                world[joint] = wrist + math.mul(rotation, palmFrame[joint]);
            hand.Joints = world;
            hand.Confidence = confidence;
            hand.SideHint = hint;
            hand.PalmFromDepth = true;
            hand.PalmDistance = 0.45f;
            hand.PalmCentre = wrist + math.mul(rotation, new float3(0f, 0f, PalmCentreZ));
            hand.PalmNormal = math.mul(rotation, new float3(0f, 1f, 0f));
            hand.EdgeMargin = 0.3f;
            hand.PinchRatio = 1f;
            hand.PalmPixel = new float2(pixelX >= 0f ? pixelX : (hint == 0 ? 480f : 800f), 360f);
            return hand;
        }

        /// <summary>Independent noise on the palm's centre and on every joint, as the lift's own error would be.</summary>
        public static void AddNoise(ref ObservedHand hand, ref HandsFilterRng rng, float sigma)
        {
            hand.PalmCentre += rng.Gaussian3(sigma);
            JointSet joints = hand.Joints;
            for (int joint = 0; joint < HandJoints.Count; joint++) joints[joint] += rng.Gaussian3(sigma);
            hand.Joints = joints;
        }

        /// <summary>The same hand moved rigidly: every joint, the palm's centre.</summary>
        public static void Translate(ref ObservedHand hand, float3 offset)
        {
            hand.PalmCentre += offset;
            JointSet joints = hand.Joints;
            for (int joint = 0; joint < HandJoints.Count; joint++) joints[joint] += offset;
            hand.Joints = joints;
        }
    }

    /// <summary>
    /// Drives a HandsFilter the way the sense step will: detections at a fixed rate, each arriving a fixed age
    /// after its capture, and frames at 60 Hz that call Observe for what has arrived and then Advance. The truth
    /// is a function of capture time handed in by the test.
    /// </summary>
    internal sealed class HandsFilterRig
    {
        /// <summary>Fills the hands seen by the detection captured at the given time; returns how many (0 to 2).</summary>
        public delegate int Source(double captureTime, ref ObservedHand a, ref ObservedHand b);

        public readonly HandsFilter Filter = new HandsFilter();
        public double DetectionHz = 30.0;
        public double FrameHz = 60.0;
        /// <summary>Seconds from capture to the frame that receives the result.</summary>
        public double Age = 0.08;
        /// <summary>Captures for which no result is delivered at all (a detector that has gone quiet), or null.</summary>
        public Func<double, bool> Silent;

        long _frame;
        long _detection;
        ObservedHand _a, _b;

        /// <summary>The time of the last frame, seconds on the app clock.</summary>
        public double Now { get; private set; }

        /// <summary>Detections delivered so far.</summary>
        public int Delivered { get; private set; }

        /// <summary>Runs frames up to and including the given time. frame is called after each Advance.</summary>
        public void Run(double until, Source source, Action<double> frame = null)
        {
            while ((_frame + 1) / FrameHz <= until + 1e-9)
            {
                _frame++;
                Now = _frame / FrameHz;
                while (_detection / DetectionHz + Age <= Now + 1e-9)
                {
                    double capture = _detection / DetectionHz;
                    _detection++;
                    if (Silent != null && Silent(capture)) continue;
                    int count = source(capture, ref _a, ref _b);
                    Filter.Observe(capture, count, in _a, in _b);
                    Delivered++;
                }
                Filter.Advance(Now);
                if (frame != null) frame(Now);
            }
        }
    }
}

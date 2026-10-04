using System;
using System.IO;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Hands.Tests
{
    /// <summary>
    /// Generates the sample hand logs of /recordings (documents/ATLAS_Hands_Design.md section 4.5): synthetic
    /// hands in front of the headset's rear camera, 30 detections a second, written the way a perfect detector
    /// and a perfect LiDAR would report them (<see cref="HandsSynth"/>), through the made-up camera of
    /// <see cref="EditorHandsSource"/>. No noise: a test that wants noise adds it. The files on disk are this
    /// generator's output; HandsLogTests keeps the two in step.
    ///
    /// The session frame and the room frame are the same here, the head rests at (0, 1.6, 0) looking along +Z,
    /// and each result is polled 80 ms after its frame was captured, as on the phone: the hands in a record are
    /// where they were at capture, the head pose of the record is the head at the poll.
    /// </summary>
    public static class SampleHands
    {
        /// <summary>Detections a second.</summary>
        public const double Rate = 30.0;
        /// <summary>Seconds from the capture of a frame to the poll of its result.</summary>
        public const double AgeSeconds = EditorHandsSource.AgeSeconds;
        /// <summary>The detector's clock reads this much more than the log's time: it is not the app clock.</summary>
        public const double DetectorClockOffset = 5000.0;
        /// <summary>The request time every result reports, milliseconds.</summary>
        public const float RequestMs = 18f;

        public const string WaveName = "hands_wave";
        public const string PressName = "hands_press";
        public const string PinchName = "hands_pinch";
        public static readonly string[] Names = { WaveName, PressName, PinchName };

        /// <summary>Two hands waving, with the head swaying a little.</summary>
        public const double WaveSeconds = 5.0;
        /// <summary>A right index finger pressing forward and back three times.</summary>
        public const double PressSeconds = 5.5;
        /// <summary>A right hand pinching three times.</summary>
        public const double PinchSeconds = 5.5;

        /// <summary>The index fingertip's distance from the camera, metres, when retracted and when pressed.</summary>
        public const float PressRetractedMetres = 0.40f, PressPressedMetres = 0.52f;
        /// <summary>How many times the press log presses and the pinch log pinches.</summary>
        public const int PressCount = 3, PinchCount = 3;

        static readonly float3 Head = new float3(0f, 1.6f, 0f);

        /// <summary>One hand in the session frame: where its palm centre is, how the palm frame is turned, and the shape.</summary>
        public struct Placement
        {
            public bool Present;
            public float3 Palm;
            /// <summary>Palm frame to session frame, columns x (thumb side), y (palm normal), z (towards the middle knuckle).</summary>
            public float3x3 Rotation;
            public float4 Curl;
            public float Thumb;
            public float Pinch;
        }

        public struct CameraPlacement
        {
            public float3 Position;
            public quaternion Rotation;

            public float4x4 ToSession { get { return float4x4.TRS(Position, Rotation, new float3(1f)); } }
        }

        /// <summary>Detections in a log: one at time 0 and one at every 1/30 s up to its length.</summary>
        public static int Records(string name)
        {
            return (int)Math.Round(SecondsOf(name) * Rate) + 1;
        }

        public static double SecondsOf(string name)
        {
            switch (name)
            {
                case WaveName: return WaveSeconds;
                case PressName: return PressSeconds;
                case PinchName: return PinchSeconds;
                default: throw new ArgumentException("No sample hand log named " + name, nameof(name));
            }
        }

        /// <summary>The whole file.</summary>
        public static byte[] Bytes(string name)
        {
            int records = Records(name);
            var stream = new MemoryStream();
            using (var writer = new HandsLogWriter(stream, true))
            {
                HandsDetection detection = default(HandsDetection);
                for (int k = 0; k < records; k++)
                {
                    Detection(name, k, ref detection, out float3 headPosition, out quaternion headRotation);
                    writer.Write(k / Rate, headPosition, headRotation, in detection);
                }
            }
            return stream.ToArray();
        }

        /// <summary>The file read back.</summary>
        public static HandsLogReader Log(string name)
        {
            return new HandsLogReader(Bytes(name));
        }

        /// <summary>The detection of record k and the head pose that polled it.</summary>
        public static void Detection(string name, int k, ref HandsDetection detection, out float3 headPosition, out quaternion headRotation)
        {
            double poll = k / Rate;
            double capture = poll - AgeSeconds;
            Scene(name, capture, out CameraPlacement camera, out Placement left, out Placement right);
            float4x4 toSession = camera.ToSession;
            HandsSynth.Begin(ref detection, k + 1, EditorHandsSource.ImageWidth, EditorHandsSource.ImageHeight,
                EditorHandsSource.FocalPixels, EditorHandsSource.FocalPixels,
                EditorHandsSource.ImageWidth * 0.5f, EditorHandsSource.ImageHeight * 0.5f,
                EditorHandsSource.DepthWidth, EditorHandsSource.DepthHeight,
                DetectorClockOffset + capture, DetectorClockOffset + poll, in toSession);
            detection.RequestMs = RequestMs;
            if (left.Present)
            {
                JointSet joints = JointsInCamera(in left, HandSide.Left, in camera);
                HandsSynth.AddHand(ref detection, in joints, (int)HandSide.Left);
            }
            if (right.Present)
            {
                JointSet joints = JointsInCamera(in right, HandSide.Right, in camera);
                HandsSynth.AddHand(ref detection, in joints, (int)HandSide.Right);
            }
            Scene(name, poll, out CameraPlacement now, out _, out _);
            headPosition = now.Position;
            headRotation = now.Rotation;
        }

        /// <summary>
        /// The truth behind record k: the 21 joints of a hand in the camera space of the frame that was captured,
        /// metres. False when the record has no such hand.
        /// </summary>
        public static bool Truth(string name, int k, HandSide side, out JointSet cameraJoints)
        {
            double capture = k / Rate - AgeSeconds;
            Scene(name, capture, out CameraPlacement camera, out Placement left, out Placement right);
            Placement hand = side == HandSide.Left ? left : right;
            cameraJoints = default(JointSet);
            if (!hand.Present) return false;
            cameraJoints = JointsInCamera(in hand, side, in camera);
            return true;
        }

        /// <summary>The same joints in the session frame.</summary>
        public static JointSet JointsInSession(in Placement hand, HandSide side)
        {
            JointSet palmFrame = EditorHandShapes.Build(side, in hand.Curl, hand.Thumb, hand.Pinch);
            float3 centre = EditorHandShapes.PalmCentre(in palmFrame);
            JointSet session = default(JointSet);
            for (int i = 0; i < HandJoints.Count; i++)
                session[i] = hand.Palm + math.mul(hand.Rotation, palmFrame[i] - centre);
            return session;
        }

        static JointSet JointsInCamera(in Placement hand, HandSide side, in CameraPlacement camera)
        {
            JointSet session = JointsInSession(in hand, side);
            quaternion back = math.inverse(camera.Rotation);
            JointSet inCamera = default(JointSet);
            for (int i = 0; i < HandJoints.Count; i++)
                inCamera[i] = Quantise(math.mul(back, session[i] - camera.Position));
            return inCamera;
        }

        // The generator's file is compared byte for byte, on whatever machine runs the tests. Everything after the
        // sines and cosines of a pose is plain arithmetic, which every machine does alike; rounding to a micrometre
        // keeps a last-bit difference in a sine from reaching the file.
        static float Quantise(float v)
        {
            return (float)(Math.Round(v * 1e6) / 1e6);
        }

        static float3 Quantise(float3 v)
        {
            return new float3(Quantise(v.x), Quantise(v.y), Quantise(v.z));
        }

        static quaternion Quantise(quaternion q)
        {
            return new quaternion(Quantise(q.value.x), Quantise(q.value.y), Quantise(q.value.z), Quantise(q.value.w));
        }

        /// <summary>The scene at a time: where the camera and the two hands are, in the session frame.</summary>
        public static void Scene(string name, double t, out CameraPlacement camera, out Placement left, out Placement right)
        {
            left = default(Placement);
            right = default(Placement);
            switch (name)
            {
                case WaveName: Wave(t, out camera, out left, out right); break;
                case PressName: Press(t, out camera, out right); break;
                case PinchName: Pinch(t, out camera, out right); break;
                default: throw new ArgumentException("No sample hand log named " + name, nameof(name));
            }
        }

        // Both hands are raised, palms away from the camera and fingers up: each swings about its elbow, which is
        // 0.20 m below the palm, in the plane of the picture. The head sways a little, so that the camera pose of a
        // record matters: a reader that forgets it sees the hands sway with the head.
        static void Wave(double t, out CameraPlacement camera, out Placement left, out Placement right)
        {
            float yaw = math.radians(3f) * (float)Math.Sin(2.0 * Math.PI * 0.30 * t);
            float pitch = math.radians(1.5f) * (float)Math.Sin(2.0 * Math.PI * 0.20 * t + 1.0);
            camera.Position = Quantise(Head + new float3((float)(0.010 * Math.Sin(2.0 * Math.PI * 0.25 * t)),
                                                         (float)(0.005 * Math.Sin(2.0 * Math.PI * 0.40 * t)), 0f));
            camera.Rotation = Quantise(math.mul(quaternion.RotateY(yaw), quaternion.RotateX(pitch)));
            float3x3 raised = EditorHandShapes.Orientation(math.radians(75f));
            left = Waving(t, -0.12f, 1.3, 0.9, in raised);
            right = Waving(t, 0.12f, 1.6, 0.0, in raised);
        }

        static Placement Waving(double t, float x, double hz, double phase, in float3x3 raised)
        {
            const float forearm = 0.20f;
            float swing = math.radians(14f) * (float)Math.Sin(2.0 * Math.PI * hz * t + phase);
            float s = math.sin(swing), c = math.cos(swing);
            var about = new float3x3(new float3(c, s, 0f), new float3(-s, c, 0f), new float3(0f, 0f, 1f));
            float3 elbow = Head + new float3(x, -0.02f - forearm, 0.45f);
            Placement hand = default(Placement);
            hand.Present = true;
            hand.Palm = elbow + math.mul(about, new float3(0f, forearm, 0f));
            hand.Rotation = math.mul(about, raised);
            return hand;
        }

        // A right hand pointing, its fingertip on a line that runs straight away from the camera: it moves from
        // 0.40 m to 0.52 m and back three times. The palm is about 10 cm behind the tip, so it is nearer than
        // LiDAR's 0.3 m for part of every press, as the design expects (Brief decision R3).
        static void Press(double t, out CameraPlacement camera, out Placement right)
        {
            camera.Position = Head;
            camera.Rotation = quaternion.identity;
            double u = Cycles(t, 0.5, 0.45, 0.20, 0.45, 1.5, PressCount);
            float z = (float)(PressRetractedMetres + (PressPressedMetres - PressRetractedMetres) * u);
            float3 tip = Head + new float3(0.04f, 0.02f, z);

            EditorHandShapes.Target(EditorHandShape.Pointing, out float4 curl, out float thumb, out float pinch);
            JointSet shape = EditorHandShapes.Build(HandSide.Right, in curl, thumb, pinch);
            float3x3 turn = EditorHandShapes.Orientation(math.radians(20f));
            right = default(Placement);
            right.Present = true;
            right.Rotation = turn;
            right.Curl = curl;
            right.Thumb = thumb;
            right.Pinch = pinch;
            right.Palm = tip - math.mul(turn, shape[HandJoint.IndexTip] - EditorHandShapes.PalmCentre(in shape));
        }

        // A right hand raised with its palm away, thumb and index closing on each other three times: 0.2 s to
        // close, 0.45 s held, 0.2 s to open, and a rest.
        static void Pinch(double t, out CameraPlacement camera, out Placement right)
        {
            camera.Position = Head;
            camera.Rotation = quaternion.identity;
            float a = (float)Cycles(t, 0.5, 0.20, 0.45, 0.20, 1.5, PinchCount);
            EditorHandShapes.Target(EditorHandShape.Pinch, out float4 closed, out float thumb, out float pinch);
            right = default(Placement);
            right.Present = true;
            right.Palm = Head + new float3(0.05f, -0.03f, 0.42f);
            right.Rotation = EditorHandShapes.Orientation(math.radians(60f));
            right.Curl = math.lerp(new float4(0.05f), closed, a);
            right.Thumb = thumb;
            right.Pinch = a * pinch;
        }

        /// <summary>0 at rest, rising to 1 over up, held for hold, back over down, then resting to the end of each cycle; after lead seconds.</summary>
        static double Cycles(double t, double lead, double up, double hold, double down, double cycle, int count)
        {
            double local = t - lead;
            if (local < 0.0 || local >= count * cycle) return 0.0;
            double c = local - Math.Floor(local / cycle) * cycle;
            if (c < up) return Smooth(c / up);
            if (c < up + hold) return 1.0;
            if (c < up + hold + down) return 1.0 - Smooth((c - up - hold) / down);
            return 0.0;
        }

        static double Smooth(double x)
        {
            return x * x * (3.0 - 2.0 * x);
        }

        /// <summary>/recordings in the repository: beside the Unity project folder.</summary>
        public static string RecordingsDirectory()
        {
            string project = Environment.GetEnvironmentVariable(RoomFixtures.ProjectDirVariable);
            if (string.IsNullOrEmpty(project)) project = Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(project, "..", "recordings"));
        }

        public static string PathOf(string name)
        {
            return Path.Combine(RecordingsDirectory(), name + ".handlog");
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Input.Tests
{
    /// <summary>
    /// Input_HandsTouch_StateMachine (documents/ATLAS_Hands_Design.md sections 6.2 and 9.1): a fingertip presses
    /// the gaze targets that dwell fires. The head looks at the ceiling throughout, so that nothing here is
    /// fired by the gaze.
    /// </summary>
    public class HandsTouchTests
    {
        private const float Dt = HandsRig.Dt;
        private const int Substeps = 4;
        private const float Substep = Dt / Substeps;

        private HandsRig _rig;

        [SetUp]
        public void SetUp()
        {
            _rig = new HandsRig();
        }

        // ---- a scripted finger and the sensing between it and input ----

        /// <summary>A fingertip's path: straight strokes that start and stop smoothly (minimum jerk), and rests.</summary>
        private sealed class Path
        {
            private readonly List<float3> _from = new List<float3>();
            private readonly List<float3> _to = new List<float3>();
            private readonly List<float> _start = new List<float>();
            private readonly List<float> _length = new List<float>();
            private float3 _end;

            public Path(float3 start)
            {
                _end = start;
            }

            public float Duration { get; private set; }

            public void To(float3 point, float seconds)
            {
                _from.Add(_end);
                _to.Add(point);
                _start.Add(Duration);
                _length.Add(seconds);
                _end = point;
                Duration += seconds;
            }

            public void Rest(float seconds)
            {
                To(_end, seconds);
            }

            public float3 At(float t)
            {
                for (int i = _start.Count - 1; i >= 0; i--)
                {
                    if (t < _start[i] && i > 0) continue;
                    float s = _length[i] > 0f ? math.saturate((t - _start[i]) / _length[i]) : 1f;
                    float eased = s * s * s * (10f - 15f * s + 6f * s * s);
                    return math.lerp(_from[i], _to[i], eased);
                }
                return _end;
            }
        }

        /// <summary>
        /// The fingertip as the hands module hands it over (section 4.4): detections of the finger at a rate,
        /// usable a latency after their capture, each with jitter along the line of sight (where a lifted
        /// fingertip is weak) and a third of it across. Filtered, it passes through a One-Euro filter with the
        /// design's constants and is predicted by its filtered velocity for at most 50 ms; unfiltered, the
        /// newest usable detection is held until the next.
        /// </summary>
        private sealed class Sensor
        {
            private readonly System.Random _rng;
            private readonly float _period, _latency, _jitter;
            private readonly bool _filtered;
            private readonly float3[] _pending = new float3[16];
            private readonly float[] _pendingTime = new float[16];
            private int _first, _count;
            private float _next;
            private bool _has;
            private float3 _x, _dx;
            private float _captured;

            public Sensor(System.Random rng, float hz, float latency, float jitter, bool filtered, float phase)
            {
                _rng = rng;
                _period = 1f / hz;
                _latency = latency;
                _jitter = jitter;
                _filtered = filtered;
                _next = phase;
            }

            /// <summary>Called every substep with where the finger is: takes a detection when one is due.</summary>
            public void Capture(float t, float3 finger, float3 head)
            {
                if (t + 1e-6f < _next) return;
                _next += _period;
                float3 seen = finger;
                if (_jitter > 0f)
                {
                    float3 sight = math.normalize(finger - head);
                    float3 across = math.normalize(math.cross(sight, new float3(0f, 1f, 0f)));
                    seen += sight * (Gauss() * _jitter) + across * (Gauss() * _jitter / 3f);
                }
                int at = (_first + _count) % _pending.Length;
                _pending[at] = seen;
                _pendingTime[at] = t;
                _count++;
            }

            /// <summary>The fingertip to hand to input at a frame's time. False while no detection is usable yet.</summary>
            public bool Read(float t, out float3 tip)
            {
                while (_count > 0 && _pendingTime[_first] + _latency <= t + 1e-6f)
                {
                    Feed(_pending[_first], _pendingTime[_first]);
                    _first = (_first + 1) % _pending.Length;
                    _count--;
                }
                tip = _x;
                if (!_has) return false;
                if (_filtered) tip = _x + _dx * math.min(t - _captured, 0.050f);
                return true;
            }

            private void Feed(float3 z, float time)
            {
                if (!_has || !_filtered)
                {
                    _x = z;
                    _dx = float3.zero;
                    _has = true;
                }
                else
                {
                    float3 raw = (z - _x) / _period;
                    _dx = math.lerp(_dx, raw, Alpha(1f));
                    _x = math.lerp(_x, z, Alpha(1.5f + 12f * math.length(_dx)));
                }
                _captured = time;
            }

            private float Alpha(float cutoff)
            {
                float tau = 1f / (2f * math.PI * cutoff);
                return 1f / (1f + tau / _period);
            }

            private float Gauss()
            {
                double u = 1.0 - _rng.NextDouble();
                double v = _rng.NextDouble();
                return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u)) * System.Math.Cos(2.0 * System.Math.PI * v));
            }
        }

        /// <summary>Plays a path through a sensor into the rig, frame by frame, up to a time.</summary>
        private static void Play(HandsRig rig, Path path, Sensor sensor, ref int step, float until)
        {
            Play(rig, path.At, sensor, ref step, until);
        }

        private static void Play(HandsRig rig, System.Func<float, float3> finger, Sensor sensor, ref int step, float until)
        {
            for (; step * Substep <= until; step++)
            {
                float t = step * Substep;
                sensor.Capture(t, finger(t), rig.Head);
                if (step % Substeps != 0) continue;
                if (sensor.Read(t, out float3 tip)) rig.Right(tip);
                else rig.Tick(default(HandsInput));
            }
        }

        /// <summary>
        /// One press by a walker who aims by eye, with 3 cm of depth noise on the fingertip. What "3 cm of noise"
        /// can mean was settled by the design's review: no rule on depth survives 3 cm of white noise on every
        /// detection (a finger held still would fire several times a second), and a constant error cannot be seen
        /// by input at all, because the walker sees the drawn fingertip and the drawn target in the same
        /// coordinates and steers the one through the other. What does reach input is the error changing while
        /// the finger moves: the drawn fingertip ends up to 3 cm short of its mark (2 cm through the face) or up to
        /// 3 cm past it. On top of that every detection has jitter along the line of sight, and a third of it
        /// across. The reach is slow, ordinary or a jab; detections come 30 times a second 70 ms late, or 15 times
        /// a second 110 ms late. When the fingertip is seen to stop short and nothing clicks, the walker pushes on
        /// to the mark. Returns how many times something fired.
        /// </summary>
        private int NoisyPress(HandsRig.Face face, float2 aimWithin, System.Random rng, int k, bool filtered, float jitter)
        {
            float hz = k % 2 == 0 ? 30f : 15f;
            float latency = k % 2 == 0 ? 0.070f : 0.110f;
            float reach = k % 3 == 0 ? 0.9f : k % 3 == 1 ? 0.5f : 0.3f;
            float u = aimWithin.x * 0.6f * ((float)rng.NextDouble() * 2f - 1f);
            float v = aimWithin.y * 0.6f * ((float)rng.NextDouble() * 2f - 1f);
            float miss = math.lerp(-0.03f, 0.03f, (float)rng.NextDouble());
            // The hand comes up from below and a little from the side, in a straight line through the point of the
            // face it aims at, and on to its mark behind the face.
            float3 rest = face.At(u + 0.05f * ((float)rng.NextDouble() * 2f - 1f), v - 0.06f, 0.25f);
            float3 aim = face.At(u, v, 0f);
            float3 mark = math.lerp(rest, aim, 0.27f / 0.25f);

            var path = new Path(rest);
            path.Rest(0.3f);
            path.To(math.lerp(rest, aim, (0.27f - miss) / 0.25f), reach);
            path.Rest(0.25f);
            var sensor = new Sensor(rng, hz, latency, jitter, filtered, (float)rng.NextDouble() / hz);

            int before = _rig.Events.Count;
            int step = 0;
            Play(_rig, path, sensor, ref step, path.Duration);
            if (_rig.Events.Count == before)
            {
                path.To(mark, 0.25f);
                path.Rest(0.25f);
            }
            path.To(rest, 0.5f);
            path.Rest(0.3f);
            Play(_rig, path, sensor, ref step, path.Duration);
            return _rig.Events.Count - before;
        }

        // ---- the press ----

        [Test]
        public void Input_HandsTouch_StateMachine_APressFromTheFrontFiresItsTargetOnce()
        {
            GazeTargetId row = _rig.Add(HandsRig.Row(AtlasAction.Primary, HandsRig.Panel, tag: 5));
            HandsRig.Face face = _rig.FaceOf(row);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Idle));

            int frames = HandsRig.Frames(0.4f);
            float hover = 0f;
            for (int i = 0; i <= frames; i++)
            {
                float z = math.lerp(0.12f, -0.02f, i / (float)frames);
                _rig.Right(face.At(0.02f, 0f, z));
                Assert.That(_rig.R.Hover, Is.GreaterThanOrEqualTo(hover), "the closeness never falls on the way in");
                hover = _rig.R.Hover;
                Assert.That(_rig.Input.Pressed(AtlasAction.Primary), Is.EqualTo(_rig.R.Fired), "the action is pressed in the frame the fingertip fires");
                if (z > 0.081f) Assert.That(_rig.R.Target.IsValid, Is.False, "nothing is hovered beyond 8 cm");
                if (z > 0f) Assert.That(_rig.Events, Is.Empty, "nothing fires in front of the face");
            }

            Assert.That(_rig.Events, Is.EqualTo(new[] { (AtlasAction.Primary, InputSource.Gaze, 5) }));
            Assert.That(_rig.Calls, Is.EqualTo(new[] { (row, 5) }));
            Assert.That(_rig.RightFired, Is.EqualTo(1), "Fired is true for one frame");
            Assert.That(_rig.Input.LastSource(AtlasAction.Primary), Is.EqualTo(InputSource.Gaze));
            Assert.That(_rig.Input.LastTag(AtlasAction.Primary), Is.EqualTo(5));
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Pressed));
            Assert.That(_rig.R.Pressed, Is.True);
            Assert.That(_rig.R.Target, Is.EqualTo(row));
            Assert.That(_rig.R.Hover, Is.EqualTo(1f));
            Assert.That(math.distance(_rig.R.FacePoint, face.At(0.02f, 0f, 0f)), Is.LessThan(1e-4f), "the point of the face under the fingertip, in the room");
            Assert.That(math.distance(_rig.R.FaceNormal, face.Normal), Is.LessThan(1e-4f), "towards the walker");
            Assert.That(_rig.L.Target.IsValid, Is.False, "the other hand is not there");

            _rig.Rest(face.At(0.02f, 0f, -0.02f), 0.5f);
            Assert.That(_rig.Events.Count, Is.EqualTo(1), "a Once target fires once however long the finger stays in");
            Assert.That(_rig.R.Pressed, Is.True);
            Assert.That(_rig.R.Fired, Is.False);

            _rig.Glide(face.At(0.02f, 0f, -0.02f), face.At(0.02f, 0f, 0.12f), 0.4f);
            Assert.That(_rig.R.Pressed, Is.False);
            Assert.That(_rig.R.Target.IsValid, Is.False);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Idle));
            Assert.That(_rig.Events.Count, Is.EqualTo(1), "nothing fires on the way out");
        }

        // The jitter of a detection has a standard deviation of 3 mm, which is already twice what the design asks of
        // a still fingertip on the phone (3 mm at the 95th percentile), or of 6 mm.
        [TestCase(false, true, 0.003f)]
        [TestCase(false, false, 0.003f)]
        [TestCase(true, true, 0.003f)]
        [TestCase(true, false, 0.003f)]
        [TestCase(false, true, 0.006f)]
        [TestCase(false, false, 0.006f)]
        [TestCase(true, true, 0.006f)]
        [TestCase(true, false, 0.006f)]
        public void Input_HandsTouch_StateMachine_TwentyNoisyPressesGiveTwentyFirings(bool sphere, bool filtered, float jitter)
        {
            // As the near panels are laid out: six menu rows edge to edge, or four signs 12 cm apart. Each press
            // aims at one of them, and that one is the one that must fire.
            var ids = new List<GazeTargetId>();
            if (sphere)
                for (int i = 0; i < 4; i++)
                    ids.Add(_rig.Add(HandsRig.Chip(AtlasAction.Primary, HandsRig.Panel + new float3(-0.18f + 0.12f * i, 0.07f, 0f), tag: i + 1)));
            else
                for (int i = 0; i < 6; i++)
                    ids.Add(_rig.Add(HandsRig.Row(AtlasAction.Primary, HandsRig.Panel + new float3(0f, 0.08f - HandsRig.RowPitch * i, 0f), tag: i + 1)));
            float2 aimWithin = sphere ? new float2(HandsRig.ChipRadius * 0.7f) : HandsRig.RowHalf;
            var rng = new System.Random(sphere ? 2026 : 1004);

            for (int k = 0; k < 20; k++)
            {
                int aimed = k % ids.Count;
                Assert.That(NoisyPress(_rig.FaceOf(ids[aimed]), aimWithin, rng, k, filtered, jitter), Is.EqualTo(1),
                            "press " + k + " fires once, and only once");
                // With 3 mm of jitter the target aimed at is the one that fires. With 6 mm a press near the edge of a
                // 3 cm row may be seen on the row beside it; it never goes farther, and never fires both.
                int fired = _rig.Events[_rig.Events.Count - 1].tag - 1;
                Assert.That(_rig.Calls[_rig.Calls.Count - 1], Is.EqualTo((ids[fired], fired + 1)));
                if (jitter <= 0.003f || sphere) Assert.That(fired, Is.EqualTo(aimed), "press " + k + " fires the target it was aimed at");
                else Assert.That(math.abs(fired - aimed), Is.LessThanOrEqualTo(1), "press " + k + " fires the row aimed at, or the one beside it");
            }
            Assert.That(_rig.Events.Count, Is.EqualTo(20));
            Assert.That(_rig.Calls.Count, Is.EqualTo(20));
            Assert.That(_rig.RightFired, Is.EqualTo(20));
        }

        [TestCase(30f, 0.070f, true)]
        [TestCase(15f, 0.110f, true)]
        [TestCase(30f, 0.070f, false)]
        [TestCase(15f, 0.110f, false)]
        public void Input_HandsTouch_StateMachine_AWanderingFingerNeverFires(float hz, float latency, bool filtered)
        {
            for (int i = 0; i < 6; i++)
                _rig.Add(HandsRig.Row(AtlasAction.Primary, HandsRig.Panel + new float3(0f, 0.08f - HandsRig.RowPitch * i, 0f), tag: i + 1));
            _rig.Add(HandsRig.Chip(AtlasAction.Secondary, HandsRig.Panel + new float3(-0.18f, 0.16f, 0f)));
            // The panel's own plane, about its middle.
            var face = new HandsRig.Face
            {
                Centre = HandsRig.Panel, Normal = new float3(0f, 0f, -1f), Right = new float3(1f, 0f, 0f), Up = new float3(0f, 1f, 0f)
            };

            // Two minutes of a hand moving about in front of the panel with no press meant: the fingertip wanders
            // between 1 and 15 cm from the rows, and all over them sideways.
            var rng = new System.Random(11);
            float a = (float)rng.NextDouble() * 6.28f, b = (float)rng.NextDouble() * 6.28f, c = (float)rng.NextDouble() * 6.28f;
            System.Func<float, float3> finger = t =>
            {
                float z = 0.08f + 0.07f * (math.sin(6.2832f * 0.23f * t + a) + math.sin(6.2832f * 0.41f * t + b) + math.sin(6.2832f * 0.77f * t + c)) / 3f;
                float u = 0.20f * math.sin(6.2832f * 0.31f * t + b);
                float v = 0.03f + 0.12f * math.sin(6.2832f * 0.19f * t + c);
                return face.At(u, v, z);
            };
            int step = 0;
            Play(_rig, finger, new Sensor(rng, hz, latency, 0.003f, filtered, 0f), ref step, 120f);
            Assert.That(_rig.Events, Is.Empty);
            Assert.That(_rig.RightFired, Is.EqualTo(0));
        }

        [TestCase(30f, 0.070f, true)]
        [TestCase(15f, 0.110f, true)]
        [TestCase(30f, 0.070f, false)]
        [TestCase(15f, 0.110f, false)]
        public void Input_HandsTouch_StateMachine_AFingerHeldShortOfTheFaceNeverFires(float hz, float latency, bool filtered)
        {
            GazeTargetId row = _rig.Add(HandsRig.Row(AtlasAction.Primary, HandsRig.Panel));
            HandsRig.Face face = _rig.FaceOf(row);
            var rng = new System.Random(7);
            // Input presses with the fingertip it is given. Held from detection to detection, that fingertip stops
            // where the finger stops, however fast it came. Predicted 50 ms ahead by a velocity that lags, it runs
            // on for 2 cm after a slow reach and 4 to 5 cm after a jab: a jab that stops 3 cm short is then seen
            // through the face, and that is a press. So the predicted fingertip is brought up at an unhurried pace.
            foreach (float reach in filtered ? new[] { 0.9f, 0.7f } : new[] { 0.9f, 0.5f, 0.3f })
            {
                float3 rest = face.At(0.03f, -0.05f, 0.25f);
                var path = new Path(rest);
                path.Rest(0.3f);
                path.To(face.At(0f, 0f, 0.03f), reach);      // brought up, and stopped 3 cm short
                path.Rest(10f);
                path.To(rest, 0.5f);
                path.Rest(0.3f);
                int step = 0;
                Play(_rig, path, new Sensor(rng, hz, latency, 0.003f, filtered, (float)rng.NextDouble() / hz), ref step, path.Duration);
            }
            Assert.That(_rig.Events, Is.Empty);
            Assert.That(_rig.RightFired, Is.EqualTo(0));
        }

        [Test]
        public void Input_HandsTouch_StateMachine_AQuickPressSampledAt15HzStillFires()
        {
            foreach (bool sphere in new[] { false, true })
            {
                foreach (float hold in new[] { 0.2f, 0.1f, 0f })
                {
                    for (int phase = 0; phase < 16; phase++)
                    {
                        var rig = new HandsRig();
                        GazeTargetId id = sphere
                            ? rig.Add(HandsRig.Chip(AtlasAction.Primary, HandsRig.Panel))
                            : rig.Add(HandsRig.Row(AtlasAction.Primary, HandsRig.Panel));
                        HandsRig.Face face = rig.FaceOf(id);
                        float3 rest = face.At(0.02f, -0.05f, 0.25f);
                        var path = new Path(rest);
                        path.Rest(0.3f);
                        path.To(face.At(0f, 0f, -0.02f), 0.3f);      // a jab: 27 cm in 0.3 s, 1.7 m/s at its fastest
                        path.Rest(hold);
                        path.To(rest, 0.3f);
                        path.Rest(0.3f);
                        // 15 detections a second, each 110 ms old when first used and held until the next: no filter, no prediction.
                        var sensor = new Sensor(null, 15f, 0.110f, 0f, false, phase / 16f / 15f);
                        int step = 0;
                        Play(rig, path, sensor, ref step, path.Duration);
                        Assert.That(rig.Events.Count, Is.EqualTo(1),
                                    (sphere ? "sphere" : "rectangle") + ", held " + hold + " s, detector phase " + phase + " of 16");
                    }
                }
            }
        }

        // ---- what is not a press ----

        [Test]
        public void Input_HandsTouch_StateMachine_APassFromBehindFiresNothing()
        {
            GazeTargetId row = _rig.Add(HandsRig.Row(AtlasAction.Primary, HandsRig.Panel));
            GazeTargetId chip = _rig.Add(HandsRig.Chip(AtlasAction.Secondary, HandsRig.Panel + new float3(0f, 0.15f, 0f)));
            foreach (GazeTargetId id in new[] { row, chip })
            {
                HandsRig.Face face = _rig.FaceOf(id);
                // Straight through from 20 cm behind to 20 cm in front, slowly and fast; the hand then leaves the view.
                _rig.Glide(face.At(0.01f, 0f, -0.20f), face.At(0.01f, 0f, 0.20f), 0.8f);
                _rig.NoHands(0.1f);
                _rig.Glide(face.At(0.01f, 0f, -0.20f), face.At(0.01f, 0f, 0.20f), 0.2f);
                _rig.NoHands(0.1f);
                // Out to 1.5 cm in front and in again: it was never seen in front by the arming distance.
                _rig.Glide(face.At(0.01f, 0f, -0.06f), face.At(0.01f, 0f, 0.015f), 0.3f);
                _rig.Glide(face.At(0.01f, 0f, 0.015f), face.At(0.01f, 0f, -0.06f), 0.3f);
                _rig.NoHands(0.1f);
            }
            Assert.That(_rig.Events, Is.Empty);
            Assert.That(_rig.RightFired, Is.EqualTo(0));

            // Out to 4 cm and in again is a press from the front like any other.
            HandsRig.Face front = _rig.FaceOf(row);
            _rig.Glide(front.At(0.01f, 0f, -0.06f), front.At(0.01f, 0f, 0.04f), 0.3f);
            _rig.Glide(front.At(0.01f, 0f, 0.04f), front.At(0.01f, 0f, -0.02f), 0.2f);
            Assert.That(_rig.Fired(), Is.EqualTo(new[] { AtlasAction.Primary }));
        }

        [Test]
        public void Input_HandsTouch_StateMachine_ASidewaysSweepFiresNothing()
        {
            GazeTargetId row = _rig.Add(HandsRig.Row(AtlasAction.Primary, HandsRig.Panel));
            GazeTargetId chip = _rig.Add(HandsRig.Chip(AtlasAction.Secondary, HandsRig.Panel + new float3(0f, 0.20f, 0f)));
            foreach (GazeTargetId id in new[] { row, chip })
            {
                HandsRig.Face face = _rig.FaceOf(id);
                // Across the face at a steady depth: in front of it, on it, on the press plane, behind it.
                foreach (float depth in new[] { 0.03f, 0.012f, 0f, -0.010f, -0.03f })
                {
                    _rig.Glide(face.At(-0.40f, 0f, depth), face.At(0.40f, 0f, depth), 0.8f);
                    _rig.NoHands(0.1f);
                    _rig.Glide(face.At(0f, -0.15f, depth), face.At(0f, 0.15f, depth), 0.25f);
                    _rig.NoHands(0.1f);
                }
                // Across the face while sinking through it: more across than in.
                _rig.Glide(face.At(-0.20f, 0f, 0.04f), face.At(0.20f, 0f, -0.04f), 0.5f);
                _rig.NoHands(0.1f);
                _rig.Glide(face.At(0.04f, -0.10f, 0.04f), face.At(0.04f, 0.10f, -0.04f), 0.3f);
                _rig.NoHands(0.1f);
            }
            Assert.That(_rig.Events, Is.Empty);
            Assert.That(_rig.RightFired, Is.EqualTo(0));

            // A sweep that ends through the face has pressed nothing, and cannot press by a nudge from there: the
            // fingertip must be seen in front again first.
            HandsRig.Face swept = _rig.FaceOf(row);
            _rig.Glide(swept.At(-0.20f, 0f, 0.04f), swept.At(0f, 0f, -0.012f), 0.3f);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Hover));
            _rig.Glide(swept.At(0f, 0f, -0.012f), swept.At(0f, 0f, 0.015f), 0.15f);
            _rig.Glide(swept.At(0f, 0f, 0.015f), swept.At(0f, 0f, -0.02f), 0.15f);
            Assert.That(_rig.Events, Is.Empty);
            _rig.Glide(swept.At(0f, 0f, -0.02f), swept.At(0f, 0f, 0.03f), 0.15f);
            _rig.Glide(swept.At(0f, 0f, 0.03f), swept.At(0f, 0f, -0.02f), 0.15f);
            Assert.That(_rig.Fired(), Is.EqualTo(new[] { AtlasAction.Primary }));
        }

        [Test]
        public void Input_HandsTouch_StateMachine_NothingFiresBelowConfidenceOrWithoutAValidHand()
        {
            GazeTargetId row = _rig.Add(HandsRig.Row(AtlasAction.Primary, HandsRig.Panel));
            HandsRig.Face face = _rig.FaceOf(row);
            float3 front = face.At(0f, 0f, 0.10f), through = face.At(0f, 0f, -0.02f);
            int frames = HandsRig.Frames(0.3f);

            // A whole press just under the confidence that touch needs: the hand hovers nothing and is near nothing.
            for (int i = 0; i <= frames; i++)
            {
                _rig.Right(math.lerp(front, through, i / (float)frames), 0.59f);
                Assert.That(_rig.R.Target.IsValid, Is.False);
                Assert.That(_rig.Input.HandsTouch.AnyNear, Is.False);
            }
            _rig.NoHands(0.1f);
            // A hand that is not valid, and hands that are not active.
            for (int i = 0; i <= frames; i++)
            {
                float3 p = math.lerp(front, through, i / (float)frames);
                _rig.Tick(new HandsInput { Active = true, Right = new HandsFingertip { Valid = false, World = p, Confidence = 1f } });
                Assert.That(_rig.R.Target.IsValid, Is.False);
            }
            for (int i = 0; i <= frames; i++)
            {
                float3 p = math.lerp(front, through, i / (float)frames);
                _rig.Tick(new HandsInput { Active = false, Right = HandsRig.Tip(p) });
                Assert.That(_rig.R.Target.IsValid, Is.False);
            }
            // A fingertip that is not a number.
            _rig.Tick(HandsRig.RightHand(new float3(float.NaN, 1.5f, 0.45f)));
            Assert.That(_rig.R.Target.IsValid, Is.False);

            // Confidence falls just as the fingertip goes through and comes back while it is in: nothing fires late.
            _rig.NoHands(0.1f);
            _rig.Glide(front, face.At(0f, 0f, 0.005f), 0.2f);
            _rig.Glide(face.At(0f, 0f, 0.005f), through, 0.1f, 0.5f);
            _rig.Rest(through, 1f);
            Assert.That(_rig.Events, Is.Empty);
            Assert.That(_rig.RightFired, Is.EqualTo(0));

            // At 0.6 it fires.
            _rig.NoHands(0.1f);
            _rig.Glide(front, through, 0.3f, 0.6f);
            Assert.That(_rig.Fired(), Is.EqualTo(new[] { AtlasAction.Primary }));
        }

        [Test]
        public void Input_HandsTouch_StateMachine_LookDownAndFarTargetsAreNeverTouched()
        {
            // LookDown has no place: whatever its unused centre and radius say, there is nothing to press.
            var down = new HandsRig();
            down.Input.Targets.Add(new GazeTargetData
            {
                Action = AtlasAction.Secondary, Space = GazeSpace.LookDown, PitchBelowDeg = -35f, Context = InputContext.Space,
                Centre = HandsRig.Panel, Radius = 0.05f
            });
            down.Glide(HandsRig.Panel - new float3(0f, 0f, 0.10f), HandsRig.Panel + new float3(0f, 0f, 0.02f), 0.3f);
            Assert.That(down.Events, Is.Empty);
            Assert.That(down.R.Target.IsValid, Is.False);
            Assert.That(down.Input.HandsTouch.AnyNear, Is.False);

            // A row 0.80 m from the head is out of reach, wherever the fingertip is said to be.
            GazeTargetId row = _rig.Add(HandsRig.Row(AtlasAction.Primary, HandsRig.Eye + new float3(0f, 0f, 0.80f)));
            HandsRig.Face face = _rig.FaceOf(row);
            _rig.Glide(face.At(0f, 0f, 0.10f), face.At(0f, 0f, -0.02f), 0.3f);
            _rig.Rest(face.At(0f, 0f, -0.02f), 0.2f);
            Assert.That(_rig.Events, Is.Empty);
            Assert.That(_rig.R.Target.IsValid, Is.False);
            Assert.That(_rig.Input.HandsTouch.AnyNear, Is.False, "a target that cannot be touched is not near either");

            // One step closer it is within 0.75 m and fires.
            _rig.NoHands(0.1f);
            _rig.Head = HandsRig.Eye + new float3(0f, 0f, 0.06f);
            _rig.Glide(face.At(0f, 0f, 0.10f), face.At(0f, 0f, -0.02f), 0.3f);
            Assert.That(_rig.Fired(), Is.EqualTo(new[] { AtlasAction.Primary }));

            // A target's own distance limit holds for the fingertip as it does for the gaze.
            var limited = new HandsRig();
            GazeTargetData chip = HandsRig.Chip(AtlasAction.Primary, HandsRig.Eye + new float3(0f, 0f, 0.60f));
            chip.MaxDistance = 0.5f;
            GazeTargetId chipId = limited.Add(chip);
            HandsRig.Face chipFace = limited.FaceOf(chipId);
            limited.Glide(chipFace.At(0f, 0f, 0.10f), chipFace.At(0f, 0f, -0.02f), 0.3f);
            Assert.That(limited.Events, Is.Empty);
            limited.NoHands(0.1f);
            limited.Head = HandsRig.Eye + new float3(0f, 0f, 0.15f);
            limited.Glide(chipFace.At(0f, 0f, 0.10f), chipFace.At(0f, 0f, -0.02f), 0.3f);
            Assert.That(limited.Events.Count, Is.EqualTo(1));
        }

        [Test]
        public void Input_HandsTouch_StateMachine_AReachThroughEndsThePress()
        {
            GazeTargetData handle = HandsRig.Chip(AtlasAction.None, HandsRig.Panel, mode: GazeMode.Hold);
            handle.Axis = AtlasAxis.Sweep;
            handle.AxisValue = -1f;
            GazeTargetId id = _rig.Input.Targets.Add(handle);
            HandsRig.Face face = _rig.FaceOf(id);
            _rig.Glide(face.At(0f, 0f, 0.10f), face.At(0f, 0f, -0.02f), 0.3f);
            Assert.That(_rig.Input.Axis(AtlasAxis.Sweep), Is.EqualTo(-1f));
            // The hand goes on through the panel to something behind it: that is not holding the handle.
            _rig.Glide(face.At(0f, 0f, -0.02f), face.At(0f, 0f, -0.12f), 0.3f);
            Assert.That(_rig.Input.Axis(AtlasAxis.Sweep), Is.EqualTo(0f));
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Idle));
            // Coming back through from behind fires and holds nothing.
            int frames = HandsRig.Frames(0.4f);
            for (int i = 0; i <= frames; i++)
            {
                _rig.Right(face.At(0f, 0f, math.lerp(-0.12f, 0.02f, i / (float)frames)));
                Assert.That(_rig.Input.Axis(AtlasAxis.Sweep), Is.EqualTo(0f));
            }
            Assert.That(_rig.RightFired, Is.EqualTo(1));

            // The same for a target that repeats: it does not go on stepping while the hand is through it.
            var other = new HandsRig();
            GazeTargetId plus = other.Add(HandsRig.Row(AtlasAction.NavRight, HandsRig.Panel, mode: GazeMode.Repeat));
            HandsRig.Face plusFace = other.FaceOf(plus);
            other.Glide(plusFace.At(0f, 0f, 0.10f), plusFace.At(0f, 0f, -0.12f), 0.4f);
            other.Rest(plusFace.At(0f, 0f, -0.12f), 3f);
            Assert.That(other.Fired(), Is.EqualTo(new[] { AtlasAction.NavRight }));
        }

        [Test]
        public void Input_HandsTouch_StateMachine_ArmingNeedsAnApproachFromTheFront()
        {
            GazeTargetId row = _rig.Add(HandsRig.Row(AtlasAction.Primary, HandsRig.Panel));
            HandsRig.Face face = _rig.FaceOf(row);
            float3 through = face.At(0f, 0f, -0.02f);

            // Two samples of a fast hand, as a slow detector gives them. From 6 cm in front and 11.5 cm above the row
            // straight to 2 cm through it: more in than a sweep, but it came from beside the row, not from in front.
            _rig.Right(face.At(0f, 0.115f, 0.06f));
            _rig.Right(through);
            _rig.Rest(through, 0.2f);
            Assert.That(_rig.Events, Is.Empty, "from the side, 55 degrees off the face's normal");
            _rig.NoHands(0.1f);
            // From 6 cm in front and 6 cm above: inside the approach cone, so it is a press.
            _rig.Right(face.At(0f, 0.06f, 0.06f));
            _rig.Right(through);
            Assert.That(_rig.Events.Count, Is.EqualTo(1), "from the front, 35 degrees off the normal");
            _rig.NoHands(0.1f);

            // In front, through the panel beside the row, out again beside it, and only then over the row and in:
            // since it was last seen in front it has been through, so it has to be seen in front again.
            _rig.Events.Clear();
            _rig.Right(face.At(0f, 0.05f, 0.04f));
            _rig.Right(face.At(0f, 0.05f, -0.03f));
            _rig.Right(face.At(0f, 0.05f, 0.01f));
            _rig.Right(face.At(0f, 0f, 0.01f));
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Hover));
            _rig.Right(through);
            Assert.That(_rig.Events, Is.Empty);
            _rig.Right(face.At(0f, 0f, 0.03f));
            _rig.Right(through);
            Assert.That(_rig.Events.Count, Is.EqualTo(1));
        }

        [Test]
        public void Input_HandsTouch_StateMachine_ThePressIsTakenWhereTheFingertipCameThrough()
        {
            GazeTargetId row = _rig.Add(HandsRig.Row(AtlasAction.Primary, HandsRig.Panel));
            HandsRig.Face face = _rig.FaceOf(row);

            // A jab that a slow detector sees 3 cm in front and next 10 cm through the panel: it pressed the row it
            // went through, once, and it is not holding it.
            _rig.Right(face.At(0f, 0f, 0.03f));
            _rig.Right(face.At(0f, 0f, -0.10f));
            Assert.That(_rig.Events.Count, Is.EqualTo(1));
            Assert.That(_rig.R.Fired, Is.True);
            Assert.That(_rig.R.Target, Is.EqualTo(row));
            _rig.Rest(face.At(0f, 0f, -0.10f), 0.3f);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Idle));
            Assert.That(_rig.R.Target.IsValid, Is.False);
            Assert.That(_rig.Events.Count, Is.EqualTo(1));
            _rig.NoHands(0.1f);

            // The same jab past the row's edge went through no target: seen from 6 cm above the row, in front of it,
            // to on the row's line, far behind it, it crossed the press plane 4 cm above the row.
            _rig.Right(face.At(0f, 0.06f, 0.05f));
            _rig.Right(face.At(0f, 0.06f, 0.03f));
            _rig.Right(face.At(0f, 0f, -0.10f));
            _rig.Rest(face.At(0f, 0f, -0.10f), 0.1f);
            Assert.That(_rig.Events.Count, Is.EqualTo(1));
            _rig.NoHands(0.1f);

            // And one that was never seen in front: nothing, and it is over nothing.
            _rig.Right(face.At(0f, 0f, 0.01f));
            _rig.Right(face.At(0f, 0f, -0.10f));
            Assert.That(_rig.Events.Count, Is.EqualTo(1));
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Idle));
            Assert.That(_rig.R.Target.IsValid, Is.False);
            _rig.NoHands(0.1f);

            // Less far through, the same rule: seen 7 cm above the row and 1 cm in front, then on the row and 7 cm
            // behind its face, the fingertip came through the press plane 5 cm above the row, not on it.
            _rig.Right(face.At(0f, 0.07f, 0.05f));
            _rig.Right(face.At(0f, 0.07f, 0.01f));
            _rig.Right(face.At(0f, 0f, -0.07f));
            Assert.That(_rig.R.Target, Is.EqualTo(row), "it is over the row now");
            Assert.That(_rig.R.Pressed, Is.False);
            Assert.That(_rig.Events.Count, Is.EqualTo(1));
            _rig.NoHands(0.1f);

            // Struck right through the row 4 cm from a small target that lies on it: the row is what it went
            // through, though the small target's centre is the nearer one.
            GazeTargetData plus = HandsRig.Row(AtlasAction.NavRight, face.At(0.17f, 0f, 0.0075f));
            plus.HalfSize = new float2(0.021f, 0.0135f);
            _rig.Add(plus);
            _rig.Right(face.At(0.11f, 0f, 0.03f));
            _rig.Right(face.At(0.11f, 0f, -0.10f));
            Assert.That(_rig.Fired(), Is.EqualTo(new[] { AtlasAction.Primary, AtlasAction.Primary }));
        }

        [Test]
        public void Input_HandsTouch_StateMachine_ATargetMovedOntoAStillFingertipIsNotPressed()
        {
            GazeTargetId row = _rig.Add(HandsRig.Row(AtlasAction.Primary, HandsRig.Panel));
            HandsRig.Face face = _rig.FaceOf(row);
            // The fingertip rests 3 cm in front of the row, armed.
            float3 resting = face.At(0f, 0f, 0.03f);
            _rig.Rest(resting, 0.3f);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Armed));

            // The row's owner moves it 5 cm towards the walker (a panel that is set again): the still fingertip is
            // now 2 cm through it. The finger did not move, so nothing is pressed.
            _rig.Input.Targets.Move(row, HandsRig.Panel - new float3(0f, 0f, 0.05f));
            _rig.Rest(resting, 0.5f);
            Assert.That(_rig.Events, Is.Empty);
            Assert.That(_rig.R.Pressed, Is.False);
            Assert.That(_rig.R.Target, Is.EqualTo(row), "it is over the row all the same");

            // The finger comes out in front of the row where it now is, and presses it.
            HandsRig.Face moved = _rig.FaceOf(row);
            _rig.Glide(resting, moved.At(0f, 0f, 0.04f), 0.2f);
            _rig.Glide(moved.At(0f, 0f, 0.04f), moved.At(0f, 0f, -0.02f), 0.2f);
            Assert.That(_rig.Events.Count, Is.EqualTo(1));
        }

        // ---- which face, which target ----

        [Test]
        public void Input_HandsTouch_StateMachine_ARectangleIsPressedFromTheSideTheHeadIsOn()
        {
            // The menu faces the walker with its -Z, the Glued sign with its +Z: a rectangle does not say which side is its front.
            foreach (float yaw in new[] { 0f, 180f })
            {
                var rig = new HandsRig();
                GazeTargetData target = HandsRig.Row(AtlasAction.Primary, HandsRig.Panel);
                target.Rotation = quaternion.RotateY(math.radians(yaw));
                GazeTargetId id = rig.Add(target);
                HandsRig.Face face = rig.FaceOf(id);
                Assert.That(face.Normal.z, Is.LessThan(-0.99f), "the walker stands on the -Z side");

                // From the far side towards the walker: nothing.
                rig.Glide(HandsRig.Panel + new float3(0.01f, 0f, 0.12f), HandsRig.Panel + new float3(0.01f, 0f, -0.02f), 0.4f);
                Assert.That(rig.Events, Is.Empty, "yaw " + yaw);
                rig.NoHands(0.1f);
                // From the walker's side: it fires, and the face looks at the walker.
                rig.Glide(face.At(0.01f, 0f, 0.12f), face.At(0.01f, 0f, -0.02f), 0.4f);
                Assert.That(rig.Events.Count, Is.EqualTo(1), "yaw " + yaw);
                Assert.That(math.distance(rig.R.FaceNormal, new float3(0f, 0f, -1f)), Is.LessThan(1e-4f));

                // The walker goes round the panel: the other side is now the front.
                rig.NoHands(0.1f);
                rig.Head = HandsRig.Eye + new float3(0f, 0f, 0.9f);
                HandsRig.Face back = rig.FaceOf(id);
                Assert.That(back.Normal.z, Is.GreaterThan(0.99f));
                rig.Glide(back.At(0.01f, 0f, 0.12f), back.At(0.01f, 0f, -0.02f), 0.4f);
                Assert.That(rig.Events.Count, Is.EqualTo(2), "yaw " + yaw);
                Assert.That(math.distance(rig.R.FaceNormal, new float3(0f, 0f, 1f)), Is.LessThan(1e-4f));
            }
        }

        [Test]
        public void Input_HandsTouch_StateMachine_ASphereIsPressedAsADiscFacingTheHead()
        {
            GazeTargetId chip = _rig.Add(HandsRig.Chip(AtlasAction.Secondary, HandsRig.Panel + new float3(0.15f, 0.05f, 0f), tag: 2));
            HandsRig.Face face = _rig.FaceOf(chip);

            // Poked from the side, across the ball 2 cm in front of its centre and out again: not through the disc.
            _rig.Glide(face.At(-0.15f, 0f, 0.02f), face.At(0.15f, 0f, 0.02f), 0.5f);
            Assert.That(_rig.Events, Is.Empty);
            _rig.NoHands(0.1f);

            // Pressed along the line to the head, off its centre: fires, and the face is the disc through the centre.
            _rig.Glide(face.At(0.02f, 0.01f, 0.12f), face.At(0.02f, 0.01f, -0.02f), 0.4f);
            Assert.That(_rig.Events, Is.EqualTo(new[] { (AtlasAction.Secondary, InputSource.Gaze, 2) }));
            Assert.That(math.distance(_rig.R.FaceNormal, face.Normal), Is.LessThan(1e-3f), "the disc faces the head");
            Assert.That(math.distance(_rig.R.FacePoint, face.At(0.02f, 0.01f, 0f)), Is.LessThan(1e-3f), "on the plane through the centre");
            _rig.NoHands(0.1f);

            // Beside the circle by more than the margin: nothing.
            _rig.Glide(face.At(0.08f, 0f, 0.12f), face.At(0.08f, 0f, -0.02f), 0.4f);
            Assert.That(_rig.Events.Count, Is.EqualTo(1));
            _rig.NoHands(0.1f);

            // The head moves: the disc turns to face it, and is pressed along the new line.
            _rig.Head = HandsRig.Eye + new float3(0.4f, 0f, 0.1f);
            HandsRig.Face turned = _rig.FaceOf(chip);
            Assert.That(math.dot(turned.Normal, face.Normal), Is.LessThan(0.9f));
            _rig.Glide(turned.At(0f, 0f, 0.12f), turned.At(0f, 0f, -0.02f), 0.4f);
            Assert.That(_rig.Events.Count, Is.EqualTo(2));
            Assert.That(math.distance(_rig.R.FaceNormal, turned.Normal), Is.LessThan(1e-3f));
        }

        [Test]
        public void Input_HandsTouch_StateMachine_HeadTargetsAreTestedInTheHeadsFrame()
        {
            var target = new GazeTargetData
            {
                Action = AtlasAction.Primary, Space = GazeSpace.Head, Shape = GazeShape.Rect,
                Centre = new float3(0f, -0.10f, 0.45f), Rotation = quaternion.identity, HalfSize = new float2(0.10f, 0.03f),
                Context = InputContext.Space, Tag = 9
            };
            GazeTargetId id = _rig.Add(target);

            // The head stands elsewhere in the room and is turned: the rectangle goes with it.
            _rig.Head = new float3(2f, 1.5f, -1f);
            quaternion turn = math.mul(quaternion.RotateY(math.radians(110f)), quaternion.RotateX(math.radians(20f)));
            _rig.Looking = turn;
            float3 from = _rig.Head + math.mul(turn, new float3(0f, -0.10f, 0.35f));
            float3 to = _rig.Head + math.mul(turn, new float3(0f, -0.10f, 0.47f));
            _rig.Glide(from, to, 0.3f);
            Assert.That(_rig.Events, Is.EqualTo(new[] { (AtlasAction.Primary, InputSource.Gaze, 9) }));
            Assert.That(_rig.R.Target, Is.EqualTo(id));
            Assert.That(math.distance(_rig.R.FaceNormal, math.mul(turn, new float3(0f, 0f, -1f))), Is.LessThan(1e-4f), "reported in the room frame");
            Assert.That(math.distance(_rig.R.FacePoint, _rig.Head + math.mul(turn, new float3(0f, -0.10f, 0.45f))), Is.LessThan(1e-4f));
            _rig.NoHands(0.1f);

            // The head turns on while the fingertip keeps its place in front of it: it moves through the room, and
            // through nothing that the head carries.
            for (int i = 0; i < 90; i++)
            {
                quaternion turning = math.mul(quaternion.RotateY(math.radians(110f + 2f * i)), quaternion.RotateX(math.radians(20f)));
                _rig.Looking = turning;
                _rig.Right(_rig.Head + math.mul(turning, new float3(0f, -0.10f, 0.41f)));
                Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Armed));
            }
            Assert.That(_rig.Events.Count, Is.EqualTo(1));

            // A fingertip that stands still in the room while the head turns the rectangle across it is swept, not pressed.
            _rig.NoHands(0.1f);
            float3 still = _rig.Head + math.mul(turn, new float3(0f, -0.10f, 0.46f));
            for (int i = 0; i < 60; i++)
            {
                _rig.Looking = math.mul(quaternion.RotateY(math.radians(80f + i)), quaternion.RotateX(math.radians(20f)));
                _rig.Right(still);
            }
            Assert.That(_rig.Events.Count, Is.EqualTo(1));
        }

        [Test]
        public void Input_HandsTouch_StateMachine_TwoAdjacentRowsNeverBothFire()
        {
            GazeTargetId upper = _rig.Add(HandsRig.Row(AtlasAction.NavDown, HandsRig.Panel, tag: 1));
            _rig.Add(HandsRig.Row(AtlasAction.NavDown, HandsRig.Panel - new float3(0f, HandsRig.RowPitch, 0f), tag: 2));
            HandsRig.Face face = _rig.FaceOf(upper);
            float pitch = HandsRig.RowPitch;

            // Presses from the middle of one row to the middle of the next: each fires one row, the one whose centre is nearer.
            for (int i = 0; i <= 8; i++)
            {
                float v = -pitch * i / 8f;
                int before = _rig.Events.Count;
                _rig.Glide(face.At(0f, v, 0.10f), face.At(0f, v, -0.02f), 0.3f);
                _rig.Glide(face.At(0f, v, -0.02f), face.At(0f, v, 0.10f), 0.3f);
                Assert.That(_rig.Events.Count - before, Is.EqualTo(1), "press " + i + " of 9 between the rows");
                int tag = _rig.Events[_rig.Events.Count - 1].tag;
                if (i < 4) Assert.That(tag, Is.EqualTo(1), "press " + i);
                if (i > 4) Assert.That(tag, Is.EqualTo(2), "press " + i);
            }

            // Pushed in on the upper row, slid down across the lower one and held there: only the upper row fired.
            _rig.Events.Clear();
            _rig.Glide(face.At(0f, 0f, 0.10f), face.At(0f, 0f, -0.02f), 0.3f);
            _rig.Glide(face.At(0f, 0f, -0.02f), face.At(0f, -2f * pitch, -0.02f), 0.4f);
            _rig.Glide(face.At(0f, -2f * pitch, -0.02f), face.At(0f, -pitch, -0.02f), 0.2f);
            _rig.Rest(face.At(0f, -pitch, -0.02f), 0.5f);
            // Out, short of the arming distance, and in again on the lower row: still nothing new.
            _rig.Glide(face.At(0f, -pitch, -0.02f), face.At(0f, -pitch, 0.015f), 0.2f);
            _rig.Glide(face.At(0f, -pitch, 0.015f), face.At(0f, -pitch, -0.02f), 0.2f);
            Assert.That(_rig.Events, Is.EqualTo(new[] { (AtlasAction.NavDown, InputSource.Gaze, 1) }));
            // Out to where it is seen in front, and in: now the lower row.
            _rig.Glide(face.At(0f, -pitch, -0.02f), face.At(0f, -pitch, 0.04f), 0.2f);
            _rig.Glide(face.At(0f, -pitch, 0.04f), face.At(0f, -pitch, -0.02f), 0.2f);
            Assert.That(_rig.Events, Is.EqualTo(new[] { (AtlasAction.NavDown, InputSource.Gaze, 1), (AtlasAction.NavDown, InputSource.Gaze, 2) }));
        }

        [Test]
        public void Input_HandsTouch_StateMachine_AFingerArmedOnOneRowPressesItsNeighbour()
        {
            GazeTargetId upper = _rig.Add(HandsRig.Row(AtlasAction.NavDown, HandsRig.Panel, tag: 1));
            _rig.Add(HandsRig.Row(AtlasAction.NavDown, HandsRig.Panel - new float3(0f, HandsRig.RowPitch, 0f), tag: 2));
            HandsRig.Face face = _rig.FaceOf(upper);
            // Up to the upper row, a long look at the list with the fingertip 1.5 cm off it, then down a row and in.
            _rig.Glide(face.At(0f, 0f, 0.10f), face.At(0f, 0f, 0.015f), 0.3f);
            _rig.Rest(face.At(0f, 0f, 0.015f), 2f);
            _rig.Glide(face.At(0f, 0f, 0.015f), face.At(0f, -HandsRig.RowPitch, 0.015f), 0.3f);
            _rig.Rest(face.At(0f, -HandsRig.RowPitch, 0.015f), 1f);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Armed), "the rows are one surface: what was armed on one is armed on the next");
            _rig.Glide(face.At(0f, -HandsRig.RowPitch, 0.015f), face.At(0f, -HandsRig.RowPitch, -0.02f), 0.15f);
            Assert.That(_rig.Events, Is.EqualTo(new[] { (AtlasAction.NavDown, InputSource.Gaze, 2) }));

            // The press goes to the row the fingertip comes through, not the row it was over on the way: resting
            // over the lower row for a second, then pushed in at a slant, quickly, so that the next sample is through
            // the panel and the line to it meets the press plane on the upper row.
            _rig.Events.Clear();
            _rig.Glide(face.At(0f, -HandsRig.RowPitch, -0.02f), face.At(0f, -HandsRig.RowPitch, 0.04f), 0.2f);
            _rig.Rest(face.At(0f, -0.022f, 0.015f), 1f);
            Assert.That(_rig.R.Target, Is.Not.EqualTo(upper), "over the lower row, 6 mm from its edge");
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Armed));
            _rig.Right(face.At(0f, 0.004f, -0.025f));
            Assert.That(_rig.Events, Is.EqualTo(new[] { (AtlasAction.NavDown, InputSource.Gaze, 1) }));
            Assert.That(_rig.R.Target, Is.EqualTo(upper));
            Assert.That(_rig.R.Pressed, Is.True);
        }

        [Test]
        public void Input_HandsTouch_StateMachine_ArmingIsNotCarriedToAnotherSurface()
        {
            GazeTargetId row = _rig.Add(HandsRig.Row(AtlasAction.NavDown, HandsRig.Panel, tag: 1));
            // A small sign that stands on edge 5 cm in front of the row, at right angles to it: its plane is x = 0.20,
            // and the walker's side of it is the side towards the row's middle.
            GazeTargetData sign = HandsRig.Row(AtlasAction.Secondary, HandsRig.Panel + new float3(0.20f, 0f, -0.05f));
            sign.Rotation = quaternion.RotateY(math.radians(90f));
            sign.HalfSize = new float2(0.03f, 0.03f);
            GazeTargetId signId = _rig.Add(sign);
            HandsRig.Face face = _rig.FaceOf(row);
            Assert.That(_rig.FaceOf(signId).Normal.x, Is.LessThan(-0.99f));

            // Armed on the row, and resting over it on the far side of the sign's plane.
            _rig.Glide(face.At(0.23f, 0f, 0.06f), face.At(0.23f, 0f, 0.015f), 0.2f);
            _rig.Rest(face.At(0.23f, 0f, 0.015f), 1f);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Armed));
            Assert.That(_rig.R.Target, Is.EqualTo(row));
            // Round to 1 cm in front of the sign: over the sign now, but not armed on it. Being armed on the row says
            // nothing about a face at right angles to the row, and the fingertip was last seen behind this one.
            _rig.Right(face.At(0.19f, 0f, 0.05f));
            Assert.That(_rig.R.Target, Is.EqualTo(signId));
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Hover));
            _rig.Right(face.At(0.22f, 0f, 0.05f));
            _rig.Rest(face.At(0.22f, 0f, 0.05f), 0.2f);
            Assert.That(_rig.Events, Is.Empty);
        }

        [Test]
        public void Input_HandsTouch_StateMachine_ATargetThatAppearsUnderAPressedFingerIsNotFired()
        {
            GazeTargetId row = _rig.Add(HandsRig.Row(AtlasAction.NavDown, HandsRig.Panel, tag: 1));
            HandsRig.Face face = _rig.FaceOf(row);
            _rig.Glide(face.At(0.17f, 0f, 0.10f), face.At(0.17f, 0f, -0.02f), 0.3f);

            // The menu now puts the selected row's plus where the finger is, 7.5 mm in front of the row.
            GazeTargetData plus = HandsRig.Row(AtlasAction.NavRight, face.At(0.17f, 0f, 0.0075f), mode: GazeMode.Repeat);
            plus.HalfSize = new float2(0.021f, 0.0135f);
            _rig.Add(plus);
            _rig.Rest(face.At(0.17f, 0f, -0.02f), 1.5f);
            // Out by 2 cm, which is 1.25 cm in front of the plus, and in again.
            _rig.Glide(face.At(0.17f, 0f, -0.02f), face.At(0.17f, 0f, 0.02f), 0.2f);
            _rig.Glide(face.At(0.17f, 0f, 0.02f), face.At(0.17f, 0f, -0.02f), 0.2f);
            Assert.That(_rig.Fired(), Is.EqualTo(new[] { AtlasAction.NavDown }));
            // Seen in front of the plus, and in: the plus.
            _rig.Glide(face.At(0.17f, 0f, -0.02f), face.At(0.17f, 0f, 0.05f), 0.2f);
            _rig.Glide(face.At(0.17f, 0f, 0.05f), face.At(0.17f, 0f, -0.02f), 0.2f);
            Assert.That(_rig.Fired(), Is.EqualTo(new[] { AtlasAction.NavDown, AtlasAction.NavRight }));

            // A press on the row 9 mm beside the plus: the fingertip passes the plus's press plane first, inside the
            // plus's margin, but it is on the row, and the row is what it presses.
            _rig.Events.Clear();
            _rig.Glide(face.At(0.17f, 0f, -0.02f), face.At(0.14f, 0f, 0.10f), 0.2f);
            _rig.Glide(face.At(0.14f, 0f, 0.10f), face.At(0.14f, 0f, -0.02f), 0.3f);
            Assert.That(_rig.Events, Is.EqualTo(new[] { (AtlasAction.NavDown, InputSource.Gaze, 1) }));
            Assert.That(_rig.R.Target, Is.EqualTo(row));
        }

        [Test]
        public void Input_HandsTouch_StateMachine_ATargetPressedByOneFingerIsNotPressedByTheOther()
        {
            GazeTargetId row = _rig.Add(HandsRig.Row(AtlasAction.Primary, HandsRig.Panel));
            HandsRig.Face face = _rig.FaceOf(row);
            int frames = HandsRig.Frames(0.3f);
            // The left fingertip presses and stays in; the right one follows it through the same row.
            for (int i = 0; i <= 3 * frames; i++)
            {
                float left = math.lerp(0.10f, -0.02f, math.saturate(i / (float)frames));
                float right = math.lerp(0.10f, -0.02f, math.saturate((i - frames) / (float)frames));
                _rig.Tick(new HandsInput
                {
                    Active = true,
                    Left = HandsRig.Tip(face.At(-0.10f, 0f, left)),
                    Right = HandsRig.Tip(face.At(0.10f, 0f, right))
                });
            }
            Assert.That(_rig.Fired(), Is.EqualTo(new[] { AtlasAction.Primary }));
            Assert.That(_rig.LeftFired, Is.EqualTo(1));
            Assert.That(_rig.RightFired, Is.EqualTo(0));
            Assert.That(_rig.L.Pressed, Is.True);
            Assert.That(_rig.L.Target, Is.EqualTo(row));
            Assert.That(_rig.R.Target.IsValid, Is.False, "one fingertip to a target");
        }

        [Test]
        public void Input_HandsTouch_StateMachine_AFingertipOnTheLineBetweenTwoRowsKeepsItsRow()
        {
            GazeTargetId upper = _rig.Add(HandsRig.Row(AtlasAction.NavDown, HandsRig.Panel, tag: 1));
            GazeTargetId lower = _rig.Add(HandsRig.Row(AtlasAction.NavDown, HandsRig.Panel - new float3(0f, HandsRig.RowPitch, 0f), tag: 2));
            HandsRig.Face face = _rig.FaceOf(upper);
            float line = -HandsRig.RowPitch / 2f;

            // Down from the upper row to the gap between the two, then trembling 1.5 mm either side of the line.
            _rig.Glide(face.At(0f, 0f, 0.04f), face.At(0f, line + 0.0015f, 0.04f), 0.2f);
            for (int i = 0; i < 120; i++)
            {
                _rig.Right(face.At(0f, line + (i % 2 == 0 ? -0.0015f : 0.0015f), 0.04f));
                Assert.That(_rig.R.Target, Is.EqualTo(upper), "the ring does not jump between the rows");
            }
            // Well onto the lower row it changes over, and then keeps that one as stubbornly.
            _rig.Glide(face.At(0f, line, 0.04f), face.At(0f, -HandsRig.RowPitch, 0.04f), 0.2f);
            Assert.That(_rig.R.Target, Is.EqualTo(lower));
            _rig.Glide(face.At(0f, -HandsRig.RowPitch, 0.04f), face.At(0f, line - 0.0015f, 0.04f), 0.2f);
            for (int i = 0; i < 120; i++)
            {
                _rig.Right(face.At(0f, line + (i % 2 == 0 ? 0.0015f : -0.0015f), 0.04f));
                Assert.That(_rig.R.Target, Is.EqualTo(lower));
            }
            Assert.That(_rig.Events, Is.Empty);
        }

        [Test]
        public void Input_HandsTouch_StateMachine_ADiscKeepsFacingWhereItFacedWhileAFingertipIsWithIt()
        {
            GazeTargetId chip = _rig.Add(HandsRig.Chip(AtlasAction.Primary, HandsRig.Panel));
            HandsRig.Face face = _rig.FaceOf(chip);
            // The fingertip comes to rest half a centimetre in front of the disc, 4 cm off its centre, and stays there.
            float3 resting = face.At(0.04f, 0f, 0.005f);
            _rig.Glide(face.At(0.04f, 0f, 0.10f), resting, 0.3f);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Armed));

            // The walker leans 30 cm to the side. A disc that turned with the head would swing its far edge through
            // the still fingertip: a press made by the head. It does not turn while the fingertip is with it.
            for (int i = 0; i <= 30; i++)
            {
                _rig.Head = HandsRig.Eye + new float3(-0.30f * i / 30f, 0f, 0f);
                _rig.Right(resting);
                Assert.That(math.distance(_rig.R.FaceNormal, face.Normal), Is.LessThan(1e-4f));
            }
            Assert.That(_rig.Events, Is.Empty);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Armed));
            float leaned = math.dot(resting - HandsRig.Panel, math.normalize(_rig.Head - HandsRig.Panel));
            Assert.That(leaned, Is.LessThan(-0.010f), "against a disc turned to the head the fingertip would be through the press plane");

            // Once the fingertip has left, the disc faces the head where it is now.
            _rig.NoHands(0.1f);
            HandsRig.Face turned = _rig.FaceOf(chip);
            _rig.Glide(turned.At(0f, 0f, 0.10f), turned.At(0f, 0f, -0.02f), 0.3f);
            Assert.That(_rig.Events.Count, Is.EqualTo(1));
            Assert.That(math.distance(_rig.R.FaceNormal, turned.Normal), Is.LessThan(1e-4f));
        }

        [Test]
        public void Input_HandsTouch_StateMachine_TheReportFollowsTheFingertip()
        {
            GazeTargetId row = _rig.Add(HandsRig.Row(AtlasAction.Primary, HandsRig.Panel));
            GazeTargetId chip = _rig.Add(HandsRig.Chip(AtlasAction.Secondary, HandsRig.Panel + new float3(0f, 0.20f, 0f)));
            HandsRig.Face face = _rig.FaceOf(row);

            // Half way in from 8 cm, and 1 cm beyond the row's end: the ring lies on the row's edge.
            _rig.Right(face.At(HandsRig.RowHalf.x + 0.01f, 0f, 0.04f));
            Assert.That(_rig.R.Target, Is.EqualTo(row));
            Assert.That(_rig.R.Hover, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(_rig.R.Pressed, Is.False);
            Assert.That(_rig.R.Fired, Is.False);
            Assert.That(math.distance(_rig.R.FacePoint, face.At(HandsRig.RowHalf.x, 0f, 0f)), Is.LessThan(1e-4f));
            Assert.That(math.distance(_rig.R.FaceNormal, face.Normal), Is.LessThan(1e-4f));
            Assert.That(_rig.Input.HandsTouch.AnyNear, Is.True);
            // 2 cm beyond the end it is over nothing, but still near.
            _rig.Right(face.At(HandsRig.RowHalf.x + 0.02f, 0f, 0.04f));
            Assert.That(_rig.R.Target.IsValid, Is.False);
            Assert.That(_rig.R.Hover, Is.EqualTo(0f));
            Assert.That(_rig.Input.HandsTouch.AnyNear, Is.True);
            // 21 cm in front of the row's middle it is near nothing; 19 cm in front it is near.
            _rig.Right(face.At(0f, -0.10f, 0.21f));
            Assert.That(_rig.Input.HandsTouch.AnyNear, Is.False);
            _rig.Right(face.At(0f, -0.10f, 0.19f));
            Assert.That(_rig.Input.HandsTouch.AnyNear, Is.False, "10 cm below the row as well: 21 cm from it");
            _rig.Right(face.At(0f, 0f, 0.19f));
            Assert.That(_rig.Input.HandsTouch.AnyNear, Is.True);

            // The left hand is reported on its own side, on a disc: the ring stays inside the circle.
            HandsRig.Face disc = _rig.FaceOf(chip);
            _rig.Tick(HandsRig.LeftHand(disc.At(HandsRig.ChipRadius + 0.01f, 0f, 0.02f)));
            Assert.That(_rig.L.Target, Is.EqualTo(chip));
            Assert.That(_rig.L.Hover, Is.EqualTo(0.75f).Within(1e-4f));
            Assert.That(math.distance(_rig.L.FacePoint, disc.At(HandsRig.ChipRadius, 0f, 0f)), Is.LessThan(1e-4f));
            Assert.That(math.distance(_rig.L.FaceNormal, disc.Normal), Is.LessThan(1e-4f));
            Assert.That(_rig.R.Target.IsValid, Is.False);
            Assert.That(_rig.LeftPhase, Is.EqualTo(HandsTouchPhase.Hover), "2 cm in front is short of the arming distance");
            // A ball is near from 20 cm off its surface.
            _rig.Tick(HandsRig.LeftHand(disc.At(0f, 0.10f, 0.24f)));
            Assert.That(_rig.Input.HandsTouch.AnyNear, Is.False);
            _rig.Tick(HandsRig.LeftHand(disc.At(0f, 0.10f, 0.22f)));
            Assert.That(_rig.Input.HandsTouch.AnyNear, Is.True);
            Assert.That(_rig.Events, Is.Empty);
        }

        // ---- Once, Repeat, Hold ----

        [Test]
        public void Input_HandsTouch_StateMachine_OnceFiresOncePerPress()
        {
            GazeTargetId row = _rig.Add(HandsRig.Row(AtlasAction.Primary, HandsRig.Panel));
            HandsRig.Face face = _rig.FaceOf(row);
            _rig.Glide(face.At(0f, 0f, 0.10f), face.At(0f, 0f, -0.02f), 0.3f);
            _rig.Rest(face.At(0f, 0f, -0.02f), 3f);
            Assert.That(_rig.Events.Count, Is.EqualTo(1));

            // Trembling about the press plane fires nothing more.
            for (int i = 0; i < 60; i++) _rig.Right(face.At(0f, 0f, -0.010f + (i % 2 == 0 ? 0.004f : -0.004f)));
            Assert.That(_rig.Events.Count, Is.EqualTo(1));
            // Out by 2 cm lets go of the press, but a new one needs the fingertip seen 2.5 cm in front.
            _rig.Glide(face.At(0f, 0f, -0.02f), face.At(0f, 0f, 0.02f), 0.2f);
            Assert.That(_rig.R.Pressed, Is.False);
            _rig.Glide(face.At(0f, 0f, 0.02f), face.At(0f, 0f, -0.02f), 0.2f);
            Assert.That(_rig.Events.Count, Is.EqualTo(1));
            _rig.Glide(face.At(0f, 0f, -0.02f), face.At(0f, 0f, 0.03f), 0.2f);
            _rig.Glide(face.At(0f, 0f, 0.03f), face.At(0f, 0f, -0.02f), 0.2f);
            Assert.That(_rig.Events.Count, Is.EqualTo(2));
            Assert.That(_rig.RightFired, Is.EqualTo(2));
        }

        [Test]
        public void Input_HandsTouch_StateMachine_RepeatFiresOnThePressAndRepeatsWhileHeld()
        {
            GazeTargetData usual = HandsRig.Row(AtlasAction.NavRight, HandsRig.Panel, mode: GazeMode.Repeat);
            GazeTargetData quick = HandsRig.Row(AtlasAction.NavLeft, HandsRig.Panel - new float3(0f, 0.10f, 0f), mode: GazeMode.Repeat);
            quick.RepeatSeconds = 0.25f;
            GazeTargetData slow = HandsRig.Row(AtlasAction.DialRight, HandsRig.Panel + new float3(0f, 0.10f, 0f), mode: GazeMode.Repeat);
            slow.RepeatSeconds = 1.5f;
            GazeTargetId plus = _rig.Add(usual);
            GazeTargetId minus = _rig.Add(quick);
            GazeTargetId dial = _rig.Add(slow);
            HandsRig.Face face = _rig.FaceOf(plus);

            // A tap: in, three tenths of a second, out. One step.
            _rig.Glide(face.At(0f, 0f, 0.10f), face.At(0f, 0f, -0.02f), 0.2f);
            _rig.Rest(face.At(0f, 0f, -0.02f), 0.3f);
            _rig.Glide(face.At(0f, 0f, -0.02f), face.At(0f, 0f, 0.10f), 0.2f);
            Assert.That(_rig.Fired(), Is.EqualTo(new[] { AtlasAction.NavRight }));

            // Held: the press, then after a second at the target's own interval (0.4 s by default).
            Assert.That(HeldFiringTimes(face, 2.5f), Is.EqualTo(new[] { 0f, 1.0f, 1.4f, 1.8f, 2.2f }).Within(Dt + 1e-4f));
            Assert.That(HeldFiringTimes(_rig.FaceOf(minus), 1.6f), Is.EqualTo(new[] { 0f, 1.0f, 1.25f, 1.5f }).Within(Dt + 1e-4f));
            // A target whose own interval is longer than a second never repeats sooner than that interval.
            Assert.That(HeldFiringTimes(_rig.FaceOf(dial), 3.2f), Is.EqualTo(new[] { 0f, 1.5f, 3.0f }).Within(Dt + 1e-4f));
        }

        /// <summary>Presses, holds for a time and lets go; returns when the target fired, in seconds after the press.</summary>
        private List<float> HeldFiringTimes(HandsRig.Face face, float seconds)
        {
            var times = new List<float>();
            int seen = _rig.Events.Count;
            float pressed = 0f;
            int frames = HandsRig.Frames(0.2f) + HandsRig.Frames(seconds);
            for (int i = 0; i <= frames; i++)
            {
                _rig.Right(face.At(0f, 0f, math.lerp(0.10f, -0.02f, math.saturate(i / (float)HandsRig.Frames(0.2f)))));
                for (; seen < _rig.Events.Count; seen++)
                {
                    if (times.Count == 0) pressed = _rig.Time;
                    times.Add(_rig.Time - pressed);
                }
            }
            // Let go: nothing more, however long.
            _rig.Glide(face.At(0f, 0f, -0.02f), face.At(0f, 0f, 0.10f), 0.2f);
            _rig.Rest(face.At(0f, 0f, 0.10f), 2f);
            Assert.That(_rig.Events.Count, Is.EqualTo(seen), "a released target does not repeat");
            return times;
        }

        [Test]
        public void Input_HandsTouch_StateMachine_HoldDrivesItsAxisWhilePressed()
        {
            GazeTargetData handle = HandsRig.Chip(AtlasAction.None, HandsRig.Panel, mode: GazeMode.Hold);
            handle.Axis = AtlasAxis.Sweep;
            handle.AxisValue = -1f;
            GazeTargetId id = _rig.Input.Targets.Add(handle);
            HandsRig.Face face = _rig.FaceOf(id);

            int frames = HandsRig.Frames(0.3f);
            for (int i = 0; i <= frames; i++)
            {
                float z = math.lerp(0.10f, -0.02f, i / (float)frames);
                _rig.Right(face.At(0f, 0f, z));
                Assert.That(_rig.Input.Axis(AtlasAxis.Sweep), Is.EqualTo(_rig.R.Pressed ? -1f : 0f));
                if (z > -0.009f) Assert.That(_rig.R.Pressed, Is.False, "not before the press plane, 1 cm behind the face");
            }
            Assert.That(_rig.R.Pressed, Is.True);
            for (int i = 0; i < 60; i++)
            {
                _rig.Right(face.At(0f, 0f, -0.02f));
                Assert.That(_rig.Input.Axis(AtlasAxis.Sweep), Is.EqualTo(-1f));
            }
            // Out: the axis is back at rest in the frame the fingertip is seen 1 cm in front of the face.
            for (int i = 0; i <= frames; i++)
            {
                float z = math.lerp(-0.02f, 0.10f, i / (float)frames);
                _rig.Right(face.At(0f, 0f, z));
                if (z >= 0.0101f) Assert.That(_rig.Input.Axis(AtlasAxis.Sweep), Is.EqualTo(0f));
                if (z <= 0.0099f) Assert.That(_rig.Input.Axis(AtlasAxis.Sweep), Is.EqualTo(-1f));
            }
            Assert.That(_rig.Events, Is.Empty, "an axis target fires no action");
            Assert.That(_rig.RightFired, Is.EqualTo(1), "the hold beginning is the press");

            // A Hold target with an action: pressed once when the hold begins, held until the finger lets go.
            GazeTargetId button = _rig.Add(HandsRig.Chip(AtlasAction.Secondary, HandsRig.Panel + new float3(0.20f, 0f, 0f), tag: 6, mode: GazeMode.Hold));
            HandsRig.Face buttonFace = _rig.FaceOf(button);
            _rig.Glide(buttonFace.At(0f, 0f, 0.10f), buttonFace.At(0f, 0f, -0.02f), 0.3f);
            for (int i = 0; i < 90; i++)
            {
                _rig.Right(buttonFace.At(0f, 0f, -0.02f));
                Assert.That(_rig.Input.Held(AtlasAction.Secondary), Is.True);
            }
            Assert.That(_rig.Events, Is.EqualTo(new[] { (AtlasAction.Secondary, InputSource.Gaze, 6) }));
            _rig.Glide(buttonFace.At(0f, 0f, -0.02f), buttonFace.At(0f, 0f, 0.10f), 0.3f);
            Assert.That(_rig.Input.Held(AtlasAction.Secondary), Is.False);
            Assert.That(_rig.Events.Count, Is.EqualTo(1));
        }

        // ---- leaving every state ----

        [Test]
        public void Input_HandsTouch_StateMachine_HoverAndArmedAreLeftWithoutAPress()
        {
            GazeTargetId row = _rig.Add(HandsRig.Row(AtlasAction.Primary, HandsRig.Panel));
            HandsRig.Face face = _rig.FaceOf(row);
            float3 inFront = face.At(0f, 0f, 0.05f);

            // Seen 5 cm in front: armed. Away to the side: idle.
            _rig.Right(inFront);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Armed));
            Assert.That(_rig.R.Target, Is.EqualTo(row));
            _rig.Right(face.At(0f, 0.10f, 0.05f));
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Idle));
            Assert.That(_rig.R.Target.IsValid, Is.False);
            // Back, and straight out beyond the ring's reach: idle.
            _rig.Right(inFront);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Armed));
            _rig.Right(face.At(0f, 0f, 0.10f));
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Idle));

            // From behind it only hovers, and leaves the way it came.
            _rig.NoHands(0.1f);
            _rig.Right(face.At(0f, 0f, -0.03f));
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Hover));
            Assert.That(_rig.R.Target, Is.EqualTo(row));
            Assert.That(_rig.R.Pressed, Is.False);
            _rig.Right(face.At(0f, 0f, -0.12f));
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Idle));
            // Hovering in front, it is armed once it is seen 2.5 cm out.
            _rig.Right(face.At(0f, 0f, -0.03f));
            _rig.Right(face.At(0f, 0f, 0.02f));
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Hover));
            _rig.Right(face.At(0f, 0f, 0.026f));
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Armed));

            // Armed, and then: the hand is lost.
            _rig.Tick(default(HandsInput));
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Idle));
            // Its confidence falls.
            _rig.Right(inFront);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Armed));
            _rig.Right(inFront, 0.5f);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Idle));
            // Hands are switched off (a Bump Guard warning, a gamepad).
            _rig.Right(inFront);
            _rig.Tick(new HandsInput { Active = false, Right = HandsRig.Tip(inFront) });
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Idle));
            // The target is disabled.
            _rig.Right(inFront);
            _rig.Input.Targets.SetEnabled(row, false);
            _rig.Right(inFront);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Idle));
            _rig.Input.Targets.SetEnabled(row, true);
            // The context changes.
            _rig.Right(inFront);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Armed));
            _rig.Input.PushContext(InputContext.Menu);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Idle));
            _rig.Right(inFront);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Idle), "the row is not live in the menu");
            _rig.Input.PopContext(InputContext.Menu);
            // The walker steps back out of reach.
            _rig.Right(inFront);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Armed));
            _rig.Head = HandsRig.Eye - new float3(0f, 0f, 0.4f);
            _rig.Right(inFront);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Idle));
            _rig.Head = HandsRig.Eye;
            // The target is removed.
            _rig.Right(inFront);
            _rig.Input.Targets.Remove(row);
            _rig.Right(inFront);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Idle));

            Assert.That(_rig.Events, Is.Empty);
            Assert.That(_rig.RightFired, Is.EqualTo(0));
        }

        [Test]
        public void Input_HandsTouch_StateMachine_PressedIsLeftInEveryDirection()
        {
            GazeTargetData handle = HandsRig.Row(AtlasAction.None, HandsRig.Panel, mode: GazeMode.Hold);
            handle.Axis = AtlasAxis.Twist;
            handle.AxisValue = 1f;
            GazeTargetId id = _rig.Input.Targets.Add(handle);
            HandsRig.Face face = _rig.FaceOf(id);
            float3 inFront = face.At(0f, 0f, 0.05f), through = face.At(0f, 0f, -0.02f);

            // Back out in front: hover on the same target, armed again once it is seen 2.5 cm out.
            _rig.Glide(inFront, through, 0.2f);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Pressed));
            _rig.Right(face.At(0f, 0f, 0.012f));
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Hover));
            Assert.That(_rig.Input.Axis(AtlasAxis.Twist), Is.EqualTo(0f));
            _rig.Right(face.At(0f, 0f, 0.03f));
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Armed));

            // Sideways: the press holds for 3 cm past the outline, then lets go.
            _rig.Glide(inFront, through, 0.2f);
            _rig.Right(face.At(0f, 0.040f, -0.02f));
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Pressed), "2.5 cm past the edge");
            Assert.That(_rig.Input.Axis(AtlasAxis.Twist), Is.EqualTo(1f));
            _rig.Right(face.At(0f, 0.050f, -0.02f));
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Idle), "3.5 cm past the edge");
            Assert.That(_rig.Input.Axis(AtlasAxis.Twist), Is.EqualTo(0f));
            _rig.NoHands(0.1f);

            // The hand is lost; hands are switched off; confidence falls: each lets go at once.
            _rig.Glide(inFront, through, 0.2f);
            _rig.Tick(default(HandsInput));
            Assert.That(_rig.Input.Axis(AtlasAxis.Twist), Is.EqualTo(0f));
            _rig.Glide(inFront, through, 0.2f);
            Assert.That(_rig.Input.Axis(AtlasAxis.Twist), Is.EqualTo(1f));
            _rig.Right(through, 0.4f);
            Assert.That(_rig.Input.Axis(AtlasAxis.Twist), Is.EqualTo(0f));
            _rig.Rest(through, 0.5f);
            Assert.That(_rig.Input.Axis(AtlasAxis.Twist), Is.EqualTo(0f), "a finger that is found again while it is in has pressed nothing");
            _rig.NoHands(0.1f);

            // The context changes, the target is disabled.
            _rig.Glide(inFront, through, 0.2f);
            _rig.Input.PushContext(InputContext.Menu);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Idle));
            _rig.Right(through);
            Assert.That(_rig.Input.Axis(AtlasAxis.Twist), Is.EqualTo(0f));
            _rig.Input.PopContext(InputContext.Menu);
            _rig.NoHands(0.1f);
            _rig.Glide(inFront, through, 0.2f);
            _rig.Input.Targets.SetEnabled(id, false);
            _rig.Right(through);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Idle));
            Assert.That(_rig.Input.Axis(AtlasAxis.Twist), Is.EqualTo(0f));
        }

        [Test]
        public void Input_HandsTouch_StateMachine_AContextChangeLetsGoAndFiresNothing()
        {
            GazeTargetData row = HandsRig.Row(AtlasAction.NavDown, HandsRig.Panel, tag: 3);
            row.Context = InputContext.Menu;
            GazeTargetId id = _rig.Add(row);
            HandsRig.Face face = _rig.FaceOf(id);

            // Not live while a space runs: untouched.
            _rig.Glide(face.At(0f, 0f, 0.10f), face.At(0f, 0f, -0.02f), 0.3f);
            Assert.That(_rig.R.Target.IsValid, Is.False);
            // The menu opens round the finger that is already through its row: nothing, until the finger is seen in front.
            _rig.Input.PushContext(InputContext.Menu);
            _rig.Rest(face.At(0f, 0f, -0.02f), 0.5f);
            Assert.That(_rig.RightPhase, Is.EqualTo(HandsTouchPhase.Hover));
            Assert.That(_rig.Events, Is.Empty);
            _rig.Glide(face.At(0f, 0f, -0.02f), face.At(0f, 0f, 0.04f), 0.2f);
            _rig.Glide(face.At(0f, 0f, 0.04f), face.At(0f, 0f, -0.02f), 0.2f);
            Assert.That(_rig.Events, Is.EqualTo(new[] { (AtlasAction.NavDown, InputSource.Gaze, 3) }));

            // A press whose target opens a menu: the context changes inside the tick, the finger is let go, and the
            // report of that tick still says what was pressed, for the click and the event log.
            var opening = new HandsRig();
            GazeTargetData chip = HandsRig.Chip(AtlasAction.ParamMenu, HandsRig.Panel, tag: 8);
            chip.Callback = (fired, tag) => opening.Input.PushContext(InputContext.Menu);
            GazeTargetId chipId = opening.Input.Targets.Add(chip);
            HandsRig.Face chipFace = opening.FaceOf(chipId);
            opening.Glide(chipFace.At(0f, 0f, 0.10f), chipFace.At(0f, 0f, -0.011f), 0.3f);
            Assert.That(opening.Input.Context, Is.EqualTo(InputContext.Menu));
            Assert.That(opening.RightPhase, Is.EqualTo(HandsTouchPhase.Idle));
            Assert.That(opening.R.Fired, Is.True);
            Assert.That(opening.R.Pressed, Is.True);
            Assert.That(opening.R.Target, Is.EqualTo(chipId));
            Assert.That(opening.Input.TagOf(opening.R.Target), Is.EqualTo(8));
            Assert.That(opening.Events, Is.EqualTo(new[] { (AtlasAction.ParamMenu, InputSource.Gaze, 8) }));
            opening.Rest(chipFace.At(0f, 0f, -0.011f), 0.2f);
            Assert.That(opening.R.Target.IsValid, Is.False, "the chip belongs to the space, and the menu is open");

            // In the hand nothing is touched at all.
            var held = new HandsRig(InputContext.InHand);
            GazeTargetId other = held.Add(HandsRig.Row(AtlasAction.Primary, HandsRig.Panel));
            HandsRig.Face otherFace = held.FaceOf(other);
            held.Glide(otherFace.At(0f, 0f, 0.10f), otherFace.At(0f, 0f, -0.02f), 0.3f);
            Assert.That(held.Events, Is.Empty);
            Assert.That(held.R.Target.IsValid, Is.False);
        }

        // ---- with the gaze ----

        [Test]
        public void Input_HandsTouch_StateMachine_ATouchJustAfterTheDwellFiredIsSwallowed()
        {
            GazeTargetId chip = _rig.Add(HandsRig.Chip(AtlasAction.Primary, HandsRig.Panel, tag: 4));
            HandsRig.Face face = _rig.FaceOf(chip);
            _rig.LookAt(HandsRig.Panel);
            _rig.NoHands(0.75f);
            Assert.That(_rig.Events.Count, Is.EqualTo(1), "the dwell got there first");

            // The finger that was already on its way arrives a fifth of a second later: it presses, and fires nothing.
            _rig.Glide(face.At(0f, 0f, 0.10f), face.At(0f, 0f, -0.02f), 0.2f);
            Assert.That(_rig.R.Pressed, Is.True);
            Assert.That(_rig.Events.Count, Is.EqualTo(1));
            Assert.That(_rig.RightFired, Is.EqualTo(0));
            _rig.Rest(face.At(0f, 0f, -0.02f), 1f);
            _rig.Glide(face.At(0f, 0f, -0.02f), face.At(0f, 0f, 0.10f), 0.2f);
            Assert.That(_rig.Events.Count, Is.EqualTo(1));

            // A press that begins after that is a press.
            _rig.Glide(face.At(0f, 0f, 0.10f), face.At(0f, 0f, -0.02f), 0.2f);
            Assert.That(_rig.Events, Is.EqualTo(new[] { (AtlasAction.Primary, InputSource.Gaze, 4), (AtlasAction.Primary, InputSource.Gaze, 4) }));
            Assert.That(_rig.RightFired, Is.EqualTo(1));
        }
    }
}

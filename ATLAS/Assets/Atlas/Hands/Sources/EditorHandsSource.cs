using System;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Atlas.Hands
{
    /// <summary>What an Editor hand does with its fingers: keys 1 to 4.</summary>
    public enum EditorHandShape : byte
    {
        /// <summary>All five fingers straight.</summary>
        Open = 0,
        /// <summary>The index out, the other fingers curled, the thumb tucked.</summary>
        Pointing = 1,
        /// <summary>The thumb tip and the index tip touching: the design's pinch ratio is 0.</summary>
        Pinch = 2,
        /// <summary>All five fingers folded in.</summary>
        Fist = 3,
    }

    /// <summary>
    /// The controls of one frame of <see cref="EditorHandsSource"/>, as the keyboard and the mouse give them. The
    /// source reads the devices itself; a test, or a script that wants to drive the hands, gives a frame's
    /// controls to <see cref="EditorHandsSource.SetControls"/> instead.
    /// </summary>
    public struct EditorHandsControls
    {
        /// <summary>Left Alt is down: the mouse's movement and the wheel count.</summary>
        public bool Hold;
        /// <summary>The mouse's movement since the last frame, in pixels of the screen, y up (the Input System's own).</summary>
        public float2 MouseDelta;
        /// <summary>The wheel's turn since the last frame, in whatever unit the platform reports; only its sign counts. Positive is away from the user.</summary>
        public float Wheel;
        /// <summary>Q went down this frame.</summary>
        public bool ChooseLeft;
        /// <summary>E went down this frame.</summary>
        public bool ChooseRight;
        /// <summary>R went down this frame.</summary>
        public bool ToggleActive;
        /// <summary>0 for none; 1 to 4 when the key went down this frame: open, pointing, pinch, fist.</summary>
        public int ShapeKey;
    }

    /// <summary>
    /// The hand shapes of the Editor's stand-in and of the sample hand logs: the template hand
    /// (<see cref="HandModel"/>) with its fingers curled, the thumb closed on the index when pinching, placed
    /// like a walker's own hand seen from behind. Engine-free.
    /// </summary>
    internal static class EditorHandShapes
    {
        const int Bisection = 24;

        /// <summary>
        /// The curls of a shape: one for each of the index, middle, ring and little fingers, one for the thumb,
        /// and how far the thumb has closed on the index fingertip, 0 to 1 (see <see cref="Build"/>).
        /// </summary>
        public static void Target(EditorHandShape shape, out float4 curl, out float thumb, out float pinch)
        {
            switch (shape)
            {
                case EditorHandShape.Pointing:
                    curl = new float4(0f, 0.95f, 0.95f, 0.95f);
                    thumb = 0.55f;
                    pinch = 0f;
                    break;
                case EditorHandShape.Pinch:
                    // The index bends until its tip is within the thumb's reach; the others relax.
                    curl = new float4(0.62f, 0.30f, 0.40f, 0.50f);
                    thumb = 0f;
                    pinch = 1f;
                    break;
                case EditorHandShape.Fist:
                    curl = new float4(1f);
                    thumb = 0.75f;
                    pinch = 0f;
                    break;
                default:
                    curl = float4.zero;
                    thumb = 0f;
                    pinch = 0f;
                    break;
            }
        }

        /// <summary>
        /// The 21 joints of a hand in its palm frame (HandModel's: x towards the thumb, y out of the palm, z towards
        /// the middle knuckle) with the given curls. A curl alone cannot bring the thumb to the index, so pinch
        /// closes it: the thumb's chain is curled until its tip is as far from its base as the target is, then
        /// turned about its base to point at the target, a point from where the thumb's tip would be to the index
        /// tip, pinch of the way. The thumb keeps its bone lengths; at 1 its tip is on the index tip.
        /// </summary>
        public static JointSet Build(HandSide side, in float4 curl, float thumbCurl, float pinch)
        {
            JointSet set = HandModel.Pose(side, in curl, thumbCurl);
            pinch = math.saturate(pinch);
            if (pinch <= 0f) return set;

            float3 thumbBase = set[HandJoint.ThumbCmc];
            float3 target = math.lerp(set[HandJoint.ThumbTip], set[HandJoint.IndexTip], pinch);
            float wanted = math.distance(target, thumbBase);

            // The tip comes nearer its base as the thumb curls: find the curl that puts it as far as the target.
            float low = 0f, high = 1f;
            for (int i = 0; i < Bisection; i++)
            {
                float middle = 0.5f * (low + high);
                float reach = math.distance(HandModel.Pose(side, in curl, middle)[HandJoint.ThumbTip], thumbBase);
                if (reach > wanted) low = middle;
                else high = middle;
            }
            JointSet curled = HandModel.Pose(side, in curl, 0.5f * (low + high));
            quaternion turn = Between(curled[HandJoint.ThumbTip] - thumbBase, target - thumbBase);
            for (int joint = (int)HandJoint.ThumbMcp; joint <= (int)HandJoint.ThumbTip; joint++)
                set[joint] = thumbBase + math.mul(turn, curled[joint] - thumbBase);
            return set;
        }

        /// <summary>The middle of the palm: the mean of the wrist and the four knuckles. Does not move when the fingers do.</summary>
        public static float3 PalmCentre(in JointSet joints)
        {
            return (joints[HandJoint.Wrist] + joints[HandJoint.IndexMcp] + joints[HandJoint.MiddleMcp] +
                    joints[HandJoint.RingMcp] + joints[HandJoint.LittleMcp]) * 0.2f;
        }

        /// <summary>
        /// A hand raised in front of the camera with its palm away from it, seen from behind as the walker sees
        /// their own: the fingers point up and away at the elevation (0 is straight away, pi / 2 straight up), the
        /// palm's normal faces away and down, and a right hand's thumb is on the left of the picture. The columns
        /// are the palm frame's x, y and z in camera space; a left hand is the same turn of the mirrored template.
        /// </summary>
        public static float3x3 Orientation(float elevationRadians)
        {
            math.sincos(elevationRadians, out float s, out float c);
            float3 fingers = new float3(0f, s, c);
            float3 normal = new float3(0f, -c, s);
            return new float3x3(math.cross(normal, fingers), normal, fingers);
        }

        static quaternion Between(float3 from, float3 to)
        {
            float3 a = math.normalizesafe(from);
            float3 b = math.normalizesafe(to);
            float3 axis = math.cross(a, b);
            float length = math.length(axis);
            if (length < 1e-6f) return quaternion.identity;
            return quaternion.AxisAngle(axis / length, math.atan2(length, math.clamp(math.dot(a, b), -1f, 1f)));
        }
    }

    /// <summary>
    /// The Editor's stand-in for the phone's detector (documents/ATLAS_Hands_Design.md section 4.5, rule "Editor
    /// first" F9): two hands in front of the camera, driven by mouse and keys. Each is the template hand in a shape
    /// (<see cref="EditorHandShape"/>), placed in the tracked camera's space and written through
    /// <see cref="HandsSynth"/> with made-up 1280 x 720 intrinsics and a 256 x 144 depth size, so the real lift,
    /// filter and everything after them run on it. It reports each result as the phone does: polled 80 ms after
    /// its frame was captured, so the hands it shows are where they were 80 ms before, and the filter's prediction
    /// has the same work to do. It follows the rate it is given, on the time passed to TryRead.
    ///
    /// <para>Available is true only in the Editor and only while Enabled, which follows the developer parameter
    /// hands.editor (default off): an Editor run is a hands session only when asked to be.</para>
    ///
    /// <para>Bindings, read through the Input System (the legacy Input class is off in this project). None of them
    /// is read by AtlasActionMap, the keyboard walker or the dev overlay, and no mouse button is used: the left
    /// button is the lever and the right one turns the head. The walker reads the mouse's movement only while the
    /// right button is down, so with Left Alt held and no button down the movement is the hand's alone.</para>
    /// <code>
    ///   hold Left Alt, move the mouse   the active hand moves across the view (one picture pixel to a mouse pixel)
    ///   hold Left Alt, turn the wheel   the active hand goes away (wheel up) or nearer (wheel down),
    ///                                   2 cm a step, 0.25 to 0.70 m from the camera
    ///   Q  /  E                         the left / the right hand becomes the active one (the right, at first)
    ///   R                               shows or hides the active hand
    ///   1  2  3  4                      the active hand takes the shape open, pointing, pinch, fist
    /// </code>
    /// <para>A hand that leaves the picture is lost, as it would be on the phone: its joints out of the picture are
    /// not seen, and a hand with none in it is not reported. Main thread only; allocates nothing.</para>
    /// </summary>
    public sealed class EditorHandsSource : IHandsSource
    {
        /// <summary>The made-up camera: 1280 x 720 pixels, a focal length of 1000 pixels, the centre in the middle.</summary>
        public const int ImageWidth = 1280, ImageHeight = 720;
        /// <summary>The focal length of the made-up camera, in pixels: a horizontal view of 65 degrees.</summary>
        public const float FocalPixels = 1000f;
        /// <summary>The made-up depth image, 256 x 144.</summary>
        public const int DepthWidth = 256, DepthHeight = 144;
        /// <summary>Seconds from the capture of a frame to the poll of its result: about what the phone's are.</summary>
        public const double AgeSeconds = 0.080;
        /// <summary>Picture pixels the hand moves for a pixel of mouse travel.</summary>
        public const float PicturePixelsPerMousePixel = 1f;
        /// <summary>Metres the hand moves for one step of the wheel.</summary>
        public const float WheelStepMetres = 0.02f;
        /// <summary>The palm's distance from the camera, metres, along its axis.</summary>
        public const float MinDistanceMetres = 0.25f, MaxDistanceMetres = 0.70f, DefaultDistanceMetres = 0.45f;
        /// <summary>How far outside the picture the palm may be taken, pixels: far enough for the whole hand to be out of view.</summary>
        public const float OutsideMarginPixels = 400f;
        /// <summary>A change of shape takes this long, seconds.</summary>
        public const float ShapeEaseSeconds = 0.15f;
        /// <summary>The fingers' elevation, degrees: up and away, as a hand raised to the view.</summary>
        public const float ElevationDegrees = 50f;

        const float CentreX = ImageWidth * 0.5f, CentreY = ImageHeight * 0.5f;
        const float HandConfidence = 0.95f;
        const int HistorySize = 32;
        // Closer in time than this, a frame replaces the last one in the history instead of adding to it: the history always reaches back past the age.
        const double SnapshotGapSeconds = 0.004;
        // A frame after a stall counts as this long when the shapes are eased.
        const double MaxStepSeconds = 0.1;
        // A result is due this much early, so that a clock that is a sum of frame times does not miss it.
        const double ClockSlack = 1e-6;

        static readonly float3x3 s_orientation = EditorHandShapes.Orientation(math.radians(ElevationDegrees));

        struct HandState
        {
            public bool Shown;
            /// <summary>Where the palm's centre is in the picture, pixels.</summary>
            public float2 Pixel;
            /// <summary>Metres from the camera to the palm's centre, along the camera's axis.</summary>
            public float Distance;
            /// <summary>The curls now: index, middle, ring, little; the thumb; how far the thumb has closed on the index.</summary>
            public float4 Curl;
            public float Thumb;
            public float Pinch;

            public static HandState Lerp(in HandState a, in HandState b, float u)
            {
                HandState s = a;
                s.Pixel = math.lerp(a.Pixel, b.Pixel, u);
                s.Distance = math.lerp(a.Distance, b.Distance, u);
                s.Curl = math.lerp(a.Curl, b.Curl, u);
                s.Thumb = math.lerp(a.Thumb, b.Thumb, u);
                s.Pinch = math.lerp(a.Pinch, b.Pinch, u);
                return s;
            }
        }

        struct Snapshot
        {
            public double Time;
            public float4x4 Camera;
            public HandState Left, Right;
        }

        readonly Func<float4x4> _cameraToSession;
        readonly HandState[] _hand = new HandState[2];
        readonly EditorHandShape[] _shape = new EditorHandShape[2];
        readonly Snapshot[] _history = new Snapshot[HistorySize];
        int _newest = -1;
        int _snapshots;
        int _active = (int)HandSide.Right;
        int _rate = HandsConstants.RateFull;
        bool _running = true;
        bool _enabled;
        bool _haveLast;
        double _lastTime;
        double _nextTime = double.NegativeInfinity;
        int _sequence;
        EditorHandsControls _controls;
        bool _hasControls;

        /// <summary>cameraToSession is asked for the tracked camera's pose in the session frame at every result (identity when null).</summary>
        public EditorHandsSource(Func<float4x4> cameraToSession)
        {
            _cameraToSession = cameraToSession;
            _hand[(int)HandSide.Left] = Start(new float2(CentreX - 150f, CentreY + 40f));
            _hand[(int)HandSide.Right] = Start(new float2(CentreX + 150f, CentreY + 40f));
        }

        static HandState Start(float2 pixel)
        {
            return new HandState { Shown = true, Pixel = pixel, Distance = DefaultDistanceMetres };
        }

        /// <summary>Follows the developer parameter hands.editor. Off, the source has nothing and reads no device.</summary>
        public bool Enabled
        {
            get { return _enabled; }
            set
            {
                if (value == _enabled) return;
                _enabled = value;
                ClearHistory();
                _hasControls = false;
            }
        }

        /// <summary>True only in the Editor, and only while <see cref="Enabled"/>.</summary>
        public bool Available
        {
            get
            {
#if UNITY_EDITOR
                return _enabled;
#else
                return false;
#endif
            }
        }

        /// <summary>"editor".</summary>
        public string Name { get { return "editor"; } }
        /// <summary>The stand-in does not fail: always 0.</summary>
        public int Fault { get { return 0; } }

        /// <summary>The hand the mouse and the wheel move.</summary>
        public HandSide ActiveSide { get { return (HandSide)_active; } }
        /// <summary>Whether a hand is there to be seen (R, for the active one).</summary>
        public bool IsShown(HandSide side) { return _hand[(int)side].Shown; }
        /// <summary>The shape asked for; the fingers reach it over <see cref="ShapeEaseSeconds"/>.</summary>
        public EditorHandShape ShapeOf(HandSide side) { return _shape[(int)side]; }
        /// <summary>Where the hand is now, not as it was 80 ms ago: the palm's centre in the picture, pixels.</summary>
        public float2 PalmPixel(HandSide side) { return _hand[(int)side].Pixel; }
        /// <summary>Where the hand is now: metres from the camera to the palm's centre along its axis.</summary>
        public float PalmDistance(HandSide side) { return _hand[(int)side].Distance; }

        /// <summary>Makes a hand the active one (Q and E).</summary>
        public void Choose(HandSide side) { _active = (int)side; }

        /// <summary>Shows or hides a hand (R, for the active one).</summary>
        public void Show(HandSide side, bool shown) { _hand[(int)side].Shown = shown; }

        /// <summary>A hand's shape; snap puts the fingers there at once instead of over <see cref="ShapeEaseSeconds"/>.</summary>
        public void SetShape(HandSide side, EditorHandShape shape, bool snap = false)
        {
            int i = (int)side;
            _shape[i] = shape;
            if (!snap) return;
            EditorHandShapes.Target(shape, out float4 curl, out float thumb, out float pinch);
            _hand[i].Curl = curl;
            _hand[i].Thumb = thumb;
            _hand[i].Pinch = pinch;
        }

        /// <summary>
        /// Puts a hand's palm centre at a picture pixel and a distance from the camera, as the mouse and the wheel
        /// would, within <see cref="OutsideMarginPixels"/> of the picture and the distance limits. The result of
        /// the next frames shows it after the 80 ms of latency, like any other movement.
        /// </summary>
        public void Place(HandSide side, float2 palmPixel, float distance)
        {
            int i = (int)side;
            _hand[i].Pixel = ClampPixel(palmPixel);
            _hand[i].Distance = math.clamp(distance, MinDistanceMetres, MaxDistanceMetres);
        }

        /// <summary>
        /// The controls of the next <see cref="TryRead"/>, instead of the keyboard's and the mouse's: for tests and
        /// scripts. Used once; the read after it goes back to the devices.
        /// </summary>
        public void SetControls(in EditorHandsControls controls)
        {
            _controls = controls;
            _hasControls = true;
        }

        /// <summary>
        /// The rate of results: 30, 15 or 5 a second on the time passed to TryRead. 0 idles the source: no result,
        /// no device read, and the hands stay where they are. Starts it when it was stopped.
        /// </summary>
        public void SetRate(int hz)
        {
            bool wasEmitting = _running && _rate > 0;
            _rate = hz > 0 ? hz : 0;
            if (hz > 0) _running = true;
            if (!wasEmitting) ClearHistory();
        }

        /// <summary>Stops the source: nothing is given and no device is read, until a rate is asked for.</summary>
        public void Stop()
        {
            _running = false;
            ClearHistory();
            _hasControls = false;
        }

        /// <summary>
        /// Reads the controls of this frame (the devices', unless <see cref="SetControls"/> gave some), moves the
        /// hands, and when a result is due at the rate gives it: the hands and the camera as they were
        /// <see cref="AgeSeconds"/> before time, polled at time. False when nothing is due, or when the source
        /// is not <see cref="Available"/>, stopped or idle.
        /// </summary>
        public bool TryRead(double time, ref HandsDetection detection)
        {
            if (!Available || !_running || _rate <= 0)
            {
                _hasControls = false;
                return false;
            }
            // A clock that was set back starts the history over.
            if (_haveLast && time < _lastTime) ClearHistory();
            float step = _haveLast ? (float)Math.Min(time - _lastTime, MaxStepSeconds) : 0f;

            // The devices are read every frame whatever the rate: a key pressed between two results counts.
            EditorHandsControls controls;
            if (_hasControls)
            {
                controls = _controls;
                _hasControls = false;
            }
            else
            {
                controls = ReadDevices();
            }
            Apply(in controls);
            Ease(step);
            float4x4 camera = _cameraToSession != null ? _cameraToSession() : float4x4.identity;
            Push(time, in camera);
            _lastTime = time;
            _haveLast = true;

            if (time + ClockSlack < _nextTime) return false;
            double period = 1.0 / _rate;
            // Results at the rate on the clock it is given; a frame slower than the rate still gives one, never two.
            _nextTime = time - _nextTime > period ? time + period : _nextTime + period;
            Emit(time, ref detection);
            return true;
        }

        // ---- Controls ----

        static EditorHandsControls ReadDevices()
        {
            var controls = default(EditorHandsControls);
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                controls.Hold = keyboard.leftAltKey.isPressed;
                controls.ChooseLeft = keyboard.qKey.wasPressedThisFrame;
                controls.ChooseRight = keyboard.eKey.wasPressedThisFrame;
                controls.ToggleActive = keyboard.rKey.wasPressedThisFrame;
                if (keyboard.digit1Key.wasPressedThisFrame) controls.ShapeKey = 1;
                else if (keyboard.digit2Key.wasPressedThisFrame) controls.ShapeKey = 2;
                else if (keyboard.digit3Key.wasPressedThisFrame) controls.ShapeKey = 3;
                else if (keyboard.digit4Key.wasPressedThisFrame) controls.ShapeKey = 4;
            }
            Mouse mouse = Mouse.current;
            if (mouse != null && controls.Hold)
            {
                Vector2 delta = mouse.delta.ReadValue();
                controls.MouseDelta = new float2(delta.x, delta.y);
                controls.Wheel = mouse.scroll.ReadValue().y;
            }
            return controls;
        }

        void Apply(in EditorHandsControls controls)
        {
            if (controls.ChooseLeft) _active = (int)HandSide.Left;
            if (controls.ChooseRight) _active = (int)HandSide.Right;
            if (controls.ToggleActive) _hand[_active].Shown = !_hand[_active].Shown;
            if (controls.ShapeKey >= 1 && controls.ShapeKey <= 4) _shape[_active] = (EditorHandShape)(controls.ShapeKey - 1);
            if (!controls.Hold) return;

            ref HandState hand = ref _hand[_active];
            // The mouse's y runs up the screen, the picture's runs down it.
            hand.Pixel = ClampPixel(hand.Pixel + new float2(controls.MouseDelta.x, -controls.MouseDelta.y) * PicturePixelsPerMousePixel);
            // Whatever the wheel's unit (120 to a notch on Windows, fractions on a Mac), a turn is a step.
            if (controls.Wheel != 0f && !float.IsNaN(controls.Wheel))
                hand.Distance = math.clamp(hand.Distance + (controls.Wheel > 0f ? WheelStepMetres : -WheelStepMetres),
                                           MinDistanceMetres, MaxDistanceMetres);
        }

        /// <summary>The fingers move towards their shape at a steady rate and arrive in <see cref="ShapeEaseSeconds"/>.</summary>
        void Ease(float step)
        {
            float k = step / ShapeEaseSeconds;
            var limit = new float4(k);
            for (int i = 0; i < 2; i++)
            {
                EditorHandShapes.Target(_shape[i], out float4 curl, out float thumb, out float pinch);
                ref HandState hand = ref _hand[i];
                hand.Curl += math.clamp(curl - hand.Curl, -limit, limit);
                hand.Thumb += math.clamp(thumb - hand.Thumb, -k, k);
                hand.Pinch += math.clamp(pinch - hand.Pinch, -k, k);
            }
        }

        static float2 ClampPixel(float2 pixel)
        {
            return math.clamp(pixel, new float2(-OutsideMarginPixels), new float2(ImageWidth + OutsideMarginPixels, ImageHeight + OutsideMarginPixels));
        }

        // ---- The 80 ms of latency: the hands and the camera as they were, kept for a little while ----

        void ClearHistory()
        {
            _newest = -1;
            _snapshots = 0;
            _haveLast = false;
            _nextTime = double.NegativeInfinity;
        }

        void Push(double time, in float4x4 camera)
        {
            // A frame too soon after the last replaces it instead of adding to the history.
            bool replace = _snapshots > 0 && time - _history[_newest].Time < SnapshotGapSeconds;
            if (!replace)
            {
                _newest = (_newest + 1) % HistorySize;
                if (_snapshots < HistorySize) _snapshots++;
            }
            ref Snapshot s = ref _history[_newest];
            s.Time = time;
            s.Camera = camera;
            s.Left = _hand[(int)HandSide.Left];
            s.Right = _hand[(int)HandSide.Right];
        }

        /// <summary>
        /// The hands as they were at a time, between the two frames around it, and the camera of the last frame at
        /// or before it. Before the first frame kept, the first.
        /// </summary>
        void Sample(double time, out HandState left, out HandState right, out float4x4 camera)
        {
            int older = -1, newer = -1;
            for (int n = 0; n < _snapshots; n++)
            {
                int i = (_newest - n + HistorySize) % HistorySize;
                if (_history[i].Time <= time)
                {
                    older = i;
                    newer = n > 0 ? (i + 1) % HistorySize : -1;
                    break;
                }
            }
            if (older < 0)
            {
                older = (_newest - (_snapshots - 1) + HistorySize) % HistorySize;
                left = _history[older].Left;
                right = _history[older].Right;
                camera = _history[older].Camera;
                return;
            }
            camera = _history[older].Camera;
            if (newer < 0)
            {
                left = _history[older].Left;
                right = _history[older].Right;
                return;
            }
            float u = (float)((time - _history[older].Time) / (_history[newer].Time - _history[older].Time));
            u = math.saturate(u);
            left = HandState.Lerp(in _history[older].Left, in _history[newer].Left, u);
            right = HandState.Lerp(in _history[older].Right, in _history[newer].Right, u);
            // Whether a hand is there is not a thing to blend: it is as it was at the frame before.
            left.Shown = _history[older].Left.Shown;
            right.Shown = _history[older].Right.Shown;
        }

        // ---- The result ----

        void Emit(double pollTime, ref HandsDetection detection)
        {
            double captureTime = pollTime - AgeSeconds;
            Sample(captureTime, out HandState left, out HandState right, out float4x4 camera);
            HandsSynth.Begin(ref detection, ++_sequence, ImageWidth, ImageHeight, FocalPixels, FocalPixels, CentreX, CentreY,
                             DepthWidth, DepthHeight, captureTime, pollTime, in camera);
            if (left.Shown) AddHand(ref detection, HandSide.Left, in left);
            if (right.Shown) AddHand(ref detection, HandSide.Right, in right);
        }

        static void AddHand(ref HandsDetection detection, HandSide side, in HandState state)
        {
            JointSet palmFrame = EditorHandShapes.Build(side, in state.Curl, state.Thumb, state.Pinch);
            float3 centre = EditorHandShapes.PalmCentre(in palmFrame);
            // The palm's centre is where the pixel and the distance say, on the camera's ray through the pixel.
            float3 target = new float3((state.Pixel.x - CentreX) / FocalPixels * state.Distance,
                                       -(state.Pixel.y - CentreY) / FocalPixels * state.Distance, state.Distance);
            JointSet joints = default(JointSet);
            int inPicture = 0;
            for (int j = 0; j < HandJoints.Count; j++)
            {
                float3 p = target + math.mul(s_orientation, palmFrame[j] - centre);
                joints[j] = p;
                if (p.z > 0f && InPicture(detection.Project(p))) inPicture++;
            }
            // A hand with no joint in the picture is not there for a detector.
            if (inPicture == 0) return;

            HandsSynth.AddHand(ref detection, in joints, (int)side, HandConfidence, true);
            if (detection.HandCount == 1) ClipToPicture(ref detection.Hand0);
            else ClipToPicture(ref detection.Hand1);
        }

        /// <summary>What is outside the picture was not seen: no confidence, no depth, no palm sample.</summary>
        static void ClipToPicture(ref DetectedHand hand)
        {
            for (int j = 0; j < HandJoints.Count; j++)
            {
                JointSample sample = hand.GetJoint(j);
                if (InPicture(sample.Pixel)) continue;
                sample.Confidence = 0f;
                sample.Depth = 0f;
                sample.DepthSpread = 0f;
                sample.DepthConfidence = -1;
                hand.SetJoint(j, in sample);
            }

            int count = hand.PalmCount;
            int kept = 0;
            for (int i = 0; i < count; i++)
            {
                float3 sample = hand.GetPalmSample(i);
                if (!InPicture(new float2(sample.x, sample.y))) continue;
                // Slot kept is at or behind slot i, which has been read: compacting in place loses nothing.
                hand.PalmCount = kept;
                hand.AddPalmSample(sample.x, sample.y, sample.z);
                kept++;
            }
            for (int i = kept; i < count; i++)
            {
                hand.PalmCount = i;
                hand.AddPalmSample(0f, 0f, 0f);
            }
            hand.PalmCount = kept;
        }

        static bool InPicture(float2 pixel)
        {
            return pixel.x >= 0f && pixel.x < ImageWidth && pixel.y >= 0f && pixel.y < ImageHeight;
        }
    }
}

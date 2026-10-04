using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>
    /// The lift (documents/ATLAS_Hands_Design.md section 4.3): from one detected hand, 21 points in a picture
    /// with LiDAR depth under them, to 21 joints in the camera's space at capture. Per detection:
    ///
    /// 1. Reject a hand with too few confident joints, or with too little of its palm to fix a frame.
    /// 2. Find the palm in depth: a plane through its LiDAR samples (<see cref="PalmPlane"/>).
    /// 3. Where LiDAR does not reach (a palm nearer than 0.32 m, as a pressing palm is), take the palm's distance
    ///    from the hand's known size and its tilt from the last plane or from the picture.
    /// 4. Put the wrist, the four knuckles and the thumb base where their rays meet that plane.
    /// 5. Raise each finger bone by bone: a joint lies on its ray at the bone's length from its parent, which
    ///    has two answers; the eight combinations of a finger are scored together and the best chain wins.
    /// 6. Measure the hand whenever the palm is square to the camera, for the walker's scale and bone lengths.
    ///
    /// The frames of section 3.4 after this one (session, world) are the caller's. The lift remembers each hand
    /// from one detection to the next (at most two, matched by where the palm was) and the walker's hand size
    /// for the session. Main thread; allocates only when it is made.
    /// </summary>
    public sealed class HandsLift
    {
        // ---- Numbers of the lift that HandsConstants does not hold: starting values, like those ----

        /// <summary>The palm counts as square to the camera, and is measured, when its normal is within this of the line of sight, degrees.</summary>
        internal const float MeasureMaxTiltDegrees = 25f;
        /// <summary>Size measurements the running median keeps.</summary>
        internal const int ScaleWindow = 90;
        /// <summary>A sample of the hand's size outside these shares of the template is a bad detection.</summary>
        internal const float SizeSaneMin = 0.5f, SizeSaneMax = 2f;
        /// <summary>The last detection of a hand counts as evidence for the next for this long, seconds.</summary>
        internal const float ContinuitySeconds = 0.3f;
        /// <summary>A hand's memory is given up when no detection has matched it for this long, seconds.</summary>
        internal const float SlotMemorySeconds = HandsConstants.PlaneMemorySeconds;
        /// <summary>Left or right is the balance of the detector's last labels, kept within this many either way.</summary>
        internal const int SideVoteLimit = 6;
        /// <summary>Weights of the evidence for a bone's two answers: LiDAR at the joint, the last frame, anatomy, the straighter answer. Every score is a distance in metres.</summary>
        internal const float WeightDepth = 1f, WeightLast = 0.25f, WeightAnatomy = 2f, WeightStraight = 0.25f;
        /// <summary>One LiDAR sample never charges an answer more than this, so that a sample off the finger cannot outvote everything else, metres.</summary>
        internal const float DepthCapMetres = 0.03f;
        /// <summary>Every limit of the anatomy is widened by this many times what the picture's noise can turn the bone by.</summary>
        internal const float AngleNoiseSigmas = 2f;
        /// <summary>LiDAR under a finger joint counts only within the finger's length to that joint plus this of its knuckle's depth: beyond that it is the wall, metres.</summary>
        internal const float DepthSlackMetres = 0.03f;
        /// <summary>A finger's bone may lean this far out of the plane the finger curls in before it is charged, degrees: fingers spread, they do not bend sideways.</summary>
        internal const float SidewaysFreeDegrees = 25f;
        /// <summary>A finger's joints may fold this far past their angles in the template's fist before they are charged, degrees.</summary>
        internal const float FoldFreeDegrees = 20f;
        /// <summary>The end joint of a finger bends about this share of what its middle joint does, and is charged past this many degrees either side of it.</summary>
        internal const float EndJointShare = 0.7f, EndJointFreeDegrees = 30f;
        /// <summary>A finger counts as straight when its middle joints are seen within this many times the picture's noise of the line from knuckle to tip.</summary>
        internal const float StraightSlackSigmas = 3f;
        /// <summary>How unsure the detector's points are taken to be in the picture, pixels; and how many times that is given back when the palm's tilt is read from the picture.</summary>
        internal const float PictureNoisePixels = 2f, PictureNoiseSigmas = 3f;
        /// <summary>The confidence term for stretched bones is 1 up to this mean stretch and 0 at this much more (shares of a bone's length).</summary>
        internal const float StretchFree = 0.05f, StretchRange = 0.25f;
        /// <summary>The steepest tilt the picture alone is believed to show, degrees.</summary>
        internal const float MaxPictureTiltDegrees = 75f;
        /// <summary>A palm joint is never placed farther than this in depth from the palm plane's centre, metres.</summary>
        internal const float PalmReachMetres = 0.12f;
        /// <summary>The usable palm joints must spread at least this far across their narrower direction (root mean square, in the template), metres.</summary>
        internal const float MinPalmSpreadMetres = 0.006f;
        /// <summary>
        /// A finger whose LiDAR depth is this far off the palm's plane is not lying in it, and is not measured,
        /// metres. Wide enough for a centimetre of LiDAR noise and for the plane's own tilt error at a fingertip.
        /// </summary>
        internal const float FlatSlackMetres = 0.04f;
        /// <summary>The palm's spans and tilt as last seen on a LiDAR plane follow each new sighting by this share: one plane's noise is not what is carried on.</summary>
        internal const float SeenSpanBlend = 0.25f;
        /// <summary>When LiDAR takes over from the hand's size the palm glides to where LiDAR puts it over this long, seconds: nothing jumps at 0.32 m, coming or going.</summary>
        internal const float PlaneEaseSeconds = 0.2f;
        /// <summary>Nothing is placed nearer the camera than this, metres.</summary>
        internal const float MinDepthMetres = 0.05f;
        /// <summary>The palm's width is never taken to be under this share of the template's when the pinch measure divides by it.</summary>
        internal const float MinWidthShare = 0.5f;

        const int PalmJoints = 6, Combinations = 8, Chains = Combinations + 2;
        /// <summary>The angles a finger's three joints reach in the template's fist (HandModel.Pose), radians. The thumb's are not used.</summary>
        static readonly float3 s_fistBend = math.radians(new float3(80f, 100f, 70f));
        /// <summary>The joints that are put on the palm's plane. The first five lie in it in the template; the thumb base stands 8 mm proud.</summary>
        static readonly int[] s_palmJoint =
        {
            (int)HandJoint.Wrist, (int)HandJoint.IndexMcp, (int)HandJoint.MiddleMcp, (int)HandJoint.RingMcp,
            (int)HandJoint.LittleMcp, (int)HandJoint.ThumbCmc
        };
        static readonly float[] s_palmWeight = { 1f, 1f, 1f, 1f, 1f, 0.5f };
        /// <summary>Where those joints are in the palm plane of the right-hand template: x to the thumb's side, z to the middle knuckle.</summary>
        static readonly float2[] s_palmTemplate = BuildPalmTemplate();

        /// <summary>What is remembered of one hand between detections.</summary>
        struct Memory
        {
            public bool Used;
            /// <summary>Capture time, sequence and hand index of the last detection lifted into this memory.</summary>
            public double Time;
            public int Sequence, HandIndex;
            /// <summary>The palm's centre, in the AR session's space so that a turn of the head does not move it.</summary>
            public float3 Palm;
            /// <summary>Out of the palm, session space.</summary>
            public float3 Normal;
            /// <summary>Every joint in the palm's own frame (origin the wrist; x, y, z the palm frame's axes).</summary>
            public JointSet Local;
            /// <summary>The last valid LiDAR plane: when, its normal towards the camera (session space) and the palm's two spans as lifted on it, metres.</summary>
            public bool HasPlane;
            public double PlaneTime;
            public float3 PlaneNormal;
            public float Width, Length;
            /// <summary>The detector's labels: up for right, down for left.</summary>
            public int SideVotes;
            /// <summary>The last lift's palm came from a LiDAR plane.</summary>
            public bool FromDepth;
            /// <summary>
            /// LiDAR has just taken over from the hand's size: since when, and where the size had the palm
            /// against where LiDAR has it (depth along the palm's ray as a ratio; the difference of the two
            /// plane normals, session space).
            /// </summary>
            public bool Easing;
            public double EaseTime;
            public float EaseDepth;
            public float3 EaseNormal;
        }

        readonly float3[] _ray = new float3[HandJoints.Count];     // camera space, z = 1: a point is its depth times this
        readonly float3[] _unit = new float3[HandJoints.Count];    // the same ray at unit length
        readonly bool[] _usable = new bool[HandJoints.Count];
        readonly float[] _lidar = new float[HandJoints.Count];     // metres under the joint, 0 when LiDAR has no say
        readonly float3[] _was = new float3[HandJoints.Count];     // the last detection's joints in the palm's own frame
        float _pictureNoise;                                        // how unsure a joint is in the picture, as a slope of its ray
        readonly float3[] _chains = new float3[Chains * 3];        // every chain's three joints: the eight combinations, then the straight ones
        readonly float[] _scores = new float[Chains], _stretches = new float[Chains];
        readonly float2[] _tiltTemplate = new float2[PalmJoints - 1], _tiltPicture = new float2[PalmJoints - 1];
        readonly Memory[] _memory = new Memory[HandsDetection.MaxHands];
        readonly RunningMedian _width = new RunningMedian(ScaleWindow), _length = new RunningMedian(ScaleWindow);
        readonly BoneLengths _bones = new BoneLengths();
        float _scale = 1f, _widthScale = 1f, _lengthScale = 1f;
        bool _measured;

        /// <summary>A lift that knows no walker yet: scale 1, the template's bone lengths, no hand remembered.</summary>
        public HandsLift() { }

        /// <summary>
        /// The walker's hand size relative to the template, 0.80 to 1.25, shared by both hands: the running
        /// median of the palm's two spans as LiDAR sees them. 1 until 30 good measurements exist.
        /// </summary>
        public float Scale { get { return _scale; } }

        /// <summary>The scale comes from this walker's hand. From then on a palm placed by its size alone is trusted enough to press with.</summary>
        public bool ScaleMeasured { get { return _measured; } }

        /// <summary>The walker's bone lengths as learnt so far (decision R11).</summary>
        internal BoneLengths Bones { get { return _bones; } }

        /// <summary>Forgets the walker and both hands: for a new session or a new walker.</summary>
        public void Reset()
        {
            for (int i = 0; i < _memory.Length; i++) _memory[i] = default(Memory);
            _width.Clear();
            _length.Clear();
            _bones.Reset();
            _scale = _widthScale = _lengthScale = 1f;
            _measured = false;
        }

        /// <summary>
        /// Lifts one hand of a detection. False when there is no usable hand: too few confident joints, too
        /// little of the palm, no way to tell its distance, or a palm farther than
        /// <see cref="HandsConstants.MaxHandDistance"/> (someone else's). On true every field of hand is filled;
        /// joints, palm centre and palm normal are in the camera's space at capture.
        /// </summary>
        public bool TryLift(in HandsDetection detection, int handIndex, out ObservedHand hand)
        {
            hand = default(ObservedHand);
            if (handIndex < 0 || handIndex >= HandsDetection.MaxHands || handIndex >= detection.HandCount) return false;
            float fx = detection.Fx, fy = detection.Fy;
            if (!(fx > 0f) || !(fy > 0f) || detection.ImageWidth <= 0 || detection.ImageHeight <= 0) return false;
            DetectedHand detected = handIndex == 0 ? detection.Hand0 : detection.Hand1;   // one copy; see PalmPlane.Fit
            double time = detection.CaptureTime;

            // ---- 1. Reject early ----
            int usable = 0;
            float confidenceSum = 0f;
            for (int joint = 0; joint < HandJoints.Count; joint++)
            {
                JointSample sample = detected.GetJoint(joint);
                bool seen = sample.Confidence >= HandsConstants.JointConfidenceFloor && math.all(math.isfinite(sample.Pixel));
                _usable[joint] = seen;
                if (seen) usable++;
                if (sample.Confidence > 0f) confidenceSum += math.min(sample.Confidence, 1f);
                float3 ray = seen
                    ? new float3((sample.Pixel.x - detection.Cx) / fx, -(sample.Pixel.y - detection.Cy) / fy, 1f)
                    : new float3(0f, 0f, 1f);
                _ray[joint] = ray;
                _unit[joint] = math.normalize(ray);
                // LiDAR at a joint has a say when its 3 x 3 pixels agree, its confidence is not low and it is in range.
                bool depth = seen && sample.DepthConfidence >= 1 && sample.DepthSpread < HandsConstants.JointDepthMaxSpread
                             && sample.Depth >= HandsConstants.DepthMinMetres && sample.Depth <= HandsConstants.DepthMaxMetres;
                _lidar[joint] = depth ? sample.Depth : 0f;
            }
            if (usable < HandsConstants.MinUsableJoints) return false;

            // The palm joints that were seen must fix a frame: the wrist or the thumb base, and knuckles apart.
            float weight = 0f;
            float2 templateMean = float2.zero;
            float3 centreRay = float3.zero;
            for (int k = 0; k < PalmJoints; k++)
            {
                if (!_usable[s_palmJoint[k]]) continue;
                weight += s_palmWeight[k];
                templateMean += s_palmWeight[k] * s_palmTemplate[k];
                centreRay += s_palmWeight[k] * _ray[s_palmJoint[k]];
            }
            if (!(weight > 0f)) return false;
            templateMean /= weight;
            centreRay /= weight;
            float cxx = 0f, cxz = 0f, czz = 0f;
            for (int k = 0; k < PalmJoints; k++)
            {
                if (!_usable[s_palmJoint[k]]) continue;
                float2 d = s_palmTemplate[k] - templateMean;
                cxx += s_palmWeight[k] * d.x * d.x;
                cxz += s_palmWeight[k] * d.x * d.y;
                czz += s_palmWeight[k] * d.y * d.y;
            }
            float determinant = cxx * czz - cxz * cxz, trace = cxx + czz;
            float narrow = 0.5f * (trace - math.sqrt(math.max(trace * trace - 4f * determinant, 0f)));
            if (!(narrow >= MinPalmSpreadMetres * MinPalmSpreadMetres * weight)) return false;

            // ---- The camera in the session, for what is remembered ----
            float4x4 pose = detection.CameraToSession;
            float3x3 sessionFromCamera = new float3x3(pose.c0.xyz, pose.c1.xyz, pose.c2.xyz);
            float3 sessionOrigin = pose.c3.xyz;
            if (!IsRotation(sessionFromCamera) || !math.all(math.isfinite(sessionOrigin)))
            {
                sessionFromCamera = float3x3.identity;   // no pose with the detection: remember in the camera's own space
                sessionOrigin = float3.zero;
            }
            float3x3 cameraFromSession = math.transpose(sessionFromCamera);

            // ---- 2. Find the palm in depth ----
            PalmPlaneFit plane = PalmPlane.Fit(in detection, in detected);
            bool fromDepth = plane.Valid;

            // ---- Which hand of the last detections this is ----
            float roughDepth;
            if (fromDepth) roughDepth = plane.Distance;
            else if (!FacingDepth(HandModel.PalmWidth * _widthScale, HandModel.PalmLength * _lengthScale, out roughDepth)) return false;
            float3 roughPalm = sessionOrigin + math.mul(sessionFromCamera, centreRay * roughDepth);
            bool remembered;
            int slot = FindMemory(roughPalm, time, detection.Sequence, handIndex, out remembered);
            Memory memory = remembered ? _memory[slot] : default(Memory);
            float sinceLast = (float)(time - memory.Time);
            bool recent = remembered && sinceLast >= 0f && sinceLast <= ContinuitySeconds;

            int sideHint = detected.Side == 0 ? 0 : detected.Side == 1 ? 1 : -1;
            if (sideHint == 1) memory.SideVotes = math.min(memory.SideVotes + 1, SideVoteLimit);
            else if (sideHint == 0) memory.SideVotes = math.max(memory.SideVotes - 1, -SideVoteLimit);
            int side = memory.SideVotes > 0 ? 1 : memory.SideVotes < 0 ? 0 : sideHint;

            // ---- 3. The palm's plane: LiDAR's, or with no valid fit the one the hand's size gives ----
            float3 normal, point;   // unit normal towards the camera, and a point on the plane
            float pictureNoise = PictureNoisePixels / math.min(fx, fy);
            _pictureNoise = pictureNoise;
            float easeLeft = 0f;   // how much of the palm's place is still the size's, while LiDAR takes over
            if (fromDepth)
            {
                normal = plane.Normal;
                point = plane.Point;

                // LiDAR has just taken over from the hand's size (a palm coming out to press crosses 0.32 m):
                // the palm starts where its size had it and glides to where LiDAR has it (decision R3).
                float3 sizeNormal, sizePoint;
                if (recent && !memory.FromDepth
                    && SizePlane(in memory, remembered, recent, time, cameraFromSession, centreRay, pictureNoise, out sizeNormal, out sizePoint))
                {
                    memory.Easing = true;
                    memory.EaseTime = time;
                    memory.EaseDepth = DepthOnRay(sizeNormal, sizePoint, centreRay) / DepthOnRay(normal, point, centreRay);
                    memory.EaseNormal = math.mul(sessionFromCamera, sizeNormal - normal);
                }
                if (memory.Easing)
                {
                    float age = (float)(time - memory.EaseTime);
                    float left = 1f - age / PlaneEaseSeconds;
                    if (!(age >= 0f) || !(left > 0f) || !(memory.EaseDepth > 0.5f) || !(memory.EaseDepth < 2f)) memory.Easing = false;
                    else
                    {
                        easeLeft = left;
                        float depth = DepthOnRay(normal, point, centreRay) * math.lerp(1f, memory.EaseDepth, left);
                        normal = math.normalizesafe(normal + left * math.mul(cameraFromSession, memory.EaseNormal), normal);
                        point = centreRay * depth;
                    }
                }
            }
            else
            {
                memory.Easing = false;
                if (!SizePlane(in memory, remembered, recent, time, cameraFromSession, centreRay, pictureNoise, out normal, out point)) return false;
            }

            // ---- 4. Place the palm ----
            JointSet joints = default(JointSet);
            float offset = math.dot(normal, point);   // normal . p = offset on the plane; negative, the plane is in front
            float nearest = math.max(point.z - PalmReachMetres, MinDepthMetres), farthest = point.z + PalmReachMetres;
            float3 placedMean = float3.zero;
            for (int k = 0; k < PalmJoints; k++)
            {
                int joint = s_palmJoint[k];
                if (!_usable[joint]) continue;
                float towards = math.dot(normal, _ray[joint]);
                float depth = towards < -1e-4f ? offset / towards : point.z;
                float3 placed = _ray[joint] * math.clamp(depth, nearest, farthest);
                joints[joint] = placed;
                placedMean += s_palmWeight[k] * placed;
            }
            placedMean /= weight;

            // The palm's own axes as the placed joints show them: the least-squares map from the template's
            // palm plane to camera space. It also says where a palm joint the detector was unsure of must be.
            float3 ax = float3.zero, az = float3.zero;
            for (int k = 0; k < PalmJoints; k++)
            {
                int joint = s_palmJoint[k];
                if (!_usable[joint]) continue;
                float2 d = s_palmTemplate[k] - templateMean;
                float3 p = joints[joint] - placedMean;
                ax += s_palmWeight[k] * d.x * p;
                az += s_palmWeight[k] * d.y * p;
            }
            float3 alongX = (ax * czz - az * cxz) / determinant, alongZ = (az * cxx - ax * cxz) / determinant;
            for (int k = 0; k < PalmJoints; k++)
            {
                int joint = s_palmJoint[k];
                if (_usable[joint]) continue;
                float2 d = s_palmTemplate[k] - templateMean;
                joints[joint] = placedMean + alongX * d.x + alongZ * d.y;
            }

            // Out of the palm, from the side and the layout: the template is a right hand, so cross(z, x) of the
            // axes just found comes out of the palm if this is a right hand and out of its back if it is a left
            // one. The plane's normal takes the sign that agrees. With no side at all (no label yet, or as many
            // one way as the other) the palm keeps the way it faced a moment ago; failing that the back of the
            // hand is taken to face the camera, as it does when a walker reaches out.
            float3 layout = math.cross(alongZ, alongX);
            bool rightShowsPalm = math.dot(normal, layout) >= 0f;
            float3 outOfPalm;
            if (side == 1) outOfPalm = rightShowsPalm ? normal : -normal;
            else if (side == 0) outOfPalm = rightShowsPalm ? -normal : normal;
            else if (recent) outOfPalm = math.dot(normal, math.mul(cameraFromSession, memory.Normal)) >= 0f ? normal : -normal;
            else outOfPalm = -normal;
            bool mirrored = math.dot(outOfPalm, layout) < 0f;   // lies in the picture as a left hand does

            // The thumb base stands a little proud of the palm's plane on the palm's side, as the template has it.
            float proud = HandModel.Rest(HandSide.Right, (int)HandJoint.ThumbCmc).y * _scale;
            if (_usable[(int)HandJoint.ThumbCmc])
            {
                float3 ray = _ray[(int)HandJoint.ThumbCmc];
                float towards = math.dot(normal, ray);
                if (towards < -1e-4f)
                    joints[(int)HandJoint.ThumbCmc] = ray * math.clamp((offset + proud * math.dot(normal, outOfPalm)) / towards, nearest, farthest);
            }
            else joints[(int)HandJoint.ThumbCmc] += outOfPalm * proud;

            float3 wrist = joints[(int)HandJoint.Wrist];
            float3 forward = joints[(int)HandJoint.MiddleMcp] - wrist;
            forward -= outOfPalm * math.dot(forward, outOfPalm);
            float forwardLength = math.length(forward);
            if (!(forwardLength > 1e-4f)) return false;
            float3 axisZ = forward / forwardLength, axisX = math.cross(outOfPalm, axisZ);

            float3 palmCentre = 0.5f * wrist + 0.125f * (joints[(int)HandJoint.IndexMcp] + joints[(int)HandJoint.MiddleMcp]
                                                         + joints[(int)HandJoint.RingMcp] + joints[(int)HandJoint.LittleMcp]);
            // Too far to be the walker (section 4.4, item 7).
            if (!(palmCentre.z > MinDepthMetres) || palmCentre.z > HandsConstants.MaxHandDistance) return false;

            // ---- 5. Raise the fingers, bone by bone ----
            if (recent)
                for (int joint = 0; joint < HandJoints.Count; joint++) _was[joint] = memory.Local[joint];
            float stretch = 0f;
            for (int finger = 0; finger < HandJoints.FingerCount; finger++)
                stretch += RaiseFinger(finger, ref joints, recent, wrist, axisX, outOfPalm, axisZ, mirrored);
            stretch /= 3 * HandJoints.FingerCount;
            if (!joints.IsFinite()) return false;

            // ---- What the hand is, for the stages after the lift ----
            float2 palmPixel = detection.Project(palmCentre);
            float edgeMargin = math.min(math.min(palmPixel.x, detection.ImageWidth - palmPixel.x),
                                        math.min(palmPixel.y, detection.ImageHeight - palmPixel.y)) / detection.ImageHeight;
            float width = math.distance(joints[(int)HandJoint.IndexMcp], joints[(int)HandJoint.LittleMcp]);
            float length = math.distance(joints[(int)HandJoint.MiddleMcp], wrist);

            // The pinch measure: the gap between the two tips in the picture, in metres at the palm's distance,
            // over the palm's width as lifted. With a tip unseen there is no measure, and it reads as open.
            float pinch = 1f;
            if (_usable[(int)HandJoint.ThumbTip] && _usable[(int)HandJoint.IndexTip])
            {
                float gap = math.length((_ray[(int)HandJoint.ThumbTip] - _ray[(int)HandJoint.IndexTip]).xy) * palmCentre.z;
                pinch = gap / math.max(width, MinWidthShare * HandModel.PalmWidth * _scale);
            }

            // ---- 6. Measure the hand ----
            float measuredScale = 0f;
            if (fromDepth)
            {
                bool spansSeen = _usable[(int)HandJoint.Wrist] && _usable[(int)HandJoint.MiddleMcp]
                                 && _usable[(int)HandJoint.IndexMcp] && _usable[(int)HandJoint.LittleMcp];
                float widthShare = width / HandModel.PalmWidth, lengthShare = length / HandModel.PalmLength;
                bool sane = spansSeen && widthShare > SizeSaneMin && widthShare < SizeSaneMax
                            && lengthShare > SizeSaneMin && lengthShare < SizeSaneMax;

                // The plane and the palm's spans as LiDAR shows them, for the moment it stops showing them (decision R3).
                if (!memory.HasPlane)
                {
                    memory.Width = sane ? width : HandModel.PalmWidth * _widthScale;
                    memory.Length = sane ? length : HandModel.PalmLength * _lengthScale;
                }
                else if (sane)
                {
                    memory.Width = math.lerp(memory.Width, width, SeenSpanBlend);
                    memory.Length = math.lerp(memory.Length, length, SeenSpanBlend);
                }
                float3 planeNormal = math.mul(sessionFromCamera, normal);
                bool fresh = memory.HasPlane && time - memory.PlaneTime >= 0.0 && time - memory.PlaneTime <= ContinuitySeconds;
                memory.PlaneNormal = fresh ? math.normalizesafe(math.lerp(memory.PlaneNormal, planeNormal, SeenSpanBlend), planeNormal) : planeNormal;
                memory.HasPlane = true;
                memory.PlaneTime = time;

                // Not while the palm is still gliding to LiDAR's plane: the spans are then not LiDAR's own.
                bool square = -math.dot(normal, math.normalize(point)) >= math.cos(math.radians(MeasureMaxTiltDegrees));
                if (sane && square && easeLeft <= 0f && edgeMargin >= HandsConstants.EdgeFadeBand)
                {
                    measuredScale = 0.5f * (widthShare + lengthShare);
                    _width.Add(widthShare);
                    _length.Add(lengthShare);
                    if (_width.Count >= HandsConstants.ScaleSamples)
                    {
                        _widthScale = _width.Median;
                        _lengthScale = _length.Median;
                        _scale = math.clamp(0.5f * (_widthScale + _lengthScale), HandsConstants.ScaleMin, HandsConstants.ScaleMax);
                        _measured = true;
                    }
                    ObserveBones(normal, offset, in joints);
                }
            }

            // ---- Confidence (section 4.4, item 6, with decision R3) ----
            // The lowest of four terms. The palm's: 1 from a LiDAR plane; from the hand's size 0.5, or 0.7 once
            // that size is this walker's own; and between the two while the palm glides from one to the other.
            float sizeTerm = _measured ? HandsConstants.ConfidenceFromMeasuredSize : HandsConstants.ConfidenceWithoutDepth;
            float planeTerm = fromDepth ? math.lerp(1f, sizeTerm, easeLeft) : sizeTerm;
            float stretchTerm = 1f - math.saturate((stretch - StretchFree) / StretchRange);
            float edgeTerm = math.saturate(edgeMargin / HandsConstants.EdgeFadeBand);
            float confidence = math.min(math.min(confidenceSum / HandJoints.Count, planeTerm), math.min(stretchTerm, edgeTerm));

            // ---- Remember the hand ----
            memory.Used = true;
            memory.Time = time;
            memory.Sequence = detection.Sequence;
            memory.HandIndex = handIndex;
            memory.Palm = sessionOrigin + math.mul(sessionFromCamera, palmCentre);
            memory.Normal = math.mul(sessionFromCamera, outOfPalm);
            memory.FromDepth = fromDepth;
            for (int joint = 0; joint < HandJoints.Count; joint++)
            {
                float3 p = joints[joint] - wrist;
                memory.Local[joint] = new float3(math.dot(p, axisX), math.dot(p, outOfPalm), math.dot(p, axisZ));
            }
            _memory[slot] = memory;

            hand.Joints = joints;
            hand.Confidence = confidence;
            hand.SideHint = sideHint;
            hand.PalmFromDepth = fromDepth;
            hand.PalmDistance = palmCentre.z;
            hand.PalmCentre = palmCentre;
            hand.PalmNormal = outOfPalm;
            hand.EdgeMargin = edgeMargin;
            hand.PinchRatio = pinch;
            hand.PalmPixel = palmPixel;
            hand.MeasuredScale = measuredScale;
            return true;
        }

        /// <summary>
        /// One finger from its base (already placed) to its tip. Each bone has two answers on its joint's ray,
        /// one nearer the camera and one farther; the eight combinations are scored together and the lowest
        /// wins. A finger that lies along one line in the picture is offered two chains more: the whole finger
        /// straight, its tip nearer or farther. Returns how far the winning chain's bones had to be stretched,
        /// as shares of their lengths.
        /// </summary>
        float RaiseFinger(int finger, ref JointSet joints, bool recent, float3 wrist,
                          float3 axisX, float3 outOfPalm, float3 axisZ, bool mirrored)
        {
            int first = HandJoints.Base(finger);
            float3 lengths = new float3(_bones.Length(first + 1), _bones.Length(first + 2), _bones.Length(first + 3));
            float3 knuckle = joints[first];
            FingerFrame frame;
            frame.Wrist = wrist;
            frame.AxisX = axisX;
            frame.AxisZ = axisZ;
            frame.OutOfPalm = outOfPalm;
            frame.Recent = recent;
            frame.IsFinger = finger > 0;   // the thumb turns about other axes: only the design's two penalties apply to it

            // The finger's own straight direction, from the template: the line a bend is measured against.
            // Flat is that line laid in the palm's plane, Side the direction a finger cannot bend in.
            float3 rest = HandModel.Rest(HandSide.Right, first + 1) - HandModel.Rest(HandSide.Right, first);
            if (mirrored) rest.x = -rest.x;
            frame.Rest = math.normalize(axisX * rest.x + outOfPalm * rest.y + axisZ * rest.z);
            float restRise = math.dot(frame.Rest, outOfPalm);
            frame.Flat = math.normalizesafe(frame.Rest - outOfPalm * restRise, axisZ);
            frame.Side = math.cross(outOfPalm, frame.Flat);
            frame.RestAngle = math.atan2(restRise, math.dot(frame.Rest, frame.Flat));

            // Every combination's chain, and what it costs before LiDAR is asked.
            int chains = Combinations;
            for (int combination = 0; combination < Combinations; combination++)
            {
                float3 parent = knuckle, parentDirection = frame.Rest;
                for (int bone = 0; bone < 3; bone++)
                {
                    int joint = first + 1 + bone;
                    float3 child;
                    if (_usable[joint])
                    {
                        // On the ray at the bone's length from the parent: t^2 - 2 t (u . p) + |p|^2 - L^2 = 0.
                        // With no answer (noise made the bone look longer than it is) the nearest point of the ray.
                        float3 u = _unit[joint];
                        float along = math.dot(u, parent);
                        float room = lengths[bone] * lengths[bone] - math.max(math.lengthsq(parent) - along * along, 0f);
                        float reach = room > 0f ? math.sqrt(room) : 0f;
                        bool nearer = (combination >> bone & 1) != 0;
                        child = u * math.max(along + (nearer ? -reach : reach), MinDepthMetres);
                    }
                    else
                    {
                        // The detector was unsure of this joint: the bone points as it did a moment ago in the
                        // palm's own frame, or straight on from the bone before it.
                        float3 direction = parentDirection;
                        if (recent)
                        {
                            float3 was = _was[joint] - _was[joint - 1];
                            direction = math.normalizesafe(axisX * was.x + outOfPalm * was.y + axisZ * was.z, parentDirection);
                        }
                        child = parent + direction * lengths[bone];
                    }
                    _chains[combination * 3 + bone] = child;
                    parentDirection = math.normalizesafe(child - parent, parentDirection);
                    parent = child;
                }
                ScoreChain(combination, first, knuckle, lengths, in frame);
            }

            // The whole finger as one straight line from its knuckle: the fingertip on its ray at the finger's
            // full length, and the two joints between where their rays pass that line. Three bones solved one
            // by one each turn the picture's noise into depth; one line three times as long does so a third as
            // much. Offered only when the joints between are seen within the picture's noise of the line.
            if (_usable[first + 1] && _usable[first + 2] && _usable[first + 3])
            {
                float total = lengths.x + lengths.y + lengths.z;
                float3 u = _unit[first + 3];
                float along = math.dot(u, knuckle);
                float room = total * total - math.max(math.lengthsq(knuckle) - along * along, 0f);
                float reach = room > 0f ? math.sqrt(room) : 0f;
                float slack = StraightSlackSigmas * _pictureNoise * knuckle.z;
                for (int way = 0; way < 2; way++)
                {
                    float3 tip = u * math.max(along + (way == 0 ? reach : -reach), MinDepthMetres);
                    float3 line = tip - knuckle;
                    float3 onLineA = knuckle + line * (lengths.x / total), onLineB = knuckle + line * ((lengths.x + lengths.y) / total);
                    float3 a = _unit[first + 1] * math.dot(_unit[first + 1], onLineA), b = _unit[first + 2] * math.dot(_unit[first + 2], onLineB);
                    if (math.distance(a, onLineA) > slack || math.distance(b, onLineB) > slack) continue;
                    _chains[chains * 3] = a;
                    _chains[chains * 3 + 1] = b;
                    _chains[chains * 3 + 2] = tip;
                    ScoreChain(chains, first, knuckle, lengths, in frame);
                    chains++;
                    if (reach <= 0f) break;   // one answer only
                }
            }

            // LiDAR at a joint, where the chains put that joint more than 2 cm apart: how far each answer is
            // from the measured depth. It only ever chooses between answers. It never places a joint: the pixel
            // under a fingertip is as likely the wall behind it, which is why a depth the finger could not reach
            // from its knuckle is not counted at all, and why one sample's charge is capped.
            float reachFromKnuckle = 0f;
            for (int bone = 0; bone < 3; bone++)
            {
                int joint = first + 1 + bone;
                reachFromKnuckle += lengths[bone];
                float measured = _lidar[joint];
                if (!(measured > 0f) || math.abs(measured - knuckle.z) > reachFromKnuckle + DepthSlackMetres) continue;
                float nearest = float.MaxValue, farthest = float.MinValue;
                for (int chain = 0; chain < chains; chain++)
                {
                    float z = _chains[chain * 3 + bone].z;
                    nearest = math.min(nearest, z);
                    farthest = math.max(farthest, z);
                }
                if (farthest - nearest <= HandsConstants.JointDepthDecides) continue;
                for (int chain = 0; chain < chains; chain++)
                    _scores[chain] += WeightDepth * math.min(math.abs(_chains[chain * 3 + bone].z - measured), DepthCapMetres);
            }

            int best = 0;
            for (int chain = 1; chain < chains; chain++)
                if (_scores[chain] < _scores[best]) best = chain;
            joints[first + 1] = _chains[best * 3];
            joints[first + 2] = _chains[best * 3 + 1];
            joints[first + 3] = _chains[best * 3 + 2];
            return _stretches[best];
        }

        /// <summary>What a finger's chains are scored against: the palm's own frame and the finger's line in it.</summary>
        struct FingerFrame
        {
            public float3 Wrist, AxisX, AxisZ, OutOfPalm;
            /// <summary>The finger's straight direction; that direction laid in the palm's plane; the direction a finger cannot bend in.</summary>
            public float3 Rest, Flat, Side;
            /// <summary>The angle of Rest out of the palm's plane, radians.</summary>
            public float RestAngle;
            public bool Recent, IsFinger;
        }

        /// <summary>
        /// Scores one chain of a finger on everything but LiDAR, into _scores, and how far its bones are
        /// stretched into _stretches. Every charge is a distance in metres.
        /// </summary>
        void ScoreChain(int chain, int first, float3 knuckle, float3 lengths, in FingerFrame frame)
        {
            float backwardLimit = math.radians(HandsConstants.BackwardBendDegrees);
            float sidewaysLimit = math.radians(SidewaysFreeDegrees);
            float foldFree = math.radians(FoldFreeDegrees), endJointFree = math.radians(EndJointFreeDegrees);
            float3 parent = knuckle, parentDirection = frame.Rest;
            float parentAngle = frame.RestAngle, middleBend = 0f;
            float score = 0f, stretch = 0f;
            for (int bone = 0; bone < 3; bone++)
            {
                int joint = first + 1 + bone;
                float length = lengths[bone];
                float3 child = _chains[chain * 3 + bone];
                float3 span = child - parent;
                float spanLength = math.length(span);
                // A bone longer than its length: noise made it look so, and the more of it the less the lift is trusted.
                if (spanLength > length) stretch += math.min(spanLength / length - 1f, 1f);

                // The last frame: how far the answer is from where this joint was, in the palm's own frame.
                if (frame.Recent)
                {
                    float3 p = child - frame.Wrist;
                    float3 local = new float3(math.dot(p, frame.AxisX), math.dot(p, frame.OutOfPalm), math.dot(p, frame.AxisZ));
                    score += WeightLast * math.distance(local, _was[joint]);
                }

                // Anatomy. The bone's angle in the plane a finger curls in is 0 along the finger's straight line
                // and rises towards the palm's side; a bend is the change of it from one bone to the next. Every
                // charge is the arc the joint would have to travel past what a hand can do.
                float3 childDirection = spanLength > 1e-6f ? span / spanLength : parentDirection;
                float rise = math.dot(childDirection, frame.OutOfPalm), lean = math.dot(childDirection, frame.Side);
                float run = math.dot(childDirection, frame.Flat);
                float angle = math.atan2(rise, math.sqrt(run * run + lean * lean) * (run >= 0f ? 1f : -1f));
                float bend = Wrap(angle - parentAngle);
                // What the picture's own noise can turn a bone of this length by, at this depth: every limit is
                // that much wider, so that only what noise cannot explain is charged.
                float unsure = math.min(AngleNoiseSigmas * _pictureNoise * parent.z * 1.4142f / length, backwardLimit);
                float beyond = 0f;
                // Backwards past the limit; for the first bone that is rising out of the back of the hand.
                if (-bend > backwardLimit + unsure) beyond += -bend - backwardLimit - unsure;
                if (frame.IsFinger)
                {
                    // Folded further than a fist folds.
                    float fold = s_fistBend[bone] + foldFree + unsure;
                    if (bend > fold) beyond += bend - fold;
                    // Leaning out of the plane the finger curls in: fingers spread a little, they do not bend sideways.
                    float sideways = math.abs(math.asin(math.clamp(lean, -1f, 1f)));
                    if (sideways > sidewaysLimit + unsure) beyond += sideways - sidewaysLimit - unsure;
                    // The end joint bends with the middle one.
                    if (bone == 1) middleBend = bend;
                    else if (bone == 2)
                    {
                        float apart = math.abs(bend - EndJointShare * middleBend);
                        if (apart > endJointFree + 2f * unsure) beyond += apart - endJointFree - 2f * unsure;
                    }
                }
                score += (WeightAnatomy * beyond + WeightStraight * math.abs(bend)) * length;

                parent = child;
                parentDirection = childDirection;
                parentAngle = angle;
            }
            _scores[chain] = score;
            _stretches[chain] = stretch;
        }

        /// <summary>
        /// The palm's plane when LiDAR gives none (section 4.3, step 3, with decision R3). Its tilt is carried
        /// over from the last valid fit while that is fresh, fading over <see cref="HandsConstants.PlaneMemorySeconds"/>
        /// into what the picture itself shows of it. Its distance is the smaller of what the palm's two spans
        /// give on a plane of that tilt: tilt that was not allowed for can only shorten a span in the picture,
        /// which makes its estimate too far. The spans are the ones LiDAR last showed on this hand, fading into
        /// the walker's measured ones, so nothing jumps where LiDAR ends. False when neither span is seen.
        /// </summary>
        bool SizePlane(in Memory memory, bool remembered, bool recent, double time, float3x3 cameraFromSession,
                       float3 centreRay, float pictureNoise, out float3 normal, out float3 point)
        {
            normal = point = float3.zero;
            float width = HandModel.PalmWidth * _widthScale, length = HandModel.PalmLength * _lengthScale;
            float3 facing = -math.normalize(centreRay);

            // What is carried over from the last valid plane, and how much of it.
            float carried = 0f;
            float3 carriedNormal = facing;
            if (remembered && memory.HasPlane)
            {
                float age = (float)(time - memory.PlaneTime);
                float3 was = math.mul(cameraFromSession, memory.PlaneNormal);
                if (age >= 0f && age < HandsConstants.PlaneMemorySeconds && math.dot(was, facing) > 0f)
                {
                    carried = 1f - age / HandsConstants.PlaneMemorySeconds;
                    carriedNormal = was;
                    width = math.lerp(width, memory.Width, carried);
                    length = math.lerp(length, memory.Length, carried);
                }
            }

            // Which way the palm tilted a moment ago settles what the picture cannot: of the two tilts that
            // look the same, the one nearer to it.
            bool hasHint = carried > 0f;
            float3 hint = carriedNormal;
            if (!hasHint && recent)
            {
                float3 was = math.mul(cameraFromSession, memory.Normal);
                hint = math.dot(was, facing) >= 0f ? was : -was;
                hasHint = true;
            }

            if (!PictureTilt(width / HandModel.PalmWidth, length / HandModel.PalmLength, centreRay, pictureNoise, hasHint, hint, out normal))
                normal = facing;   // too little of the palm for its tilt: taken to face the camera
            if (carried > 0f) normal = math.normalizesafe(math.lerp(normal, carriedNormal, carried), facing);

            // Each span says how far the plane is: the span in metres over what it measures on the plane at
            // unit distance. The smaller answer is taken.
            float distance = float.MaxValue;
            bool any = false;
            float spanDistance;
            if (SpanDistance((int)HandJoint.IndexMcp, (int)HandJoint.LittleMcp, width, normal, out spanDistance))
            {
                distance = math.min(distance, spanDistance);
                any = true;
            }
            if (SpanDistance((int)HandJoint.Wrist, (int)HandJoint.MiddleMcp, length, normal, out spanDistance))
            {
                distance = math.min(distance, spanDistance);
                any = true;
            }
            if (!any) return false;

            // The plane is  -normal . p = distance.
            float towards = -math.dot(normal, centreRay);
            if (!(towards > 1e-3f)) return false;
            point = centreRay * (distance / towards);
            return math.all(math.isfinite(point)) && point.z > MinDepthMetres;
        }

        /// <summary>The depth at which a z = 1 ray meets a plane given by its normal and a point on it.</summary>
        static float DepthOnRay(float3 normal, float3 point, float3 ray)
        {
            float towards = math.dot(normal, ray);
            return math.abs(towards) > 1e-4f ? math.dot(normal, point) / towards : point.z;
        }

        /// <summary>How far a plane of the given tilt is from the camera, from one of the palm's spans: both joints seen, on a plane their rays meet.</summary>
        bool SpanDistance(int a, int b, float metres, float3 normal, out float distance)
        {
            distance = 0f;
            if (!_usable[a] || !_usable[b]) return false;
            float towardsA = -math.dot(normal, _ray[a]), towardsB = -math.dot(normal, _ray[b]);
            if (!(towardsA > 0.05f) || !(towardsB > 0.05f)) return false;
            float atUnit = math.distance(_ray[a] / towardsA, _ray[b] / towardsB);
            if (!(atUnit > 1e-5f)) return false;
            distance = metres / atUnit;
            return true;
        }

        /// <summary>The palm's depth if it faced the camera, by the design's plain rule: the smaller of the two spans' estimates. Only a first guess of where the hand is.</summary>
        bool FacingDepth(float width, float length, out float depth)
        {
            depth = float.MaxValue;
            bool any = false;
            if (_usable[(int)HandJoint.IndexMcp] && _usable[(int)HandJoint.LittleMcp])
            {
                float span = math.distance(_ray[(int)HandJoint.IndexMcp].xy, _ray[(int)HandJoint.LittleMcp].xy);
                if (span > 1e-5f)
                {
                    depth = math.min(depth, width / span);
                    any = true;
                }
            }
            if (_usable[(int)HandJoint.Wrist] && _usable[(int)HandJoint.MiddleMcp])
            {
                float span = math.distance(_ray[(int)HandJoint.Wrist].xy, _ray[(int)HandJoint.MiddleMcp].xy);
                if (span > 1e-5f)
                {
                    depth = math.min(depth, length / span);
                    any = true;
                }
            }
            return any;
        }

        /// <summary>
        /// The palm's tilt as the picture alone shows it. The wrist and the four knuckles lie in one plane of
        /// known shape. Seen from far enough, a plane's picture is that shape under a linear map whose larger
        /// stretch is the true scale and whose smaller is the scale times the cosine of the tilt, about the
        /// direction of the larger. That fixes the tilt but for its sign: tipped towards the camera and tipped
        /// away look alike. The hint (the way the palm tilted a moment ago) decides; without one the wrist is
        /// taken to be nearer than the knuckles, as it is on a hand reaching out from the walker. A second pass
        /// allows for perspective: it corrects the picture by the depths the first pass found. pictureNoise is
        /// how unsure the detector's points are, as a slope of the ray. False with fewer than three of the five
        /// joints, or with three in a line.
        /// </summary>
        bool PictureTilt(float widthShare, float lengthShare, float3 centreRay, float pictureNoise, bool hasHint, float3 hint, out float3 normal)
        {
            normal = float3.zero;

            // A frame on the ray through the palm: c along it, a to the right, b up.
            float3 c = math.normalize(centreRay);
            float3 a = math.normalizesafe(math.cross(new float3(0f, 1f, 0f), c), new float3(1f, 0f, 0f));
            float3 b = math.cross(c, a);
            float3 hintLocal = new float3(math.dot(hint, a), math.dot(hint, b), math.dot(hint, c));

            int count = 0;
            float2 templateMean = float2.zero;
            for (int k = 0; k < PalmJoints - 1; k++)
            {
                int joint = s_palmJoint[k];
                if (!_usable[joint]) continue;
                float3 ray = _ray[joint];
                _tiltTemplate[count] = s_palmTemplate[k] * new float2(widthShare, lengthShare);
                _tiltPicture[count] = new float2(math.dot(ray, a), math.dot(ray, b)) / math.dot(ray, c);
                templateMean += _tiltTemplate[count];
                count++;
            }
            if (count < 3) return false;
            templateMean /= count;
            float cxx = 0f, cxz = 0f, czz = 0f;
            for (int i = 0; i < count; i++)
            {
                float2 d = _tiltTemplate[i] - templateMean;
                cxx += d.x * d.x;
                cxz += d.x * d.y;
                czz += d.y * d.y;
            }
            float determinant = cxx * czz - cxz * cxz, trace = cxx + czz;
            float narrow = 0.5f * (trace - math.sqrt(math.max(trace * trace - 4f * determinant, 0f)));
            if (!(narrow >= MinPalmSpreadMetres * MinPalmSpreadMetres * count)) return false;
            // One unit of noise in the picture moves a stretch of the map by this much per metre of distance.
            float stretchNoise = PictureNoiseSigmas * pictureNoise * math.sqrt(trace / determinant);
            float maxSine = math.sin(math.radians(MaxPictureTiltDegrees));

            float2 depthSlope = float2.zero;   // metres of depth per metre along the template's x and z
            float distance = 1f;
            for (int pass = 0; pass < 2; pass++)
            {
                // The least-squares linear map from the template's plane to the picture, each joint's place in
                // the picture first scaled by how much farther than the palm's middle the pass before put it.
                float2 pictureMean = float2.zero;
                for (int i = 0; i < count; i++)
                    pictureMean += _tiltPicture[i] * Perspective(_tiltTemplate[i] - templateMean, depthSlope, distance);
                pictureMean /= count;
                float2 ax = float2.zero, az = float2.zero;
                for (int i = 0; i < count; i++)
                {
                    float2 d = _tiltTemplate[i] - templateMean;
                    float2 q = _tiltPicture[i] * Perspective(d, depthSlope, distance) - pictureMean;
                    ax += d.x * q;
                    az += d.y * q;
                }
                float2 mapX = (ax * czz - az * cxz) / determinant, mapZ = (az * cxx - ax * cxz) / determinant;

                // Its two stretches, from the eigenvalues of its Gram matrix.
                float gxx = math.dot(mapX, mapX), gxz = math.dot(mapX, mapZ), gzz = math.dot(mapZ, mapZ);
                float root = math.sqrt((gxx - gzz) * (gxx - gzz) + 4f * gxz * gxz);
                float larger = 0.5f * (gxx + gzz + root), smaller = 0.5f * (gxx + gzz - root);
                if (!(larger > 1e-12f)) return false;
                distance = 1f / math.sqrt(larger);

                // The cosine of the tilt is the smaller stretch over the larger. Noise alone pulls the two
                // apart, and near square a little of that reads as a lot of tilt. So what noise could account
                // for is taken off, in quadrature: a palm nearly square to the line of sight is taken to be
                // square to it, and a palm well tilted keeps its tilt.
                float apart = 1f - math.sqrt(math.saturate(smaller / larger)), noise = stretchNoise * distance;
                float cosine = 1f - math.sqrt(math.max(apart * apart - noise * noise, 0f));
                float sine = math.min(math.sqrt(math.saturate(1f - cosine * cosine)), maxSine);

                // The template direction that is foreshortened, the eigenvector of the smaller stretch, and
                // with it how fast depth changes along the template's x and z.
                float2 first = new float2(gxz, smaller - gxx), second = new float2(smaller - gzz, gxz);
                float2 shortened = math.lengthsq(first) >= math.lengthsq(second) ? first : second;
                shortened = math.normalizesafe(shortened, gxx >= gzz ? new float2(0f, 1f) : new float2(1f, 0f));
                float2 slope = sine * shortened;

                // The two tilts that look alike, as plane normals towards the camera in the frame of the ray.
                float3 one = TiltNormal(mapX * distance, mapZ * distance, slope);
                float3 other = TiltNormal(mapX * distance, mapZ * distance, -slope);
                bool takeOther = hasHint ? math.dot(other, hintLocal) > math.dot(one, hintLocal) : slope.y < 0f;
                float3 local = takeOther ? other : one;
                normal = a * local.x + b * local.y + c * local.z;

                // The second pass corrects for the depths of this one only as far as this one's tilt stands
                // clear of the noise: correcting for a tilt that is itself noise would make more of it.
                depthSlope = (takeOther ? -slope : slope) * math.saturate((apart - noise) / math.max(noise, 1e-6f));
            }
            return math.all(math.isfinite(normal)) && math.lengthsq(normal) > 0.5f;
        }

        /// <summary>How much farther than the palm's middle a point of the template is, as a factor on its place in the picture.</summary>
        static float Perspective(float2 fromMiddle, float2 depthSlope, float distance)
        {
            return math.clamp(1f + math.dot(fromMiddle, depthSlope) / distance, 0.5f, 2f);
        }

        /// <summary>
        /// The normal, towards the camera, of the plane whose x and z axes look like sideX and sideZ in the
        /// picture and run into depth by depthSlope. In the frame of the ray: x right, y up, z along the ray.
        /// </summary>
        static float3 TiltNormal(float2 sideX, float2 sideZ, float2 depthSlope)
        {
            float3 n = math.normalizesafe(math.cross(new float3(sideZ, depthSlope.y), new float3(sideX, depthSlope.x)),
                                          new float3(0f, 0f, -1f));
            return n.z <= 0f ? n : -n;
        }

        /// <summary>
        /// Shows the walker's bone lengths one sighting of a palm square to the camera: every joint where its
        /// ray meets the palm's plane, and which of them may be used. A finger whose LiDAR depth says it is off
        /// that plane is left out.
        /// </summary>
        void ObserveBones(float3 normal, float offset, in JointSet joints)
        {
            JointSet flat = joints;   // the palm's joints are already on the plane
            int seen = 0;
            for (int joint = 0; joint < HandJoints.Count; joint++)
            {
                if (!_usable[joint]) continue;
                float3 ray = _ray[joint];
                float towards = math.dot(normal, ray);
                if (!(towards < -1e-4f)) continue;
                float depth = offset / towards;
                float measured = _lidar[joint];
                if (measured > 0f && math.abs(measured - depth) > FlatSlackMetres) continue;
                flat[joint] = ray * depth;
                seen |= 1 << joint;
            }
            _bones.Observe(in flat, seen, _scale);
        }

        /// <summary>
        /// The memory of the hand whose palm was nearest to this one, within <see cref="HandsConstants.AssociateMetres"/>
        /// and not already taken by the other hand of this same detection; remembered is then true. Otherwise a
        /// memory to start afresh in: an unused one, or the one that has gone longest without a hand.
        /// </summary>
        int FindMemory(float3 palm, double time, int sequence, int handIndex, out bool remembered)
        {
            int best = -1;
            float bestDistance = HandsConstants.AssociateMetres;
            for (int i = 0; i < _memory.Length; i++)
            {
                if (!_memory[i].Used || TakenByTheOtherHand(i, time, sequence, handIndex)) continue;
                double age = time - _memory[i].Time;
                if (!(age >= 0.0) || age > SlotMemorySeconds) continue;
                float distance = math.distance(_memory[i].Palm, palm);
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            remembered = best >= 0;
            if (remembered) return best;

            for (int i = 0; i < _memory.Length; i++)
            {
                if (TakenByTheOtherHand(i, time, sequence, handIndex)) continue;
                if (!_memory[i].Used) return i;
                if (best < 0 || _memory[i].Time < _memory[best].Time) best = i;
            }
            return best < 0 ? handIndex : best;
        }

        bool TakenByTheOtherHand(int i, double time, int sequence, int handIndex)
        {
            return _memory[i].Used && _memory[i].Sequence == sequence && _memory[i].Time == time && _memory[i].HandIndex != handIndex;
        }

        static bool IsRotation(float3x3 m)
        {
            float3 lengths = new float3(math.lengthsq(m.c0), math.lengthsq(m.c1), math.lengthsq(m.c2));
            return math.all(lengths > 0.96f) && math.all(lengths < 1.04f) && math.abs(math.determinant(m) - 1f) < 0.04f;
        }

        /// <summary>An angle brought into -pi to pi.</summary>
        static float Wrap(float angle)
        {
            return angle - 2f * math.PI * math.floor((angle + math.PI) / (2f * math.PI));
        }

        static float2[] BuildPalmTemplate()
        {
            var template = new float2[PalmJoints];
            for (int k = 0; k < PalmJoints; k++)
            {
                float3 rest = HandModel.Rest(HandSide.Right, s_palmJoint[k]);
                template[k] = rest.xz;
            }
            return template;
        }
    }
}

using Unity.Mathematics;

namespace Atlas.Input
{
    /// <summary>Where a fingertip stands with the target it is over.</summary>
    internal enum HandsTouchPhase : byte
    {
        /// <summary>Over no target.</summary>
        Idle = 0,
        /// <summary>Over a target, but not seen coming from its front: pushing in fires nothing.</summary>
        Hover = 1,
        /// <summary>Over a target and seen in front of it: the next push through its press plane fires it.</summary>
        Armed = 2,
        /// <summary>Through the press plane of the target it pressed, and not yet let go.</summary>
        Pressed = 3
    }

    /// <summary>
    /// Touch: an index fingertip presses the gaze targets that dwell already fires (documents/ATLAS_Hands_Design.md
    /// section 6.2, completed after its review). Engine-free (README F2): it is given the fingertips, the head pose
    /// and the frame time, reads no clock and allocates nothing per frame. It decides what is pressed;
    /// <see cref="AtlasInputCore"/> fires it, through the same Fire as the dwell.
    ///
    /// A target is met at its face. A rectangle's face is the rectangle, and its front is the side the head is on,
    /// whatever its rotation says. A sphere's face is the disc through its centre, of its radius, that faces the
    /// head. The front stays the front while a fingertip is with the target. Depth is the fingertip's distance in
    /// front of the face, towards the walker; negative is through it. A room-frame target is met in room
    /// coordinates, a head-frame target in the head's frame of this tick. LookDown targets, targets farther than
    /// 0.75 m from the head, and targets beyond their own MaxDistance are never met.
    ///
    /// There is one state for each fingertip, not one for each target:
    ///   IDLE    -- inside the outline + 1.5 cm, between 8 cm in front of the face and 8 cm through it --> HOVER
    ///   HOVER   -- seen 2.5 cm or more in front: now, or on its way here within the last 0.5 s ------------> ARMED
    ///   ARMED   -- comes through the press plane, 1 cm behind the face, inside the outline + 1 cm,
    ///              and came more in than across ------------------------------------------------------------> PRESSED, and fires
    ///   ARMED   -- goes through the press plane any other way -----------------------------------------------> HOVER
    ///   PRESSED -- back to 1 cm in front of the face --------------------------------------------------------> HOVER
    ///   PRESSED -- 3 cm outside the outline, or 8 cm through the face ---------------------------------------> IDLE
    ///   HOVER, ARMED -- leaves the volume of the first line -------------------------------------------------> IDLE
    ///   any     -- hand not valid or below confidence 0.6, hands not active, context change,
    ///              target removed, disabled or out of reach ------------------------------------------------> IDLE
    /// A fingertip belongs to one target: one whose outline it is inside before one it is only beside, then the one
    /// whose centre is nearest sideways. Which target a press goes to is judged the same way at the point where
    /// the fingertip met the press plane. While it is PRESSED it stays with the target it pressed, and no other
    /// fingertip can take that target. A Once target fires on the press. A Repeat target fires on the press and,
    /// once it has stayed pressed for a second, at its own interval. A Hold target is held, or drives its axis,
    /// for as long as it is pressed.
    ///
    /// Why this survives the sensing (section 4): the walker sees the drawn fingertip and the drawn target in the
    /// same coordinates and steers the one through the other, so a depth error of centimetres only moves where
    /// the real finger is; what the planes must absorb is jitter and overshoot, and they are 1.5 cm or more apart.
    /// "Seen in front" reaches back 20 cm and half a second, so a jab that a 15 Hz detector samples once on the
    /// way in is armed all the same. A press is taken at the first sample behind the press plane, where the line
    /// from the sample before meets that plane, so a fingertip that is 70 to 110 ms late still presses, late, and
    /// one that a slow detector next sees right through the panel has pressed what it went through.
    /// </summary>
    internal sealed class HandsTouch
    {
        // ---- Every threshold of touch and pinch. Metres and seconds; a depth is in front of a face, negative through it. ----

        /// <summary>A target farther than this from the head is beyond an arm's reach and is never touched.</summary>
        public const float ReachMetres = 0.75f;
        /// <summary>Below this confidence a hand is not trusted (the hands module's InteractConfidence): its depth is a guess.</summary>
        public const float MinConfidence = 0.6f;
        /// <summary>A dwell waits while a fingertip is this close to its target: the finger is about to do the dwell's job (decision R6).</summary>
        public const float NearMetres = 0.20f;
        /// <summary>The ring under the fingertip shows, and that hand's pinch gives way, from this far in front of a face.</summary>
        public const float HoverDepth = 0.08f;
        /// <summary>A press counts only after the fingertip was seen this far in front: more than jitter, so one push cannot fire twice.</summary>
        public const float ArmDepth = 0.025f;
        /// <summary>"Seen in front" counts out to here: deeper than one 15 Hz step of a quick finger (11 cm), so a jab cannot jump over it.</summary>
        public const float ApproachDepth = 0.20f;
        /// <summary>The approach may be this much beside the outline for each metre in front: a finger arriving at up to 45 degrees is still seen in front.</summary>
        public const float ApproachSlope = 1f;
        /// <summary>An approach seen longer ago than this says nothing about a press now.</summary>
        public const float ApproachSeconds = 0.5f;
        /// <summary>The press plane lies behind the face: a finger that stops on the face, and a predicted fingertip's overshoot, do not fire.</summary>
        public const float PressDepth = -0.010f;
        /// <summary>A press ends once the fingertip is back this far in front: 2 cm from the press plane, more than jitter, less than a tap's way out.</summary>
        public const float ReleaseDepth = 0.010f;
        /// <summary>Deeper than this the hand is reaching through the panel for what is behind it, not pressing.</summary>
        public const float ThroughDepth = -0.08f;
        /// <summary>Sideways a fingertip is good to millimetres: the outline is widened this much for hovering...</summary>
        public const float HoverMargin = 0.015f;
        /// <summary>...less for the press, so a press beside a target is not a press on it...</summary>
        public const float PressMargin = 0.010f;
        /// <summary>...and more for letting go, so a pressed finger that wanders does not drop a Hold.</summary>
        public const float ReleaseMargin = 0.030f;
        /// <summary>A crossing is a sweep, not a press, when the fingertip went more than this many times as far across the face as into it.</summary>
        public const float SweepRatio = 2f;
        /// <summary>The way in is measured back to the last sample at least this much farther out: long enough to have a direction.</summary>
        public const float SweepMetres = 0.010f;
        /// <summary>...and never farther back than this: what the hand did before is another movement.</summary>
        public const float SweepSeconds = 0.25f;
        /// <summary>A neighbour takes the fingertip over only when the fingertip is this far past the edge, or the neighbour's centre this much nearer: no flicker on the line between two rows.</summary>
        public const float SwitchMetres = 0.005f;
        /// <summary>Faces within 30 degrees of each other are one surface: a fingertip armed on a row is armed on the next (cosine).</summary>
        public const float SameSurfaceCos = 0.866f;
        /// <summary>A Repeat target repeats only after this long pressed: a tap stays in for up to 0.7 s and must step once.</summary>
        public const float RepeatDelaySeconds = 1.0f;
        /// <summary>A second route to a target that has only just fired is the same intent, not a second one: a touch on a target the gaze fired less than this ago fires nothing, nor does a pinch on a target that anything fired.</summary>
        public const float SwallowSeconds = 0.5f;

        private const int HandCount = 2;
        /// <summary>Where the pinch's firing is remembered, after the two hands'.</summary>
        private const int PinchIndex = HandCount;
        /// <summary>Samples kept for each fingertip: ApproachSeconds at 60 frames a second.</summary>
        private const int HistoryLength = 32;
        private const float TimeEpsilon = 1e-4f;

        private struct Finger
        {
            public HandsTouchPhase Phase;
            /// <summary>The target the fingertip is with; meaningful unless Idle.</summary>
            public int Slot;
            public int Generation;
            /// <summary>That target is in the head's frame.</summary>
            public bool InHead;
            /// <summary>The face's normal towards the walker in the target's own frame, kept while the fingertip is with the target.</summary>
            public float3 Normal;
            public float PressedSeconds;
            public float NextRepeat;
            /// <summary>The target fires this frame.</summary>
            public bool Fire;
            /// <summary>The hand may act this frame.</summary>
            public bool Trusted;
            public int Count;
            public int Newest;
        }

        /// <summary>A target a fingertip may belong to, and how well: one target for each fingertip.</summary>
        private struct Pick
        {
            public int Slot;
            /// <summary>The fingertip is inside the outline, not only beside it.</summary>
            public bool Inside;
            /// <summary>From the target's centre, across its face.</summary>
            public float Sideways;
            public float Depth;

            public static Pick None { get { return new Pick { Slot = -1 }; } }

            /// <summary>Inside an outline before beside one, then the nearest centre.</summary>
            public bool Beats(in Pick other)
            {
                return other.Slot < 0 || (Inside && !other.Inside) || (Inside == other.Inside && Sideways < other.Sideways);
            }

            /// <summary>The best target, unless the one the fingertip is with already is as good to within a tremble.</summary>
            public static Pick Settle(in Pick best, in Pick mine)
            {
                if (mine.Slot < 0 || best.Slot == mine.Slot) return best;
                bool clearly = (best.Inside && !mine.Inside) || (best.Inside == mine.Inside && best.Sideways + SwitchMetres < mine.Sideways);
                return clearly ? best : mine;
            }
        }

        private struct Face
        {
            public float3 Centre;
            /// <summary>Unit length, towards the walker.</summary>
            public float3 Normal;
            public float3 U, V;
            public float2 Half;
            public float Radius;
            public bool Rect;
        }

        private readonly GazeRegistry _registry;
        private readonly Finger[] _fingers = new Finger[HandCount];
        private readonly float3[] _world = new float3[HandCount * HistoryLength];
        private readonly float3[] _inHead = new float3[HandCount * HistoryLength];
        private readonly double[] _when = new double[HandCount * HistoryLength];
        private readonly GazeTargetId[] _firedId = new GazeTargetId[HandCount + 1];
        private readonly string[] _firedLabel = new string[HandCount + 1];
        private readonly int[] _firedTag = new int[HandCount + 1];
        private HandsTouchState _state;
        private ulong _near;
        private double _clock;

        public HandsTouch(GazeRegistry registry)
        {
            _registry = registry ?? throw new System.ArgumentNullException(nameof(registry));
        }

        /// <summary>What touch and pinch did in the last step.</summary>
        public ref readonly HandsTouchState State { get { return ref _state; } }

        /// <summary>One bit for each slot of the registry: a trusted fingertip is within <see cref="NearMetres"/> of that target.</summary>
        public ulong Near { get { return _near; } }

        /// <summary>The state of a fingertip: 0 the left hand's, 1 the right hand's.</summary>
        public HandsTouchPhase PhaseOf(int hand)
        {
            return hand >= 0 && hand < HandCount ? _fingers[hand].Phase : HandsTouchPhase.Idle;
        }

        /// <summary>
        /// Lets go of everything, firing nothing: a context change. The report of the last step stays, so whoever
        /// changed the context from a firing can still read what fired.
        /// </summary>
        public void Reset()
        {
            for (int hand = 0; hand < HandCount; hand++) Drop(ref _fingers[hand]);
            _near = 0UL;
        }

        /// <summary>
        /// One frame, before the dwell is ticked: where each fingertip stands, which targets a fingertip is near,
        /// and what is pressed. The dwell is asked only how long ago it fired a target.
        /// </summary>
        public void Step(in HandsInput hands, float3 headPosition, quaternion headRotation, float deltaTime,
                         InputContext context, GazeDwell dwell)
        {
            float dt = deltaTime > 0f ? deltaTime : 0f;
            _clock += dt;
            _near = 0UL;
            _state = default;
            bool active = hands.Active && context != InputContext.InHand;
            quaternion rotation = math.normalizesafe(headRotation);

            for (int hand = 0; hand < HandCount; hand++)
            {
                ref Finger finger = ref _fingers[hand];
                HandsFingertip tip = hand == 0 ? hands.Left : hands.Right;
                finger.Fire = false;
                finger.Trusted = active && tip.Valid && tip.Confidence >= MinConfidence && math.all(math.isfinite(tip.World));
                if (!finger.Trusted)
                {
                    Drop(ref finger);
                    continue;
                }

                float3 inHead = math.rotate(math.conjugate(rotation), tip.World - headPosition);
                Remember(hand, ref finger, tip.World, inHead);
                Follow(hand, ref finger, tip.World, inHead, headPosition, rotation, dt, context, dwell);
                if (finger.Phase == HandsTouchPhase.Idle) continue;
                if (finger.Fire) NoteFired(hand, new GazeTargetId(finger.Slot, finger.Generation));
                Report(ref finger, ref (hand == 0 ? ref _state.Left : ref _state.Right), tip.World, inHead, headPosition, rotation);
            }
            _state.AnyNear = _near != 0UL;
        }

        /// <summary>The target a fingertip fires this frame: on its press, or on a repeat.</summary>
        public bool Firing(int hand, out int slot)
        {
            slot = _fingers[hand].Slot;
            return _fingers[hand].Fire;
        }

        /// <summary>The Hold target a fingertip is holding this frame.</summary>
        public bool Holding(int hand, out int slot)
        {
            slot = _fingers[hand].Slot;
            return _fingers[hand].Phase == HandsTouchPhase.Pressed && _registry.DataAt(slot).Mode == GazeMode.Hold;
        }

        /// <summary>
        /// A pinch closed this frame on a hand that may pinch: one that is trusted and whose fingertip is over no
        /// target. A hand that is touching does not pinch: touch wins.
        /// </summary>
        public bool Pinched(in HandsInput hands)
        {
            return (hands.PinchDownLeft && Free(0)) || (hands.PinchDownRight && Free(1));
        }

        /// <summary>The pinch fired a target: for the report, and so that its label outlives it.</summary>
        public void NotePinch(GazeTargetId id)
        {
            _state.PinchFired = true;
            _state.PinchTarget = id;
            NoteFired(PinchIndex, id);
        }

        /// <summary>A target's label; null when it has none or is unknown. A target a hand fired in the last step is known even if firing it removed it.</summary>
        public string LabelOf(GazeTargetId id)
        {
            if (!id.IsValid) return null;
            if (_registry.Contains(id)) return _registry.DataAt(id.Slot).Label;
            for (int i = 0; i <= HandCount; i++)
                if (_firedId[i] == id) return _firedLabel[i];
            return null;
        }

        /// <summary>A target's tag; 0 when it is unknown.</summary>
        public int TagOf(GazeTargetId id)
        {
            if (!id.IsValid) return 0;
            if (_registry.Contains(id)) return _registry.DataAt(id.Slot).Tag;
            for (int i = 0; i <= HandCount; i++)
                if (_firedId[i] == id) return _firedTag[i];
            return 0;
        }

        private bool Free(int hand)
        {
            return _fingers[hand].Trusted && _fingers[hand].Phase == HandsTouchPhase.Idle;
        }

        private void NoteFired(int index, GazeTargetId id)
        {
            ref GazeTargetData d = ref _registry.DataAt(id.Slot);
            _firedId[index] = id;
            _firedLabel[index] = d.Label;
            _firedTag[index] = d.Tag;
        }

        // Idle, and with no memory of where the fingertip was: what a hand did before it was lost says nothing now.
        private static void Drop(ref Finger finger)
        {
            finger.Phase = HandsTouchPhase.Idle;
            finger.Fire = false;
            finger.Count = 0;
        }

        private void Remember(int hand, ref Finger finger, float3 world, float3 inHead)
        {
            finger.Newest = finger.Count == 0 ? 0 : (finger.Newest + 1) % HistoryLength;
            if (finger.Count < HistoryLength) finger.Count++;
            int at = hand * HistoryLength + finger.Newest;
            _world[at] = world;
            _inHead[at] = inHead;
            _when[at] = _clock;
        }

        // The sample taken `back` frames before the newest (back < Count), in the room's frame or the head's, and its age.
        private float3 Sample(int hand, ref Finger finger, int back, bool head, out float age)
        {
            int at = hand * HistoryLength + (finger.Newest - back + HistoryLength) % HistoryLength;
            age = (float)(_clock - _when[at]);
            return head ? _inHead[at] : _world[at];
        }

        private void Follow(int hand, ref Finger finger, float3 world, float3 inHead, float3 headPosition, quaternion rotation,
                            float dt, InputContext context, GazeDwell dwell)
        {
            // The target it was with may be gone: removed, disabled, or of another context.
            if (finger.Phase != HandsTouchPhase.Idle && !_registry.IsLive(finger.Slot, finger.Generation, context))
                finger.Phase = HandsTouchPhase.Idle;

            if (finger.Phase == HandsTouchPhase.Pressed) KeepPressing(ref finger, world, inHead, headPosition, rotation, dt);

            // Every target of this context within reach: is the fingertip near it, which one is it over, and which
            // one has it just come through? For each of the last two, the best target and the one it is with already.
            Pick over = Pick.None, overMine = Pick.None, through = Pick.None, throughMine = Pick.None;
            int taken = _fingers[1 - hand].Phase == HandsTouchPhase.Pressed ? _fingers[1 - hand].Slot : -1;
            int limit = _registry.SlotLimit;
            for (int slot = 0; slot < limit; slot++)
            {
                if (!_registry.IsLive(slot, context)) continue;
                ref GazeTargetData d = ref _registry.DataAt(slot);
                if (!WithinReach(ref d, headPosition)) continue;
                bool mine = finger.Phase != HandsTouchPhase.Idle && slot == finger.Slot;
                float3 tip = d.Space == GazeSpace.Head ? inHead : world;
                Face face = FaceOf(ref d, headPosition, rotation, mine, finger.Normal);
                Measure(in face, tip, out float z, out float outside, out float sideways);
                if (Away(in face, tip, z, outside) <= NearMetres) _near |= 1UL << slot;

                if (finger.Phase == HandsTouchPhase.Pressed || slot == taken) continue;
                // The target it is with still counts as "inside" a little way past its edge: rows lie edge to edge.
                float edge = mine ? SwitchMetres : 0f;

                // A press is judged where the fingertip met the press plane, not where it is seen now: a quick finger
                // may by now be right through the panel, and beside where it went in.
                if (z <= PressDepth && Crossed(hand, ref finger, in face, d.Space == GazeSpace.Head, tip, z, out float3 at))
                {
                    Measure(in face, at, out _, out float outsideThere, out float sidewaysThere);
                    if (outsideThere <= PressMargin)
                    {
                        var there = new Pick { Slot = slot, Inside = outsideThere <= edge, Sideways = sidewaysThere, Depth = z };
                        if (mine) throughMine = there;
                        if (there.Beats(in through)) through = there;
                    }
                }

                if (outside > HoverMargin || z > HoverDepth || z < ThroughDepth) continue;
                var here = new Pick { Slot = slot, Inside = outside <= edge, Sideways = sideways, Depth = z };
                if (mine) overMine = here;
                if (here.Beats(in over)) over = here;
            }
            if (finger.Phase == HandsTouchPhase.Pressed) return;

            // The target it is with keeps the fingertip unless another is clearly the one.
            over = Pick.Settle(in over, in overMine);
            through = Pick.Settle(in through, in throughMine);

            // It came through a target's press plane: that target is pressed, if the fingertip was armed. A target
            // it only brushed past, beside its outline, gives way to one whose outline the fingertip is inside
            // (the "+" that stands a little in front of its row, when the finger is on the row beside it).
            bool brushed = !through.Inside && over.Inside && over.Slot != through.Slot;
            if (through.Slot >= 0 && !brushed)
            {
                Meet(hand, ref finger, through.Slot, true, world, inHead, headPosition, rotation, dwell);
                // Right through the panel with nothing pressed: it is over nothing.
                if (finger.Phase != HandsTouchPhase.Pressed && through.Depth < ThroughDepth) finger.Phase = HandsTouchPhase.Idle;
                return;
            }
            if (over.Slot < 0)
            {
                finger.Phase = HandsTouchPhase.Idle;
                return;
            }
            Meet(hand, ref finger, over.Slot, false, world, inHead, headPosition, rotation, dwell);
        }

        // PRESSED: the fingertip stays with the target it pressed until it lets go, slides off or goes on through.
        private void KeepPressing(ref Finger finger, float3 world, float3 inHead, float3 headPosition, quaternion rotation, float dt)
        {
            ref GazeTargetData d = ref _registry.DataAt(finger.Slot);
            if (!WithinReach(ref d, headPosition))
            {
                finger.Phase = HandsTouchPhase.Idle;
                return;
            }
            Face face = FaceOf(ref d, headPosition, rotation, true, finger.Normal);
            Measure(in face, finger.InHead ? inHead : world, out float z, out float outside, out _);
            finger.Normal = face.Normal;
            if (z < ThroughDepth || outside > ReleaseMargin)
            {
                finger.Phase = HandsTouchPhase.Idle;
            }
            else if (z >= ReleaseDepth)
            {
                finger.Phase = HandsTouchPhase.Hover;       // let go: a new press needs the fingertip seen in front again
            }
            else
            {
                finger.PressedSeconds += dt;
                if (d.Mode == GazeMode.Repeat && finger.PressedSeconds >= finger.NextRepeat - TimeEpsilon)
                {
                    finger.NextRepeat += d.RepeatSeconds;
                    finger.Fire = true;
                }
            }
        }

        // HOVER and ARMED: the fingertip is over this target. Is it armed, and has it just pressed? cameThrough: it
        // met this target's press plane, inside the press outline, since the last sample.
        private void Meet(int hand, ref Finger finger, int slot, bool cameThrough, float3 world, float3 inHead, float3 headPosition,
                          quaternion rotation, GazeDwell dwell)
        {
            ref GazeTargetData d = ref _registry.DataAt(slot);
            bool head = d.Space == GazeSpace.Head;
            bool same = finger.Phase != HandsTouchPhase.Idle && finger.Slot == slot;
            float3 tip = head ? inHead : world;
            Face face = FaceOf(ref d, headPosition, rotation, same, finger.Normal);
            float z = math.dot(tip - face.Centre, face.Normal);

            bool armed;
            if (same)
            {
                armed = finger.Phase == HandsTouchPhase.Armed;
            }
            else
            {
                // A row's neighbour is the same surface: what was armed on the one is armed on the other. Anything
                // else must have been seen in front of this face on the way here.
                float3 was = finger.InHead ? math.rotate(rotation, finger.Normal) : finger.Normal;
                float3 now = head ? math.rotate(rotation, face.Normal) : face.Normal;
                bool carried = finger.Phase == HandsTouchPhase.Armed && (z > PressDepth || cameThrough)
                               && math.dot(was, now) >= SameSurfaceCos;
                finger.Slot = slot;
                finger.Generation = _registry.GenerationAt(slot);
                finger.InHead = head;
                armed = carried || SeenInFront(hand, ref finger, in face, head);
            }
            finger.Normal = face.Normal;

            bool pressed = false;
            if (z >= ArmDepth)
            {
                armed = true;
            }
            else if (z <= PressDepth)
            {
                pressed = armed && cameThrough && CameIn(hand, ref finger, in face, head, tip, z);
                armed = false;      // through with no press: it must be seen in front again before it can press
            }

            if (!pressed)
            {
                finger.Phase = armed ? HandsTouchPhase.Armed : HandsTouchPhase.Hover;
                return;
            }
            finger.Phase = HandsTouchPhase.Pressed;
            finger.PressedSeconds = 0f;
            finger.NextRepeat = RepeatDelaySeconds;
            // The dwell fired this target a moment ago: the finger that was already on its way presses, and fires nothing.
            finger.Fire = dwell == null || dwell.SecondsSinceFired(new GazeTargetId(slot, finger.Generation)) >= SwallowSeconds;
        }

        // On its way here the fingertip was in front of this face, inside the approach cone, and has not been through it since.
        private bool SeenInFront(int hand, ref Finger finger, in Face face, bool head)
        {
            for (int back = 1; back < finger.Count; back++)
            {
                float3 sample = Sample(hand, ref finger, back, head, out float age);
                if (age > ApproachSeconds) break;
                Measure(in face, sample, out float z, out float outside, out _);
                if (z <= PressDepth) return false;
                if (z >= ArmDepth && z <= ApproachDepth && outside <= HoverMargin + ApproachSlope * z) return true;
            }
            return false;
        }

        // The fingertip has come through the press plane since the last sample, and where. A fingertip that was
        // through already has not: it slid here behind the plane, or the target was moved onto it.
        private bool Crossed(int hand, ref Finger finger, in Face face, bool head, float3 tip, float z, out float3 at)
        {
            at = tip;
            if (finger.Count < 2 || z > PressDepth) return false;
            float3 from = Sample(hand, ref finger, 1, head, out _);
            float before = math.dot(from - face.Centre, face.Normal);
            if (before <= PressDepth) return false;
            at = math.lerp(from, tip, (before - PressDepth) / (before - z));
            return true;
        }

        // The fingertip came more in than across: a push, not a sweep.
        private bool CameIn(int hand, ref Finger finger, in Face face, bool head, float3 tip, float z)
        {
            // The way in: back to the last sample at least SweepMetres farther out, and no farther back than SweepSeconds.
            float3 from = tip;
            for (int back = 1; back < finger.Count; back++)
            {
                float3 sample = Sample(hand, ref finger, back, head, out float age);
                if (age > SweepSeconds) break;
                from = sample;
                if (math.dot(from - face.Centre, face.Normal) - z >= SweepMetres) break;
            }
            float3 way = tip - from;
            float inwards = -math.dot(way, face.Normal);
            float across = math.length(way + inwards * face.Normal);
            return across <= SweepRatio * math.max(inwards, SweepMetres);
        }

        // What the hands view and the event log are told about a fingertip that is over a target.
        private void Report(ref Finger finger, ref HandsTouchHand report, float3 world, float3 inHead, float3 headPosition,
                            quaternion rotation)
        {
            ref GazeTargetData d = ref _registry.DataAt(finger.Slot);
            Face face = FaceOf(ref d, headPosition, rotation, true, finger.Normal);
            float3 offset = (finger.InHead ? inHead : world) - face.Centre;
            float z = math.dot(offset, face.Normal);
            float3 across = offset - z * face.Normal;
            if (face.Rect)
            {
                across = face.U * math.clamp(math.dot(across, face.U), -face.Half.x, face.Half.x)
                       + face.V * math.clamp(math.dot(across, face.V), -face.Half.y, face.Half.y);
            }
            else
            {
                float length = math.length(across);
                if (length > face.Radius) across *= face.Radius / length;
            }
            float3 point = face.Centre + across;

            report.Target = new GazeTargetId(finger.Slot, finger.Generation);
            report.Hover = math.saturate(1f - z / HoverDepth);
            report.Pressed = finger.Phase == HandsTouchPhase.Pressed;
            report.Fired = finger.Fire;
            report.FacePoint = finger.InHead ? headPosition + math.rotate(rotation, point) : point;
            report.FaceNormal = finger.InHead ? math.rotate(rotation, face.Normal) : face.Normal;
        }

        private static bool WithinReach(ref GazeTargetData d, float3 headPosition)
        {
            if (d.Space == GazeSpace.LookDown) return false;        // it has no place to touch
            float distance = d.Space == GazeSpace.Head ? math.length(d.Centre) : math.distance(d.Centre, headPosition);
            return distance <= ReachMetres && (d.MaxDistance <= 0f || distance <= d.MaxDistance);
        }

        // The face of a target in the target's own frame. keep: the fingertip is with this target, so its front stays where it was.
        private static Face FaceOf(ref GazeTargetData d, float3 headPosition, quaternion headRotation, bool keep, float3 kept)
        {
            bool inHead = d.Space == GazeSpace.Head;
            float3 head = inHead ? float3.zero : headPosition;
            Face face;
            face.Centre = d.Centre;
            face.Rect = d.Shape == GazeShape.Rect;
            if (face.Rect)
            {
                face.U = math.rotate(d.Rotation, new float3(1f, 0f, 0f));
                face.V = math.rotate(d.Rotation, new float3(0f, 1f, 0f));
                face.Half = d.HalfSize;
                face.Radius = 0f;
                float3 normal = math.rotate(d.Rotation, new float3(0f, 0f, 1f));
                float side = keep ? math.dot(normal, kept) : math.dot(head - d.Centre, normal);
                face.Normal = side < 0f ? -normal : normal;
            }
            else
            {
                face.U = float3.zero;
                face.V = float3.zero;
                face.Half = float2.zero;
                face.Radius = d.Radius;
                if (keep)
                {
                    face.Normal = kept;
                }
                else
                {
                    // With the head on the centre itself the disc faces back along the gaze.
                    float3 back = inHead ? new float3(0f, 0f, -1f) : -math.rotate(headRotation, new float3(0f, 0f, 1f));
                    face.Normal = math.normalizesafe(head - d.Centre, back);
                }
            }
            return face;
        }

        // z: in front of the face. outside: beside the outline, negative inside it. sideways: from the centre, across the face.
        private static void Measure(in Face face, float3 point, out float z, out float outside, out float sideways)
        {
            float3 offset = point - face.Centre;
            z = math.dot(offset, face.Normal);
            if (face.Rect)
            {
                float2 across = math.abs(new float2(math.dot(offset, face.U), math.dot(offset, face.V)));
                float2 over = across - face.Half;
                outside = over.x <= 0f && over.y <= 0f ? math.max(over.x, over.y) : math.length(math.max(over, float2.zero));
                sideways = math.length(across);
            }
            else
            {
                sideways = math.length(offset - z * face.Normal);
                outside = sideways - face.Radius;
            }
        }

        // How far a fingertip is from the target itself: from the rectangle, or from the ball's surface.
        private static float Away(in Face face, float3 point, float z, float outside)
        {
            if (!face.Rect) return math.max(math.distance(point, face.Centre) - face.Radius, 0f);
            float beside = math.max(outside, 0f);
            return math.sqrt(beside * beside + z * z);
        }
    }
}

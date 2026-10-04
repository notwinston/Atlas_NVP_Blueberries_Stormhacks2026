using System;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>
    /// Filtering, prediction, loss and left and right (documents/ATLAS_Hands_Design.md section 4.4, with the loss
    /// timing of decision R2). Detections arrive 30 or 15 times a second, 70 to 110 ms after the light hit the
    /// sensor; frames are drawn 60 times a second. The sense step calls <see cref="Observe"/> for every new
    /// result and <see cref="Advance"/> once every frame after that, and everything else reads
    /// <see cref="Hand"/>. Engine-free: time is passed in, joints are already in world space, and nothing
    /// allocates after construction.
    ///
    /// Each hand is a track. A result's hands are matched to the tracks whose predicted palm is nearest within
    /// <see cref="HandsConstants.AssociateMetres"/>, the rest start new tracks. A track keeps a One-Euro filter on
    /// the palm's position and another on its orientation, both in the room frame, so that head motion is never
    /// smoothed, and a quicker one on every joint in the palm's own frame, so that a moving hand does not smear its
    /// fingers. Each frame shows the palm moved along its filtered velocity by the age of the pose, no more than
    /// <see cref="HandsConstants.MaxPredictSeconds"/>; fingers are not predicted.
    ///
    /// A hand that a result leaves out is lost from that result on: it keeps its pose, coasts on a velocity that
    /// dies away in <see cref="HandsConstants.CoastDecaySeconds"/>, stays tracked until
    /// <see cref="HandsConstants.LossGraceSeconds"/> have passed, and is then given up. A hand that is found
    /// again within that time and within <see cref="HandsConstants.ReacquireMetres"/> of where it was carries on;
    /// found farther away, its filters start again.
    ///
    /// A hand's side is the majority of its last <see cref="HandsConstants.SideVotes"/> labels from the
    /// detector, and two hands never share a side.
    /// </summary>
    public sealed class HandsFilter
    {
        const int TrackCount = 2;

        /// <summary>
        /// A hand that no result has been received for in this long is taken as lost, whatever the reason (a
        /// detector gone quiet, a source that stopped), seconds. It is the interval of the slowest rate the
        /// detector has, so a healthy detector never reaches it.
        /// </summary>
        internal const float ResultTimeoutSeconds = 0.2f;

        /// <summary>
        /// Below this speed the filtered velocity is mostly detector noise, and moving the palm along it would
        /// turn the noise of a still hand into a shimmer: the prediction fades in over this, as speed squared over
        /// speed squared plus this squared (half at this speed, nine tenths at three times it). Metres per second.
        /// </summary>
        internal const float PredictSpeedFloor = 0.05f;

        /// <summary>Counted detections stop here (about two years at 30 Hz).</summary>
        const int GoodDetectionsCap = 1 << 30;

        /// <summary>Angular speed counts as the speed of a point one hand length out, so one Beta serves both.</summary>
        static readonly float RotationBeta = HandsConstants.PalmBeta * HandModel.HandLength;

        sealed class Track
        {
            public bool Alive;
            /// <summary>0 left, 1 right, -1 not decided yet.</summary>
            public int Side = -1;

            // The last SideVotes labels, a ring, and how many of each.
            public readonly sbyte[] Votes = new sbyte[HandsConstants.SideVotes];
            public int VoteNext, VoteCount, LeftVotes, RightVotes;

            public OneEuro Palm;
            public OneEuroRotation Rotation;
            public readonly OneEuro[] Joints = new OneEuro[HandJoints.Count];

            // From the last detection.
            public double CaptureTime;
            public float Confidence, EdgeMargin, PinchRatio, PalmDistance, PalmPixelX;
            public bool PalmFromDepth;
            public int GoodDetections;

            // A detection was taken into this track by the Observe in progress.
            public bool Seen;
            // Set by Observe, stamped by the next Advance, which is where the frame's clock is known.
            public bool PendingStart, PendingFresh;
            public double StartNow;
            public bool Lost, LostStamped;
            public double LostStartNow;
            /// <summary>How far ahead of the filtered pose the palm was drawn when the loss began, seconds of velocity.</summary>
            public float PredictAtLoss;

            public Track()
            {
                Palm = new OneEuro(HandsConstants.PalmMinCutoff, HandsConstants.PalmBeta, HandsConstants.DerivativeCutoff);
                Rotation = new OneEuroRotation
                {
                    MinCutoff = HandsConstants.PalmMinCutoff,
                    Beta = RotationBeta,
                    DerivativeCutoff = HandsConstants.DerivativeCutoff
                };
                for (int i = 0; i < Joints.Length; i++)
                    Joints[i] = new OneEuro(HandsConstants.FingerMinCutoff, HandsConstants.FingerBeta, HandsConstants.DerivativeCutoff);
            }

            public void ResetFilters()
            {
                Palm.Reset();
                Rotation.Reset();
                for (int i = 0; i < Joints.Length; i++) Joints[i].Reset();
            }

            public void ClearVotes()
            {
                for (int i = 0; i < Votes.Length; i++) Votes[i] = 0;
                VoteNext = 0;
                VoteCount = 0;
                LeftVotes = 0;
                RightVotes = 0;
            }

            public void AddVote(int side)
            {
                if (VoteCount == Votes.Length)
                {
                    if (Votes[VoteNext] == 0) LeftVotes--; else RightVotes--;
                }
                else
                {
                    VoteCount++;
                }
                Votes[VoteNext] = (sbyte)side;
                if (side == 0) LeftVotes++; else RightVotes++;
                VoteNext = (VoteNext + 1) % Votes.Length;
            }
        }

        readonly Track[] _tracks = new Track[TrackCount];
        readonly TrackedHand[] _out = new TrackedHand[2];

        // Scratch for Observe: the observed hands that can be used, which of the two arguments each came from,
        // its palm frame, the track it was matched to and how far from that track's predicted palm.
        readonly int[] _from = new int[2];
        readonly quaternion[] _frame = new quaternion[2];
        readonly int[] _match = new int[2];
        readonly float[] _distance = new float[2];
        readonly float[] _cost = new float[TrackCount * 2];

        double _lastCapture = double.NegativeInfinity;
        double _now = double.NegativeInfinity;
        double _lastResultNow = double.NegativeInfinity;
        bool _resultPending;

        /// <summary>
        /// The prediction limit, seconds: how far ahead of the filtered pose the palm may be drawn. Starts at
        /// <see cref="HandsConstants.MaxPredictSeconds"/>; 0 switches prediction off (documents/ATLAS_Hands_Design.md
        /// section 9.1: if it does not beat none on the recorded hands it ships off).
        /// </summary>
        internal float PredictionLimitSeconds = HandsConstants.MaxPredictSeconds;

        /// <summary>
        /// The picture column that divides the left half of the view from the right, pixels: half the captured
        /// image's width. A hand with no label and no history is given the side of the half its palm is in. The
        /// sense step sets it from the detection's image width; the default is for a picture 1280 wide.
        /// </summary>
        internal float ViewCentreX = 640f;

        /// <summary>An empty filter: no hands. Everything it will ever need is allocated here.</summary>
        public HandsFilter()
        {
            for (int i = 0; i < TrackCount; i++) _tracks[i] = new Track();
        }

        /// <summary>Forgets every hand and every clock. The next result starts from nothing.</summary>
        public void Reset()
        {
            for (int i = 0; i < TrackCount; i++)
            {
                Track t = _tracks[i];
                t.Alive = false;
                t.Side = -1;
                t.Seen = false;
                t.PendingStart = false;
                t.PendingFresh = false;
                t.Lost = false;
                t.LostStamped = false;
                t.GoodDetections = 0;
                t.ClearVotes();
                t.ResetFilters();
            }
            _out[0] = default(TrackedHand);
            _out[1] = default(TrackedHand);
            _lastCapture = double.NegativeInfinity;
            _now = double.NegativeInfinity;
            _lastResultNow = double.NegativeInfinity;
            _resultPending = false;
        }

        /// <summary>
        /// This frame's hand on a side, as <see cref="Advance"/> left it. Not <see cref="TrackedHand.Tracked"/>
        /// means there is no hand on that side, and every other field is then zero. Valid until the next Advance.
        /// </summary>
        public ref readonly TrackedHand Hand(HandSide side)
        {
            return ref _out[side == HandSide.Left ? 0 : 1];
        }

        /// <summary>
        /// A new detection result: count hands (0, 1 or 2; a result with none still has to be reported, it is how a
        /// loss is known), whose joints, palm centre and palm normal are already in world space, captured at
        /// captureTime on the app clock. Call it before <see cref="Advance"/> in the frame it arrives. A result
        /// whose time is not later than the last one's is ignored.
        /// </summary>
        public void Observe(double captureTime, int count, in ObservedHand a, in ObservedHand b)
        {
            if (double.IsNaN(captureTime) || double.IsInfinity(captureTime) || captureTime <= _lastCapture) return;
            _lastCapture = captureTime;
            _resultPending = true;

            // Which of the observed hands can be used at all.
            int n = 0;
            if (count >= 1 && TryFrame(in a, out _frame[n])) _from[n++] = 0;
            if (count >= 2 && TryFrame(in b, out _frame[n])) _from[n++] = 1;

            for (int i = 0; i < TrackCount; i++) _tracks[i].Seen = false;
            Associate(n, captureTime, in a, in b);

            // Hands that continue a track first, so that a new hand never takes the slot of one that was matched.
            for (int i = 0; i < n; i++)
            {
                if (_match[i] < 0) continue;
                Track t = _tracks[_match[i]];
                if (_from[i] == 0) Take(t, in a, _frame[i], captureTime, _distance[i] > HandsConstants.ReacquireMetres);
                else Take(t, in b, _frame[i], captureTime, _distance[i] > HandsConstants.ReacquireMetres);
            }
            for (int i = 0; i < n; i++)
            {
                if (_match[i] >= 0) continue;
                Track t = Acquire(_from[i] == 0 ? a.SideHint : b.SideHint);
                if (t == null) continue;
                Start(t);
                if (_from[i] == 0) Take(t, in a, _frame[i], captureTime, true);
                else Take(t, in b, _frame[i], captureTime, true);
            }

            // What the result did not have is lost, from this result on.
            for (int i = 0; i < TrackCount; i++)
            {
                Track t = _tracks[i];
                if (!t.Alive || t.Seen || t.Lost) continue;
                t.Lost = true;
                t.LostStamped = false;
            }
            ResolveSides();
        }

        /// <summary>
        /// Once per frame, after any <see cref="Observe"/> of the frame: moves the clock on to now (the app clock),
        /// gives up the hands whose loss has lasted <see cref="HandsConstants.LossGraceSeconds"/>, and writes this
        /// frame's <see cref="Hand"/> for both sides. A track is not shown before its first Advance.
        /// </summary>
        public void Advance(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) return;
            if (now < _now) now = _now;
            _now = now;
            if (_resultPending)
            {
                _lastResultNow = now;
                _resultPending = false;
            }
            bool quiet = now - _lastResultNow > ResultTimeoutSeconds;

            _out[0] = default(TrackedHand);
            _out[1] = default(TrackedHand);
            for (int i = 0; i < TrackCount; i++)
            {
                Track t = _tracks[i];
                if (!t.Alive) continue;
                if (t.PendingStart)
                {
                    t.StartNow = now;
                    t.PendingStart = false;
                }
                bool fresh = t.PendingFresh;
                t.PendingFresh = false;

                if (!t.Lost && quiet)
                {
                    t.Lost = true;
                    t.LostStamped = false;
                }
                double lostFor = 0.0;
                if (t.Lost)
                {
                    if (!t.LostStamped)
                    {
                        t.LostStartNow = now;
                        t.PredictAtLoss = (float)math.min(math.max(0.0, now - t.CaptureTime), (double)PredictionLimitSeconds);
                        t.LostStamped = true;
                    }
                    lostFor = now - t.LostStartNow;
                    if (lostFor >= HandsConstants.LossGraceSeconds)
                    {
                        t.Alive = false;
                        continue;
                    }
                }
                if (t.Side < 0) continue;
                Compose(t, now, fresh, (float)lostFor, ref _out[t.Side]);
            }
        }

        // ---- Observe ----

        /// <summary>
        /// The palm's own frame of an observed hand, or false when the hand cannot be used: its numbers are not
        /// finite, its palm is too far to be the walker's, or its normal and its wrist-to-knuckle direction do not
        /// make a frame. Origin at the palm centre; Y the palm normal; Z from the wrist towards the middle
        /// knuckle, made square to Y; X their cross product.
        /// </summary>
        static bool TryFrame(in ObservedHand hand, out quaternion rotation)
        {
            rotation = quaternion.identity;
            if (!(hand.PalmDistance <= HandsConstants.MaxHandDistance)) return false;
            if (!math.all(math.isfinite(hand.PalmCentre)) || !math.all(math.isfinite(hand.PalmNormal))) return false;
            JointSet joints = hand.Joints;
            if (!joints.IsFinite()) return false;

            float3 y = hand.PalmNormal;
            float length = math.length(y);
            if (!(length > 0.5f && length < 2f)) return false;
            y /= length;
            float3 z = joints[HandJoint.MiddleMcp] - joints[HandJoint.Wrist];
            z -= y * math.dot(y, z);
            float reach = math.length(z);
            if (!(reach > 1e-3f)) return false;
            z /= reach;
            float3 x = math.cross(y, z);
            rotation = new quaternion(new float3x3(x, y, z));
            return true;
        }

        /// <summary>Where the track's palm is predicted to have been at a capture time, for matching.</summary>
        float3 PredictedPalm(Track t, double captureTime)
        {
            float elapsed = (float)math.max(0.0, captureTime - t.CaptureTime);
            float reach = HandsConstants.CoastDecaySeconds * (1f - math.exp(-elapsed / HandsConstants.CoastDecaySeconds));
            return t.Palm.Value + t.Palm.Derivative * reach;
        }

        /// <summary>
        /// Pairs the usable observed hands with tracks: as many pairs as possible, each within AssociateMetres of
        /// the track's predicted palm, and of those the pairing with the least total distance.
        /// </summary>
        void Associate(int n, double captureTime, in ObservedHand a, in ObservedHand b)
        {
            for (int i = 0; i < 2; i++)
            {
                _match[i] = -1;
                _distance[i] = 0f;
            }
            for (int i = 0; i < n; i++)
            {
                float3 palm = _from[i] == 0 ? a.PalmCentre : b.PalmCentre;
                for (int j = 0; j < TrackCount; j++)
                {
                    Track t = _tracks[j];
                    _cost[i * TrackCount + j] = t.Alive ? math.distance(palm, PredictedPalm(t, captureTime)) : float.PositiveInfinity;
                }
            }

            int bestMatches = -1;
            float bestTotal = float.PositiveInfinity;
            // Every way to give observation 0 and observation 1 a track of their own, or none.
            for (int c0 = -1; c0 < TrackCount; c0++)
            {
                if (c0 >= 0 && n < 1) break;
                for (int c1 = -1; c1 < TrackCount; c1++)
                {
                    if (c1 >= 0 && n < 2) break;
                    if (c0 >= 0 && c0 == c1) continue;
                    int matches = 0;
                    float total = 0f;
                    if (c0 >= 0)
                    {
                        float d = _cost[c0];
                        if (!(d <= HandsConstants.AssociateMetres)) continue;
                        matches++;
                        total += d;
                    }
                    if (c1 >= 0)
                    {
                        float d = _cost[TrackCount + c1];
                        if (!(d <= HandsConstants.AssociateMetres)) continue;
                        matches++;
                        total += d;
                    }
                    if (matches > bestMatches || (matches == bestMatches && total < bestTotal))
                    {
                        bestMatches = matches;
                        bestTotal = total;
                        _match[0] = c0;
                        _match[1] = c1;
                    }
                }
            }
            for (int i = 0; i < n; i++)
                _distance[i] = _match[i] >= 0 ? _cost[i * TrackCount + _match[i]] : 0f;
        }

        /// <summary>
        /// A slot for a new hand: a free one, else one this result did not match: the hand on the side the new
        /// hand's label names, which it is most likely to be, or failing that the one gone longest unheard of.
        /// </summary>
        Track Acquire(int sideHint)
        {
            Track oldest = null, sameSide = null;
            for (int i = 0; i < TrackCount; i++)
            {
                Track t = _tracks[i];
                if (!t.Alive) return t;
                if (t.Seen) continue;
                if (sideHint >= 0 && t.Side == sideHint) sameSide = t;
                if (oldest == null || t.CaptureTime < oldest.CaptureTime) oldest = t;
            }
            return sameSide != null ? sameSide : oldest;
        }

        void Start(Track t)
        {
            t.Alive = true;
            t.Side = -1;
            t.Seen = false;
            t.PendingStart = true;
            t.PendingFresh = false;
            t.Lost = false;
            t.LostStamped = false;
            t.GoodDetections = 0;
            t.ClearVotes();
            t.ResetFilters();
        }

        /// <summary>Takes one detection into a track: the filters, the labels, the counters and the last-seen fields.</summary>
        void Take(Track t, in ObservedHand hand, quaternion frame, double captureTime, bool restart)
        {
            if (restart) t.ResetFilters();

            // The palm in the room, its orientation in the room, and every joint in the palm's own frame.
            float3 centre = hand.PalmCentre;
            t.Palm.Filter(centre, captureTime);
            t.Rotation.Filter(frame, captureTime);
            JointSet joints = hand.Joints;
            quaternion inverse = math.conjugate(frame);
            for (int j = 0; j < HandJoints.Count; j++)
                t.Joints[j].Filter(math.mul(inverse, joints[j] - centre), captureTime);

            t.CaptureTime = captureTime;
            float confidence = hand.Confidence;
            if (!(confidence >= 0f)) confidence = 0f;
            else if (confidence > 1f) confidence = 1f;
            t.Confidence = confidence;
            t.EdgeMargin = hand.EdgeMargin;
            t.PinchRatio = hand.PinchRatio;
            t.PalmDistance = hand.PalmDistance;
            t.PalmFromDepth = hand.PalmFromDepth;
            t.PalmPixelX = hand.PalmPixel.x;

            // A detection below the enter confidence starts the count again; a result that missed the hand does
            // not, so that the hold of a brief loss is not mistaken for a new hand.
            if (confidence >= HandsConstants.EnterConfidence) t.GoodDetections = math.min(t.GoodDetections + 1, GoodDetectionsCap);
            else t.GoodDetections = 0;
            if (hand.SideHint == 0 || hand.SideHint == 1) t.AddVote(hand.SideHint);

            t.Seen = true;
            t.PendingFresh = true;
            t.Lost = false;
            t.LostStamped = false;
        }

        // ---- Left and right ----

        static int Wanted(Track t)
        {
            if (t.LeftVotes > t.RightVotes) return 0;
            if (t.RightVotes > t.LeftVotes) return 1;
            return -1;
        }

        /// <summary>
        /// Gives each live track a side. A track wants the side most of its labels name; one with a tie or no
        /// labels keeps the side it has, or takes the other side from its neighbour, or, alone and new, the half of
        /// the view its palm is in. Two tracks never get the same side: the one with the weaker vote takes the
        /// other, a tie going to the side's present holder and then to the position in the picture. A hand that
        /// is lost has no vote against one that is being seen: it is given up rather than moved to the other side,
        /// because a hand that appears with its side's label is that hand, back from farther than it could be matched.
        /// </summary>
        void ResolveSides()
        {
            Track first = null, second = null;
            for (int i = 0; i < TrackCount; i++)
            {
                Track t = _tracks[i];
                if (!t.Alive) continue;
                if (first == null) first = t; else second = t;
            }
            if (first == null) return;
            if (second == null)
            {
                int wanted = Wanted(first);
                if (wanted >= 0) first.Side = wanted;
                else if (first.Side < 0) first.Side = first.PalmPixelX < ViewCentreX ? 0 : 1;
                return;
            }

            int wantFirst = Wanted(first), wantSecond = Wanted(second);
            int claimFirst = wantFirst >= 0 ? wantFirst : first.Side;
            int claimSecond = wantSecond >= 0 ? wantSecond : second.Side;
            if (claimFirst >= 0 && claimFirst == claimSecond)
            {
                int strengthFirst = Math.Abs(first.LeftVotes - first.RightVotes);
                int strengthSecond = Math.Abs(second.LeftVotes - second.RightVotes);
                bool firstWins;
                if (first.Lost != second.Lost) firstWins = second.Lost;
                else if (strengthFirst != strengthSecond) firstWins = strengthFirst > strengthSecond;
                else if ((first.Side == claimFirst) != (second.Side == claimSecond)) firstWins = first.Side == claimFirst;
                else firstWins = (first.PalmPixelX <= second.PalmPixelX) == (claimFirst == 0);
                Track winner = firstWins ? first : second;
                Track loser = firstWins ? second : first;
                winner.Side = claimFirst;
                if (loser.Lost && !winner.Lost) loser.Alive = false;
                else loser.Side = 1 - claimFirst;
            }
            else if (claimFirst >= 0 && claimSecond >= 0)
            {
                first.Side = claimFirst;
                second.Side = claimSecond;
            }
            else if (claimFirst >= 0)
            {
                first.Side = claimFirst;
                second.Side = 1 - claimFirst;
            }
            else if (claimSecond >= 0)
            {
                second.Side = claimSecond;
                first.Side = 1 - claimSecond;
            }
            else
            {
                first.Side = first.PalmPixelX <= second.PalmPixelX ? 0 : 1;
                second.Side = 1 - first.Side;
            }
        }

        // ---- Advance ----

        /// <summary>
        /// The prediction's share of the filtered velocity: nothing for a hand at rest, all of it for a hand that
        /// is moving, see <see cref="PredictSpeedFloor"/>.
        /// </summary>
        static float PredictionGain(float speed)
        {
            float square = speed * speed;
            return square / (square + PredictSpeedFloor * PredictSpeedFloor);
        }

        /// <summary>
        /// Writes one track's hand for the frame at now. Live: the filtered pose with the palm moved along its
        /// velocity by the pose's age, at most the prediction limit. Lost: the same pose, the palm carried on from
        /// where it was drawn at the moment of loss by a velocity that decays with CoastDecaySeconds; the
        /// orientation and the joints in the palm's frame are held.
        /// </summary>
        void Compose(Track t, double now, bool fresh, float lostSeconds, ref TrackedHand hand)
        {
            float age = (float)math.max(0.0, now - t.CaptureTime);
            float3 velocity = t.Palm.Derivative;
            float gain = PredictionGain(math.length(velocity));
            float3 offset;
            if (t.Lost)
            {
                float decay = math.exp(-lostSeconds / HandsConstants.CoastDecaySeconds);
                offset = velocity * (gain * (t.PredictAtLoss + HandsConstants.CoastDecaySeconds * (1f - decay)));
                velocity *= decay;
            }
            else
            {
                offset = velocity * (gain * math.min(age, PredictionLimitSeconds));
            }

            float3 centre = t.Palm.Value + offset;
            float3x3 turn = new float3x3(t.Rotation.Value);
            hand.Tracked = true;
            hand.Fresh = fresh;
            for (int j = 0; j < HandJoints.Count; j++)
                hand.World[j] = centre + math.mul(turn, t.Joints[j].Value);
            hand.Confidence = t.Confidence;
            hand.AgeSeconds = age;
            hand.LostSeconds = lostSeconds;
            hand.TrackedSeconds = (float)math.max(0.0, now - t.StartNow);
            hand.GoodDetections = t.GoodDetections;
            hand.PalmCentre = centre;
            hand.PalmNormal = turn.c1;
            hand.PalmVelocity = velocity;
            hand.Predicted = offset;
            hand.EdgeMargin = t.EdgeMargin;
            hand.PinchRatio = t.PinchRatio;
            hand.PalmDistance = t.PalmDistance;
            hand.PalmFromDepth = t.PalmFromDepth;
        }
    }
}

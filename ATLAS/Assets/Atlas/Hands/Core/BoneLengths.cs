using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>
    /// The length of each bone of this walker's hand (decision R11 of the hands brief). The detector's 21 points
    /// are not anatomical joint centres, so the distances between them are not the template's times one scale:
    /// they are learnt from what is seen, and until a bone is learnt it is the template's times the hand's scale.
    ///
    /// A bone in a picture can only look shorter than it is. So a finger is learnt only from the sightings in
    /// which it looks about as long as it ever has: flat in the palm's plane, with the palm square to the camera
    /// (the caller's part). The length of a bone is the median of those sightings, which a few bad detections
    /// cannot move. An estimate that is a little short is the safe side: the lift then lays the bone flat. One
    /// that is too long turns straight into depth.
    /// </summary>
    public sealed class BoneLengths
    {
        /// <summary>Sightings kept per bone.</summary>
        internal const int Window = 90;
        /// <summary>Sightings a bone needs before it is used: as many as the hand's scale needs measurements.</summary>
        internal const int MinSightings = HandsConstants.ScaleSamples;
        /// <summary>A finger is learnt from a sighting when it looks at least this share of the longest it has looked.</summary>
        internal const float FullStretchShare = 0.93f;
        /// <summary>"The longest it has looked" is the length this share of its last sightings stay under: a few bad detections do not set it.</summary>
        internal const float LongestShare = 0.9f;
        /// <summary>Until a finger is learnt, the longest it has looked is never taken to be under this share of the template's finger at the hand's scale.</summary>
        internal const float UnlearntFloorShare = 0.80f;
        /// <summary>A sighting outside these shares of the template's bone is a bad detection.</summary>
        internal const float SaneMin = 0.4f, SaneMax = 2.5f;

        readonly RunningMedian[] _sightings = new RunningMedian[HandJoints.Count];
        readonly float[] _learnt = new float[HandJoints.Count];              // metres; 0 until learnt
        readonly RunningMedian[] _looks = new RunningMedian[HandJoints.FingerCount];   // knuckle to tip as seen, metres
        readonly float[] _best = new float[HandJoints.FingerCount];          // the longest the learnt finger has been
        float _scale = 1f;

        /// <summary>Every bone the template's, at scale 1.</summary>
        public BoneLengths()
        {
            for (int joint = 1; joint < HandJoints.Count; joint++) _sightings[joint] = new RunningMedian(Window);
            for (int finger = 0; finger < HandJoints.FingerCount; finger++) _looks[finger] = new RunningMedian(Window);
        }

        /// <summary>Forgets the walker: every bone is the template's again, at scale 1.</summary>
        public void Reset()
        {
            for (int joint = 1; joint < HandJoints.Count; joint++)
            {
                _sightings[joint].Clear();
                _learnt[joint] = 0f;
            }
            for (int finger = 0; finger < HandJoints.FingerCount; finger++)
            {
                _looks[finger].Clear();
                _best[finger] = 0f;
            }
            _scale = 1f;
        }

        /// <summary>
        /// Metres from a joint to the joint it hangs from: what was learnt, or the template's bone times the
        /// hand's scale until enough has been seen. 0 for the wrist.
        /// </summary>
        public float Length(int joint)
        {
            if (joint <= 0 || joint >= HandJoints.Count) return 0f;
            float learnt = _learnt[joint];
            return learnt > 0f ? learnt : HandModel.BoneLength(joint) * _scale;
        }

        /// <summary>True once the bone's length comes from this walker's hand and not from the template.</summary>
        internal bool Learnt(int joint)
        {
            return joint > 0 && joint < HandJoints.Count && _learnt[joint] > 0f;
        }

        /// <summary>
        /// One sighting of a hand whose palm is square to the camera and seen by LiDAR. sighting holds every joint
        /// where its ray meets the palm's plane, so a bone lying in that plane is seen at its full length and
        /// any other bone shorter. Bit j of seenJoints is set for each joint that may be used: confident in the
        /// picture, and not shown by LiDAR to be off the plane. scale is the hand's scale, which the bones not
        /// yet learnt take from now on. Allocates nothing.
        /// </summary>
        public void Observe(in JointSet sighting, int seenJoints, float scale)
        {
            if (scale > 0f) _scale = scale;   // false for NaN
            JointSet flat = sighting;         // one copy: its indexer is not readonly, so each read through `in` would copy it again

            // The palm's bones, wrist to each finger's base, lie in the palm's plane: every sighting is a full one.
            if ((seenJoints & 1) != 0)
            {
                float3 wrist = flat[0];
                for (int finger = 0; finger < HandJoints.FingerCount; finger++)
                {
                    int joint = HandJoints.Base(finger);
                    if ((seenJoints >> joint & 1) != 0) Add(joint, math.distance(flat[joint], wrist));
                }
            }

            for (int finger = 0; finger < HandJoints.FingerCount; finger++)
            {
                int first = HandJoints.Base(finger);
                int all = 0xF << first;
                if ((seenJoints & all) != all) continue;

                float a = math.distance(flat[first + 1], flat[first]);
                float b = math.distance(flat[first + 2], flat[first + 1]);
                float c = math.distance(flat[first + 3], flat[first + 2]);
                float seen = a + b + c;
                float template = HandModel.BoneLength(first + 1) + HandModel.BoneLength(first + 2) + HandModel.BoneLength(first + 3);
                if (!(seen > SaneMin * template) || !(seen < SaneMax * template)) continue;   // also false for NaN

                // The longest the finger has looked: at least what was learnt of it, and before that at least
                // most of the template's, so that a finger first seen curled is not taken for a short one.
                RunningMedian looks = _looks[finger];
                looks.Add(seen);
                float floor = _best[finger] > 0f ? _best[finger] : UnlearntFloorShare * template * _scale;
                if (seen < FullStretchShare * math.max(looks.Quantile(LongestShare), floor)) continue;

                Add(first + 1, a);
                Add(first + 2, b);
                Add(first + 3, c);
                float learnt = _learnt[first + 1] + _learnt[first + 2] + _learnt[first + 3];
                if (Learnt(first + 1) && Learnt(first + 2) && Learnt(first + 3) && learnt > _best[finger]) _best[finger] = learnt;
            }
        }

        void Add(int joint, float metres)
        {
            float template = HandModel.BoneLength(joint);
            if (!(metres > SaneMin * template) || !(metres < SaneMax * template)) return;   // also false for NaN
            RunningMedian sightings = _sightings[joint];
            sightings.Add(metres);
            if (sightings.Count >= MinSightings) _learnt[joint] = sightings.Median;
        }
    }

    /// <summary>
    /// The median, or any other quantile, of the last values given to it, at most its capacity: a ring of the
    /// values in the order they came and the same values kept sorted, so that adding one costs a shift and
    /// reading costs nothing. Allocates only when it is made.
    /// </summary>
    internal sealed class RunningMedian
    {
        readonly float[] _ring, _sorted;
        int _count, _next;

        /// <summary>Keeps the last capacity values.</summary>
        public RunningMedian(int capacity)
        {
            _ring = new float[math.max(capacity, 1)];
            _sorted = new float[_ring.Length];
        }

        /// <summary>Values kept so far, at most the capacity.</summary>
        public int Count { get { return _count; } }

        /// <summary>The middle value, or 0 when there is none yet.</summary>
        public float Median
        {
            get
            {
                if (_count == 0) return 0f;
                return (_count & 1) == 1 ? _sorted[_count / 2] : 0.5f * (_sorted[_count / 2 - 1] + _sorted[_count / 2]);
            }
        }

        /// <summary>The value that the given share of the kept values do not exceed (0.5 is close to the median), or 0 when there is none yet.</summary>
        public float Quantile(float share)
        {
            if (_count == 0) return 0f;
            return _sorted[math.clamp((int)math.ceil(share * _count) - 1, 0, _count - 1)];
        }

        /// <summary>Forgets every value.</summary>
        public void Clear()
        {
            _count = 0;
            _next = 0;
        }

        /// <summary>Adds a value, dropping the oldest once full. A value that is not a finite number is ignored.</summary>
        public void Add(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            if (_count == _ring.Length)
            {
                // The oldest value is the one the ring is about to overwrite; take it out of the sorted copy.
                float oldest = _ring[_next];
                int at = 0;
                while (at < _count - 1 && _sorted[at] != oldest) at++;
                for (int i = at; i < _count - 1; i++) _sorted[i] = _sorted[i + 1];
                _count--;
            }
            _ring[_next] = value;
            _next = (_next + 1) % _ring.Length;

            int place = _count;
            while (place > 0 && _sorted[place - 1] > value)
            {
                _sorted[place] = _sorted[place - 1];
                place--;
            }
            _sorted[place] = value;
            _count++;
        }
    }
}

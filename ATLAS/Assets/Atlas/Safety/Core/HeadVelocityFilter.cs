using Unity.Mathematics;

namespace Atlas.Safety
{
    /// <summary>
    /// Head velocity on the floor from the raw pose, averaged over the last 0.2 s (Master Plan section 4.10).
    /// A step of more than 0.2 m in one frame is a pose jump: the new position is taken at once and the velocity
    /// is reset to zero, so a relocalisation never produces a look-ahead spike.
    /// </summary>
    public unsafe struct HeadVelocityFilter
    {
        const int Capacity = 16;
        // A window of exactly 0.2 s must still hold the sample taken 12 frames ago at 60 Hz.
        const double WindowSlack = 1e-3;

        fixed float _x[Capacity];
        fixed float _z[Capacity];
        fixed double _t[Capacity];
        int _next;
        int _count;
        float2 _position;
        float2 _velocity;

        /// <summary>Velocity on the floor (x, z), metres per second, room frame.</summary>
        public float2 Velocity => _velocity;
        /// <summary>Last floor position taken, metres.</summary>
        public float2 Position => _position;
        /// <summary>True when the last update was a pose jump.</summary>
        public bool Jumped { get; private set; }

        /// <summary>Forgets everything; the next update only takes the position.</summary>
        public void Reset()
        {
            _count = 0;
            _next = 0;
            _velocity = float2.zero;
            Jumped = false;
        }

        /// <summary>Feeds one raw head position on the floor (x, z). Call only with a pose from Normal tracking.</summary>
        public void Update(float2 position, double time)
        {
            Jumped = false;
            // Written so that a NaN position counts as a jump.
            if (_count > 0 && !(math.lengthsq(position - _position) <= BumpConstants.JumpDistance * BumpConstants.JumpDistance))
            {
                Jumped = true;
                _count = 0;
            }

            // Velocity against the oldest sample that is still inside the window.
            _velocity = float2.zero;
            for (int k = _count; k >= 1; k--)
            {
                int i = (_next - k + 2 * Capacity) % Capacity;
                double age = time - _t[i];
                if (age > BumpConstants.VelocityWindow + WindowSlack) continue;
                if (age > 0.0) _velocity = (position - new float2(_x[i], _z[i])) / (float)age;
                break;
            }

            _x[_next] = position.x;
            _z[_next] = position.y;
            _t[_next] = time;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
            _position = position;
        }
    }
}

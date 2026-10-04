using Atlas.Link;
using Unity.Mathematics;

namespace Atlas.App
{
    /// <summary>
    /// The walk input the Station sends from its Map page (<c>OperatorCmd walk</c>). The command router writes
    /// it; the input step adds it to the walker's own input once per frame. An input lasts
    /// <see cref="OperatorCommand.WalkHoldSeconds"/> unless a newer one replaces it, so the walker stops by
    /// itself when the Console goes quiet or the link drops. Engine-free; main thread only.
    /// </summary>
    public sealed class RemoteWalkInput
    {
        float2 _move;
        float _turn;
        float _speed = OperatorCommand.WalkDefaultSpeed;
        float _left;

        /// <summary>An input is in force: it has not run out and asks for movement or a turn.</summary>
        public bool Active { get { return _left > 0f; } }
        /// <summary>Right and forward, -1 to 1 each, of the input in force; zero when none is.</summary>
        public float2 Move { get { return Active ? _move : float2.zero; } }
        /// <summary>Degrees per second, positive right, of the input in force; zero when none is.</summary>
        public float Turn { get { return Active ? _turn : 0f; } }

        /// <summary>A new input, clamped as the link clamps it. A still input stops the walker at once.</summary>
        public void Set(float moveX, float moveY, float turnDegreesPerSecond, float speed)
        {
            OperatorCommand c = OperatorCommand.Walk(moveX, moveY, turnDegreesPerSecond, speed);
            _move = new float2(c.MoveX, c.MoveY);
            _turn = c.Turn;
            _speed = c.Speed;
            _left = c.IsStill ? 0f : OperatorCommand.WalkHoldSeconds;
        }

        public void Stop()
        {
            _left = 0f;
        }

        /// <summary>
        /// This frame's share of the input in force: the move, the yaw increment in degrees (turn times
        /// deltaTime) and the speed. Counts the hold down by deltaTime. False, with zeros, when no input is in force.
        /// </summary>
        public bool Take(float deltaTime, out float2 move, out float yawDegrees, out float speed)
        {
            if (_left <= 0f)
            {
                move = float2.zero;
                yawDegrees = 0f;
                speed = 0f;
                return false;
            }
            float dt = deltaTime > 0f ? deltaTime : 0f;
            move = _move;
            yawDegrees = _turn * dt;
            speed = _speed;
            _left -= dt;
            return true;
        }
    }
}

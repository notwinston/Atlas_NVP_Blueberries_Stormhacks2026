namespace Atlas.Input
{
    /// <summary>Keyboard and mouse for the Editor walker (README F9). All zero on a phone.</summary>
    public struct WalkerInput
    {
        /// <summary>x right, y forward, -1..1: A/D and S/W.</summary>
        public UnityEngine.Vector2 Move;
        /// <summary>Yaw and pitch increments this frame: mouse delta * 0.15 while the right button is held.</summary>
        public UnityEngine.Vector2 LookDegrees;
        /// <summary>m/s: 0.7; 1.0 with Left Shift; 0.3 with Left Ctrl.</summary>
        public float Speed;
    }
}

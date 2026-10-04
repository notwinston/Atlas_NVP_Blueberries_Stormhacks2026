namespace Atlas.Safety
{
    /// <summary>No haptic device: Available is false and nothing happens.</summary>
    public sealed class NullHaptics : IHaptics
    {
        public bool Available { get { return false; } }

        public void Pulse(float strength)
        {
        }

        public void SetPattern(HapticPattern pattern)
        {
        }
    }
}

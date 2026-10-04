namespace Atlas.World
{
    /// <summary>ARKit's three tracking states. The zero value is the unsafe one on purpose: a default pose is not tracked.</summary>
    public enum TrackingState : byte { NotTracking = 0, Limited = 1, Normal = 2 }

    public static class TrackingStates
    {
        /// <summary>Bits 0-1 of State.flags and the Telemetry tracking byte (Appendix B.1): 0 normal, 1 limited, 2 lost.</summary>
        public static byte ToWire(TrackingState state)
        {
            int s = (int)state;
            return (byte)(s > 2 ? 2 : 2 - s);
        }

        /// <summary>From the wire value. Values above 2 give NotTracking.</summary>
        public static TrackingState FromWire(byte wire)
        {
            return wire > 2 ? TrackingState.NotTracking : (TrackingState)(2 - wire);
        }
    }
}

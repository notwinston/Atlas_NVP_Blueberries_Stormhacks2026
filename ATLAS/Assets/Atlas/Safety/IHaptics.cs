namespace Atlas.Safety
{
    /// <summary>The pulse rhythm of the warning cue (Master Plan section 4.10, outputs table).</summary>
    public enum HapticPattern : byte
    {
        Off = 0,
        /// <summary>Two pulses per second: WARN and the rescan icon.</summary>
        Warn = 1,
        /// <summary>Six pulses per second, continuous: STRONG and tracking lost.</summary>
        Strong = 2
    }

    /// <summary>One haptic channel of the warning cue: the phone's own engine, or a gamepad's rumble.</summary>
    public interface IHaptics
    {
        /// <summary>The channel can be felt now.</summary>
        bool Available { get; }
        /// <summary>One pulse now. strength 0..1. BumpCue times the pulses: 2 per second for Warn, 6 for Strong.</summary>
        void Pulse(float strength);
        /// <summary>
        /// Tells a device that keeps its own pattern (a rumble motor) what is running. Others ignore it.
        /// BumpCue calls it every frame, so an implementation acts only on a change.
        /// </summary>
        void SetPattern(HapticPattern pattern);
    }
}

#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Atlas.Safety
{
    /// <summary>
    /// The phone's own haptic engine, felt through the headset (Master Plan section 4.10), through
    /// Assets/Plugins/iOS/AtlasHaptics.mm. In the Editor and off iOS every call does nothing and
    /// <see cref="Available"/> is false (contracts F9).
    /// </summary>
    public sealed class IosHaptics : IHaptics
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void AtlasHapticPrepare();
        [DllImport("__Internal")] static extern void AtlasHapticPulse(float intensity);
        [DllImport("__Internal")] static extern void AtlasHapticStop();

        /// <summary>Starts the engine now, so that the first pulse of a cue is not delayed by its start-up.</summary>
        public IosHaptics()
        {
            AtlasHapticPrepare();
        }

        public bool Available { get { return true; } }

        public void Pulse(float strength)
        {
            AtlasHapticPulse(strength < 0f ? 0f : (strength > 1f ? 1f : strength));
        }

        /// <summary>Stops the engine. The next pulse starts it again.</summary>
        public void Stop()
        {
            AtlasHapticStop();
        }
#else
        public bool Available { get { return false; } }

        public void Pulse(float strength)
        {
        }

        /// <summary>Stops the engine. The next pulse starts it again.</summary>
        public void Stop()
        {
        }
#endif

        /// <summary>The phone has no pattern of its own: BumpCue times every pulse.</summary>
        public void SetPattern(HapticPattern pattern)
        {
        }
    }
}

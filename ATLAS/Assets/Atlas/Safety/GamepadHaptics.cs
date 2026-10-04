using UnityEngine;
using UnityEngine.InputSystem;

namespace Atlas.Safety
{
    /// <summary>
    /// The gamepad's rumble as a second channel of the warning cue (Master Plan section 4.10: "the gamepad's
    /// rumble when a gamepad is in hand"). A rumble motor is too slow for single pulses, so it keeps its own
    /// pattern: a low rumble for WARN, a hard one for STRONG and tracking lost, off otherwise.
    /// The Input System has no rumble on iOS in this Unity version, so on the phone this channel reports that
    /// it is not available and sends nothing (open item 9); in the Editor and on the desktop it drives the pad.
    /// </summary>
    public sealed class GamepadHaptics : IHaptics
    {
        const float WarnLow = 0.25f, WarnHigh = 0.35f;
        const float StrongLow = 0.8f, StrongHigh = 1.0f;

        readonly bool _supported = Application.platform != RuntimePlatform.IPhonePlayer;
        Gamepad _pad;                               // the pad the motors were last set on
        HapticPattern _sent = HapticPattern.Off;

        public bool Available { get { return _supported && Gamepad.current != null; } }

        /// <summary>Nothing: the motors follow <see cref="SetPattern"/>.</summary>
        public void Pulse(float strength)
        {
        }

        /// <summary>Called every frame by BumpCue. Sends a command only when the pattern or the pad has changed.</summary>
        public void SetPattern(HapticPattern pattern)
        {
            if (!_supported) return;
            Gamepad pad = Gamepad.current;
            if (ReferenceEquals(pad, _pad) && pattern == _sent) return;

            // The walker picked up another pad: the one left behind must not keep rumbling.
            if (_pad != null && !ReferenceEquals(pad, _pad) && _sent != HapticPattern.Off && _pad.added)
                _pad.SetMotorSpeeds(0f, 0f);

            _pad = pad;
            _sent = pattern;
            if (pad == null) return;
            switch (pattern)
            {
                case HapticPattern.Warn: pad.SetMotorSpeeds(WarnLow, WarnHigh); break;
                case HapticPattern.Strong: pad.SetMotorSpeeds(StrongLow, StrongHigh); break;
                default: pad.SetMotorSpeeds(0f, 0f); break;
            }
        }
    }
}

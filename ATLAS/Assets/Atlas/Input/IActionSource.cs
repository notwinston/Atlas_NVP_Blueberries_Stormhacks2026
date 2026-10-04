// PLACEHOLDER (lane C, P1-C10): contract surface only; replaced by lane C's implementation
using UnityEngine;
using UnityEngine.InputSystem;

namespace Atlas.Input
{
    // Master Plan §0.5 rule 5, §10.1.3 (placeholder choice PH-28).
    public interface IActionSource
    {
        bool Triggered(AtlasAction action);   // true on the frame the action fires: gamepad, lever or gaze dwell
        Vector2 Stick { get; }                // left stick, -1..1 ("stick + A", Master Plan §10.2.2)
    }

    // Placeholder source: Gamepad.current only. ReturnHome is "B held" in the plan; the hold time is lane C's.
    public sealed class GamepadActionSource : IActionSource
    {
        public bool Triggered(AtlasAction action)
        {
            var pad = Gamepad.current;
            if (pad == null) return false;
            switch (action)
            {
                case AtlasAction.Confirm: return pad.buttonSouth.wasPressedThisFrame;
                case AtlasAction.DialPrev: return pad.dpad.left.wasPressedThisFrame;
                case AtlasAction.DialNext: return pad.dpad.right.wasPressedThisFrame;
                case AtlasAction.ModeToggle: return pad.buttonNorth.wasPressedThisFrame;
                case AtlasAction.ReturnHome: return pad.buttonEast.isPressed;
                default: return false;
            }
        }

        public Vector2 Stick => Gamepad.current != null ? Gamepad.current.leftStick.ReadValue() : Vector2.zero;
    }
}

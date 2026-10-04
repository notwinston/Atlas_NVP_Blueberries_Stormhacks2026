using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Atlas.Scan
{
    /// <summary>
    /// Keyboard and mouse for the Editor walker when the application does not feed it: W, A, S, D walk, the
    /// mouse turns the head while its right button is held, Left Shift walks fast and Left Ctrl slowly.
    /// On a phone there is no keyboard and no mouse, and nothing is fed.
    /// </summary>
    public static class KeyboardWalkerInput
    {
        /// <summary>Metres per second: an unhurried walk.</summary>
        public const float WalkSpeed = 1.0f;
        public const float FastSpeed = 2.0f;
        public const float SlowSpeed = 0.3f;
        /// <summary>Degrees of head turn per pixel of mouse travel.</summary>
        public const float DegreesPerPixel = 0.15f;

        /// <summary>Reads the devices and hands the result to the walker for its next sample. Allocates nothing.</summary>
        public static void Feed(KeyboardWalkerPoseSource walker)
        {
            if (walker == null) return;
            float2 move = float2.zero;
            float speed = WalkSpeed;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed) move.y += 1f;
                if (keyboard.sKey.isPressed) move.y -= 1f;
                if (keyboard.dKey.isPressed) move.x += 1f;
                if (keyboard.aKey.isPressed) move.x -= 1f;
                if (keyboard.leftShiftKey.isPressed) speed = FastSpeed;
                else if (keyboard.leftCtrlKey.isPressed) speed = SlowSpeed;
            }
            float2 look = float2.zero;
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                look = new float2(delta.x, delta.y) * DegreesPerPixel;
            }
            walker.Feed(move, look, speed);
        }
    }
}

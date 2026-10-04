using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Atlas.Input
{
    /// <summary>
    /// The one action map, built in code from the twin table (no .inputactions asset; Master Plan task P1-C10).
    /// One button action per <see cref="AtlasAction"/>, with its gamepad binding and its Editor keys, plus the
    /// sticks, the triggers, the chord modifier, the lever and the Editor walker. Polled once per frame.
    /// </summary>
    public sealed class AtlasActionMap : IDisposable
    {
        private const float LookDegreesPerPixel = 0.15f;
        private const float WalkSpeed = 0.7f;
        private const float WalkSpeedFast = 1.0f;
        private const float WalkSpeedSlow = 0.3f;

        private readonly InputActionMap _map = new InputActionMap("Atlas");
        private readonly InputAction[] _actions = new InputAction[InputTwins.ActionCount + 1];
        private readonly InputAction _select;
        private readonly InputAction _leftStick;
        private readonly InputAction _rightStick;
        private readonly InputAction _leftTrigger;
        private readonly InputAction _rightTrigger;
        private readonly InputAction _twistKeys;
        private readonly InputAction _lever;
        private readonly InputAction _pointerPosition;
        private readonly InputAction _walkMove;
        private readonly InputAction _walkLook;
        private readonly InputAction _walkLookHold;
        private readonly InputAction _walkFast;
        private readonly InputAction _walkSlow;
        private bool _disposed;

        public AtlasActionMap()
        {
            for (int i = 1; i <= InputTwins.ActionCount; i++)
            {
                ref readonly ActionTwin twin = ref InputTwins.Of((AtlasAction)i);
                InputAction action = _map.AddAction(twin.Action.ToString(), InputActionType.Button);
                Bind(action, twin.GamepadPath);
                Bind(action, twin.GamepadAltPath);
                Bind(action, twin.KeyPath);
                Bind(action, twin.KeyAltPath);
                _actions[i] = action;
            }

            _select = _map.AddAction("ChordModifier", InputActionType.Button, InputTwins.ChordModifierPath);

            ref readonly AxisTwin leftX = ref InputTwins.Of(AtlasAxis.LeftX);
            ref readonly AxisTwin leftY = ref InputTwins.Of(AtlasAxis.LeftY);
            _leftStick = _map.AddAction("LeftStick", InputActionType.Value, "<Gamepad>/leftStick", expectedControlLayout: "Vector2");
            _leftStick.AddCompositeBinding("2DVector")
                .With("Up", leftY.KeyPositivePath).With("Down", leftY.KeyNegativePath)
                .With("Left", leftX.KeyNegativePath).With("Right", leftX.KeyPositivePath);
            _rightStick = _map.AddAction("RightStick", InputActionType.Value, "<Gamepad>/rightStick", expectedControlLayout: "Vector2");

            // The triggers are read apart and subtracted: a 1DAxis composite would return 0 when both are pulled.
            ref readonly AxisTwin twist = ref InputTwins.Of(AtlasAxis.Twist);
            _leftTrigger = _map.AddAction("TwistNegative", InputActionType.Value, twist.GamepadNegativePath, expectedControlLayout: "Axis");
            _rightTrigger = _map.AddAction("TwistPositive", InputActionType.Value, twist.GamepadPositivePath, expectedControlLayout: "Axis");
            _twistKeys = _map.AddAction("TwistKeys", InputActionType.Value, expectedControlLayout: "Axis");
            _twistKeys.AddCompositeBinding("1DAxis")
                .With("Negative", twist.KeyNegativePath).With("Positive", twist.KeyPositivePath);

            // The lever touches the screen; the touch beginning is what counts (a held lever is not a "tap").
            _lever = _map.AddAction("Lever", InputActionType.Button, InputTwins.LeverPath);
            _lever.AddBinding(InputTwins.LeverEditorPath);
            _pointerPosition = _map.AddAction("PointerPosition", InputActionType.Value, "<Touchscreen>/primaryTouch/position", expectedControlLayout: "Vector2");
            _pointerPosition.AddBinding("<Mouse>/position");

            // Editor walker (README F9).
            _walkMove = _map.AddAction("WalkMove", InputActionType.Value, expectedControlLayout: "Vector2");
            _walkMove.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _walkLook = _map.AddAction("WalkLook", InputActionType.Value, "<Mouse>/delta", expectedControlLayout: "Vector2");
            _walkLookHold = _map.AddAction("WalkLookHold", InputActionType.Button, "<Mouse>/rightButton");
            _walkFast = _map.AddAction("WalkFast", InputActionType.Button, "<Keyboard>/leftShift");
            _walkSlow = _map.AddAction("WalkSlow", InputActionType.Button, "<Keyboard>/leftCtrl");
        }

        /// <summary>The Input System map, for inspection and tests.</summary>
        public InputActionMap Map => _map;

        /// <summary>The button action of an AtlasAction; null for None.</summary>
        public InputAction ActionFor(AtlasAction action)
        {
            int i = (int)action;
            return i >= 1 && i <= InputTwins.ActionCount ? _actions[i] : null;
        }

        public void Enable() => _map.Enable();
        public void Disable() => _map.Disable();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _map.Disable();
            _map.Dispose();
        }

        /// <summary>Reads the devices into the frame's raw state. No allocation; all zero with no device.</summary>
        public void Sample(ref RawInput raw, ref WalkerInput walker)
        {
            uint pad = 0u;
            uint key = 0u;
            for (int i = 1; i <= InputTwins.ActionCount; i++)
            {
                InputAction action = _actions[i];
                if (!action.IsPressed() && !action.WasPressedThisFrame()) continue;
                InputControl control = action.activeControl;
                if (control != null && control.device is Keyboard) key |= 1u << i;
                else pad |= 1u << i;
            }
            raw.PadDown = pad;
            raw.KeyDown = key;
            raw.SelectDown = _select.IsPressed();

            Vector2 left = _leftStick.ReadValue<Vector2>();
            Vector2 right = _rightStick.ReadValue<Vector2>();
            raw.LeftStick = new float2(left.x, left.y);
            raw.RightStick = new float2(right.x, right.y);
            raw.Twist = math.clamp(_rightTrigger.ReadValue<float>() - _leftTrigger.ReadValue<float>() + _twistKeys.ReadValue<float>(), -1f, 1f);

            raw.PointerDown = _lever.IsPressed() || _lever.WasPressedThisFrame();
            Vector2 pointer = _pointerPosition.ReadValue<Vector2>();
            raw.PointerPosition = new float2(pointer.x, pointer.y);
            raw.GamepadPresent = Gamepad.current != null;

            walker.Move = _walkMove.ReadValue<Vector2>();
            walker.LookDegrees = _walkLookHold.IsPressed() ? _walkLook.ReadValue<Vector2>() * LookDegreesPerPixel : Vector2.zero;
            walker.Speed = _walkFast.IsPressed() ? WalkSpeedFast : _walkSlow.IsPressed() ? WalkSpeedSlow : WalkSpeed;
        }

        private static void Bind(InputAction action, string path)
        {
            if (!string.IsNullOrEmpty(path)) action.AddBinding(path);
        }
    }
}

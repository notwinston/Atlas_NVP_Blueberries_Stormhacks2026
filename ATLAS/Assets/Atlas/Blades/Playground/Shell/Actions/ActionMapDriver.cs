using System;
using Atlas.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Atlas.Blades.Playground
{
    /// <summary>
    /// Feeds an <see cref="ActionMap"/> from the input service and the lever's touch: the thin Unity side of the
    /// action twins (task P2-C07). It listens to <see cref="IAtlasInput.ActionFired"/>, reads the touch the way the
    /// service does (<see cref="InputTwins.LeverPath"/>; the left mouse button in the Editor), and ticks the map
    /// once a frame after the service's own tick. Sets the dwell time to 700 ms.
    /// </summary>
    public sealed class ActionMapDriver : IDisposable
    {
        readonly IAtlasInput _input;
        readonly ActionMap _map;
        bool _disposed;

        public ActionMapDriver(IAtlasInput input, ActionMap map)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _input.ActionFired += _map.OnActionFired;
            DwellReticle.Apply(_input);
        }

        public ActionMap Map { get { return _map; } }

        /// <summary>After the input service's Tick. In the hand the lever is a touch UI: nothing is read.</summary>
        public void Tick(float deltaTime)
        {
            if (_disposed) return;
            bool down = _input.Context != InputContext.InHand && LeverDown();
            _map.Tick(down, deltaTime);
        }

        /// <summary>The headset lever: a touch on the screen; in the Editor the mouse button stands in for it.</summary>
        public static bool LeverDown()
        {
            Touchscreen touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed) return true;
#if UNITY_EDITOR
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed) return true;
#endif
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _input.ActionFired -= _map.OnActionFired;
        }
    }
}

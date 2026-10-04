using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Atlas.Input
{
    /// <summary>
    /// The input service (Master Plan section 0.5 rule 5, task P1-C10): a thin adapter that reads the action
    /// map, hands the frame to the engine-free <see cref="AtlasInputCore"/> and draws the reticle. The frame
    /// loop ticks it once per frame; everything else reads the frozen snapshot. Tracked hands reach it through
    /// <see cref="IHandsInput"/>, which only this class implements: <see cref="IAtlasInput"/> has no member for
    /// them.
    /// </summary>
    public sealed class AtlasInput : IAtlasInput, IHandsInput, IDisposable
    {
        private readonly AtlasInputCore _core = new AtlasInputCore();
        private readonly AtlasActionMap _actions;
        private readonly GazeReticle _reticle;
        private RawInput _raw;
        private WalkerInput _walker;
        private ReticleDepth _reticleDepth;
        private bool _handsSet;
        private Func<Vector2, bool> _pointerBlocked;
        private bool _disposed;

        private AtlasInput(Transform hud, Material reticleMaterial)
        {
            _actions = new AtlasActionMap();
            _actions.Enable();
            if (hud != null) _reticle = new GazeReticle(hud, reticleMaterial);
            Application.quitting += Dispose;     // also raised when the Editor leaves Play mode
        }

        /// <summary>
        /// Row 10 of the composition root's creation order. Builds the actions in code (no .inputactions
        /// asset), enables them and creates the reticle under hud. Returns the service; the composition root
        /// publishes it under IAtlasInput. The reticle draws with its own unlit shader from Resources;
        /// reticleMaterial is used only if that shader is missing. hud may be null (tests, Spectator): no
        /// reticle is drawn.
        /// </summary>
        public static IAtlasInput Install(Transform hud, Material reticleMaterial)
        {
            return new AtlasInput(hud, reticleMaterial);
        }

        /// <summary>The engine-free core: the snapshot, the gaze dwell and the target registry.</summary>
        public AtlasInputCore Core => _core;

        /// <summary>The gaze target registry, for callers that work in Unity.Mathematics types.</summary>
        public GazeRegistry Targets => _core.Targets;

        /// <summary>
        /// A press that begins on a screen point (pixels from the bottom left) this returns true for is not the
        /// headset lever: the Editor's development overlay sits there, and a click on its buttons must not fire
        /// the gaze target under the reticle. Null: nothing is blocked. Set once; called once per press.
        /// </summary>
        public Func<Vector2, bool> PointerBlocked
        {
            get => _pointerBlocked;
            set
            {
                _pointerBlocked = value;
                _core.PointerBlocked = value == null ? (Func<float2, bool>)null : p => _pointerBlocked(new Vector2(p.x, p.y));
            }
        }

        /// <summary>The action map, for inspection.</summary>
        public AtlasActionMap Actions => _actions;

        public event Action<AtlasAction, InputSource, int> ActionFired
        {
            add => _core.ActionFired += value;
            remove => _core.ActionFired -= value;
        }

        public void Tick(Vector3 headPosition, Quaternion headRotation, float deltaTime)
        {
            if (_disposed) return;
            _actions.Sample(ref _raw, ref _walker);
            // The action map fills the devices' fields and leaves the hands' alone: they are this frame's only if
            // SetHands was called since the last tick, and they never outlive the tick. A fingertip or a pinch
            // edge left standing would be replayed in every frame the hands module does not run.
            if (!_handsSet) _raw.Hands = default;
            _core.Tick(in _raw,
                       new float3(headPosition.x, headPosition.y, headPosition.z),
                       new quaternion(headRotation.x, headRotation.y, headRotation.z, headRotation.w),
                       deltaTime);
            _raw.Hands = default;
            _handsSet = false;

            // Raw calls on purpose: the reticle is the service's own display and must not count as a poll.
            bool visible = _core.Context != InputContext.InHand;
            float metres = visible ? _reticleDepth.Step(_core.GazeDistance, deltaTime) : _reticleDepth.Reset();
            _reticle?.Set(visible, _core.GazeProgress, metres);
        }

        public void SetHands(in HandsInput hands)
        {
            if (_disposed) return;
            _raw.Hands = hands;
            _handsSet = true;
        }

        public ref readonly HandsTouchState HandsTouch => ref _core.HandsTouch;
        public string LabelOf(GazeTargetId id) => _core.LabelOf(id);
        public int TagOf(GazeTargetId id) => _core.TagOf(id);

        public InputContext Context => _core.Context;
        public void PushContext(InputContext context) => _core.PushContext(context);
        public void PopContext(InputContext context) => _core.PopContext(context);

        public bool Pressed(AtlasAction action) => _core.Pressed(action);
        public bool Held(AtlasAction action) => _core.Held(action);
        public float Axis(AtlasAxis axis) => _core.Axis(axis);
        public InputSource LastSource(AtlasAction action) => _core.LastSource(action);
        public int LastTag(AtlasAction action) => _core.LastTag(action);

        public float DwellSeconds
        {
            get => _core.DwellSeconds;
            set => _core.DwellSeconds = value;
        }

        public GazeTargetId RegisterGazeTarget(in GazeTarget target)
        {
            GazeTargetData data = target.ToData();
            GazeTargetId id = _core.Targets.Add(in data);
            if (!id.IsValid)
                Debug.LogWarning("Atlas.Input: gaze target '" + target.Label + "' not registered, all " + GazeRegistry.Capacity + " slots are in use.");
            return id;
        }

        public void MoveGazeTarget(GazeTargetId id, Vector3 centre)
            => _core.Targets.Move(id, new float3(centre.x, centre.y, centre.z));

        /// <summary>Turns a rectangle target.</summary>
        public void RotateGazeTarget(GazeTargetId id, Quaternion rotation)
            => _core.Targets.SetRotation(id, new quaternion(rotation.x, rotation.y, rotation.z, rotation.w));

        public void SetGazeTargetEnabled(GazeTargetId id, bool enabled) => _core.Targets.SetEnabled(id, enabled);
        public void UnregisterGazeTarget(GazeTargetId id) => _core.Targets.Remove(id);

        public GazeState Gaze
        {
            get
            {
                float3 p = _core.GazePoint;
                GazeTargetId focused = _core.GazeFocused;
                return new GazeState
                {
                    Focused = focused,
                    Progress = _core.GazeProgress,
                    Point = focused.IsValid ? new Vector3(p.x, p.y, p.z) : Vector3.zero
                };
            }
        }

        public void CopyActionsWithoutGazeTwin(List<AtlasAction> into) => _core.CopyActionsWithoutGazeTwin(into);

        /// <summary>The axes polled in the current context that no enabled Hold target drives.</summary>
        public void CopyAxesWithoutGazeTwin(List<AtlasAxis> into) => _core.CopyAxesWithoutGazeTwin(into);

        public bool TryGetTap(out Vector2 screenPosition)
        {
            bool tap = _core.TryGetTap(out float2 p);
            screenPosition = new Vector2(p.x, p.y);
            return tap;
        }

        public bool GamepadPresent => _core.GamepadPresent;

        public void SetRumble(float lowFrequency, float highFrequency)
        {
            // No rumble reaches a gamepad on iOS before Unity 6000.7 (Master Plan section 14, open item 9);
            // the call is harmless there and works in the Editor and on the desktop.
            Gamepad pad = Gamepad.current;
            if (pad != null) pad.SetMotorSpeeds(lowFrequency, highFrequency);
        }

        public WalkerInput Walker => _walker;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Application.quitting -= Dispose;
            Gamepad pad = Gamepad.current;
            if (pad != null) pad.SetMotorSpeeds(0f, 0f);
            _actions.Dispose();
            _reticle?.Dispose();
        }
    }
}

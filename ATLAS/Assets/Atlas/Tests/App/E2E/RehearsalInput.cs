using System;
using System.Collections.Generic;
using Atlas.Input;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.E2E.Tests
{
    /// <summary>One firing of an action as the input service raised it.</summary>
    struct InputFiring
    {
        public AtlasAction Action;
        public InputSource Source;
        public int Tag;
    }

    /// <summary>
    /// The input service with no engine: the real <see cref="AtlasInputCore"/> behind the service's interface and
    /// behind the hands' door to it, as Atlas.Input.AtlasInput is on the phone. That class reads the action map
    /// and draws the reticle, which need Unity; this one keeps its handling of the hands line for line (they are
    /// this frame's only if they were set since the last tick, and they never outlive the tick) and takes the
    /// one device the rehearsal has a use for, the gamepad's presence, from a field.
    /// </summary>
    sealed class RehearsalInput : IAtlasInput, IHandsInput
    {
        public readonly AtlasInputCore Core = new AtlasInputCore();
        /// <summary>Every action fired since the start, in order.</summary>
        public readonly List<InputFiring> Fired = new List<InputFiring>(16);
        /// <summary>A gamepad is connected: what the action map would read from the devices.</summary>
        public bool GamepadConnected;
        /// <summary>What the hands module handed over before the last tick; default when it handed nothing.</summary>
        public HandsInput LastHands;
        /// <summary>Calls of <see cref="SetHands"/> since the start.</summary>
        public int HandsSets;

        RawInput _raw;
        bool _handsSet;

        public RehearsalInput()
        {
            Core.ActionFired += (action, source, tag) => Fired.Add(new InputFiring { Action = action, Source = source, Tag = tag });
        }

        public void Tick(Vector3 headPosition, Quaternion headRotation, float deltaTime)
        {
            _raw.GamepadPresent = GamepadConnected;
            if (!_handsSet) _raw.Hands = default(HandsInput);
            LastHands = _raw.Hands;
            Core.Tick(in _raw, new float3(headPosition.x, headPosition.y, headPosition.z),
                new quaternion(headRotation.x, headRotation.y, headRotation.z, headRotation.w), deltaTime);
            _raw.Hands = default(HandsInput);
            _handsSet = false;
        }

        // ---- IHandsInput ----

        public void SetHands(in HandsInput hands)
        {
            _raw.Hands = hands;
            _handsSet = true;
            HandsSets++;
        }

        public ref readonly HandsTouchState HandsTouch { get { return ref Core.HandsTouch; } }
        public string LabelOf(GazeTargetId id) { return Core.LabelOf(id); }
        public int TagOf(GazeTargetId id) { return Core.TagOf(id); }

        // ---- IAtlasInput ----

        public InputContext Context { get { return Core.Context; } }
        public void PushContext(InputContext context) { Core.PushContext(context); }
        public void PopContext(InputContext context) { Core.PopContext(context); }
        public bool Pressed(AtlasAction action) { return Core.Pressed(action); }
        public bool Held(AtlasAction action) { return Core.Held(action); }
        public float Axis(AtlasAxis axis) { return Core.Axis(axis); }
        public InputSource LastSource(AtlasAction action) { return Core.LastSource(action); }
        public int LastTag(AtlasAction action) { return Core.LastTag(action); }

        public event Action<AtlasAction, InputSource, int> ActionFired
        {
            add { Core.ActionFired += value; }
            remove { Core.ActionFired -= value; }
        }

        public float DwellSeconds { get { return Core.DwellSeconds; } set { Core.DwellSeconds = value; } }

        public GazeTargetId RegisterGazeTarget(in GazeTarget target)
        {
            GazeTargetData data = target.ToData();
            return Core.Targets.Add(in data);
        }

        public void MoveGazeTarget(GazeTargetId id, Vector3 centre) { Core.Targets.Move(id, new float3(centre.x, centre.y, centre.z)); }
        public void SetGazeTargetEnabled(GazeTargetId id, bool enabled) { Core.Targets.SetEnabled(id, enabled); }
        public void UnregisterGazeTarget(GazeTargetId id) { Core.Targets.Remove(id); }

        public GazeState Gaze
        {
            get
            {
                float3 p = Core.GazePoint;
                GazeTargetId focused = Core.GazeFocused;
                return new GazeState
                {
                    Focused = focused,
                    Progress = Core.GazeProgress,
                    Point = focused.IsValid ? new Vector3(p.x, p.y, p.z) : default(Vector3)
                };
            }
        }

        public void CopyActionsWithoutGazeTwin(List<AtlasAction> into) { Core.CopyActionsWithoutGazeTwin(into); }
        public bool TryGetTap(out Vector2 screenPosition) { screenPosition = default(Vector2); return false; }
        public bool GamepadPresent { get { return Core.GamepadPresent; } }
        public void SetRumble(float lowFrequency, float highFrequency) { }
        public WalkerInput Walker { get { return default(WalkerInput); } }
    }
}

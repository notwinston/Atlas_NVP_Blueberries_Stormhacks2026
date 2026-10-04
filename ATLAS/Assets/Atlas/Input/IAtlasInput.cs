using System;
using System.Collections.Generic;
using UnityEngine;

namespace Atlas.Input
{
    /// <summary>The one action map (Master Plan section 0.5 rule 5, task P1-C10). Main thread only. Published
    /// through AtlasServices under this interface.</summary>
    public interface IAtlasInput
    {
        /// <summary>Step 1 of the frame loop, after the pose is read: polls the devices, runs the gaze dwell with
        /// the head pose given, freezes the frame's snapshot, raises ActionFired. Called by the frame loop's
        /// input step and by nothing else.</summary>
        void Tick(Vector3 headPosition, Quaternion headRotation, float deltaTime);

        InputContext Context { get; }
        /// <summary>Contexts nest: a menu pushes Menu when it opens and pops it when it closes. Onboarding pushes
        /// Space at the headset prompt. The bottom of the stack is InHand.</summary>
        void PushContext(InputContext context);
        void PopContext(InputContext context);

        /// <summary>True during the frame in which the action fired, from any twin.</summary>
        bool Pressed(AtlasAction action);
        bool Held(AtlasAction action);
        float Axis(AtlasAxis axis);
        /// <summary>Source and tag of the action's last firing.</summary>
        InputSource LastSource(AtlasAction action);
        int LastTag(AtlasAction action);
        /// <summary>Raised inside Tick: action, source, tag.</summary>
        event Action<AtlasAction, InputSource, int> ActionFired;

        /// <summary>Clamped to 0.6 .. 0.8; default 0.7.</summary>
        float DwellSeconds { get; set; }
        /// <summary>Returns default when 64 targets are already registered.</summary>
        GazeTargetId RegisterGazeTarget(in GazeTarget target);
        void MoveGazeTarget(GazeTargetId id, Vector3 centre);
        void SetGazeTargetEnabled(GazeTargetId id, bool enabled);
        void UnregisterGazeTarget(GazeTargetId id);
        GazeState Gaze { get; }
        /// <summary>Actions polled with Pressed or Held in the current context that have no enabled gaze target.</summary>
        void CopyActionsWithoutGazeTwin(List<AtlasAction> into);

        /// <summary>InHand: a touch that began this frame, in screen pixels.</summary>
        bool TryGetTap(out Vector2 screenPosition);

        bool GamepadPresent { get; }
        /// <summary>Gamepad motors, 0..1. Does nothing without a gamepad, and nothing on iOS in this Unity version.</summary>
        void SetRumble(float lowFrequency, float highFrequency);

        WalkerInput Walker { get; }
    }
}

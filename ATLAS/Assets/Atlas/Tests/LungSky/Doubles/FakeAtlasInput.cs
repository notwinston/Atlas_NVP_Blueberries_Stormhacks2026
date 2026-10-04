using System;
using System.Collections.Generic;
using Atlas.Input;
using UnityEngine;

namespace Atlas.Blades.Lung.Sky.Tests
{
    // The one action source the beat reads (IAtlasInput): Confirm fires for one frame after ConfirmNext, the left stick is a value.
    public sealed class FakeAtlasInput : IAtlasInput
    {
        public bool ConfirmNext; public Vector2 StickValue;

        public bool Pressed(AtlasAction action)
        {
            if (action != AtlasAction.Confirm || !ConfirmNext) return false;
            ConfirmNext = false; return true;
        }

        public float Axis(AtlasAxis axis) => axis == AtlasAxis.LeftX ? StickValue.x : axis == AtlasAxis.LeftY ? StickValue.y : 0f;

        public bool Held(AtlasAction action) => false;
        public InputContext Context => InputContext.Space;
        public void Tick(Vector3 headPosition, Quaternion headRotation, float deltaTime) { }
        public void PushContext(InputContext context) { }
        public void PopContext(InputContext context) { }
        public InputSource LastSource(AtlasAction action) => InputSource.None;
        public int LastTag(AtlasAction action) => 0;
        public event Action<AtlasAction, InputSource, int> ActionFired { add { } remove { } }
        public float DwellSeconds { get; set; } = 0.7f;
        public GazeTargetId RegisterGazeTarget(in GazeTarget target) => default;
        public void MoveGazeTarget(GazeTargetId id, Vector3 centre) { }
        public void SetGazeTargetEnabled(GazeTargetId id, bool enabled) { }
        public void UnregisterGazeTarget(GazeTargetId id) { }
        public GazeState Gaze => default;
        public void CopyActionsWithoutGazeTwin(List<AtlasAction> into) { }
        public bool TryGetTap(out Vector2 screenPosition) { screenPosition = default; return false; }
        public bool GamepadPresent => false;
        public void SetRumble(float lowFrequency, float highFrequency) { }
        public WalkerInput Walker => default;
    }

    static class SkyTestEnv
    {
        // A normal-tracking frame: the geometry head pose equals the raw one.
        public static Atlas.World.FrameContext Frame(long index, double time, float dt, Unity.Mathematics.float3 head, Unity.Mathematics.quaternion rotation)
        {
            return new Atlas.World.FrameContext
            {
                FrameIndex = (int)index, Time = time, DeltaTime = dt, RawHeadPosition = head, RawHeadRotation = rotation,
                Tracking = Atlas.World.TrackingState.Normal, HeadPosition = head, HeadRotation = rotation,
            };
        }
    }
}

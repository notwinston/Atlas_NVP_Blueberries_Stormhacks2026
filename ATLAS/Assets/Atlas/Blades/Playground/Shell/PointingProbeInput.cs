using System;
using Atlas.Input;
using UnityEngine;

namespace Atlas.Blades.Playground
{
    /// <summary>
    /// Feeds <see cref="PointingProbe"/> from the action map: A (<see cref="AtlasAction.Confirm"/>, from the gamepad,
    /// the prompt chip or a headset click) takes the measurement while the probe is armed, and the head ray held
    /// still takes it as the gaze twin. The director calls <see cref="Tick"/> once per frame with the head pose
    /// (room frame) and the frame's delta time. Main thread only.
    /// </summary>
    public sealed class PointingProbeInput : IDisposable
    {
        readonly IAtlasInput _input;
        readonly PointingProbe _probe;
        readonly Func<double> _clock;
        Vector3 _position;
        Vector3 _forward = Vector3.forward;

        public PointingProbeInput(IAtlasInput input, PointingProbe probe, Func<double> clock)
        {
            if (input == null) throw new ArgumentNullException("input");
            if (probe == null) throw new ArgumentNullException("probe");
            if (clock == null) throw new ArgumentNullException("clock");
            _input = input;
            _probe = probe;
            _clock = clock;
            _input.ActionFired += OnAction;
        }

        public void Tick(Vector3 headPosition, Quaternion headRotation, float deltaTime)
        {
            _position = headPosition;
            _forward = headRotation * Vector3.forward;
            _probe.TickDwell(_position.x, _position.z, _forward.x, _forward.z, deltaTime, _clock());
        }

        void OnAction(AtlasAction action, InputSource source, int tag)
        {
            if (action != AtlasAction.Confirm || !_probe.IsArmed) return;
            _probe.Capture(_position.x, _position.z, _forward.x, _forward.z, _clock());
        }

        public void Dispose()
        {
            _input.ActionFired -= OnAction;
        }
    }
}

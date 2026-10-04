using System;
using Atlas.Input;
using Atlas.Link;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>Beat G, the needle sky (MP §10.2.2 beat g, §10.2.5). Tick is called by the Lung director at §6.3 step 5 (A-G7-1, D13).</summary>
    public sealed class NeedleSkyBeat : ILungBeat, IDisposable
    {
        [AtlasParam("lung.skyOn", true)] bool _skyOn = true;                          // G11 kill switch
        [AtlasParam("lung.skyPhase", 0f, 9f, 0f)] int _skyPhase;                      // phase layer
        [AtlasParam("lung.skyRobust", true)] bool _skyRobust = true;                  // robust layer
        [AtlasParam("lung.skyPickAz", -180f, 180f, 0f)] float _skyPickAz;             // Console twin, deg
        [AtlasParam("lung.skyPickEl", -90f, 90f, 0f)] float _skyPickEl;               // Console twin, deg
        [AtlasParam("lung.skyPick", 0f, 65535f, 0f)] int _skyPick;                    // Console twin: each change commits one pick

        readonly NeedleSkyModel _model;
        readonly IAtlasInput _input;
        readonly INeedleSkyPresenter _presenter;
        readonly ParamBinding _binding;
        int _lastSkyPick;
        bool _entered;

        public NeedleSkyBeat(SkyMaps maps, ParamRegistry registry, ISkyEvents events, IAtlasInput inputOrNull, INeedleSkyPresenter presenterOrNull)
        {
            _model = new NeedleSkyModel(maps, events);
            _input = inputOrNull; _presenter = presenterOrNull;
            _binding = ParamBinder.Bind(this, registry);
            _lastSkyPick = _skyPick;
        }

        public LungBeat Beat => LungBeat.G;
        public NeedleSkyModel Model => _model;
        public NeedleSkyReadout Readout => _model.Readout;
        public int ActivePhaseLayer => _skyPhase < 0 ? 0 : (_skyPhase > 9 ? 9 : _skyPhase);
        public bool RobustLayerOn => _skyRobust;
        public bool SkyOn => _skyOn;
        public bool Completed => _model.Completed;

        public void Enter()
        {
            _model.Reset();
            _lastSkyPick = _skyPick;        // a stale counter does not fire (brief §5.2)
            _presenter?.SetVisible(SkyOn);
            _entered = true;
        }

        public void Exit() { _presenter?.SetVisible(false); _entered = false; }

        public void Tick(in FrameContext frame)
        {
            if (!_entered) return;
            if (!_skyOn) { _presenter?.SetVisible(false); return; }
            _presenter?.SetVisible(true);
            double3 gaze = math.normalize((double3)math.mul(frame.HeadRotation, new float3(0, 0, 1)));
            bool consolePick = _skyPick != _lastSkyPick;
            _lastSkyPick = _skyPick;
            bool confirm = _input != null && _input.Pressed(AtlasAction.Confirm);
            float2 stick = _input != null ? new float2(_input.Axis(AtlasAxis.LeftX), _input.Axis(AtlasAxis.LeftY)) : float2.zero;
            _model.Step(new SkyInput
            {
                Gaze = gaze, Stick = stick, Confirm = confirm, ConsolePick = consolePick,
                ConsoleAzDeg = _skyPickAz, ConsoleElDeg = _skyPickEl, DeltaTime = frame.DeltaTime,
            });
            if (_presenter == null) return;
            int n = _model.PickCount;
            _presenter.Present(new NeedleSkyFrame(frame.HeadPosition, frame.HeadRotation, ActivePhaseLayer, _skyRobust,
                (float3)_model.Cursor, n,
                n > 0 ? (float3)_model.Pick(0) : float3.zero, n > 1 ? (float3)_model.Pick(1) : float3.zero, n > 2 ? (float3)_model.Pick(2) : float3.zero,
                n == NeedleSkyConstants.PicksPerCheck, _model.Readout));
        }

        public void Dispose() { _binding.Dispose(); (_presenter as IDisposable)?.Dispose(); }
    }
}

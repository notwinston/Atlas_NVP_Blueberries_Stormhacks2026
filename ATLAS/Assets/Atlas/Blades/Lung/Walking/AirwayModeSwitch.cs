using System;
using Atlas.Link;
using Atlas.Telemetry;
using Atlas.World;

namespace Atlas.Blades.Lung.Walking
{
    // P2-B05. MP §10.2.3 switch rule: "Mode 2 if the FMS exceeds 3 of 20 or the free rectangle gives s < 1.5 m; the Console can
    // also switch". The Console path is ParamSet -> registry entry curved.mode (lane D drops it under the demo lock).
    // The actual switch happens at a chamber inside AirwayWing.TrySwitch.
    public sealed class AirwayModeSwitch : IDisposable
    {
        public const float FmsLimit = 3f;          // MP §10.2.3 "FMS > 3/20"
        public const double MinLapSide = 1.5;      // m, MP §10.2.3 "s < 1.5 m"

        readonly ParamEntry _mode;
        readonly EventLog _log;
        bool _disposed;

        // log: the session's EventLog (AtlasServices holds it as IEventLog), or null for none.
        public AirwayModeSwitch(ParamRegistry registry, RoomModel room, EventLog log)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (room == null) throw new ArgumentNullException(nameof(room));
            _mode = AirwayParams.DeclareCurvedMode(registry);
            if (room.LapSide < MinLapSide) _mode.Set(AirwayParams.IndexOf(AirwayMode.AlignedCells));   // automatic rule 2
            _log = log;
            if (_log != null) _log.Logged += OnEvent;   // DV-G6-9: the FMS source is the "fms" event {score} (lane C to confirm)
        }

        public AirwayMode Requested => AirwayParams.ModeOf(_mode);

        // Automatic rule 1: FMS > 3 selects Mode 2.
        public void ReportFms(float score)
        {
            if (_disposed) return;
            if (score > FmsLimit) _mode.Set(AirwayParams.IndexOf(AirwayMode.AlignedCells));
        }

        void OnEvent(EventMessage m)
        {
            if (m.Ev != "fms" || m.Data == null || !m.Data.Has("score")) return;
            ReportFms(m.Data.GetFloat("score"));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_log != null) _log.Logged -= OnEvent;
        }
    }
}

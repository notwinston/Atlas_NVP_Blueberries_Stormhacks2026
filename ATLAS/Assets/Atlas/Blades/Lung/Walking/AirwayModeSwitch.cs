using System;
using Atlas.Link;
using Atlas.Telemetry;
using Atlas.World;
using UnityEngine;

namespace Atlas.Blades.Lung.Walking
{
    // P2-B05. MP §10.2.3 switch rule: "Mode 2 if the FMS exceeds 3 of 20 or the free rectangle gives s < 1.5 m; the Console can
    // also switch". The Console path is ParamSet -> registry.ApplyRemote -> AirwayParams.CurvedMode (AtlasParamScanner).
    // The actual switch happens at a chamber inside AirwayWing.TrySwitch.
    public sealed class AirwayModeSwitch : IDisposable
    {
        public const float FmsLimit = 3f;          // MP §10.2.3 "FMS > 3/20"
        public const double MinLapSide = 1.5;      // m, MP §10.2.3 "s < 1.5 m"

        [Serializable] struct FmsData { public float score; }

        readonly ParamRegistry _registry;
        readonly ushort _id;
        readonly AirwayParams _p;
        bool _disposed;

        public AirwayModeSwitch(ParamRegistry registry, AirwayParams p, RoomModel room)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _p = p ?? throw new ArgumentNullException(nameof(p));
            if (room == null) throw new ArgumentNullException(nameof(room));
            AtlasParamScanner.Bind(p, registry);
            if (!registry.TryGetId("curved.mode", out _id)) throw new InvalidOperationException("curved.mode is not registered");
            if (room.lapSquare.s < MinLapSide) registry.Set(_id, (float)AirwayMode.AlignedCells);   // automatic rule 2
            EventLog.Emitted += OnEvent;   // DV-G6-9: the FMS source is the "fms" event {score} (lane C to confirm)
        }

        public AirwayMode Requested => (AirwayMode)_p.CurvedMode;

        // Automatic rule 1: FMS > 3 selects Mode 2.
        public void ReportFms(float score)
        {
            if (_disposed) return;
            if (score > FmsLimit) _registry.Set(_id, (float)AirwayMode.AlignedCells);
        }

        void OnEvent(EventMessage m)
        {
            if (m.Ev != "fms" || string.IsNullOrEmpty(m.Data)) return;
            ReportFms(JsonUtility.FromJson<FmsData>(m.Data).score);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            EventLog.Emitted -= OnEvent;
        }
    }
}

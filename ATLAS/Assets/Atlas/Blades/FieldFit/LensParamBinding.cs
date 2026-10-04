using Atlas.Link;
using Atlas.Telemetry;

namespace Atlas.Blades.FieldFit
{
    // Master Plan §6.5, §7.3, §10.4.6; Brief §4 (R9, R10). The only file that names lane D types.
    // Angles are DEGREES as registered (DV17); FieldFitFeature converts once.
    internal interface ILensParamSource
    {
        bool TryRead(out float eDeg, out float rho0Deg, out float e1Deg, out float t, out int profile, out bool on);
        void SetLensOn(bool on);
    }

    internal interface ILensEventSink { void Emit(string ev, string json); }

    /// <summary>Hand-over of lane D's registry (DV-B03-f): lane A's bootstrap calls BindRegistry once; the placeholder SpaceLab has no static accessor.</summary>
    public static class LensParamBinding
    {
        static ParamRegistry registry;
        public static void BindRegistry(ParamRegistry r) { registry = r; }
        internal static ILensParamSource CreateSource() { var s = new RegistryLensSource(registry); return s.Resolved ? s : null; }
        internal static ILensEventSink CreateSink() { return new EventLogSink(); }
    }

    /// <summary>R9 over lane D's ParamRegistry. Names resolved to ids once; lane B declares nothing (P3-D02 does).</summary>
    internal sealed class RegistryLensSource : ILensParamSource
    {
        readonly ParamRegistry reg;
        readonly ushort idE, idRho0, idE1, idT, idProfile, idOn;
        readonly bool resolved;

        public RegistryLensSource(ParamRegistry registry)
        {
            reg = registry;
            resolved = reg != null
                && reg.TryGetId("lens.E", out idE) && reg.TryGetId("lens.rho0", out idRho0) && reg.TryGetId("lens.e1", out idE1)
                && reg.TryGetId("lens.t", out idT) && reg.TryGetId("lens.profile", out idProfile) && reg.TryGetId("lens.on", out idOn);
        }

        public bool Resolved => resolved;

        public bool TryRead(out float eDeg, out float rho0Deg, out float e1Deg, out float t, out int profile, out bool on)
        {
            if (!resolved) { eDeg = rho0Deg = e1Deg = t = 0; profile = 0; on = false; return false; }
            eDeg = reg.Get(idE); rho0Deg = reg.Get(idRho0); e1Deg = reg.Get(idE1); t = reg.Get(idT);
            profile = (int)reg.Get(idProfile);
            on = reg.Get(idOn) >= 0.5f;
            return true;
        }

        // Clamped and echoed like an on-device menu change (Master Plan §7.3).
        public void SetLensOn(bool on) { if (resolved) reg.Set(idOn, on ? 1f : 0f); }
    }

    /// <summary>R10 over lane D's event log. Scene string: the use-case scene name.</summary>
    internal sealed class EventLogSink : ILensEventSink
    {
        public void Emit(string ev, string json) { EventLog.Emit("Blade_FieldFit", ev, json); }
    }

}

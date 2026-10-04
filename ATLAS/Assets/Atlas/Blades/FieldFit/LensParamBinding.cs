using Atlas.Link;
using Atlas.Telemetry;
using Atlas.World;

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

    /// <summary>The two lane D services FieldFit uses, taken from AtlasServices when the feature enables.</summary>
    internal static class LensParamBinding
    {
        /// <summary>Null when no registry is published or the six lens.* parameters are not declared (P3-D02 declares them).</summary>
        internal static ILensParamSource CreateSource()
        {
            if (!AtlasServices.TryGet(out ParamRegistry registry)) return null;
            var s = new RegistryLensSource(registry);
            return s.Resolved ? s : null;
        }

        internal static ILensEventSink CreateSink() { return new EventLogSink(); }
    }

    /// <summary>R9 over lane D's ParamRegistry. Entries are looked up once; lane B declares nothing (P3-D02 does).</summary>
    internal sealed class RegistryLensSource : ILensParamSource
    {
        readonly ParamEntry e, rho0, e1, t, profile, on;

        public RegistryLensSource(ParamRegistry registry)
        {
            if (registry == null) return;
            if (registry.TryGet("lens.E", out e) && registry.TryGet("lens.rho0", out rho0) && registry.TryGet("lens.e1", out e1)
                && registry.TryGet("lens.t", out t) && registry.TryGet("lens.profile", out profile) && registry.TryGet("lens.on", out on))
                return;
            e = rho0 = e1 = t = profile = on = null;
        }

        public bool Resolved => on != null;

        public bool TryRead(out float eDeg, out float rho0Deg, out float e1Deg, out float tt, out int prof, out bool isOn)
        {
            if (!Resolved) { eDeg = rho0Deg = e1Deg = tt = 0; prof = 0; isOn = false; return false; }
            eDeg = e.Value; rho0Deg = rho0.Value; e1Deg = e1.Value; tt = t.Value;
            prof = (int)profile.Value;
            isOn = on.Value >= 0.5f;
            return true;
        }

        // Clamped and echoed like an on-device menu change (Master Plan §7.3).
        public void SetLensOn(bool value) { if (Resolved) on.Set(value); }
    }

    /// <summary>R10 over lane D's event log. Scene string: the use-case scene name.</summary>
    internal sealed class EventLogSink : ILensEventSink
    {
        public void Emit(string ev, string json)
        {
            if (AtlasServices.TryGet(out IEventLog log)) log.LogJson("Blade_FieldFit", ev, json);
        }
    }

}

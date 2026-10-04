namespace Atlas.World
{
    /// <summary>
    /// The session's event log (Master Plan Appendix B.2, Event). Published through AtlasServices, so that
    /// rendering, scan, the spaces and the app can log with no reference to Atlas.Telemetry.
    /// </summary>
    public interface IEventLog
    {
        void Log(string ev);
        void Log(string ev, string key, double value);
        void Log(string ev, string key, string value);
        void Log(string ev, string key1, double value1, string key2, double value2);
        /// <summary>dataJson is one JSON object as text, for example {"from":"S0","to":"S1"}.</summary>
        void LogJson(string ev, string dataJson);
    }
}

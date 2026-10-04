namespace Atlas.Safety
{
    /// <summary>
    /// What Bump Guard is telling the walker (Master Plan section 6.5). The order is the order of urgency, so
    /// levels can be compared. The values are those of State.bumpLevel (Appendix B.1).
    /// </summary>
    public enum BumpLevel
    {
        /// <summary>Nothing within reach on the predicted path.</summary>
        Clear = 0,
        /// <summary>A hazard is within 0.30 m, or the next 0.8 s of walking reaches one.</summary>
        Warn = 1,
        /// <summary>A hazard is within 0.20 m, or the next 0.4 s of walking reaches one.</summary>
        Strong = 2,
        /// <summary>Tracking has not been Normal for 0.5 s: stand still.</summary>
        TrackingLost = 3
    }
}

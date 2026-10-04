namespace Atlas.Spaces.Glued
{
    /// <summary>
    /// What S1 shows, read by the Playground's equation card and Glued exhibit (tasks P2-A03, P2-C12) without touching the
    /// component: the gluing, the walker's cell, the crossings, the numbers of the card and the guided steps.
    /// <see cref="GluedSpace"/> implements it; a test implements it without the engine.
    /// </summary>
    public interface IGluedReadings
    {
        GluedLayout Layout { get; }
        GluedCell WalkerCell { get; }
        int Crossings { get; }
        /// <summary>x mod a, z mod b, the cell sides, the wraps and the parity of the walker's cell.</summary>
        GluedCardValues Card { get; }
        /// <summary>The guided steps; null before the space is entered.</summary>
        GluedTour Tour { get; }
    }
}

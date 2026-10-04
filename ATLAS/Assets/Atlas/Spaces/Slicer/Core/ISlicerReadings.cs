namespace Atlas.Spaces.Slicer
{
    /// <summary>
    /// What the Slicer shows, read by the Playground's equation card and toybox exhibit (tasks P2-A01, P2-A03) without
    /// touching the component: the body, its slice, the cut and the three turns (radians), and the guided steps.
    /// <see cref="SlicerSpace"/> implements it; a test implements it without the engine.
    /// </summary>
    public interface ISlicerReadings
    {
        SlicerObject Body { get; }
        SectionInfo Section { get; }
        float W { get; }
        float Xw { get; }
        float Yw { get; }
        float Zw { get; }
        SlicerTour Tour { get; }
    }
}

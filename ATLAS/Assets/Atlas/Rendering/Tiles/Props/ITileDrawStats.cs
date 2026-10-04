// P1-B07: what the tile path drew this frame, for the telemetry row (Master Plan §7.3.3 visibleTiles, §11.2). Published in
// AtlasServices by the renderer while it is configured; the composition root copies TilesDrawn to the perf probe.
namespace Atlas.Rendering
{
    public interface ITileDrawStats
    {
        /// <summary>Tiles drawn by the last Draw, per eye.</summary>
        int TilesDrawn { get; }
        /// <summary>Their triangles, per eye.</summary>
        int TrianglesDrawn { get; }
    }
}

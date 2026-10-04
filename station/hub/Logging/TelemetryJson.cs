using Atlas.Link;

namespace Atlas.Station
{
    /// <summary>
    /// One <c>Telemetry</c> row as the Console gets it: a JSON object keyed by the CSV column names of
    /// <see cref="TelemetryCsv.Header"/>, in the same order (lane D contract 3.3). The CSV line itself is written
    /// by <see cref="TelemetryCsv"/>, the code the phone uses for its own file.
    /// </summary>
    public static class TelemetryJson
    {
        public static JsonValue ToJson(in TelemetryMessage t)
        {
            return JsonValue.Object()
                .Set("t_us", t.TimeUs).Set("seq", (long)t.Seq).Set("sceneId", (int)t.SceneId).Set("spaceId", (int)t.SpaceId)
                .Set("tier", (int)t.Tier).Set("fps", t.Fps).Set("p50", t.P50).Set("p95", t.P95).Set("p99", t.P99).Set("max", t.Max)
                .Set("over17_2", t.Over17_2).Set("over33_8", t.Over33_8).Set("cpuMainP95", t.CpuMainP95)
                .Set("cpuRenderP95", t.CpuRenderP95).Set("gpuP95", t.GpuP95).Set("renderScale", t.RenderScale)
                .Set("drawCalls", (int)t.DrawCalls).Set("setPass", (int)t.SetPass).Set("trisK", (int)t.TrisK).Set("memMB", (int)t.MemMB)
                .Set("visibleTiles", (int)t.VisibleTiles).Set("copies", (int)t.Copies).Set("rttMs", (int)t.RttMs)
                .Set("lossPermille", (int)t.LossPermille).Set("cueCount", (int)t.CueCount).Set("gcAllocB", (long)t.GcAllocB)
                .Set("thermal", (int)t.Thermal).Set("battery", (int)t.Battery).Set("tracking", (int)t.Tracking).Set("msaa", (int)t.Msaa)
                .Set("stateHz", (int)t.StateHz).Set("bumpMax", (int)t.BumpMax).Set("assetsPlaced", (int)t.AssetsPlaced)
                .Set("assetsPending", (int)t.AssetsPending);
        }
    }
}

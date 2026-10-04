using System.Globalization;
using Atlas.Kernel;
using Atlas.World;

namespace Atlas.Spaces.SmallPlanet
{
    // The walk.* events of Master Plan §10.1.3 (logging §11.2), same payloads as Curved Walk's
    // (Spaces may not reference one another, MP §6.2, so the few lines are repeated here).
    public static class WalkEvents
    {
        /// <summary>Logs under the entered space's scene. Nothing is logged without a log, or for an event that is no corner.</summary>
        public static void Emit(IEventLog log, LapWalk walk, LapEvent ev)
        {
            if (log == null || (ev != LapEvent.Corner && ev != LapEvent.Closed)) return;
            var c = CultureInfo.InvariantCulture;
            string flag = walk.DistanceToFlagM.ToString("F3", c), turn = walk.WorldTurnDeg.ToString("F2", c);
            log.LogJson("walk.corner", "{\"q\":" + walk.Q + ",\"corner\":" + walk.Corners + ",\"flag_m\":" + flag + ",\"turn_deg\":" + turn + "}");
            if (walk.Corners % 4 == 0)
                log.LogJson("walk.lap", "{\"q\":" + walk.Q + ",\"laps\":" + (walk.Corners / 4) + ",\"flag_m\":" + flag + ",\"turn_deg\":" + turn + "}");
            if (ev == LapEvent.Closed)
                log.LogJson("walk.closed", "{\"q\":" + walk.Q + ",\"corners\":" + walk.Corners + ",\"closure_m\":" + flag + ",\"turn_deg\":" + turn + "}");
        }
    }
}

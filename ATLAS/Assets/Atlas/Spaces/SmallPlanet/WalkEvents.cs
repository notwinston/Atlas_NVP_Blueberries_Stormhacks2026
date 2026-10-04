using System.Globalization;
using Atlas.Kernel;
using Atlas.Telemetry;

namespace Atlas.Spaces.SmallPlanet
{
    // The walk.* events of Master Plan §10.1.3 (logging §11.2), same payloads as CurvedWalkSpace.EmitWalkEvents
    // (Spaces may not reference one another, MP §6.2, so the few lines are repeated here).
    public static class WalkEvents
    {
        public static void Emit(LapWalk walk, LapEvent ev, string scene)
        {
            if (ev != LapEvent.Corner && ev != LapEvent.Closed) return;
            var c = CultureInfo.InvariantCulture;
            string flag = walk.DistanceToFlagM.ToString("F3", c), turn = walk.WorldTurnDeg.ToString("F2", c);
            EventLog.Emit(scene, "walk.corner", "{\"q\":" + walk.Q + ",\"corner\":" + walk.Corners + ",\"flag_m\":" + flag + ",\"turn_deg\":" + turn + "}");
            if (walk.Corners % 4 == 0)
                EventLog.Emit(scene, "walk.lap", "{\"q\":" + walk.Q + ",\"laps\":" + (walk.Corners / 4) + ",\"flag_m\":" + flag + ",\"turn_deg\":" + turn + "}");
            if (ev == LapEvent.Closed)
                EventLog.Emit(scene, "walk.closed", "{\"q\":" + walk.Q + ",\"corners\":" + walk.Corners + ",\"closure_m\":" + flag + ",\"turn_deg\":" + turn + "}");
        }
    }
}

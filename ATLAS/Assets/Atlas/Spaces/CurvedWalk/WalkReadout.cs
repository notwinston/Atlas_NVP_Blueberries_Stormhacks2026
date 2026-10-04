using System.Globalization;
using System.Text;
using Atlas.Kernel;
using Atlas.World;

namespace Atlas.Spaces.CurvedWalk
{
    // P1-B09: the read-outs of the 3-4-5 walk (Master Plan §10.1.3): the corner counter, the closure error and the world turn,
    // as the walk.* events of the log (§11.2) and as the three lines shown in the headset. Pure: no engine type.
    public static class WalkReadout
    {
        /// <summary>The three event names of MP §10.1.3. The log stamps the scene (S2) itself; a null log drops them.</summary>
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

        /// <summary>
        /// Rebuilds the headset text into sb and returns false (sb empty) when there is nothing to show: no walk armed.
        /// Line 1 the corners walked of q, line 2 the distance to the flag (the closure error once the loop has closed),
        /// line 3 the turn of the world. Before the walker reaches the flag there is one line that says where to start.
        /// </summary>
        public static bool Build(StringBuilder sb, LapWalk walk)
        {
            sb.Length = 0;
            if (walk == null || !walk.Armed) return false;
            var c = CultureInfo.InvariantCulture;
            if (!walk.Started)
            {
                sb.Append("walk to the flag");
                return true;
            }
            sb.Append("corners ").Append(walk.Corners).Append(" / ").Append(walk.Q).Append('\n');
            sb.Append(walk.Closed ? "closure " : "flag ").Append(walk.DistanceToFlagM.ToString("F2", c)).Append(" m\n");
            sb.Append("turn ").Append(walk.WorldTurnDeg.ToString("F0", c)).Append(" deg");
            return true;
        }
    }
}

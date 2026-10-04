using System;
using System.Collections.Generic;
using System.Globalization;
using Atlas.Link;

namespace Atlas.Blades.Lung.Walking
{
    // P2-B12 run report (MP §10.2.8 row P2-B12: "4 forks without rescue in >= 9 of 10 naive runs; FMS <= 3/20").
    // Built from the fork / compass_choice / fms events of one run (MP §10.2.6; App. B.2 {t_us, scene, ev, data}). G6 §3.10.
    public sealed class AirwayRunReport
    {
        public int RouteForks;
        public int ForksWalked, ForksCorrect, BeaconShownCount;
        public bool RescueFree;
        public float FmsMax = -1;
        public bool CompassForkLogged;
        public double DurationS;

        public static AirwayRunReport FromEvents(IEnumerable<EventMessage> events, string endAddress)
        {
            var r = new AirwayRunReport { RouteForks = endAddress?.Length ?? 0 };
            long first = long.MaxValue, last = long.MinValue;
            foreach (var e in events)
            {
                switch (e.Ev)
                {
                    case "fork":
                        var d = ForkEventData.FromJson(e.Data);
                        r.ForksWalked++;
                        if (d.correct) r.ForksCorrect++;
                        if (d.beacon_shown) r.BeaconShownCount++;
                        first = Math.Min(first, e.TimeUs); last = Math.Max(last, e.TimeUs);
                        break;
                    case "compass_choice": r.CompassForkLogged = true; break;
                    case "fms": r.FmsMax = Math.Max(r.FmsMax, e.Data.GetFloat("score")); break;   // DV-G6-9: any scene
                }
            }
            r.RescueFree = r.RouteForks > 0 && r.ForksCorrect == r.RouteForks && r.BeaconShownCount == 0;
            r.DurationS = last >= first ? (last - first) / 1e6 : 0;
            return r;
        }

        // Lines of events.jsonl (App. B.2 {t_us, scene, ev, data}); a blank or malformed line is skipped.
        public static AirwayRunReport FromJsonLines(IEnumerable<string> eventsJsonl, string endAddress)
        {
            var list = new List<EventMessage>();
            foreach (var line in eventsJsonl)
                if (EventMessage.TryParseLine(line, out var m)) list.Add(m);
            return FromEvents(list, endAddress);
        }

        public string ToJson()
        {
            var inv = CultureInfo.InvariantCulture;
            return "{\"RouteForks\":" + RouteForks + ",\"ForksWalked\":" + ForksWalked + ",\"ForksCorrect\":" + ForksCorrect +
                   ",\"BeaconShownCount\":" + BeaconShownCount + ",\"RescueFree\":" + (RescueFree ? "true" : "false") +
                   ",\"FmsMax\":" + FmsMax.ToString("0.###", inv) + ",\"CompassForkLogged\":" + (CompassForkLogged ? "true" : "false") +
                   ",\"DurationS\":" + DurationS.ToString("0.###", inv) + "}";
        }

        public bool SameAs(AirwayRunReport o) => o != null && ToJson() == o.ToJson();
    }
}

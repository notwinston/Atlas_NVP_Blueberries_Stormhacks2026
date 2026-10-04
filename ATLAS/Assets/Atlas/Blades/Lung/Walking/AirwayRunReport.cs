using System;
using System.Collections.Generic;
using System.Globalization;
using Atlas.Link;
using UnityEngine;

namespace Atlas.Blades.Lung.Walking
{
    // P2-B12 run report (MP §10.2.8 row P2-B12: "4 forks without rescue in >= 9 of 10 naive runs; FMS <= 3/20").
    // Built from the fork / compass_choice / fms events of one run (MP §10.2.6; App. B.2 {t_us, scene, ev, data}). G6 §3.10.
    public sealed class AirwayRunReport
    {
        [Serializable] class ForkData { public string address; public string choice; public bool correct; public bool beacon_shown; public float t_s; }
        [Serializable] class FmsData { public float score; }
        [Serializable] class LineHead { public long t_us; public string scene; public string ev; }

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
                        var d = JsonUtility.FromJson<ForkData>(e.Data);
                        r.ForksWalked++;
                        if (d.correct) r.ForksCorrect++;
                        if (d.beacon_shown) r.BeaconShownCount++;
                        first = Math.Min(first, e.TUs); last = Math.Max(last, e.TUs);
                        break;
                    case "compass_choice": r.CompassForkLogged = true; break;
                    case "fms": r.FmsMax = Math.Max(r.FmsMax, JsonUtility.FromJson<FmsData>(e.Data).score); break;   // DV-G6-9: any scene
                }
            }
            r.RescueFree = r.RouteForks > 0 && r.ForksCorrect == r.RouteForks && r.BeaconShownCount == 0;
            r.DurationS = last >= first ? (last - first) / 1e6 : 0;
            return r;
        }

        public static AirwayRunReport FromJsonLines(IEnumerable<string> eventsJsonl, string endAddress)
        {
            var list = new List<EventMessage>();
            foreach (var line in eventsJsonl)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var h = JsonUtility.FromJson<LineHead>(line);
                list.Add(new EventMessage { TUs = h.t_us, Scene = h.scene, Ev = h.ev, Data = ExtractData(line) });
            }
            return FromEvents(list, endAddress);
        }

        // Serialises an event as one App. B.2 line (used by tests and by the phone export check).
        public static string ToJsonLine(in EventMessage e) =>
            "{\"t_us\":" + e.TUs.ToString(CultureInfo.InvariantCulture) + ",\"scene\":\"" + e.Scene + "\",\"ev\":\"" + e.Ev + "\",\"data\":" + (e.Data ?? "{}") + "}";

        // The balanced {...} object after "data":. Strings inside may hold braces, so quotes are tracked.
        static string ExtractData(string line)
        {
            int k = line.IndexOf("\"data\"", StringComparison.Ordinal);
            if (k < 0) return "{}";
            int s = line.IndexOf('{', k);
            if (s < 0) return "{}";
            int depth = 0; bool str = false;
            for (int i = s; i < line.Length; i++)
            {
                char c = line[i];
                if (str) { if (c == '\\') i++; else if (c == '"') str = false; continue; }
                if (c == '"') str = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return line.Substring(s, i - s + 1);
            }
            return "{}";
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

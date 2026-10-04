// Event payloads of the airway wing (Master Plan 10.2.6): fork {address, choice, correct, beacon_shown, t_s} and
// compass_choice. Plain structs; the adapter hands them to the Link's event writer. ToJson allocates: once per event.
using System;
using System.Globalization;
using System.Text;

namespace Atlas.Airway
{
    /// <summary>fork {address, choice, correct, beacon_shown, t_s}: the plan's five fields.</summary>
    public readonly struct ForkEvent
    {
        /// <summary>Address of the branch that divides (the fork).</summary>
        public readonly AirwayAddress Address;
        /// <summary>The child the walker took.</summary>
        public readonly AirwayChoice Choice;
        /// <summary>The choice is the beacon's step (the next letter of the target's address).</summary>
        public readonly bool Correct;
        /// <summary>The beacon was on screen when the choice was made.</summary>
        public readonly bool BeaconShown;
        /// <summary>Seconds from entering the fork chamber to the choice.</summary>
        public readonly double TSeconds;

        public ForkEvent(AirwayAddress address, AirwayChoice choice, bool correct, bool beaconShown, double tSeconds)
        {
            Address = address; Choice = choice; Correct = correct; BeaconShown = beaconShown; TSeconds = tSeconds;
        }

        public string ToJson() { return AirwayEvents.ForkJson(this); }
    }

    /// <summary>Which compass the walker's choice agrees with.</summary>
    public enum CompassFollowed : byte
    {
        Beacon = 0,
        Compass = 1,
        /// <summary>Both compasses pointed to the child taken (not a Two Compasses fork).</summary>
        Both = 2,
        /// <summary>The compass was tied or agreed with the beacon, and the walker took the other child.</summary>
        Neither = 3,
    }

    /// <summary>
    /// compass_choice. The plan names the event and no fields. "choice" (text) and "correct" are the two members
    /// atlas_pipelines/pilot.py reads; the others are additions of this core (see README.md, open point for lane D).
    /// </summary>
    public readonly struct CompassChoiceEvent
    {
        public readonly AirwayAddress Address;
        public readonly CompassFollowed Choice;
        /// <summary>The walker took the beacon's child (the one on the route).</summary>
        public readonly bool Correct;
        public readonly AirwayChoice Taken, Compass, Beacon;
        public readonly double Margin;
        public readonly double TSeconds;

        public CompassChoiceEvent(AirwayAddress address, CompassFollowed choice, bool correct, AirwayChoice taken,
            AirwayChoice compass, AirwayChoice beacon, double margin, double tSeconds)
        {
            Address = address; Choice = choice; Correct = correct; Taken = taken; Compass = compass; Beacon = beacon;
            Margin = margin; TSeconds = tSeconds;
        }

        public string ToJson() { return AirwayEvents.CompassChoiceJson(this); }
    }

    public static class AirwayEvents
    {
        /// <summary>The names as the plan writes them (10.2.6). The underscore of compass_choice does not match the
        /// event-name pattern of docs/contracts/lane-d-link.md section 2.6; lane D settles that, a rename is one edit here.</summary>
        public const string ForkName = "fork";
        public const string CompassChoiceName = "compass_choice";

        /// <summary>{"address":"RR","choice":"R","correct":true,"beacon_shown":false,"t_s":3.25}; t_s with 2 decimals.</summary>
        public static string ForkJson(ForkEvent e)
        {
            var sb = new StringBuilder(96);
            sb.Append("{\"address\":\"").Append(e.Address.ToString())
              .Append("\",\"choice\":\"").Append(Letter(e.Choice))
              .Append("\",\"correct\":").Append(e.Correct ? "true" : "false")
              .Append(",\"beacon_shown\":").Append(e.BeaconShown ? "true" : "false")
              .Append(",\"t_s\":").Append(F(e.TSeconds, "F2")).Append('}');
            return sb.ToString();
        }

        /// <summary>{"address":"","choice":"beacon","correct":true,"taken":"R","compass":"L","beacon":"R","margin":0.142047,"t_s":6.50}.</summary>
        public static string CompassChoiceJson(CompassChoiceEvent e)
        {
            var sb = new StringBuilder(144);
            sb.Append("{\"address\":\"").Append(e.Address.ToString())
              .Append("\",\"choice\":\"").Append(FollowedText(e.Choice))
              .Append("\",\"correct\":").Append(e.Correct ? "true" : "false")
              .Append(",\"taken\":\"").Append(Letter(e.Taken))
              .Append("\",\"compass\":\"").Append(Letter(e.Compass))
              .Append("\",\"beacon\":\"").Append(Letter(e.Beacon))
              .Append("\",\"margin\":").Append(F(e.Margin, "F6"))
              .Append(",\"t_s\":").Append(F(e.TSeconds, "F2")).Append('}');
            return sb.ToString();
        }

        public static string FollowedText(CompassFollowed f)
        {
            switch (f)
            {
                case CompassFollowed.Beacon: return "beacon";
                case CompassFollowed.Compass: return "compass";
                case CompassFollowed.Both: return "both";
                default: return "neither";
            }
        }

        /// <summary>"L", "R", or "" for none (a tied compass).</summary>
        static string Letter(AirwayChoice c)
        {
            return c == AirwayChoice.Left ? "L" : c == AirwayChoice.Right ? "R" : "";
        }

        static string F(double v, string fmt)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException("event numbers must be finite");
            string s = v.ToString(fmt, CultureInfo.InvariantCulture);
            if (s.StartsWith("-0.", StringComparison.Ordinal) && IsZeros(s)) s = s.Substring(1); // no "-0.00"
            return s;
        }

        static bool IsZeros(string s)
        {
            for (int i = 1; i < s.Length; i++) if (s[i] != '0' && s[i] != '.') return false;
            return true;
        }
    }
}

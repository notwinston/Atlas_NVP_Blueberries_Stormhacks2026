using System;
using Atlas.World;

namespace Atlas.App
{
    /// <summary>
    /// What the command line asks of the Editor-first path (contract README F9). Engine-free.
    /// <code>-atlasRoom sample_3x3 | path/to/room.json   -atlasReplay path/to/walk.poselog
    /// -atlasSource tracked|replay|walker   -atlasSpace S1   -atlasHand</code>
    /// </summary>
    public sealed class BootOptions
    {
        /// <summary>A fixture name or the path of a RoomModel file; null when not given.</summary>
        public string Room;
        /// <summary>The path of a pose log to replay; null when not given.</summary>
        public string Replay;
        /// <summary>AppParams.SourceTracked, SourceReplay or SourceWalker; -1 when not given.</summary>
        public int Source = -1;
        /// <summary>The space entered after the room hand-over.</summary>
        public SpaceId EntrySpace = SpaceId.Home;
        /// <summary>Stay in hand mode after the hand-over, where the Editor would go to the headset view.</summary>
        public bool StayInHand;

        /// <summary>True when Room names one of the fixtures.</summary>
        public bool RoomIsFixture { get { return Room != null && RoomFixtures.IsFixture(Room); } }

        public static BootOptions Parse(string[] args)
        {
            var options = new BootOptions();
            if (args == null) return options;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string value = i + 1 < args.Length ? args[i + 1] : null;
                if (Is(arg, "-atlasRoom") && value != null) { options.Room = value; i++; }
                else if (Is(arg, "-atlasReplay") && value != null)
                {
                    options.Replay = value;
                    if (options.Source < 0) options.Source = AppParams.SourceReplay;
                    i++;
                }
                else if (Is(arg, "-atlasSource") && value != null)
                {
                    if (Is(value, "tracked")) options.Source = AppParams.SourceTracked;
                    else if (Is(value, "replay")) options.Source = AppParams.SourceReplay;
                    else if (Is(value, "walker")) options.Source = AppParams.SourceWalker;
                    i++;
                }
                else if (Is(arg, "-atlasSpace") && value != null)
                {
                    SpaceId id;
                    if (SpaceIds.TryParseCode(value, out id)) options.EntrySpace = id;
                    i++;
                }
                else if (Is(arg, "-atlasHand")) options.StayInHand = true;
            }
            return options;
        }

        static bool Is(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}

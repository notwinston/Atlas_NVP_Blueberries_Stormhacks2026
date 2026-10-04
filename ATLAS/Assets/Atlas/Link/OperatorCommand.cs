using System.Globalization;

namespace Atlas.Link
{
    /// <summary>The six operator commands of the Console (Master Plan section 7.3.2). There are exactly six.</summary>
    public enum OperatorCommandKind : byte
    {
        None = 0,
        /// <summary>Switch space (<c>SceneCmd</c>).</summary>
        SwitchSpace = 1,
        /// <summary>Return to Home (<c>OperatorCmd home</c>).</summary>
        Home = 2,
        /// <summary>Recentre (<c>OperatorCmd recentre</c>).</summary>
        Recentre = 3,
        /// <summary>Force a quality tier 0-3 (<c>OperatorCmd quality</c>).</summary>
        Quality = 4,
        /// <summary>
        /// The Console's STOP button (<c>OperatorCmd cue</c>): fires the Bump Guard cue at STRONG level. The world
        /// keeps rendering; nothing freezes.
        /// </summary>
        Cue = 5,
        /// <summary>Demo lock on or off (<c>OperatorCmd lock</c>): the phone ignores <c>ParamSet</c> while on.</summary>
        Lock = 6
    }

    /// <summary>
    /// One operator command as the app consumes it. The link turns a received <c>SceneCmd</c> or
    /// <c>OperatorCmd</c> into this, queues it, and the app applies it on the next frame (section 7.5, rule 2).
    /// </summary>
    public struct OperatorCommand
    {
        public const string CmdHome = "home";
        public const string CmdRecentre = "recentre";
        public const string CmdQuality = "quality";
        public const string CmdCue = "cue";
        public const string CmdLock = "lock";

        /// <summary>Quality tiers: -1 hands the ladder back to automatic, 0-3 are its rungs, 4 is the high tier.</summary>
        public const int TierAuto = -1;
        public const int TierHigh = 4;

        public OperatorCommandKind Kind;
        /// <summary>Target space 0-6 of <see cref="OperatorCommandKind.SwitchSpace"/>.</summary>
        public int Space;
        /// <summary>Variant of the target space; empty for the default.</summary>
        public string Variant;
        /// <summary>Tier of <see cref="OperatorCommandKind.Quality"/>: -1 automatic, 0-3 the ladder rungs, 4 high.</summary>
        public int Tier;
        /// <summary>State of <see cref="OperatorCommandKind.Lock"/>.</summary>
        public bool On;
        /// <summary>The command's raw <c>args</c> object, for members this struct does not name. May be null.</summary>
        public JsonValue Args;

        public static OperatorCommand SwitchSpace(int space, string variant = null, JsonValue args = null)
        {
            return new OperatorCommand { Kind = OperatorCommandKind.SwitchSpace, Space = space, Variant = variant ?? string.Empty, Args = args };
        }

        public static OperatorCommand Home() { return new OperatorCommand { Kind = OperatorCommandKind.Home }; }

        public static OperatorCommand Recentre() { return new OperatorCommand { Kind = OperatorCommandKind.Recentre }; }

        public static OperatorCommand Quality(int tier)
        {
            return new OperatorCommand { Kind = OperatorCommandKind.Quality, Tier = tier < TierAuto ? TierAuto : tier > TierHigh ? TierHigh : tier };
        }

        /// <summary>The STOP button.</summary>
        public static OperatorCommand Stop() { return new OperatorCommand { Kind = OperatorCommandKind.Cue }; }

        public static OperatorCommand Lock(bool on) { return new OperatorCommand { Kind = OperatorCommandKind.Lock, On = on }; }

        /// <summary>The wire message for this command: a <c>SceneCmd</c> or an <c>OperatorCmd</c>; null for None.</summary>
        public JsonMessage ToMessage()
        {
            switch (Kind)
            {
                case OperatorCommandKind.SwitchSpace:
                    return new SceneCmdMessage
                    {
                        Space = "S" + Space.ToString(CultureInfo.InvariantCulture),
                        Variant = Variant ?? string.Empty,
                        Args = Args
                    };
                case OperatorCommandKind.Home: return new OperatorCmdMessage { Cmd = CmdHome, Args = Args };
                case OperatorCommandKind.Recentre: return new OperatorCmdMessage { Cmd = CmdRecentre, Args = Args };
                case OperatorCommandKind.Quality:
                    return new OperatorCmdMessage { Cmd = CmdQuality, Args = JsonValue.Object().Set("tier", Tier) };
                case OperatorCommandKind.Cue: return new OperatorCmdMessage { Cmd = CmdCue, Args = Args };
                case OperatorCommandKind.Lock:
                    return new OperatorCmdMessage { Cmd = CmdLock, Args = JsonValue.Object().Set("on", On) };
                default: return null;
            }
        }

        /// <summary>Reads a <c>SceneCmd</c>. False when its space is not S0..S6.</summary>
        public static bool TryFrom(SceneCmdMessage message, out OperatorCommand command)
        {
            command = default(OperatorCommand);
            if (message == null) return false;
            string s = message.Space;
            if (s == null || s.Length != 2 || (s[0] != 'S' && s[0] != 's') || s[1] < '0' || s[1] > '6') return false;
            command = SwitchSpace(s[1] - '0', message.Variant, message.Args);
            return true;
        }

        /// <summary>Reads an <c>OperatorCmd</c>. False for a command name this version does not know.</summary>
        public static bool TryFrom(OperatorCmdMessage message, out OperatorCommand command)
        {
            command = default(OperatorCommand);
            if (message == null) return false;
            JsonValue args = message.Args ?? JsonValue.Null;
            switch (message.Cmd)
            {
                case CmdHome: command = Home(); break;
                case CmdRecentre: command = Recentre(); break;
                case CmdQuality: command = Quality(args.GetInt("tier")); break;
                case CmdCue: command = Stop(); break;
                case CmdLock: command = Lock(args.GetBool("on")); break;
                default: return false;
            }
            command.Args = message.Args;
            return true;
        }

        /// <summary>The name written to the event log for this command.</summary>
        public string Name
        {
            get
            {
                switch (Kind)
                {
                    case OperatorCommandKind.SwitchSpace: return "space";
                    case OperatorCommandKind.Home: return CmdHome;
                    case OperatorCommandKind.Recentre: return CmdRecentre;
                    case OperatorCommandKind.Quality: return CmdQuality;
                    case OperatorCommandKind.Cue: return CmdCue;
                    case OperatorCommandKind.Lock: return CmdLock;
                    default: return "none";
                }
            }
        }

        /// <summary>The <c>cmd</c> event the phone logs when it applies this command (L10: every command is logged).</summary>
        public EventMessage ToEvent(long timeUs, string scene)
        {
            JsonValue data = JsonValue.Object().Set("cmd", Name);
            if (Kind == OperatorCommandKind.SwitchSpace) data.Set("space", Space).Set("variant", Variant ?? string.Empty);
            else if (Kind == OperatorCommandKind.Quality) data.Set("tier", Tier);
            else if (Kind == OperatorCommandKind.Lock) data.Set("on", On);
            return new EventMessage(timeUs, scene, LinkEvents.Command, data);
        }
    }
}

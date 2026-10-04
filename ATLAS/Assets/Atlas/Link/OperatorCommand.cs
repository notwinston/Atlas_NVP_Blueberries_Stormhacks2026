using System.Globalization;

namespace Atlas.Link
{
    /// <summary>
    /// The six operator commands of the Console (Master Plan section 7.3.2), and the Map page's walk input, a
    /// development aid that drives the phone's walker head from the Station's keyboard.
    /// </summary>
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
        Lock = 6,
        /// <summary>
        /// The walker's input from the Map page (<c>OperatorCmd walk</c>, args <c>{x, y, turn, speed}</c>): held
        /// until the next walk command or for <see cref="OperatorCommand.WalkHoldSeconds"/>, whichever is first.
        /// Moves the head only while <c>pose.source</c> is the walker.
        /// </summary>
        Walk = 7,
        /// <summary>
        /// The View page (<c>OperatorCmd view</c>, args <c>{on, port}</c>): the phone posts what it shows, one eye,
        /// as JPEG to the Hub's HTTP <c>port</c>. The Hub repeats <c>on</c> every second while anyone watches; the
        /// phone stops after <see cref="OperatorCommand.ViewHoldSeconds"/> without it, or at once on <c>off</c>.
        /// </summary>
        View = 8
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
        public const string CmdWalk = "walk";
        public const string CmdView = "view";

        /// <summary>Quality tiers: -1 hands the ladder back to automatic, 0-3 are its rungs, 4 is the high tier.</summary>
        public const int TierAuto = -1;
        public const int TierHigh = 4;

        /// <summary>
        /// Seconds a walk input stays in force with no newer one. The Console repeats a held input well inside
        /// this, so a lost Console or a dropped link stops the walker instead of leaving it walking.
        /// </summary>
        public const float WalkHoldSeconds = 0.6f;
        /// <summary>Degrees per second: the fastest turn a walk command asks for.</summary>
        public const float WalkMaxTurn = 360f;
        /// <summary>Metres per second: the walk speed range.</summary>
        public const float WalkMinSpeed = 0.1f, WalkMaxSpeed = 4f, WalkDefaultSpeed = 1f;

        /// <summary>Seconds the phone keeps sending its view with no newer <c>view on</c>; the Hub repeats it every second.</summary>
        public const float ViewHoldSeconds = 4f;
        /// <summary>The Hub's HTTP port when a view command names none (Appendix B.3).</summary>
        public const int DefaultHttpPort = 8080;

        public OperatorCommandKind Kind;
        /// <summary>Target space 0-6 of <see cref="OperatorCommandKind.SwitchSpace"/>.</summary>
        public int Space;
        /// <summary>Variant of the target space; empty for the default.</summary>
        public string Variant;
        /// <summary>Tier of <see cref="OperatorCommandKind.Quality"/>: -1 automatic, 0-3 the ladder rungs, 4 high.</summary>
        public int Tier;
        /// <summary>State of <see cref="OperatorCommandKind.Lock"/> and <see cref="OperatorCommandKind.View"/>.</summary>
        public bool On;
        /// <summary>View: the Hub's HTTP port, 1 to 65535.</summary>
        public int Port;
        /// <summary>Walk: right and forward in the walker's heading frame, -1 to 1 each.</summary>
        public float MoveX, MoveY;
        /// <summary>Walk: degrees per second, positive turns right (Unity's yaw sense).</summary>
        public float Turn;
        /// <summary>Walk: metres per second at full stick.</summary>
        public float Speed;
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

        /// <summary>A walk input, clamped: move -1..1 per axis, turn ±WalkMaxTurn, speed WalkMinSpeed..WalkMaxSpeed. NaN reads as 0.</summary>
        public static OperatorCommand Walk(float moveX, float moveY, float turn, float speed)
        {
            return new OperatorCommand
            {
                Kind = OperatorCommandKind.Walk,
                MoveX = Clamp(moveX, -1f, 1f, 0f),
                MoveY = Clamp(moveY, -1f, 1f, 0f),
                Turn = Clamp(turn, -WalkMaxTurn, WalkMaxTurn, 0f),
                Speed = Clamp(speed, WalkMinSpeed, WalkMaxSpeed, WalkDefaultSpeed)
            };
        }

        /// <summary>Start or stop the phone's view stream to the Hub's HTTP port; a port outside 1..65535 reads as the default.</summary>
        public static OperatorCommand View(bool on, int port = DefaultHttpPort)
        {
            return new OperatorCommand { Kind = OperatorCommandKind.View, On = on, Port = port > 0 && port <= 65535 ? port : DefaultHttpPort };
        }

        /// <summary>True for a walk command that asks for no movement and no turn: the walker stops.</summary>
        public bool IsStill { get { return MoveX == 0f && MoveY == 0f && Turn == 0f; } }

        static float Clamp(float v, float min, float max, float whenNaN)
        {
            if (float.IsNaN(v)) return whenNaN;
            return v < min ? min : (v > max ? max : v);
        }

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
                case OperatorCommandKind.Walk:
                    return new OperatorCmdMessage { Cmd = CmdWalk, Args = WalkArgs() };
                case OperatorCommandKind.View:
                    return new OperatorCmdMessage { Cmd = CmdView, Args = JsonValue.Object().Set("on", On).Set("port", Port) };
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
                case CmdWalk:
                    command = Walk(args.GetFloat("x"), args.GetFloat("y"), args.GetFloat("turn"),
                        args.GetFloat("speed", WalkDefaultSpeed));
                    break;
                case CmdView: command = View(args.GetBool("on"), args.GetInt("port", DefaultHttpPort)); break;
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
                    case OperatorCommandKind.Walk: return CmdWalk;
                    case OperatorCommandKind.View: return CmdView;
                    default: return "none";
                }
            }
        }

        JsonValue WalkArgs()
        {
            return JsonValue.Object().Set("x", MoveX).Set("y", MoveY).Set("turn", Turn).Set("speed", Speed);
        }

        /// <summary>The <c>cmd</c> event the phone logs when it applies this command (L10: every command is logged).</summary>
        public EventMessage ToEvent(long timeUs, string scene)
        {
            JsonValue data = JsonValue.Object().Set("cmd", Name);
            if (Kind == OperatorCommandKind.SwitchSpace) data.Set("space", Space).Set("variant", Variant ?? string.Empty);
            else if (Kind == OperatorCommandKind.Quality) data.Set("tier", Tier);
            else if (Kind == OperatorCommandKind.Lock) data.Set("on", On);
            else if (Kind == OperatorCommandKind.View) data.Set("on", On).Set("port", Port);
            else if (Kind == OperatorCommandKind.Walk) data.Set("x", MoveX).Set("y", MoveY).Set("turn", Turn).Set("speed", Speed);
            return new EventMessage(timeUs, scene, LinkEvents.Command, data);
        }
    }
}

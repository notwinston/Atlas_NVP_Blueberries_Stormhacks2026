using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Atlas.Link;
using Atlas.World;

namespace Atlas.App.Menu
{
    /// <summary>The four pages of the on-device menu, in the order the page row steps through them.</summary>
    public enum MenuPage : byte
    {
        Spaces = 0,
        Quality = 1,
        Parameters = 2,
        Session = 3
    }

    /// <summary>What one row of the menu is.</summary>
    public enum MenuRowKind : byte
    {
        /// <summary>The first row of every page: stepping it changes the page.</summary>
        Page = 0,
        /// <summary>The name of a parameter group. Never focused.</summary>
        Heading = 1,
        /// <summary>A line of status text. Never focused.</summary>
        Info = 2,
        /// <summary>One registry parameter.</summary>
        Param = 3,
        /// <summary>One space the walker can switch to.</summary>
        Space = 4,
        /// <summary>Something the menu does when the row is confirmed.</summary>
        Command = 5
    }

    public enum MenuCommand : byte
    {
        None = 0,
        ReturnHome = 1,
        ResetSwitches = 2,
        Recentre = 3,
        Rescan = 4,
        Close = 5
    }

    /// <summary>Which status line an <see cref="MenuRowKind.Info"/> row shows.</summary>
    public enum MenuInfo : byte
    {
        None = 0,
        Tier = 1,
        Link = 2,
        Station = 3,
        Session = 4,
        Version = 5,
        Room = 6
    }

    /// <summary>One row. A plain value: the model keeps its rows in one list that is refilled in place.</summary>
    public struct MenuRow
    {
        public MenuRowKind Kind;
        /// <summary>The parameter of a <see cref="MenuRowKind.Param"/> row.</summary>
        public ParamEntry Entry;
        public SpaceId Space;
        public MenuCommand Command;
        public MenuInfo Info;
        /// <summary>The group key of a heading, the text of a command or of a status line.</summary>
        public string Label;

        /// <summary>Headings and status lines are skipped by the focus.</summary>
        public bool Focusable { get { return Kind != MenuRowKind.Heading && Kind != MenuRowKind.Info; } }
    }

    /// <summary>
    /// The figures of the Quality and Session pages that are not parameters. Filled by the menu's adapter twice
    /// per second while the menu is open; the strings are ones their owners already hold, so filling allocates
    /// nothing.
    /// </summary>
    public struct MenuStatus
    {
        /// <summary>"offline", "resync", "connected" or "degraded".</summary>
        public string Link;
        public int RttMs;
        /// <summary>The pairing address of the Station: dotted IPv4 and the port.</summary>
        public string StationHost;
        public int StationPort;
        public string Session;
        public string Version;
        /// <summary>Id of the active RoomModel, or null.</summary>
        public string Room;
        /// <summary>The quality tier in force, 0 to 4.</summary>
        public int Tier;
        /// <summary>The phone's thermal state, 0 to 3.</summary>
        public int Thermal;

        public bool Same(in MenuStatus other)
        {
            return RttMs == other.RttMs && StationPort == other.StationPort && Tier == other.Tier && Thermal == other.Thermal
                && string.Equals(Link, other.Link, StringComparison.Ordinal)
                && string.Equals(StationHost, other.StationHost, StringComparison.Ordinal)
                && string.Equals(Session, other.Session, StringComparison.Ordinal)
                && string.Equals(Version, other.Version, StringComparison.Ordinal)
                && string.Equals(Room, other.Room, StringComparison.Ordinal);
        }
    }

    /// <summary>What the Session page asks of the app.</summary>
    public interface IMenuActions
    {
        /// <summary>Puts the walker back on the floor under the head and restarts the pose filter.</summary>
        void Recentre();
        /// <summary>Starts a new scan of the room. The phone goes back to the hand.</summary>
        void Rescan();
    }

    /// <summary>
    /// The engine-free core of the on-device menu (Master Plan section 12.4, task P1-D13; lane D contract
    /// section 5.7): pages, rows, focus, step sizes and pending values. Every value goes through the registry,
    /// so it is clamped there, reaches the Station when the link is up and is in the phone's event log when it
    /// is not (gate G6). The view draws what this class holds and the adapter feeds it input; neither decides
    /// anything.
    /// </summary>
    public sealed class MenuModel
    {
        /// <summary>Rows shown at once; the list scrolls with the focus.</summary>
        public const int VisibleRows = 9;
        public const int PageCount = 4;
        /// <summary>A float parameter moves by this fraction of its range per step.</summary>
        public const float FloatStepFraction = 1f / 20f;

        public const string EventMenu = "menu";
        public const string EventParamSet = "param.set";

        /// <summary>
        /// The kill switches of Master Plan section 12.4 in the order of lane D's contract, section 1.5, and
        /// after them the switch of the tracked hands (documents/ATLAS_Hands_Design.md section 3.7, E11), so
        /// that hands can be turned off in the headset with the Station off. The Quality page lists the ones
        /// that are declared.
        /// </summary>
        public static readonly string[] KillSwitchIds =
        {
            "space.id", "demoLock", "thermal.pinTier", "render.scale", "render.msaa", "curved.cullRho", "curved.mode",
            "cells.viewDepth", "planet.R", "planet.fogRho", "glued.mode", "glued.copies", "rooms.count", "portal.depth",
            "slicer.source", "pose.gyroPredict", "scan.route", "link.enabled", "link.stateHz", "telemetry.mode",
            "forge.backend", "hands.enabled"
        };

        // Resetting space.id would walk the wearer out of the space they are standing in.
        const string SpaceParam = "space.id";
        /// <summary>The tracked hands' switch, shown at the top of the Spaces page as "Hands".</summary>
        public const string HandsEnabled = "hands.enabled";

        static readonly string[] s_pageNames = { "Spaces", "Quality", "Parameters", "Session" };

        readonly ParamRegistry _registry;
        readonly ISpaceHost _spaces;
        readonly IMenuActions _actions;
        readonly List<MenuRow> _rows = new List<MenuRow>(96);
        readonly List<string> _groups = new List<string>(24);
        readonly StringBuilder _json = new StringBuilder(96);

        MenuPage _page;
        int _selected, _top;
        bool _hasPending;
        float _pendingValue;
        MenuStatus _status;
        int _seenManifest = -1, _seenVersion = -1;
        bool _seenHasSpace;
        SpaceId _seenSpace;

        /// <param name="spaces">SpaceLab; null leaves the Spaces page empty (tests).</param>
        /// <param name="actions">Recentre and rescan; null makes those rows do nothing.</param>
        public MenuModel(ParamRegistry registry, ISpaceHost spaces = null, IMenuActions actions = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _registry = registry;
            _spaces = spaces;
            _actions = actions;
            Rebuild();
        }

        /// <summary>Where "menu" and "param.set" events go; null logs nothing here.</summary>
        public IEventLog Events { get; set; }

        public ParamRegistry Registry { get { return _registry; } }
        public MenuPage Page { get { return _page; } }
        public int RowCount { get { return _rows.Count; } }
        /// <summary>The focused row.</summary>
        public int Selected { get { return _selected; } }
        /// <summary>The first visible row.</summary>
        public int Top { get { return _top; } }
        /// <summary>Goes up whenever what the view shows may have changed.</summary>
        public int Revision { get; private set; }
        /// <summary>Set by the Close row and by a row that hands the walker over to something else.</summary>
        public bool CloseRequested { get; private set; }
        /// <summary>True while the focused row holds a value that Confirm will apply (a parameter flagged onRelease).</summary>
        public bool HasPending { get { return _hasPending; } }
        public float PendingValue { get { return _pendingValue; } }
        /// <summary>True when Apply does something on the focused row: a pending value, or a space other than
        /// the one the walker stands in. The view lights the Apply chip with it.</summary>
        public bool ApplyReady
        {
            get
            {
                if (_hasPending) return true;
                if (_selected >= _rows.Count) return false;
                MenuRow row = _rows[_selected];
                return row.Kind == MenuRowKind.Space && !IsCurrent(row.Space);
            }
        }
        public ref readonly MenuStatus Status { get { return ref _status; } }

        /// <summary>Raised when a Spaces row is confirmed, before SpaceLab is asked.</summary>
        public event Action<SpaceId> SpaceRequested;

        public static string PageName(MenuPage page)
        {
            int i = (int)page;
            return i < PageCount ? s_pageNames[i] : "?";
        }

        public MenuRow Row(int index) { return _rows[index]; }

        /// <summary>True for the space the walker stands in.</summary>
        public bool IsCurrent(SpaceId space) { return _seenHasSpace && _seenSpace == space; }

        // ---- opening and closing -------------------------------------------------------------------------

        /// <summary>The menu was opened: the rows are brought up to date and the event is logged.</summary>
        public void NoteOpened()
        {
            CloseRequested = false;
            ClearPending();
            Rebuild();
            LogMenu("open");
        }

        public void NoteClosed()
        {
            CloseRequested = false;
            ClearPending();
            LogMenu("close");
        }

        // ---- keeping up with the app -----------------------------------------------------------------------

        /// <summary>
        /// Once per frame while the menu is open: rebuilds the rows when a parameter was declared, a group
        /// changed its state or the walker changed space, and marks the view stale when a value changed (a
        /// Console slider, the thermal ladder). A few integer compares when nothing did.
        /// </summary>
        public void Sync()
        {
            bool hasSpace = _spaces != null && _spaces.HasSpace;
            SpaceId space = hasSpace ? _spaces.Current : SpaceId.Home;
            if (_registry.ManifestVersion != _seenManifest || hasSpace != _seenHasSpace || space != _seenSpace)
            {
                Rebuild();
                return;
            }
            if (_registry.Version != _seenVersion)
            {
                _seenVersion = _registry.Version;
                Revision++;
            }
        }

        /// <summary>The status lines. The view is marked stale only when a figure changed.</summary>
        public void SetStatus(in MenuStatus status)
        {
            if (_status.Same(in status)) return;
            _status = status;
            // Only these two pages show a status line.
            if (_page == MenuPage.Quality || _page == MenuPage.Session) Revision++;
        }

        /// <summary>Rebuilds the rows of the current page from the registry and the space host.</summary>
        public void Rebuild()
        {
            _seenManifest = _registry.ManifestVersion;
            _seenVersion = _registry.Version;
            _seenHasSpace = _spaces != null && _spaces.HasSpace;
            _seenSpace = _seenHasSpace ? _spaces.Current : SpaceId.Home;

            ParamEntry keep = _selected < _rows.Count ? _rows[_selected].Entry : null;
            _rows.Clear();
            _rows.Add(new MenuRow { Kind = MenuRowKind.Page });
            switch (_page)
            {
                case MenuPage.Spaces: BuildSpaces(); break;
                case MenuPage.Quality: BuildQuality(); break;
                case MenuPage.Parameters: BuildParameters(); break;
                default: BuildSession(); break;
            }

            // The focus stays on the parameter it was on when the list moved under it.
            if (keep != null)
            {
                for (int i = 0; i < _rows.Count; i++)
                {
                    if (!ReferenceEquals(_rows[i].Entry, keep)) continue;
                    _selected = i;
                    break;
                }
            }
            if (_selected >= _rows.Count || !ReferenceEquals(_rows[_selected].Entry, keep)) ClearPending();
            _selected = NearestFocusable(_selected);
            EnsureVisible();
            Revision++;
        }

        void BuildSpaces()
        {
            if (_spaces == null) return;
            // The tracked hands' switch first, where the menu opens: hands.enabled itself, so it stays in step
            // with the kill switch on the Quality page and with the Station.
            ParamEntry hands = _registry.Find(HandsEnabled);
            if (hands != null) _rows.Add(new MenuRow { Kind = MenuRowKind.Param, Entry = hands, Label = "Hands" });
            for (int i = 0; i < SpaceIds.Count; i++)
            {
                var id = (SpaceId)i;
                if (_spaces.IsAvailable(id)) _rows.Add(new MenuRow { Kind = MenuRowKind.Space, Space = id });
            }
            _rows.Add(new MenuRow { Kind = MenuRowKind.Command, Command = MenuCommand.ReturnHome, Label = "Return to Home" });
        }

        void BuildQuality()
        {
            _rows.Add(new MenuRow { Kind = MenuRowKind.Info, Info = MenuInfo.Tier, Label = "Tier in force" });
            for (int i = 0; i < KillSwitchIds.Length; i++)
            {
                ParamEntry entry = _registry.Find(KillSwitchIds[i]);
                if (entry != null) _rows.Add(new MenuRow { Kind = MenuRowKind.Param, Entry = entry });
            }
            _rows.Add(new MenuRow { Kind = MenuRowKind.Command, Command = MenuCommand.ResetSwitches, Label = "Reset switches to defaults" });
        }

        void BuildParameters()
        {
            _registry.GetGroups(_groups);
            // The group of the space the walker stands in comes first: those are the values they came to change.
            string first = _seenHasSpace ? SpaceIds.ParamGroup(_seenSpace) : null;
            if (first != null && _groups.Contains(first)) AddGroup(first);
            for (int g = 0; g < _groups.Count; g++)
                if (!string.Equals(_groups[g], first, StringComparison.Ordinal)) AddGroup(_groups[g]);
        }

        void AddGroup(string group)
        {
            _rows.Add(new MenuRow { Kind = MenuRowKind.Heading, Label = group });
            for (int i = 0; i < _registry.Count; i++)
            {
                ParamEntry entry = _registry[i];
                if (string.Equals(entry.Group, group, StringComparison.Ordinal))
                    _rows.Add(new MenuRow { Kind = MenuRowKind.Param, Entry = entry });
            }
        }

        void BuildSession()
        {
            _rows.Add(new MenuRow { Kind = MenuRowKind.Command, Command = MenuCommand.Recentre, Label = "Recentre" });
            _rows.Add(new MenuRow { Kind = MenuRowKind.Command, Command = MenuCommand.Rescan, Label = "Rescan the room" });
            _rows.Add(new MenuRow { Kind = MenuRowKind.Info, Info = MenuInfo.Link, Label = "Link" });
            _rows.Add(new MenuRow { Kind = MenuRowKind.Info, Info = MenuInfo.Station, Label = "Station" });
            _rows.Add(new MenuRow { Kind = MenuRowKind.Info, Info = MenuInfo.Session, Label = "Session" });
            _rows.Add(new MenuRow { Kind = MenuRowKind.Info, Info = MenuInfo.Room, Label = "Room" });
            _rows.Add(new MenuRow { Kind = MenuRowKind.Info, Info = MenuInfo.Version, Label = "Version" });
            _rows.Add(new MenuRow { Kind = MenuRowKind.Command, Command = MenuCommand.Close, Label = "Close the menu" });
        }

        // ---- focus -----------------------------------------------------------------------------------------

        /// <summary>Shows another page with the focus on its page row.</summary>
        public void SetPage(MenuPage page)
        {
            int i = (int)page;
            if (i >= PageCount || page == _page) return;
            _page = page;
            _selected = 0;
            _top = 0;
            ClearPending();
            Rebuild();
        }

        /// <summary>Moves the focus up (negative) or down by that many focusable rows, stopping at the ends.</summary>
        public void Move(int delta)
        {
            if (delta == 0) return;
            int direction = delta > 0 ? 1 : -1;
            int target = _selected;
            for (int steps = delta * direction; steps > 0; steps--)
            {
                int next = target + direction;
                while (next >= 0 && next < _rows.Count && !_rows[next].Focusable) next += direction;
                if (next < 0 || next >= _rows.Count) break;
                target = next;
            }
            Focus(target);
        }

        /// <summary>Puts the focus on a row: what a gaze dwell on it does. A heading or a status line is ignored.</summary>
        public void Select(int row)
        {
            if (row < 0 || row >= _rows.Count || !_rows[row].Focusable) return;
            Focus(row);
        }

        void Focus(int row)
        {
            if (row == _selected) return;
            _selected = row;
            ClearPending();
            EnsureVisible();
            Revision++;
        }

        int NearestFocusable(int row)
        {
            if (row >= _rows.Count) row = _rows.Count - 1;
            if (row < 0) row = 0;
            for (int i = row; i < _rows.Count; i++) if (_rows[i].Focusable) return i;
            for (int i = row; i >= 0; i--) if (_rows[i].Focusable) return i;
            return 0;
        }

        void EnsureVisible()
        {
            int top = _top;
            if (_selected < top) top = _selected;
            if (_selected >= top + VisibleRows) top = _selected - VisibleRows + 1;
            // The heading above the first parameter of a group scrolls in with it.
            if (_selected > 0 && _selected - 1 < top && !_rows[_selected - 1].Focusable) top = _selected - 1;
            int last = _rows.Count - VisibleRows;
            if (top > last) top = last;
            if (top < 0) top = 0;
            _top = top;
        }

        // ---- changing values -------------------------------------------------------------------------------

        /// <summary>The size of one step of a parameter: 1/20 of a float's range, 1 for the other kinds.</summary>
        public static float StepSize(ParamEntry entry)
        {
            if (entry == null) return 0f;
            return entry.Kind == ParamKind.Float ? (entry.Max - entry.Min) * FloatStepFraction : 1f;
        }

        /// <summary>
        /// The value one step up or down from <paramref name="from"/>. A float stays on the grid of twenty steps
        /// that starts at its minimum; an int and an enum move by one and do not wrap; a bool toggles. The result
        /// is inside the range: the registry clamps again when it is set.
        /// </summary>
        public static float Stepped(ParamEntry entry, float from, int direction)
        {
            if (entry == null || direction == 0) return from;
            int sign = direction > 0 ? 1 : -1;
            switch (entry.Kind)
            {
                case ParamKind.Bool:
                    return from >= 0.5f ? 0f : 1f;
                case ParamKind.Float:
                {
                    float step = StepSize(entry);
                    if (!(step > 0f)) return entry.Min;
                    double k = Math.Round((from - entry.Min) / step, MidpointRounding.AwayFromZero) + sign;
                    return entry.Sanitise((float)(entry.Min + k * step));
                }
                default:
                    return entry.Sanitise(from + sign);
            }
        }

        /// <summary>
        /// One step on the focused row, -1 or +1: the dwell on "-" or "+", or left and right on the gamepad. A
        /// parameter flagged onRelease only shows the new value until <see cref="Confirm"/>, because applying it
        /// rebuilds geometry. A command row runs on +1. A space row does not: it is selected, and Apply takes the
        /// walker there.
        /// </summary>
        public void Step(int direction)
        {
            if (direction == 0 || _selected >= _rows.Count) return;
            MenuRow row = _rows[_selected];
            switch (row.Kind)
            {
                case MenuRowKind.Page:
                {
                    int page = (int)_page + (direction > 0 ? 1 : -1);
                    if (page >= 0 && page < PageCount) SetPage((MenuPage)page);
                    break;
                }
                case MenuRowKind.Param:
                {
                    ParamEntry entry = row.Entry;
                    float value = Stepped(entry, _hasPending ? _pendingValue : entry.Value, direction);
                    if (!entry.OnRelease)
                    {
                        Apply(entry, value);
                        break;
                    }
                    _hasPending = value != entry.Value;
                    _pendingValue = value;
                    Revision++;
                    break;
                }
                case MenuRowKind.Command:
                    if (direction > 0) Run(in row);
                    break;
            }
        }

        /// <summary>Applies the pending value of the focused row, toggles a bool, or runs a space or command row.</summary>
        public void Confirm()
        {
            if (_selected >= _rows.Count) return;
            MenuRow row = _rows[_selected];
            switch (row.Kind)
            {
                case MenuRowKind.Param:
                    if (_hasPending)
                    {
                        float value = _pendingValue;
                        ClearPending();
                        Apply(row.Entry, value);
                    }
                    else if (row.Entry.Kind == ParamKind.Bool && !row.Entry.OnRelease)
                    {
                        Apply(row.Entry, Stepped(row.Entry, row.Entry.Value, 1));
                    }
                    break;
                case MenuRowKind.Space:
                case MenuRowKind.Command:
                    Run(in row);
                    break;
            }
        }

        // The one place the menu writes a parameter. ParamSource.Local is "the phone itself": the link echoes
        // the applied value to the Station and the link driver writes it to the phone's log.
        bool Apply(ParamEntry entry, float value)
        {
            bool changed = _registry.Set(entry, value, ParamSource.Local);
            // A callback may have declared parameters or changed a group: Sync sees that through the versions.
            _seenVersion = _registry.Version;
            Revision++;
            if (changed) LogSet(entry);
            return changed;
        }

        void Run(in MenuRow row)
        {
            if (row.Kind == MenuRowKind.Space)
            {
                Action<SpaceId> requested = SpaceRequested;
                if (requested != null) requested(row.Space);
                bool accepted = _spaces != null && _spaces.RequestSwitch(row.Space);
                // The walker asked to be somewhere else: the menu gets out of the way of the fade.
                if (accepted) CloseRequested = true;
                Revision++;
                return;
            }

            switch (row.Command)
            {
                case MenuCommand.ReturnHome:
                    if (_spaces != null && _spaces.ReturnHome()) CloseRequested = true;
                    break;
                case MenuCommand.ResetSwitches:
                    ResetSwitches();
                    break;
                case MenuCommand.Recentre:
                    if (_actions != null) _actions.Recentre();
                    break;
                case MenuCommand.Rescan:
                    CloseRequested = true;
                    if (_actions != null) _actions.Rescan();
                    break;
                case MenuCommand.Close:
                    CloseRequested = true;
                    break;
            }
            Revision++;
        }

        /// <summary>Puts every kill switch but space.id back to its default. Returns how many changed.</summary>
        public int ResetSwitches()
        {
            int changed = 0;
            for (int i = 0; i < KillSwitchIds.Length; i++)
            {
                if (KillSwitchIds[i] == SpaceParam) continue;
                ParamEntry entry = _registry.Find(KillSwitchIds[i]);
                if (entry != null && Apply(entry, entry.Default)) changed++;
            }
            ClearPending();
            return changed;
        }

        void ClearPending()
        {
            _hasPending = false;
            _pendingValue = 0f;
        }

        // ---- the event log ---------------------------------------------------------------------------------

        void LogMenu(string state)
        {
            IEventLog events = Events;
            if (events != null) events.Log(EventMenu, "state", state);
        }

        // {"name":"render.scale","value":0.7,"src":"menu"}: an edge, written once per change.
        void LogSet(ParamEntry entry)
        {
            IEventLog events = Events;
            if (events == null) return;
            _json.Length = 0;
            _json.Append("{\"name\":\"").Append(entry.Name).Append("\",\"value\":");
            if (entry.Kind == ParamKind.Float) _json.Append(entry.Value.ToString("R", CultureInfo.InvariantCulture));
            else _json.Append(entry.Int.ToString(CultureInfo.InvariantCulture));
            _json.Append(",\"src\":\"menu\"}");
            events.LogJson(EventParamSet, _json.ToString());
        }
    }
}

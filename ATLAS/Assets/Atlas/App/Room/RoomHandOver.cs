using System;
using Atlas.World;

namespace Atlas.App
{
    /// <summary>
    /// Makes a RoomModel the active room, whatever it came from (a scan, a saved file, a fixture), in one
    /// fixed order (lane C contract, section 14.2):
    /// <list type="number">
    /// <item>the room's anchor goes to the pose pipeline;</item>
    /// <item>the space host takes the room: the kernel is configured for its lap square, the walker reset
    /// is latched, and RoomChanged hands the room to the room frame (pose pipeline), Bump Guard, the room
    /// content and the link, in the order they were installed;</item>
    /// <item>in the first frame whose head is in the room frame the frame loop places the walker;</item>
    /// <item>then the entry space is requested: Home, which is the built-in grid space while no Home space
    /// is registered. Never in the same frame as step 2.</item>
    /// </list>
    /// </summary>
    public sealed class RoomHandOver : IAfterFrameStep
    {
        readonly ISpaceHost _host;
        readonly IWalkerReset _walker;
        readonly Action<object> _setAnchor;
        bool _enterPending;
        bool _roomSet;

        /// <param name="setAnchor">Gives the room's anchor object to the pose pipeline; null where there is none.</param>
        public RoomHandOver(ISpaceHost host, IWalkerReset walker, Action<object> setAnchor = null)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (walker == null) throw new ArgumentNullException(nameof(walker));
            _host = host;
            _walker = walker;
            _setAnchor = setAnchor;
        }

        /// <summary>The session's event log. May be null.</summary>
        public IEventLog Events { get; set; }
        /// <summary>The space entered after a hand-over. Home unless a command line or a test says otherwise.</summary>
        public SpaceId EntrySpace { get; set; } = SpaceId.Home;
        /// <summary>False: the room is made active but no space is entered (onboarding enters Home itself, after its headset prompt).</summary>
        public bool AutoEnter { get; set; } = true;
        /// <summary>True from Activate until the entry space has been requested.</summary>
        public bool EnterPending { get { return _enterPending; } }

        /// <summary>Raised at the end of Activate, with the room in force.</summary>
        public event Action<RoomModel> Activated;

        /// <summary>
        /// Hands the room over. anchor is the room's AR anchor transform, or null for a fixture, a replay or
        /// a room loaded from a file (the room's own anchor pose is used then).
        /// </summary>
        public void Activate(RoomModel room, object anchor = null)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (_setAnchor != null) _setAnchor(anchor);
            _host.SetRoom(room);
            _roomSet = true;
            // With a space entered the host reloads it by itself; otherwise the entry space follows.
            _enterPending = AutoEnter && !_host.HasSpace && _host.State == SpaceHostState.Empty;
            IEventLog events = Events;
            if (events != null) events.Log("room.active", "id", room.Id ?? string.Empty);
            Action<RoomModel> activated = Activated;
            if (activated != null) activated(room);
        }

        /// <summary>Requests the entry space now if the room frame is in force. For onboarding, after its prompt.</summary>
        public bool EnterNow()
        {
            if (!_roomSet || _host.Room == null) return false;
            _enterPending = true;
            return TryEnter();
        }

        /// <summary>Step 8b: requests the entry space once the walker has been placed in the room frame.</summary>
        public void AfterFrame(in FrameContext frame)
        {
            if (_enterPending) TryEnter();
        }

        bool TryEnter()
        {
            if (_walker.RoomResetPending) return false;
            _enterPending = false;
            if (_host.HasSpace || _host.State != SpaceHostState.Empty) return true;
            SpaceId entry = _host.IsAvailable(EntrySpace) ? EntrySpace : SpaceId.Home;
            if (_host.RequestSwitch(entry)) return true;
            IEventLog events = Events;
            if (events != null) events.Log("room.noEntrySpace", "space", SpaceIds.Code(entry));
            return false;
        }
    }
}

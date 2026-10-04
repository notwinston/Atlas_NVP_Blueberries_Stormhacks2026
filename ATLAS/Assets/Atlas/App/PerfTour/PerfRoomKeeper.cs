using Atlas.World;

namespace Atlas.App
{
    /// <summary>
    /// Which room a performance run stands in, and the session's own room while the run borrows its place.
    /// The tour and the soaks are measured on the budget fixture (Master Plan task P1-A14); the ladder check
    /// (task P1-A07) only needs Home, so it stays in the room that is active. The walker's scanned room and
    /// its AR anchor are remembered when a run starts and handed back when it ends: the walls drawn and the
    /// walls Bump Guard watches are the real ones again. Engine-free.
    /// </summary>
    public sealed class PerfRoomKeeper
    {
        readonly string _runRoomId;
        RoomModel _saved;
        object _savedAnchor;
        bool _usesRunRoom;

        /// <param name="runRoomId">The id of the fixture the measured runs stand in.</param>
        public PerfRoomKeeper(string runRoomId)
        {
            _runRoomId = runRoomId ?? string.Empty;
        }

        /// <summary>True while a room waits to be handed back.</summary>
        public bool Holding { get { return _saved != null; } }

        /// <summary>True for the runs whose numbers are those of the budget fixture: every run but the ladder check.</summary>
        public static bool UsesRunRoom(PerfRunKind kind)
        {
            return kind != PerfRunKind.None && kind != PerfRunKind.Ladder;
        }

        /// <summary>
        /// A run starts. active is the room in force and anchor its AR anchor (null for a fixture or a room
        /// from a file). Both are kept when the run is about to put the fixture in their place.
        /// </summary>
        public void Begin(PerfRunKind kind, RoomModel active, object anchor)
        {
            _usesRunRoom = UsesRunRoom(kind);
            bool keep = _usesRunRoom && active != null && active.Id != _runRoomId;
            _saved = keep ? active : null;
            _savedAnchor = keep ? anchor : null;
        }

        /// <summary>The room id the run's file names: the fixture, or for the ladder check the room it found.</summary>
        public string RoomIdOfRun(RoomModel active)
        {
            if (_usesRunRoom || active == null) return _runRoomId;
            return active.Id ?? string.Empty;
        }

        /// <summary>
        /// True when the fixture must be made active before the run can go on. The ladder check asks for it
        /// only when there is no room at all, since Home cannot be entered without one.
        /// </summary>
        public bool NeedsRunRoom(RoomModel active)
        {
            if (active == null) return true;
            return _usesRunRoom && active.Id != _runRoomId;
        }

        /// <summary>
        /// The run has ended. True when the remembered room must be made active again, with its anchor. False
        /// when nothing was remembered, or when the room in force is no longer the fixture: either the run
        /// never got as far as replacing the room, or a newer room was scanned or loaded meanwhile and stays.
        /// The remembered room is let go either way.
        /// </summary>
        public bool TakeBack(RoomModel active, out RoomModel room, out object anchor)
        {
            room = _saved;
            anchor = _savedAnchor;
            _saved = null;
            _savedAnchor = null;
            if (room == null) return false;
            if (active != null && active.Id != _runRoomId)
            {
                room = null;
                anchor = null;
                return false;
            }
            return true;
        }
    }
}

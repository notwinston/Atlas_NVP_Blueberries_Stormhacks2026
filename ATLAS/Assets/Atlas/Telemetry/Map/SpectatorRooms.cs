using System;
using System.Text;
using Atlas.Link;
using Atlas.World;

namespace Atlas.Telemetry
{
    /// <summary>
    /// Where the Spectator gets the walker's RoomModel from (lane D contract, section 5.8). Nothing here waits:
    /// a search is begun, and asked about once per frame.
    /// </summary>
    public interface ISpectatorRooms
    {
        /// <summary>
        /// Begins looking for a room; an earlier search is abandoned. roomId may be empty: the room is then
        /// known only by the outline the phone sent, which may be null as well.
        /// </summary>
        void Begin(string roomId, string forgeUrl, RoomOutlineMessage outline);
        /// <summary>True once the search has ended. room is null when nothing was found.</summary>
        bool TryTake(out RoomModel room);
    }

    /// <summary>Whether a RoomModel is the room an outline message describes: the same floor, to the millimetre.</summary>
    public static class SpectatorRoomMatch
    {
        public const float Tolerance = 0.002f;

        public static bool Agrees(RoomModel room, RoomOutlineMessage outline)
        {
            if (room == null || outline == null) return false;
            if (room.Outline.Length != outline.PointCount) return false;
            for (int i = 0; i < outline.PointCount; i++)
            {
                if (Math.Abs(room.Outline[i].x - outline.PointsXZ[2 * i]) > Tolerance) return false;
                if (Math.Abs(room.Outline[i].y - outline.PointsXZ[2 * i + 1]) > Tolerance) return false;
            }
            if (Math.Abs(room.FreeRect.Wf - outline.RectW) > Tolerance) return false;
            if (Math.Abs(room.FreeRect.Df - outline.RectD) > Tolerance) return false;
            int objects = 0;
            for (int i = 0; i < room.Objects.Length; i++) if (room.Objects[i] != null) objects++;
            return objects == outline.BoxCount || outline.BoxCount == RoomOutlineMessage.MaxBoxes;
        }
    }

    /// <summary>
    /// The Spectator's room source: a fixture by its name, else <c>GET /rooms/{roomId}</c> from the Forge the
    /// Hub named. With no room id (a replayed walk recorded without one) a fixture is taken only if its floor
    /// is the outline the phone sent; no room is ever made up. Engine-free: the Station passes the Unity HTTP
    /// transport and the Resources loader, tests pass their own.
    /// </summary>
    public sealed class SpectatorRoomSource : ISpectatorRooms
    {
        public const float TimeoutSeconds = 5f;

        readonly IForgeTransport _transport;
        readonly Func<string, RoomModel> _loadFixture;
        RoomModel _result;
        bool _done = true, _taken = true;
        int _handle;

        /// <param name="transport">HTTP to the Forge; null means fixtures only.</param>
        /// <param name="loadFixture">Loads a fixture by name; may return null or throw.</param>
        public SpectatorRoomSource(IForgeTransport transport, Func<string, RoomModel> loadFixture)
        {
            _transport = transport;
            _loadFixture = loadFixture;
        }

        /// <summary>Why the last search found nothing, for the log; null when it found a room.</summary>
        public string LastError { get; private set; }

        public void Begin(string roomId, string forgeUrl, RoomOutlineMessage outline)
        {
            if (_handle != 0 && _transport != null) _transport.Cancel(_handle);
            _handle = 0;
            _result = null;
            _done = true;
            _taken = false;
            LastError = null;

            if (!string.IsNullOrEmpty(roomId))
            {
                if (RoomFixtures.IsFixture(roomId))
                {
                    _result = Fixture(roomId);
                    if (_result == null) LastError = "fixture '" + roomId + "' could not be loaded";
                    return;
                }
                if (_transport == null || string.IsNullOrEmpty(forgeUrl))
                {
                    LastError = "no Forge to ask for room '" + roomId + "'";
                    return;
                }
                _handle = _transport.Begin("GET", forgeUrl.TrimEnd('/') + "/rooms/" + Uri.EscapeDataString(roomId), null, null, TimeoutSeconds);
                _done = _handle == 0;
                return;
            }

            if (outline == null)
            {
                LastError = "the phone named no room";
                return;
            }
            for (int i = 0; i < RoomFixtures.All.Length; i++)
            {
                RoomModel candidate = Fixture(RoomFixtures.All[i]);
                if (!SpectatorRoomMatch.Agrees(candidate, outline)) continue;
                _result = candidate;
                return;
            }
            LastError = "the phone named no room and its outline is no fixture";
        }

        public bool TryTake(out RoomModel room)
        {
            room = null;
            if (_taken) return false;
            if (!_done)
            {
                int status;
                byte[] body;
                if (!_transport.TryTake(_handle, out status, out body)) return false;
                _handle = 0;
                _done = true;
                if (status == 200 && body != null)
                {
                    string error;
                    if (!RoomModelJson.TryRead(Encoding.UTF8.GetString(body), out _result, out error))
                    {
                        _result = null;
                        LastError = "the Forge's room does not read: " + error;
                    }
                }
                else LastError = status == 0 ? "the Forge did not answer" : "the Forge answered " + status;
            }
            _taken = true;
            room = _result;
            _result = null;
            return true;
        }

        RoomModel Fixture(string name)
        {
            if (_loadFixture == null) return null;
            try { return _loadFixture(name); }
            catch (Exception) { return null; }
        }
    }
}

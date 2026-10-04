using System;
using System.Collections.Generic;
using System.IO;
using Atlas.World;

namespace Atlas.Scan
{
    /// <summary>
    /// Which saved room the app starts with, and the world map file beside a room (contract README F8, lane C
    /// section 12; Master Plan section 4.2, pre-scan path). Engine-free: the caller reads the remembered id
    /// from RoomStore.LastRoomId on the main thread and passes it in.
    /// </summary>
    public static class RoomIndex
    {
        /// <summary>Index of a room in a list from RoomStore.List, or -1.</summary>
        public static int Find(List<RoomSummary> rooms, string id)
        {
            if (rooms == null || string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i].Id == id) return i;
            return -1;
        }

        /// <summary>
        /// The room to start with: the remembered one when its file is still a valid room, otherwise the
        /// newest valid room in the folder. False when no room can be read. scratch is filled with the list.
        /// </summary>
        public static bool TryMostRecent(RoomStore rooms, string rememberedId, List<RoomSummary> scratch, out RoomSummary summary)
        {
            if (rooms == null) throw new ArgumentNullException(nameof(rooms));
            if (scratch == null) throw new ArgumentNullException(nameof(scratch));
            summary = default(RoomSummary);
            if (rooms.List(scratch) == 0) return false;
            int index = Find(scratch, rememberedId);
            summary = scratch[index >= 0 ? index : 0];      // the list is newest first and holds only readable rooms
            return true;
        }

        /// <summary>Loads the room TryMostRecent picks. False, with the reason, when there is none.</summary>
        public static bool TryLoadMostRecent(RoomStore rooms, string rememberedId, out RoomModel room, out string error)
        {
            if (rooms == null) throw new ArgumentNullException(nameof(rooms));
            // The remembered room first: one file read, no listing of the folder.
            if (!string.IsNullOrEmpty(rememberedId) && rooms.TryLoad(rememberedId, out room, out error)) return true;
            RoomSummary summary;
            if (!TryMostRecent(rooms, null, new List<RoomSummary>(), out summary))
            {
                room = null;
                error = "no saved room";
                return false;
            }
            return rooms.TryLoad(summary.Id, out room, out error);
        }

        /// <summary>True when room_id.worldmap exists and is not empty.</summary>
        public static bool HasWorldMap(RoomStore rooms, string id)
        {
            if (rooms == null || string.IsNullOrEmpty(id)) return false;
            try
            {
                var file = new FileInfo(rooms.WorldMapPath(id));
                return file.Exists && file.Length > 0;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Writes room_id.worldmap: to a temporary file, then renamed, so a crash never leaves half a map.
        /// Blocks; the app calls it from a worker thread. False, with the reason, on a file error.
        /// </summary>
        public static bool TryWriteWorldMap(RoomStore rooms, string id, byte[] bytes, out string error)
        {
            if (rooms == null) throw new ArgumentNullException(nameof(rooms));
            if (string.IsNullOrEmpty(id)) { error = "no room id"; return false; }
            if (bytes == null || bytes.Length == 0) { error = "no world map"; return false; }
            try
            {
                Directory.CreateDirectory(rooms.RootFolder);
                string path = rooms.WorldMapPath(id), temporary = path + ".tmp";
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
                error = null;
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>Reads room_id.worldmap. Blocks. False, with the reason, when it is missing, empty or unreadable.</summary>
        public static bool TryReadWorldMap(RoomStore rooms, string id, out byte[] bytes, out string error)
        {
            if (rooms == null) throw new ArgumentNullException(nameof(rooms));
            bytes = null;
            if (string.IsNullOrEmpty(id)) { error = "no room id"; return false; }
            try
            {
                string path = rooms.WorldMapPath(id);
                if (!File.Exists(path)) { error = "no world map for room " + id; return false; }
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                bytes = null;
                error = e.Message;
                return false;
            }
            if (bytes.Length == 0)
            {
                bytes = null;
                error = "the world map of room " + id + " is empty";
                return false;
            }
            error = null;
            return true;
        }
    }
}

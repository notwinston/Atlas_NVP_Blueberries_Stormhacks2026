using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Atlas.World;

namespace Atlas.Scan
{
    /// <summary>What the room list shows of one saved room.</summary>
    public struct RoomSummary
    {
        public string Id, CreatedUtc;
        public float Wf, Df;
        public int ObjectCount;
        public bool HasWorldMap;
    }

    /// <summary>
    /// RoomModel files on the phone (contract README F8 and section 12; Master Plan section 4.2, pre-scan
    /// path): room_&lt;id&gt;.json under persistentDataPath/rooms, with room_&lt;id&gt;.worldmap beside it
    /// when a world map was saved. Writing runs on a worker thread, so saving never holds a frame.
    /// </summary>
    public sealed class RoomStore
    {
        /// <summary>PlayerPrefs key of the last room confirmed or relocalised.</summary>
        public const string LastRoomKey = "atlas.room.last";

        readonly string _root;
        readonly object _gate = new object();
        readonly Dictionary<string, string> _pending = new Dictionary<string, string>();
        bool _writing;
        string _lastError;

        /// <summary>rootFolder is persistentDataPath + "/rooms", read once on the main thread by the caller.</summary>
        public RoomStore(string rootFolder)
        {
            if (string.IsNullOrEmpty(rootFolder)) throw new ArgumentException("The room store needs a folder.", nameof(rootFolder));
            _root = rootFolder;
        }

        public string RootFolder { get { return _root; } }
        /// <summary>The message of the last failed write, or null.</summary>
        public string LastError { get { lock (_gate) return _lastError; } }

        public string JsonPath(string id) { return Path.Combine(_root, "room_" + id + ".json"); }
        public string WorldMapPath(string id) { return Path.Combine(_root, "room_" + id + ".worldmap"); }

        /// <summary>True when room_id.json exists.</summary>
        public bool Exists(string id)
        {
            return !string.IsNullOrEmpty(id) && File.Exists(JsonPath(id));
        }

        /// <summary>The saved rooms, newest first. Returns the count. Files that cannot be read are left out.</summary>
        public int List(List<RoomSummary> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            if (!Directory.Exists(_root)) return 0;
            foreach (string file in Directory.GetFiles(_root, "room_*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                string id = name.Substring("room_".Length);
                RoomModel room;
                string error;
                if (!TryLoad(id, out room, out error)) continue;
                into.Add(new RoomSummary
                {
                    Id = id, CreatedUtc = room.CreatedUtc ?? "", Wf = room.FreeRect.Wf, Df = room.FreeRect.Df,
                    ObjectCount = room.Objects.Length, HasWorldMap = File.Exists(WorldMapPath(id)),
                });
            }
            // The time stamp sorts as text; the id (a time stamp as well for a scan) settles ties.
            into.Sort((a, b) =>
            {
                int byTime = string.CompareOrdinal(b.CreatedUtc, a.CreatedUtc);
                return byTime != 0 ? byTime : string.CompareOrdinal(b.Id, a.Id);
            });
            return into.Count;
        }

        /// <summary>
        /// Reads room_id.json. False, with the reason, when there is no such file or it is no valid RoomModel.
        /// The triangle targets of the room's objects are assigned again by today's rule (RefreshTargets); the
        /// file itself is left as it is.
        /// </summary>
        public bool TryLoad(string id, out RoomModel room, out string error)
        {
            room = null;
            if (string.IsNullOrEmpty(id)) { error = "no room id"; return false; }
            string text;
            lock (_gate) _pending.TryGetValue(id, out text);       // a write still queued is the newest state
            if (text == null)
            {
                string path = JsonPath(id);
                try
                {
                    if (!File.Exists(path)) { error = "no saved room " + id; return false; }
                    text = File.ReadAllText(path);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    error = e.Message;
                    return false;
                }
            }
            if (!RoomModelJson.TryRead(text, out room, out error)) return false;
            RefreshTargets(room);
            return true;
        }

        /// <summary>
        /// A triangle target is derived from the boxes by the rule of the day (ObjectMeshRules), not scanned. A
        /// room saved under an earlier rule carries that rule's targets and would fail rule R17 now, so a
        /// loaded room gets today's. A room of stand-ins keeps its own: each of them is 5,000 by definition.
        /// </summary>
        public static void RefreshTargets(RoomModel room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            RoomObject[] objects = room.Objects;
            for (int k = 0; k < objects.Length; k++)
                if (objects[k].Mesh == MeshState.StandIn) return;
            ObjectMeshRules.Assign(objects);
        }

        /// <summary>
        /// Queues a write of room_id.json: to a temporary file, then renamed, so a crash never leaves half a
        /// room. The latest call for an id wins. The text is made here, on the caller's thread, because the
        /// objects of a room change on the main thread.
        /// </summary>
        public void Save(RoomModel room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (string.IsNullOrEmpty(room.Id)) throw new ArgumentException("A room needs an id to be saved.", nameof(room));
            string text = RoomModelJson.Write(room);
            lock (_gate)
            {
                _pending[room.Id] = text;
                if (_writing) return;
                _writing = true;
            }
            Task.Run((Action)WriteQueued);
        }

        /// <summary>True when the room is kept on the phone: room_id.json exists or a write of it is queued.</summary>
        public bool IsKept(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            lock (_gate) if (_pending.ContainsKey(id)) return true;
            return Exists(id);
        }

        /// <summary>
        /// Saves a kept room again (IsKept), as Save does, so that its file follows what was placed after it was first
        /// written: the matched props and their cells (standalone-phone plan section 6.5). A room that was never
        /// saved stays unsaved. False when nothing was queued.
        /// </summary>
        public bool SaveIfKept(RoomModel room)
        {
            if (room == null || !IsKept(room.Id)) return false;
            Save(room);
            return true;
        }

        /// <summary>Writes room_id.json before returning. For tools and tests; the app uses Save.</summary>
        public void SaveNow(RoomModel room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            WriteFile(room.Id, RoomModelJson.Write(room));
        }

        /// <summary>Waits until every queued write is on disk. False when the time runs out.</summary>
        public bool WaitForWrites(int timeoutMilliseconds)
        {
            int waited = 0;
            while (true)
            {
                lock (_gate) if (!_writing && _pending.Count == 0) return true;
                if (waited >= timeoutMilliseconds) return false;
                Thread.Sleep(5);
                waited += 5;
            }
        }

        /// <summary>Removes the JSON and the world map of a room, and a write of it still queued.</summary>
        public void Delete(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            lock (_gate) _pending.Remove(id);
            try
            {
                File.Delete(JsonPath(id));
                File.Delete(WorldMapPath(id));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                lock (_gate) _lastError = e.Message;
            }
        }

        /// <summary>Id of the last room confirmed or relocalised, or "". PlayerPrefs: main thread only.</summary>
        public string LastRoomId
        {
            get { return UnityEngine.PlayerPrefs.GetString(LastRoomKey, ""); }
            set
            {
                UnityEngine.PlayerPrefs.SetString(LastRoomKey, value ?? "");
                UnityEngine.PlayerPrefs.Save();
            }
        }

        void WriteQueued()
        {
            while (true)
            {
                string id = null, text = null;
                lock (_gate)
                {
                    foreach (KeyValuePair<string, string> entry in _pending)
                    {
                        id = entry.Key;
                        text = entry.Value;
                        break;
                    }
                    if (id == null)
                    {
                        _writing = false;
                        return;
                    }
                }
                try
                {
                    WriteFile(id, text);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException)
                {
                    lock (_gate) _lastError = e.Message;
                }
                lock (_gate)
                {
                    // Still the same text: done. A newer one stays queued for the next round.
                    string now;
                    if (_pending.TryGetValue(id, out now) && ReferenceEquals(now, text)) _pending.Remove(id);
                }
            }
        }

        void WriteFile(string id, string text)
        {
            Directory.CreateDirectory(_root);
            string path = JsonPath(id), temporary = path + ".tmp";
            File.WriteAllText(temporary, text);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }
    }
}

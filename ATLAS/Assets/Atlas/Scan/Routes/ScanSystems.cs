using System;
using System.IO;
using Atlas.World;
using UnityEngine;

namespace Atlas.Scan
{
    /// <summary>
    /// The scan session and the room store, built on the pose systems and published for onboarding (contract
    /// README F3, lane C section 14.1). The composition root creates one after ArPoseSystems.
    /// </summary>
    public sealed class ScanSystems
    {
        public ScanSession Session { get; private set; }
        public RoomStore Rooms { get; private set; }

        ScanSystems() { }

        /// <summary>
        /// Builds the scan session on the pose systems' AR session set-up and pose pipeline, and the room
        /// store on roomsFolder (null: persistentDataPath/rooms, read here on the main thread). Publishes
        /// nothing: call Publish for that.
        /// </summary>
        public static ScanSystems Create(ArPoseSystems pose, string roomsFolder = null)
        {
            if (pose == null) throw new ArgumentNullException(nameof(pose));
            return new ScanSystems
            {
                Session = new ScanSession(pose.Rig, pose.Pipeline),
                Rooms = new RoomStore(roomsFolder ?? Path.Combine(Application.persistentDataPath, "rooms")),
            };
        }

        /// <summary>Publishes IScanSession, ScanSession, RoomStore and this object through AtlasServices.</summary>
        public void Publish()
        {
            AtlasServices.Register<IScanSession>(Session);
            AtlasServices.Register(Session);
            AtlasServices.Register(Rooms);
            AtlasServices.Register(this);
        }

        /// <summary>
        /// Loads a saved room and makes it the scan's result with no scan (the pre-scan path without a world
        /// map, and the Editor). False, with the reason, when the file is missing or invalid.
        /// </summary>
        public bool UseSavedRoom(string id, out string error)
        {
            RoomModel room;
            if (!Rooms.TryLoad(id, out room, out error)) return false;
            Session.UseRoom(room);
            return true;
        }

        /// <summary>Saves the scan's result and remembers it as the last room. False when there is no result.</summary>
        public bool SaveResult()
        {
            RoomModel room = Session.State == ScanState.Done ? Session.Result : null;
            if (room == null) return false;
            Rooms.Save(room);
            Rooms.LastRoomId = room.Id;
            return true;
        }
    }
}

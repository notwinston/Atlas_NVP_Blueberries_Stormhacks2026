using System;
using System.IO;
using Atlas.World;
using UnityEngine;

namespace Atlas.Scan
{
    /// <summary>
    /// The scan session, its object photo capture, the room store and the world map store, built on the pose
    /// systems and published for onboarding (contract README F3, lane C section 14.1). The composition root
    /// creates one after ArPoseSystems.
    /// </summary>
    public sealed class ScanSystems
    {
        public ScanSession Session { get; private set; }
        /// <summary>
        /// The photos of the furniture for the Forge (contract section 11). The session drives it; the
        /// composition root sets its Sink once the asset client exists.
        /// </summary>
        public ObjectPhotoCapture Photos { get; private set; }
        public RoomStore Rooms { get; private set; }
        /// <summary>
        /// The pre-scan path (contract section 12): saves the session's world map beside a room and
        /// relocalises to it at launch. It has no frame hook: whoever begins a save or a relocalisation
        /// calls its Tick every frame while it is Busy.
        /// </summary>
        public WorldMapStore WorldMaps { get; private set; }

        ScanSystems() { }

        /// <summary>
        /// Builds the scan session on the pose systems' AR session set-up and pose pipeline, and the room
        /// store and the world map store on roomsFolder (null: persistentDataPath/rooms, read here on the
        /// main thread). Publishes nothing: call Publish for that.
        /// </summary>
        public static ScanSystems Create(ArPoseSystems pose, string roomsFolder = null)
        {
            if (pose == null) throw new ArgumentNullException(nameof(pose));
            // Holds nothing until a sweep starts: the key-frame ring fills only while scanning.
            var photos = new ObjectPhotoCapture(pose.Rig);
            var rooms = new RoomStore(roomsFolder ?? Path.Combine(Application.persistentDataPath, "rooms"));
            return new ScanSystems
            {
                Photos = photos,
                Session = new ScanSession(pose.Rig, pose.Pipeline, photos),
                Rooms = rooms,
                WorldMaps = new WorldMapStore(rooms, pose.Rig),
            };
        }

        /// <summary>Publishes IScanSession, ScanSession, ObjectPhotoCapture, RoomStore, WorldMapStore and this object through AtlasServices.</summary>
        public void Publish()
        {
            Publish(null);
        }

        /// <summary>
        /// The same, and adds the hand-over of object photos to step 8b of the frame loop: one object per
        /// frame once a scanned room is confirmed, one comparison otherwise. loop may be null in a test.
        /// </summary>
        public void Publish(IFrameLoop loop)
        {
            AtlasServices.Register<IScanSession>(Session);
            AtlasServices.Register(Session);
            AtlasServices.Register(Photos);
            AtlasServices.Register(Rooms);
            AtlasServices.Register(WorldMaps);
            AtlasServices.Register(this);
            if (loop != null) loop.AddAfterFrame(Session.PhotoDelivery);
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

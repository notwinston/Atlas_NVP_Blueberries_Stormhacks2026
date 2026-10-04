using System;
using UnityEngine;

namespace Atlas.World
{
    // The one part of RoomFixtures that needs the engine: fixtures ship in a Resources folder so that a
    // player build holds them with nothing referencing them (contract README F8).
    public static partial class RoomFixtures
    {
        /// <summary>Resources.Load of "AtlasRooms/name", then RoomModelJson.Read. Main thread. Null name or a
        /// missing fixture: ArgumentException.</summary>
        public static RoomModel Load(string name)
        {
            return RoomModelJson.Read(LoadText(name));
        }

        /// <summary>The text of a fixture from Resources. Main thread. Null name or a missing fixture: ArgumentException.</summary>
        public static string LoadText(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("No fixture name", nameof(name));
            TextAsset asset = Resources.Load<TextAsset>(ResourcesFolder + "/" + name);
            if (asset == null) throw new ArgumentException("No RoomModel fixture named " + name + " in Resources/" + ResourcesFolder, nameof(name));
            string text = asset.text;
            Resources.UnloadAsset(asset);
            return text;
        }
    }
}

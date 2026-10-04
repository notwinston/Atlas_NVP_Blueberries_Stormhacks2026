using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARKit;
using UnityEngine.XR.ARSubsystems;

namespace Atlas.Scan.ARKit
{
    /// <summary>
    /// The ARKit side of the pre-scan path (Master Plan section 4.2, task P1-C13): the ARWorldMap is taken
    /// from the session, serialised, and applied again at the next launch. Registers itself before the first
    /// scene loads; nothing references this assembly (contract README F1). In the Editor there is no ARKit
    /// session subsystem, so every call answers "no world map".
    /// </summary>
    public sealed class ArKitWorldMapPlatform : IArWorldMapPlatform
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterAtStartUp()
        {
            ArWorldMapPlatform.Register(new ArKitWorldMapPlatform());
        }

        public string Name { get { return "ARKit"; } }

        public bool WorldMapSupported
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                return ARKitSessionSubsystem.worldMapSupported;
#else
                return false;
#endif
            }
        }

        public WorldMappingQuality MappingQuality(XRSessionSubsystem session)
        {
            var arkit = session as ARKitSessionSubsystem;
            if (arkit == null || !WorldMapSupported) return WorldMappingQuality.NotAvailable;
            switch (arkit.worldMappingStatus)
            {
                case ARWorldMappingStatus.Limited: return WorldMappingQuality.Limited;
                case ARWorldMappingStatus.Extending: return WorldMappingQuality.Extending;
                case ARWorldMappingStatus.Mapped: return WorldMappingQuality.Mapped;
                default: return WorldMappingQuality.NotAvailable;
            }
        }

        public void BeginGetWorldMap(XRSessionSubsystem session, Action<byte[]> done)
        {
            if (done == null) return;
            var arkit = session as ARKitSessionSubsystem;
            if (arkit == null || !WorldMapSupported)
            {
                done(null);
                return;
            }
            arkit.GetARWorldMapAsync((status, map) =>
            {
                // On Success the map is ours to dispose; otherwise it is the default value.
                if (status != ARWorldMapRequestStatus.Success)
                {
                    Debug.LogWarning("Atlas: ARKit world map request ended as " + status);
                    done(null);
                    return;
                }
                byte[] bytes = null;
                try
                {
                    // Persistent: a map runs to tens of megabytes, and this callback need not be on the main thread.
                    NativeArray<byte> native = map.Serialize(Allocator.Persistent);
                    try
                    {
                        bytes = native.ToArray();
                    }
                    finally
                    {
                        native.Dispose();
                    }
                }
                catch (InvalidOperationException e)
                {
                    Debug.LogWarning("Atlas: ARKit world map could not be serialised: " + e.Message);
                }
                finally
                {
                    map.Dispose();
                }
                done(bytes);
            });
        }

        public bool ApplyWorldMap(XRSessionSubsystem session, byte[] data)
        {
            var arkit = session as ARKitSessionSubsystem;
            if (arkit == null || !WorldMapSupported || data == null || data.Length == 0) return false;
            var native = new NativeArray<byte>(data, Allocator.Persistent);
            try
            {
                ARWorldMap map;
                if (!ARWorldMap.TryDeserialize(native, out map)) return false;
                try
                {
                    if (!map.valid) return false;
                    // The session keeps the map as its initial world map; our handle can go afterwards.
                    arkit.ApplyWorldMap(map);
                    return true;
                }
                finally
                {
                    map.Dispose();
                }
            }
            finally
            {
                native.Dispose();
            }
        }
    }
}

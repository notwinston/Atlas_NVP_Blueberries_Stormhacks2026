using System;
using UnityEngine.XR.ARSubsystems;

namespace Atlas.Scan
{
    /// <summary>ARKit's world mapping status: how well the session's map would relocalise if saved now.</summary>
    public enum WorldMappingQuality : byte { NotAvailable = 0, Limited = 1, Extending = 2, Mapped = 3 }

    /// <summary>
    /// What the pre-scan path needs from a provider-specific assembly: the ARWorldMap (lane C sections 10.6
    /// and 12). Atlas.Scan names no ARKit type (contract README F1); Atlas.Scan.ARKit implements this and
    /// registers itself before the first scene loads.
    /// </summary>
    public interface IArWorldMapPlatform
    {
        /// <summary>"ARKit".</summary>
        string Name { get; }

        /// <summary>True on an ARKit device. False in the Editor, in XR Simulation and on Windows.</summary>
        bool WorldMapSupported { get; }

        WorldMappingQuality MappingQuality(XRSessionSubsystem session);

        /// <summary>
        /// Asks ARKit for its world map and serialises it. done receives the bytes, or null on failure, and
        /// may be called from any thread. Call on the main thread.
        /// </summary>
        void BeginGetWorldMap(XRSessionSubsystem session, Action<byte[]> done);

        /// <summary>Deserialises and applies a world map: the session starts to relocalise. False on bad data. Main thread.</summary>
        bool ApplyWorldMap(XRSessionSubsystem session, byte[] data);
    }

    /// <summary>The registered provider, or a neutral stand-in.</summary>
    public static class ArWorldMapPlatform
    {
        sealed class Neutral : IArWorldMapPlatform
        {
            public string Name { get { return "none"; } }
            public bool WorldMapSupported { get { return false; } }
            public WorldMappingQuality MappingQuality(XRSessionSubsystem session) { return WorldMappingQuality.NotAvailable; }
            public void BeginGetWorldMap(XRSessionSubsystem session, Action<byte[]> done) { if (done != null) done(null); }
            public bool ApplyWorldMap(XRSessionSubsystem session, byte[] data) { return false; }
        }

        static readonly IArWorldMapPlatform s_neutral = new Neutral();
        static IArWorldMapPlatform s_current;

        /// <summary>Never null. With nothing registered: no world map, so a saved room is used as it is.</summary>
        public static IArWorldMapPlatform Current { get { return s_current ?? s_neutral; } }

        /// <summary>Called by the provider assembly from a BeforeSceneLoad method. Null removes the registration.</summary>
        public static void Register(IArWorldMapPlatform platform)
        {
            s_current = platform;
        }
    }
}

using UnityEngine;

namespace Atlas.Telemetry
{
    /// <summary>
    /// The app's one AtlasKit (Spaces/Glued/Resources/AtlasKit): the kit's files are TextAssets in Resources, read and
    /// checked against the manifest's hashes once per run and shared by every space that draws a kit piece (Glued's
    /// landmark and avatar, S3's duck). <see cref="KitSet"/> itself stays engine-free; this is its Resources reader.
    /// </summary>
    public static class KitResources
    {
        static KitSet s_Shared;

        /// <summary>The kit, loaded at the first call of the run. A missing or refused piece is reported by the KitSet.</summary>
        public static KitSet Shared()
        {
            if (s_Shared == null) s_Shared = KitSet.Load(Read);
            return s_Shared;
        }

        /// <summary>The bytes of "manifest" or "&lt;piece&gt;.glb" in Resources/AtlasKit, or null when there is no such file.</summary>
        public static byte[] Read(string name)
        {
            var asset = Resources.Load<TextAsset>(KitSet.ResourceFolder + "/" + name);
            if (asset == null) return null;
            byte[] bytes = asset.bytes;
            Resources.UnloadAsset(asset);
            return bytes;
        }

        // Play mode entered without a domain reload: the next run reads the kit again.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlayMode()
        {
            s_Shared = null;
        }
    }
}

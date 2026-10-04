using UnityEngine.XR.ARSubsystems;

namespace Atlas.Scan
{
    /// <summary>
    /// What the AR session set-up needs from a provider-specific assembly. Atlas.Scan names no ARKit type,
    /// because Unity.XR.ARKit exists only in the Editor and on iOS (contract README F1): Atlas.Scan.ARKit
    /// implements this and registers itself before the first scene loads.
    /// </summary>
    public interface IArSessionPlatform
    {
        /// <summary>"ARKit".</summary>
        string Name { get; }
        /// <summary>True when the session subsystem belongs to this provider.</summary>
        bool Owns(XRSessionSubsystem session);
        /// <summary>Provider facts for the start-up log (gate G1), for example "world map yes". Never null.</summary>
        string Describe(XRSessionSubsystem session);
    }

    /// <summary>The registered provider extras, or a neutral stand-in.</summary>
    public static class ArSessionPlatform
    {
        sealed class Neutral : IArSessionPlatform
        {
            public string Name { get { return "none"; } }
            public bool Owns(XRSessionSubsystem session) { return false; }
            public string Describe(XRSessionSubsystem session) { return ""; }
        }

        static readonly IArSessionPlatform s_neutral = new Neutral();
        static IArSessionPlatform s_current;

        /// <summary>Never null. Off the device: owns nothing and describes nothing.</summary>
        public static IArSessionPlatform Current { get { return s_current ?? s_neutral; } }

        /// <summary>Called by the provider assembly from a BeforeSceneLoad method. Null removes the registration.</summary>
        public static void Register(IArSessionPlatform platform)
        {
            s_current = platform;
        }
    }
}

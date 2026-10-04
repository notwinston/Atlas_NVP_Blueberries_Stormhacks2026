using UnityEngine;
using UnityEngine.XR.ARKit;
using UnityEngine.XR.ARSubsystems;

namespace Atlas.Scan.ARKit
{
    /// <summary>
    /// The ARKit side of the AR session set-up. Registers itself before the first scene loads; nothing
    /// references this assembly (contract README F1).
    /// </summary>
    public sealed class ArKitSessionPlatform : IArSessionPlatform
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterAtStartUp()
        {
            ArSessionPlatform.Register(new ArKitSessionPlatform());
        }

        public string Name { get { return "ARKit"; } }

        public bool Owns(XRSessionSubsystem session)
        {
            return session is ARKitSessionSubsystem;
        }

        public string Describe(XRSessionSubsystem session)
        {
            if (!(session is ARKitSessionSubsystem)) return "";
            // Static capability flags: safe to read anywhere, they are false off the device.
            return "world map " + (ARKitSessionSubsystem.worldMapSupported ? "yes" : "no") +
                   ", coaching overlay " + (ARKitSessionSubsystem.coachingOverlaySupported ? "yes" : "no");
        }
    }
}

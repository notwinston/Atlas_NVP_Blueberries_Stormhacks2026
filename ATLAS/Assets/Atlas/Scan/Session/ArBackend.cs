using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

namespace Atlas.Scan
{
    /// <summary>Which AR provider is running.</summary>
    public enum XrBackend : byte
    {
        /// <summary>No XR loader: the Editor without XR Simulation, the Windows Station.</summary>
        None = 0,
        /// <summary>ARKit on a phone.</summary>
        ARKit = 1,
        /// <summary>XR Simulation in the Editor.</summary>
        Simulation = 2,
        /// <summary>A loader this project does not know.</summary>
        Other = 3,
    }

    /// <summary>Finds the running AR provider with no reference to ARKit or XR Simulation, so it compiles everywhere.</summary>
    public static class ArBackend
    {
        public const string ArKitSessionId = "ARKit-Session";
        public const string SimulationSessionId = "XRSimulation-Session";

        public static XrBackend Detect()
        {
            XRLoader loader = LoaderUtility.GetActiveLoader();      // null when no loader is active
            if (loader == null) return XrBackend.None;
            XRSessionSubsystem session = loader.GetLoadedSubsystem<XRSessionSubsystem>();
            if (session == null) return XrBackend.Other;
            return FromSessionId(session.subsystemDescriptor.id);
        }

        /// <summary>The backend a session subsystem id stands for.</summary>
        public static XrBackend FromSessionId(string id)
        {
            if (id == ArKitSessionId) return XrBackend.ARKit;
            if (id == SimulationSessionId) return XrBackend.Simulation;
            return XrBackend.Other;
        }

        public static string Name(XrBackend backend)
        {
            switch (backend)
            {
                case XrBackend.ARKit: return "ARKit";
                case XrBackend.Simulation: return "XR Simulation";
                case XrBackend.Other: return "other";
                default: return "none";
            }
        }
    }
}

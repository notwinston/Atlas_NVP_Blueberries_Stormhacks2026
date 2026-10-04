using UnityEngine.XR;
using UnityEngine.XR.ARSubsystems;

namespace Atlas.Scan
{
    /// <summary>
    /// What the scan routes need from a provider-specific assembly: the face classes of the LiDAR mesh and
    /// the RoomPlan capture behind the furniture boxes. Atlas.Scan names no ARKit type (contract README F1);
    /// Atlas.Scan.ARKit implements this and registers itself before the first scene loads.
    /// </summary>
    public interface IArMeshPlatform
    {
        /// <summary>"ARKit".</summary>
        string Name { get; }

        /// <summary>True where meshes carry face classes (an ARKit device). False in the Editor and in XR Simulation.</summary>
        bool HasFaceClasses { get; }

        /// <summary>Switches per-face classification of the mesh on or off. Does nothing where there is none.</summary>
        void SetMeshClassification(XRMeshSubsystem meshes, bool enabled);

        /// <summary>
        /// One class per face of a mesh, in face order, as ARKit's own numbers (the values of
        /// Atlas.Scan.Grid.SurfaceClass). Returns the face count; 0 when there are none or into is too short.
        /// </summary>
        int CopyFaceClasses(XRMeshSubsystem meshes, TrackableId meshId, byte[] into);

        /// <summary>Starts the RoomPlan capture that produces furniture boxes. False when it cannot start.</summary>
        bool StartRoomCapture(XRBoundingBoxSubsystem boxes);
        void StopRoomCapture(XRBoundingBoxSubsystem boxes);
        /// <summary>RoomPlan's own instruction to the walker, or None.</summary>
        ScanHint RoomCaptureHint(XRBoundingBoxSubsystem boxes);
    }

    /// <summary>The registered provider, or a neutral stand-in.</summary>
    public static class ArMeshPlatform
    {
        sealed class Neutral : IArMeshPlatform
        {
            public string Name { get { return "none"; } }
            public bool HasFaceClasses { get { return false; } }
            public void SetMeshClassification(XRMeshSubsystem meshes, bool enabled) { }
            public int CopyFaceClasses(XRMeshSubsystem meshes, TrackableId meshId, byte[] into) { return 0; }
            public bool StartRoomCapture(XRBoundingBoxSubsystem boxes) { return false; }
            public void StopRoomCapture(XRBoundingBoxSubsystem boxes) { }
            public ScanHint RoomCaptureHint(XRBoundingBoxSubsystem boxes) { return ScanHint.None; }
        }

        static readonly IArMeshPlatform s_neutral = new Neutral();
        static IArMeshPlatform s_current;

        /// <summary>
        /// Never null. With nothing registered: no classes (the scan then classes faces by their geometry,
        /// NeutralSurfaceClasses), no capture.
        /// </summary>
        public static IArMeshPlatform Current { get { return s_current ?? s_neutral; } }

        /// <summary>Called by the provider assembly from a BeforeSceneLoad method. Null removes the registration.</summary>
        public static void Register(IArMeshPlatform platform)
        {
            s_current = platform;
        }
    }
}

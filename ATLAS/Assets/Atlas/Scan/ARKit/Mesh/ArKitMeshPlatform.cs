using Unity.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARKit;
using UnityEngine.XR.ARSubsystems;

namespace Atlas.Scan.ARKit
{
    /// <summary>
    /// The ARKit side of the scan routes (Master Plan section 4.2): per-face classes of the LiDAR mesh. The
    /// furniture boxes come from the RoomPlan plugin (ArKitRoomPlan). Registers itself before the first scene loads;
    /// nothing references this assembly (contract README F1).
    /// <para>The mesh classes come through ARKitMeshSubsystemExtensions, which is marked obsolete but is the
    /// only route on Unity 6000.3: the XRMeshSubsystem replacement exists from 6000.4.</para>
    /// </summary>
    public sealed class ArKitMeshPlatform : IArMeshPlatform
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterAtStartUp()
        {
            ArMeshPlatform.Register(new ArKitMeshPlatform());
        }

        public string Name { get { return "ARKit"; } }

        public bool HasFaceClasses
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

#pragma warning disable CS0618 // ARKitMeshSubsystemExtensions: see the class summary
        public void SetMeshClassification(XRMeshSubsystem meshes, bool enabled)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (meshes == null) return;
            if (meshes.GetClassificationEnabled() != enabled) meshes.SetClassificationEnabled(enabled);
#endif
        }

        public int CopyFaceClasses(XRMeshSubsystem meshes, TrackableId meshId, byte[] into)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (meshes == null || into == null) return 0;
            using (NativeArray<ARMeshClassification> faces = meshes.GetFaceClassifications(meshId, Allocator.Temp))
            {
                int count = faces.Length;
                if (count == 0 || count > into.Length) return 0;
                // ARMeshClassification's numbers are SurfaceClass's numbers: None 0 to Door 7.
                for (int i = 0; i < count; i++) into[i] = (byte)faces[i];
                return count;
            }
#else
            return 0;
#endif
        }
#pragma warning restore CS0618

        // The RoomPlan capture is no longer Unity's RoomPlanBoundingBoxSubsystem: the scan's one RoomPlan session is
        // the Swift plugin (ArKitRoomPlan, standalone-phone plan D2), and a second RoomCaptureSession on the same
        // ARSession crashed the scan on the device. These members of IArMeshPlatform start nothing.

        public bool StartRoomCapture(XRBoundingBoxSubsystem boxes)
        {
            return false;
        }

        public void StopRoomCapture(XRBoundingBoxSubsystem boxes)
        {
        }

        public ScanHint RoomCaptureHint(XRBoundingBoxSubsystem boxes)
        {
            return ScanHint.None;
        }
    }
}

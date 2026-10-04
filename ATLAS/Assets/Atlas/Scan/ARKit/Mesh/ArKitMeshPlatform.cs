using Unity.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARKit;
using UnityEngine.XR.ARSubsystems;

namespace Atlas.Scan.ARKit
{
    /// <summary>
    /// The ARKit side of the scan routes (Master Plan section 4.2): per-face classes of the LiDAR mesh and
    /// the RoomPlan capture behind the furniture boxes. Registers itself before the first scene loads;
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

        public bool StartRoomCapture(XRBoundingBoxSubsystem boxes)
        {
            // Off the device the subsystem is XR Simulation's (or none) and the cast gives null.
            var roomPlan = boxes as RoomPlanBoundingBoxSubsystem;
            if (roomPlan == null) return false;
            if (!roomPlan.SetupRoomCapture()) return false;
            roomPlan.StartRoomCapture();
            return true;
        }

        public void StopRoomCapture(XRBoundingBoxSubsystem boxes)
        {
            var roomPlan = boxes as RoomPlanBoundingBoxSubsystem;
            if (roomPlan != null && roomPlan.IsRoomCapturing()) roomPlan.StopRoomCapture();
        }

        public ScanHint RoomCaptureHint(XRBoundingBoxSubsystem boxes)
        {
            var roomPlan = boxes as RoomPlanBoundingBoxSubsystem;
            if (roomPlan == null || !roomPlan.IsRoomCapturing()) return ScanHint.None;
            XRBoundingBoxInstructions instruction;
            roomPlan.GetRoomCaptureInstruction(out instruction);
            // One line is shown: the instruction that matters most to the scan comes first.
            if ((instruction & XRBoundingBoxInstructions.TurnOnLight) != 0) return ScanHint.TurnOnLight;
            if ((instruction & XRBoundingBoxInstructions.SlowDown) != 0) return ScanHint.SlowDown;
            if ((instruction & XRBoundingBoxInstructions.MoveAwayFromWall) != 0) return ScanHint.MoveAwayFromWall;
            if ((instruction & XRBoundingBoxInstructions.MoveCloseToWall) != 0) return ScanHint.MoveCloseToWall;
            return ScanHint.None;
        }
    }
}

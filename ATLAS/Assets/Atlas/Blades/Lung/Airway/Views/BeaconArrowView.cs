using Atlas.Kernel;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Blades.Lung
{
    // World-space floor arrow 0.6 m ahead of the head along the exit corridor (D-G4-5, D-G4-12). Driven by the module (MP §6.3): no Update,
    // never reads the head pose itself. The material is lane A's shared material; no shader property is set (_WarpMode stays 0, no warp:
    // exact near the head because the log chart preserves directions from the head).
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class BeaconArrowView : MonoBehaviour
    {
        [SerializeField] Material material;
        [SerializeField] int paletteCell = 0; // D-G4-12: serialized until lane A's palette rows exist

        Mesh mesh;

        void EnsureMesh()
        {
            if (mesh != null) return;
            mesh = ArrowMesh.Build(paletteCell);
            GetComponent<MeshFilter>().sharedMesh = mesh;
            if (material != null) GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        void OnDestroy() { if (mesh != null) Destroy(mesh); }

        public void Apply(in FrameContext frame, in WalkerPose pose, AirwayBeacon beacon)
        {
            EnsureMesh();
            bool visible = beacon.Visible && beacon.ExitCanonicalEdge >= 0;
            gameObject.SetActive(visible);
            if (!visible) return;
            double2 d = AirwayBeacon.ArrowDirection(pose, beacon.ExitCanonicalEdge);
            // The walker frame's axes are the world x and z axes (kernel.md §4.5).
            var head = frame.HeadPosition;
            float ahead = (float)AirwayBeacon.ArrowAheadM;
            transform.position = new Vector3(head.x + ahead * (float)d.x, (float)AirwayBeacon.ArrowHeightM, head.z + ahead * (float)d.y);
            transform.rotation = Quaternion.LookRotation(new Vector3((float)d.x, 0, (float)d.y), Vector3.up);
        }
    }
}

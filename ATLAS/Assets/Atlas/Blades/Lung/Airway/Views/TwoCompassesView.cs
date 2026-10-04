using Atlas.Kernel;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Blades.Lung
{
    // Ghost compass arrow: the Euclidean compass's corridor, shown beside the beacon arrow only at the designed fork (MP §10.2.3, D-G4-7).
    // Same placement rule as BeaconArrowView (D-G4-12); driven by the module, no Update.
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class TwoCompassesView : MonoBehaviour
    {
        [SerializeField] Material material;
        [SerializeField] int paletteCell = 1; // D-G4-12: serialized until lane A's palette rows exist

        Mesh mesh;

        void EnsureMesh()
        {
            if (mesh != null) return;
            mesh = ArrowMesh.Build(paletteCell);
            GetComponent<MeshFilter>().sharedMesh = mesh;
            if (material != null) GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        void OnDestroy() { if (mesh != null) Destroy(mesh); }

        public void Apply(in FrameContext frame, in WalkerPose pose, AirwayTree tree, AirwayBeacon beacon, AirwayRoute route)
        {
            EnsureMesh();
            bool active = beacon.AtCompassFork && route.HasTwoCompasses(tree);
            gameObject.SetActive(active);
            if (!active) return;
            string fork = tree.CompassForkAddress;
            char compass = TwoCompasses.EuclideanChild(tree, fork, route.TargetAddress);
            int edge = tree.FrameOf(AirwayTile.Fork(fork)).CanonicalEdge(compass == 'L' ? RuleS.LeftEdge : RuleS.RightEdge);
            double2 d = AirwayBeacon.ArrowDirection(pose, edge);
            var head = frame.HeadPosition;
            float ahead = (float)AirwayBeacon.ArrowAheadM;
            transform.position = new Vector3(head.x + ahead * (float)d.x, (float)AirwayBeacon.ArrowHeightM, head.z + ahead * (float)d.y);
            transform.rotation = Quaternion.LookRotation(new Vector3((float)d.x, 0, (float)d.y), Vector3.up);
        }
    }
}

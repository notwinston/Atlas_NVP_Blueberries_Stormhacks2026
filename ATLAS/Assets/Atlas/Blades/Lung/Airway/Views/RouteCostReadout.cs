using UnityEngine;

namespace Atlas.Blades.Lung
{
    // Route cost read-out text (D-G4-15: built-in TextMesh, TextMeshPro absent; font shader flagged for lane A). No Update.
    public sealed class RouteCostReadout : MonoBehaviour
    {
        [SerializeField] TextMesh text;

        public void Apply(in RouteCost cost)
        {
            if (text != null) text.text = cost.Format();
        }
    }
}

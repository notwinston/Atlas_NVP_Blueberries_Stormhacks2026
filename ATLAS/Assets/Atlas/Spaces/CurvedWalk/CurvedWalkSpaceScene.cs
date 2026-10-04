using UnityEngine;

namespace Atlas.Spaces.CurvedWalk
{
    // Scene component of Space_CurvedWalk.unity (P1-B09): owns the module that lane A registers with SpaceLab
    // (host wiring: SpaceRequested -> SpaceLab.Request, Home = SpaceLab.Home, Actions = lane C's map; see docs/laneB/TESTING.md).
    public sealed class CurvedWalkSpaceScene : MonoBehaviour
    {
        public Material sharedMaterial;   // the Atlas/AtlasSurface material; required (no runtime shader lookup)
        CurvedWalkModule module;

        public CurvedWalkModule Module
        {
            get
            {
                if (module == null)
                {
                    if (sharedMaterial == null) throw new System.InvalidOperationException("sharedMaterial is not assigned: the Atlas/AtlasSurface material is referenced by the scene (no runtime shader lookup, MP §8.3)");
                    module = new CurvedWalkModule { SharedMaterial = sharedMaterial };
                }
                return module;
            }
        }
    }
}

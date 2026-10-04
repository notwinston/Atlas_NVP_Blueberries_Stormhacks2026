using UnityEngine;

namespace Atlas.Spaces.SmallPlanet
{
    // Scene component of Space_SmallPlanet.unity (P1-B09): owns the module that lane A registers with SpaceLab.
    public sealed class SmallPlanetSpaceScene : MonoBehaviour
    {
        public Material sharedMaterial;   // the Atlas/AtlasSurface material; required (no runtime shader lookup)
        SmallPlanetModule module;

        public SmallPlanetModule Module
        {
            get
            {
                if (module == null)
                {
                    if (sharedMaterial == null) throw new System.InvalidOperationException("sharedMaterial is not assigned: the Atlas/AtlasSurface material is referenced by the scene (no runtime shader lookup, MP §8.3)");
                    module = new SmallPlanetModule { SharedMaterial = sharedMaterial };
                }
                return module;
            }
        }
    }
}

using UnityEngine;

namespace Atlas.Rendering
{
    // Master Plan §8.3: warped renderers get huge localBounds so Unity culls nothing; the §8.5 draw rule decides.
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class AtlasWarpBounds : MonoBehaviour
    {
        public const float Size = 1000f; // BP §1 item 5 "e.g. 1000 m"

        void OnEnable()
        {
            if (TryGetComponent(out Renderer r)) Apply(r);
        }

        public static void Apply(Renderer renderer) =>
            renderer.localBounds = new Bounds(Vector3.zero, new Vector3(Size, Size, Size));
    }
}

using Atlas.Kernel;
using Atlas.World;
using UnityEngine;

namespace Atlas.Spaces.AlignedCells
{
    // Master Plan §9.3: systems register themselves from their own scene. [P1-B12 D4]
    public sealed class AlignedCellsSpaceHost : MonoBehaviour
    {
        [SerializeField] Material template;   // null: new Material(Shader.Find("Atlas/AtlasSurface")) (PH-5)

        public AlignedCellsModule Module { get; private set; }

        public void Register(SpaceLab lab)
        {
            var mat = template != null ? template : new Material(Shader.Find("Atlas/AtlasSurface"));
            Module = new AlignedCellsModule(new TileGraph(5), transform, mat, () => lab.Home);
            lab.Register(Module);
        }

        void OnDestroy() => Module?.Dispose();
    }
}

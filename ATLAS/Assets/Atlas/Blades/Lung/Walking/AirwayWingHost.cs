using Atlas.Kernel;
using Atlas.Safety;
using Atlas.World;
using UnityEngine;

namespace Atlas.Blades.Lung.Walking
{
    // P2-B05. Lives in Lung_Airway.unity; lane A calls Register (cross-lane A-G6-1). Loads the tree once via P2-B01 (PH-33).
    // Step 1: the fork choice never chooses; the real input arrives in P2-B12 through NavigationBridge.
    public sealed class AirwayWingHost : MonoBehaviour
    {
        sealed class NoChoice : IForkChoice { public bool TryGetChoice(string a, out char l) { l = 'L'; return false; } }

        public AirwayWingModule Module { get; private set; }

        // G6 §3.9 returns the AirwayBeat; that type arrives with P2-B09 (DV-B05-3), so this returns the module.
        public AirwayWingModule Register(SpaceLab lab, IClearanceQuery liveBumpGuard, IChamberView view)
        {
            var tree = new AirwayTreeBinding(new AirwayTree(AirwayTreeFile.Load(CaseManifest.Root("illustrative")), TileFrame.Zero));
            Module = new AirwayWingModule(tree, liveBumpGuard, new NoChoice(), view, lab.Request);
            lab.Register(Module);
            return Module;
        }
    }
}

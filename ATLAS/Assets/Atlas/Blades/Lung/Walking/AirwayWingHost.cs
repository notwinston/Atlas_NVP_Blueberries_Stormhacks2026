using Atlas.Kernel;
using Atlas.Safety;
using Atlas.World;
using UnityEngine;

namespace Atlas.Blades.Lung.Walking
{
    // P2-B05. Lives in Lung_Airway.unity; the composition of a Phase 2 build calls Register. Loads the tree once via P2-B01 (PH-33).
    // Step 1: the fork choice never chooses; the real input arrives in P2-B12 through NavigationBridge.
    public sealed class AirwayWingHost : MonoBehaviour
    {
        sealed class NoChoice : IForkChoice { public bool TryGetChoice(string a, out char l) { l = 'L'; return false; } }

        public AirwayWingModule Module { get; private set; }

        // G6 §3.9 returns the AirwayBeat; that type arrives with P2-B09 (DV-B05-3), so this returns the module.
        // clearance: the live Bump Guard (BumpGuard is an IClearanceQuery); null takes the one SafetyInstaller published.
        // The module is built once and handed to SpaceRegistry as S2's factory, which is how lane A's loader creates a space.
        public AirwayWingModule Register(IClearanceQuery clearance, IChamberView view)
        {
            if (clearance == null && AtlasServices.TryGet<IBumpGuard>(out var guard)) clearance = guard as IClearanceQuery;
            var tree = new AirwayTreeBinding(new AirwayTree(AirwayTreeFile.Load(CaseManifest.Root("illustrative")), TileFrame.Zero));
            Module = new AirwayWingModule(tree, clearance, new NoChoice(), view, id => AtlasServices.Get<ISpaceHost>().RequestSwitch(id));
            SpaceRegistry.Register(SpaceId.CurvedWalk, () => Module);
            return Module;
        }
    }
}

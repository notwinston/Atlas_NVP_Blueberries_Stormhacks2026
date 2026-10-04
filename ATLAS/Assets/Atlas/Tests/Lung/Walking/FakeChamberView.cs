using System.Collections.Generic;
using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Walking.Tests
{
    sealed class FakeChamberView : IChamberView
    {
        public readonly Dictionary<TileId, ShellState> CurrentShell = new Dictionary<TileId, ShellState>();
        public readonly List<(TileId tile, int edge)> IrisCalls = new List<(TileId, int)>();
        public readonly List<(bool visible, double2 dir, bool wait)> PromptCalls = new List<(bool, double2, bool)>();
        public void SetShell(TileId chamber, ShellState s) => CurrentShell[chamber] = s;
        public void SetIris(TileId chamber, int tileEdge) => IrisCalls.Add((chamber, tileEdge));
        public void SetTurnPrompt(bool visible, double2 direction, bool wait) => PromptCalls.Add((visible, direction, wait));
    }

    sealed class ScriptedForkChoice : IForkChoice
    {
        readonly string _route;
        public string Override;   // when set, replaces the letter at index 0 (deliberate wrong choice)
        public ScriptedForkChoice(string route) { _route = route; }
        public bool TryGetChoice(string forkAddress, out char letter)
        {
            letter = 'L';
            if (forkAddress.Length >= _route.Length) return false;
            letter = _route[forkAddress.Length];
            if (forkAddress.Length == 0 && Override != null) letter = Override[0];
            return true;
        }
    }

    // Tile ids straight from Rule S (no tree.json), for the generation-16 dead end.
    sealed class FakeForkTiles : IForkTiles
    {
        public TileId Root => RuleS.FrameOf("", TileFrame.Zero).Tile;
        public TileId ForkTile(string a) => RuleS.FrameOf(a, TileFrame.Zero).Tile;
        public string TargetAddress => "LRLRLRLRLRLRLRLR";
        public string CompassForkAddress => "";
        public void Focus(string address) { }
    }
}

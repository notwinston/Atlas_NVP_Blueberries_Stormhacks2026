using System;
using Atlas.Telemetry;
using UnityEngine;

namespace Atlas.Blades.Lung.Walking
{
    // P2-B12 (G6 §3.10). The only walking file that names P2-B06 / P2-B08 members:
    //   P2-B06  AirwayBeacon (Reset, Tick, RescueShown, AtCompassFork), AirwayTree.SetCurrent, AirwayTile, AirwayRoute
    //   P2-B08  TwoCompasses.EuclideanChild / BeaconChild, CompassChoiceEventData (the compass_choice event, D-G4-14)
    // IForkChoice: the choice comes from a source the host supplies (fork input of lanes A/C; Submit for tests). P2-B08 has no
    // fork-UI class of its own (DV-B12-1), so the source is a delegate. The wing emits the "fork" event; this class emits
    // "compass_choice" itself, not through ForkLog, which would emit a second "fork" (DV-B12-2).
    public sealed class NavigationBridge : IForkChoice
    {
        readonly AirwayTree _tree; readonly IForkTiles _tiles; readonly AirwayBeacon _beacon; AirwayRoute _route;
        char _pending; string _compassLogged = "";
        public Func<string, char> ChoiceSource { get; set; }   // returns 'L', 'R' or '\0' (none yet)

        public NavigationBridge(AirwayTree tree, IForkTiles tiles, AirwayRoute route)
        {
            _tree = tree ?? throw new ArgumentNullException(nameof(tree));
            _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
            _beacon = new AirwayBeacon(tree);
            _route = route; _beacon.Reset(route);
        }

        public AirwayBeacon Beacon => _beacon;
        public bool BeaconShown => _beacon.RescueShown;   // D-G4-7: the Two Compasses arrival does not count as rescue

        public void Submit(char letter) { _pending = letter; }

        public void Attach(AirwayWing wing) { wing.BeaconShown = () => _beacon.RescueShown; }

        // Once per frame after AirwayWing.Tick (H-2 of airway_nav.md): recentre the tree window, then tick the beacon.
        public void Observe(AirwayWing wing, double time)
        {
            string a = wing.Address;
            if (a == null) return;
            AirwayTile tile;
            if (a.Length == 0 || wing.CurrentTile.Equals(_tiles.ForkTile(a))) tile = AirwayTile.Fork(a);
            else if (wing.CurrentTile.Equals(_tiles.ForkTile(a.Substring(0, a.Length - 1)))) tile = AirwayTile.Fork(a.Substring(0, a.Length - 1));   // chose, not yet walked
            else tile = AirwayTile.Bend(a);
            _tree.SetCurrent(tile);
            _beacon.Tick(tile, time);
        }

        public bool TryGetChoice(string forkAddress, out char letter)
        {
            letter = _pending != '\0' ? _pending : (ChoiceSource != null ? ChoiceSource(forkAddress) : '\0');
            if (letter != 'L' && letter != 'R') { letter = 'L'; return false; }
            _pending = '\0';
            bool correct = AirwayAddress.IsProperPrefixOf(forkAddress, _route.TargetAddress) && _route.TargetAddress[forkAddress.Length] == letter;
            if (_route.HasTwoCompasses(_tree) && forkAddress == _tree.CompassForkAddress && _compassLogged != forkAddress)
            {
                _compassLogged = forkAddress;
                EventLog.Emit("lung", "compass_choice", JsonUtility.ToJson(new CompassChoiceEventData
                {
                    address = forkAddress, choice = letter.ToString(), correct = correct,
                    compass = TwoCompasses.EuclideanChild(_tree, forkAddress, _route.TargetAddress).ToString(),
                    beacon = TwoCompasses.BeaconChild(forkAddress, _route.TargetAddress).ToString()
                }));
            }
            return true;
        }
    }
}

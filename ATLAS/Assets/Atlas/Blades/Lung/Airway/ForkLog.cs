using System;
using Atlas.Telemetry;
using UnityEngine;

namespace Atlas.Blades.Lung
{
    // MP §10.2.6 field names verbatim.
    [Serializable] public sealed class ForkEventData { public string address; public string choice; public bool correct; public bool beacon_shown; public float t_s; }
    // D-G4-14: compass_choice event of P2-B08.
    [Serializable] public sealed class CompassChoiceEventData { public string address; public string choice; public string compass; public string beacon; public bool correct; public float t_s; }

    // Emits fork (D-G4-13) and compass_choice events on the transition Fork(a) -> Bend(a+c). Call after AirwayBeacon.Tick, same frame. No Update.
    public sealed class ForkLog
    {
        readonly AirwayTree tree;
        string target = "";
        bool twoCompasses;
        bool hasPrev, prevIsFork, shown;
        string prevAddress = "";
        double entered;

        public ForkLog(AirwayTree tree) { this.tree = tree ?? throw new ArgumentNullException(nameof(tree)); Reset(AirwayRoute.Full(tree)); }

        public void Reset(AirwayRoute route)
        {
            target = route.TargetAddress;
            twoCompasses = route.HasTwoCompasses(tree);
            hasPrev = false; prevIsFork = false; shown = false; prevAddress = "";
        }

        public void Observe(AirwayTile current, double timeSeconds, bool rescueShownNow)
        {
            bool isFork = current.Kind == AirwayTileKind.Fork;
            if (hasPrev && prevIsFork && !isFork && current.Address.Length == prevAddress.Length + 1
                && current.Address.StartsWith(prevAddress, StringComparison.Ordinal))
            {
                string a = prevAddress, c = current.Address.Substring(a.Length);
                bool correct = AirwayAddress.IsProperPrefixOf(a, target) && target[a.Length] == c[0];
                float t = (float)(timeSeconds - entered);
                EventLog.Emit("lung", "fork", JsonUtility.ToJson(new ForkEventData { address = a, choice = c, correct = correct, beacon_shown = shown, t_s = t }));
                if (twoCompasses && a == tree.CompassForkAddress)
                    EventLog.Emit("lung", "compass_choice", JsonUtility.ToJson(new CompassChoiceEventData
                    {
                        address = a, choice = c, correct = correct, t_s = t,
                        compass = TwoCompasses.EuclideanChild(tree, a, target).ToString(), beacon = TwoCompasses.BeaconChild(a, target).ToString()
                    }));
            }
            if (isFork)
            {
                if (!(hasPrev && prevIsFork && prevAddress == current.Address)) { entered = timeSeconds; shown = false; }
                shown |= rescueShownNow;
            }
            hasPrev = true; prevIsFork = isFork; prevAddress = current.Address;
        }
    }
}

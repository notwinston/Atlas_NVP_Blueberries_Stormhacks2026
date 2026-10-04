using System;
using Atlas.Telemetry;
using UnityEngine;

namespace Atlas.Blades.Lung.Walking
{
    // MP §10.2.6 "fork {address, choice, correct, beacon_shown, t_s}". G6 owns the fork event (DV-G6-12);
    // G4's spec emits only compass_choice.
    [Serializable] public struct ForkEventData { public string address; public string choice; public bool correct; public bool beacon_shown; public float t_s; }

    public static class ForkEvent
    {
        public static void Emit(in ForkEventData d) => EventLog.Emit("lung", "fork", JsonUtility.ToJson(d));
    }
}

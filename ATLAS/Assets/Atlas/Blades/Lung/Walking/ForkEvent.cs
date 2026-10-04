using Atlas.Link;

namespace Atlas.Blades.Lung.Walking
{
    // MP §10.2.6 "fork {address, choice, correct, beacon_shown, t_s}". G6 owns the fork event (DV-G6-12);
    // G4's spec emits only compass_choice.
    public struct ForkEventData
    {
        public string address; public string choice; public bool correct; public bool beacon_shown; public float t_s;

        public JsonValue ToJson() => JsonValue.Object().Set("address", address ?? "").Set("choice", choice ?? "").Set("correct", correct)
            .Set("beacon_shown", beacon_shown).Set("t_s", t_s);

        public static ForkEventData FromJson(JsonValue j) => new ForkEventData
        {
            address = j.GetString("address", ""), choice = j.GetString("choice", ""), correct = j.GetBool("correct"),
            beacon_shown = j.GetBool("beacon_shown"), t_s = j.GetFloat("t_s"),
        };
    }

    public static class ForkEvent
    {
        public static void Emit(in ForkEventData d) => LungEvents.Emit("fork", d.ToJson());
    }
}

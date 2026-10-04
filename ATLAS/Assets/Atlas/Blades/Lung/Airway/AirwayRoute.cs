namespace Atlas.Blades.Lung
{
    // The walked route: its length always comes from tree.json (PH-39: the fixture route has 6 forks; never hard-code 4).
    public readonly struct AirwayRoute
    {
        public readonly string TargetAddress;

        AirwayRoute(string target) { TargetAddress = target; }

        public int Forks => TargetAddress.Length;
        public int TileSteps => 2 * Forks; // MP §10.2.3: one generation = 2 tile steps

        public static AirwayRoute Full(AirwayTree t) => new AirwayRoute(t.TargetAddress);

        // D-G4-9: the judged-demo one-fork route (MP §10.2.3 "one-fork route") ends at the first fork's end.
        public static AirwayRoute OneFork(AirwayTree t) => new AirwayRoute(t.TargetAddress.Substring(0, 1));

        public bool HasTwoCompasses(AirwayTree t) => AirwayAddress.IsProperPrefixOf(t.CompassForkAddress, TargetAddress);
    }
}

namespace Atlas.Spaces.SmallPlanet
{
    // Lane-B plain stand-in for RoomModel.lapSquare (§4.3); the loop-fit clamp is lane C's.
    public readonly struct SmallPlanetRoom
    {
        public readonly double LapSide; // s, metres
        public SmallPlanetRoom(double lapSide) { LapSide = lapSide; }
    }
}

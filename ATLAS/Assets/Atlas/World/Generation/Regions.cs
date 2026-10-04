using Atlas.Kernel;

namespace Atlas.World
{
    public static class Regions
    {
        public const int Count = 4;

        // Master Plan §3.4: the edge of tile zero through which the geodesic from tile zero's centre leaves = argmax(X, Y, -X, -Y)
        // (tile centre = (sqrt(phi) X, sqrt(phi) Y, T), A.5; edge 0 faces +x, kernel.md §5.3); ties -> lower edge index.
        public static int RegionOf(TileId id)
        {
            int k = 0; long bu = id.Xu, bv = id.Xv;
            if (ZPhiMath.Compare(id.Yu, id.Yv, bu, bv) > 0) { k = 1; bu = id.Yu; bv = id.Yv; }
            if (ZPhiMath.Compare(-id.Xu, -id.Xv, bu, bv) > 0) { k = 2; bu = -id.Xu; bv = -id.Xv; }
            if (ZPhiMath.Compare(-id.Yu, -id.Yv, bu, bv) > 0) { k = 3; }
            return k;
        }

        // Degrees; provisional until P1-A06's palette (Master Plan §8.1).
        public static float Hue(int region) => 30f + 90f * region;
    }
}

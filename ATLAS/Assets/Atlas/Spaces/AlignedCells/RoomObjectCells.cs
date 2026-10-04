using System;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells
{
    // Inclusive cell range; .x = column (anchor x), .y = row (anchor z), as CellGrid. [P1-B12 D1]
    public readonly struct CellRange : IEquatable<CellRange>
    {
        public readonly int2 Min, Max;
        public CellRange(int2 min, int2 max) { Min = min; Max = max; }
        public bool Contains(int2 cell) => math.all(cell >= Min & cell <= Max);
        public bool Equals(CellRange o) => math.all(Min == o.Min) && math.all(Max == o.Max);
        public override bool Equals(object o) => o is CellRange r && Equals(r);
        public override int GetHashCode() => (int)math.hash(new int4(Min, Max));
        public static bool operator ==(CellRange a, CellRange b) => a.Equals(b);
        public static bool operator !=(CellRange a, CellRange b) => !a.Equals(b);
        public override string ToString() => $"({Min.x},{Min.y})-({Max.x},{Max.y})";
    }

    // Master Plan §4.3 (oriented box), §4.7 ("drawn in whichever tile currently occupies its cell"); [P1-B12 D1]
    public static class RoomObjectCells
    {
        // Box-local (x, y, z) (origin at the centre of the base, §6.4) to anchor frame, the yaw sense of RoomObject.Rotation:
        // (px + x cos psi + z sin psi, y, pz - x sin psi + z cos psi), psi = obj.Yaw (rad).
        public static double3 BoxToAnchor(RoomObject obj, double3 local)
        {
            double c = Math.Cos(obj.Yaw), s = Math.Sin(obj.Yaw);
            return new double3(obj.Base.x + local.x * c + local.z * s,
                               obj.Base.y + local.y,
                               obj.Base.z - local.x * s + local.z * c);
        }

        // [P1-B12 D1] CellAt clamps, so an object outside the grid (§4.3: freeRect is 0.30 m inside the furniture) maps to the nearest edge cells.
        public static CellRange Of(CellGrid grid, RoomObject obj)
        {
            double hx = obj.Size.x / 2.0, hz = obj.Size.z / 2.0;
            int2 lo = new int2(int.MaxValue), hi = new int2(int.MinValue);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    double3 p = BoxToAnchor(obj, new double3(sx * hx, 0, sz * hz));
                    int2 c = grid.CellAt(new double2(p.x, p.z));
                    lo = math.min(lo, c); hi = math.max(hi, c);
                }
            return new CellRange(lo, hi);
        }
    }
}

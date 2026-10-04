using System;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Endoluminal
{
    // One atlas.tree/2 node (MP 10.2.4): address, pos_mm (proximal start, PH-35), dir, len_mm, radius_mm. Patient frame, millimetres (D-11, D-12).
    public readonly struct AirwaySegmentMm
    {
        public readonly string Address;
        public readonly double3 PosMm, Dir;
        public readonly double LenMm, RadiusMm;

        public AirwaySegmentMm(string address, double3 posMm, double3 dir, double lenMm, double radiusMm)
        {
            if (address == null) throw new ArgumentException("address must not be null", nameof(address));
            double n = math.length(dir);
            if (!(n >= 1e-9)) throw new ArgumentException("dir must be non-zero", nameof(dir));
            if (!(lenMm > 0)) throw new ArgumentException("len must be positive", nameof(lenMm));
            if (!(radiusMm > 0)) throw new ArgumentException("radius must be positive", nameof(radiusMm));
            Address = address; PosMm = posMm; Dir = dir / n; LenMm = lenMm; RadiusMm = radiusMm;
        }

        public double3 EndMm => PosMm + Dir * LenMm;
    }

    // A bifurcation: the parent segment and its two children (addresses parent + "L" and parent + "R"). The tree.json reader is P2-B01's (R8, PH-30).
    public readonly struct ForkView
    {
        public readonly AirwaySegmentMm Parent, Left, Right;

        public ForkView(in AirwaySegmentMm parent, in AirwaySegmentMm left, in AirwaySegmentMm right)
        {
            if (parent.Address == null || left.Address != parent.Address + "L" || right.Address != parent.Address + "R")
                throw new ArgumentException("child addresses must be parent + \"L\" and parent + \"R\"");
            Parent = parent; Left = left; Right = right;
        }

        public string Address => Parent.Address;

        public AirwaySegmentMm Child(int i) => i == 0 ? Left : Right;   // 0 = L, 1 = R
    }
}

using Unity.Mathematics;

namespace Atlas.Blades.Lung
{
    // One node of the offline 3D airway tree (MP §10.2.4). Dir is as stored, NOT renormalised (the compass rule must match the generator, D-G4-16).
    public readonly struct AirwayNode
    {
        public readonly string Address;
        public readonly int Gen;
        public readonly double LenMm, RadiusMm;
        public readonly double3 PosMm, Dir;
        public readonly string Source;

        public AirwayNode(AirwayTreeFileNode n)
        {
            Address = n.address; Gen = n.gen; LenMm = n.len_mm; RadiusMm = n.radius_mm;
            PosMm = new double3(n.pos_mm[0], n.pos_mm[1], n.pos_mm[2]);
            Dir = new double3(n.dir[0], n.dir[1], n.dir[2]);
            Source = n.source;
        }
    }
}

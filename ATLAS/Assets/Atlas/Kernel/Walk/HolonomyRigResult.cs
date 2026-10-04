namespace Atlas.Kernel
{
    public readonly struct HolonomyRigResult
    {
        public readonly long Frame;
        public readonly bool Stepped;
        public readonly Unity.Mathematics.double2 Applied;
        public readonly double Speed;
        public readonly bool Still;
        public readonly int Crossings;
        public readonly int LastEdge;
        public readonly bool CurvatureChanged;

        internal HolonomyRigResult(long frame, bool stepped, Unity.Mathematics.double2 applied, double speed, bool still,
            int crossings, int lastEdge, bool curvatureChanged)
        {
            Frame = frame; Stepped = stepped; Applied = applied; Speed = speed; Still = still;
            Crossings = crossings; LastEdge = lastEdge; CurvatureChanged = curvatureChanged;
        }
    }
}

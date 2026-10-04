using System;
using System.Collections.Generic;
using Atlas.Safety;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Walking.Tests
{
    // G6 §4: clearance = min(static SDF, distance to the nearest dynamic cell); a dynamic cell is the 5 cm square containing
    // the stamped point and the distance is 0 inside it (MP §4.10 clear(p)).
    sealed class FakeClearance : IClearanceQuery
    {
        readonly RoomModel _room;
        readonly List<double2> _cells = new List<double2>();   // minimum corners of the stamped 5 cm squares
        public FakeClearance(RoomModel room) { _room = room; }
        public void AddDynamic(double2 p) => _cells.Add(new double2(Math.Floor(p.x / 0.05) * 0.05, Math.Floor(p.y / 0.05) * 0.05));
        public void ClearDynamic() => _cells.Clear();
        public float Clearance(float2 p)
        {
            double best = _room.Sdf.Sample(p);
            for (int i = 0; i < _cells.Count; i++)
            {
                double dx = Math.Max(Math.Max(_cells[i].x - p.x, 0), p.x - (_cells[i].x + 0.05));
                double dy = Math.Max(Math.Max(_cells[i].y - p.y, 0), p.y - (_cells[i].y + 0.05));
                best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy));
            }
            return (float)best;
        }
    }

    sealed class ConstClearance : IClearanceQuery
    {
        readonly float _v; public ConstClearance(float v) { _v = v; }
        public float Clearance(float2 p) => _v;
    }
}

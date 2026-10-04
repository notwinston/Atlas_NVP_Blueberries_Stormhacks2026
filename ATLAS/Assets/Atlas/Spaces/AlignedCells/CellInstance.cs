using System;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells
{
    public struct CellInstance
    {
        public int2 Cell;
        public CellFrame Frame;
        public int GateCount;                       // 0..4
        public double4 Gate0, Gate1, Gate2, Gate3;  // (post.x, post.z, side +-1, 0), anchor metres; unused = all 0

        public double4 Gate(int i) => i switch { 0 => Gate0, 1 => Gate1, 2 => Gate2, _ => Gate3 };
    }

    public sealed class CellInstanceList
    {
        public const int Capacity = SightLines.MaxInstances;
        readonly CellInstance[] items = new CellInstance[Capacity];
        public int Count { get; private set; }
        public ref readonly CellInstance this[int i] => ref items[i];
        internal void Clear() => Count = 0;
        internal void Add(in CellInstance c)
        {
            if (Count >= Capacity) throw new InvalidOperationException("more than 57 cell instances");
            items[Count++] = c;
        }
    }

    // Exact C# mirror of AtlasGatesClip (Master Plan §4.7, §8.3; R1 §F.7).
    public static class GateMath
    {
        public static bool Keeps(in CellInstance inst, double2 eye, double2 fragment)
        {
            double2 d = fragment - eye;
            for (int i = 0; i < inst.GateCount; i++)
            {
                double4 gate = inst.Gate(i);
                double2 g = gate.xy - eye;
                if (gate.z != 0 && gate.z * (g.x * d.y - g.y * d.x) < 0) return false;
            }
            return true;
        }
    }
}

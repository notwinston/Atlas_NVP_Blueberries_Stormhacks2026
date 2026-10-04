using Atlas.Kernel;
using UnityEngine;

namespace Atlas.Rendering
{
    public static class WarpAnchor
    {
        // Master Plan §4.6: anchor[t] = inverse(U)·F[t], product in double
        public static Iso2 Compute(in Iso2 walker, in Iso2 chain) => walker.Inverse() * chain;

        // kernel.md §6.2 upload layout; the only double to float cast (MP §4.6 "cast to float last")
        public static Matrix4x4 ToMatrix(in Iso2 anchor)
        {
            var m = Matrix4x4.zero;
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    m[r, c] = (float)anchor.M[c][r]; // double3x3 is [column][row]; Matrix4x4 is [row, column]
            m[3, 3] = 1f;
            return m;
        }
    }
}

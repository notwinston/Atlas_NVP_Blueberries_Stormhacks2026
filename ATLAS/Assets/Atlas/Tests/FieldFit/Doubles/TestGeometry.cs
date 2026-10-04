using UnityEngine;

namespace Atlas.Blades.FieldFit.Tests
{
    // Master Plan §10.4.3 / Brief §10 test fixtures: a test double, not lane A's coefficients.
    internal sealed class TestGeometry : IEyeGeometry
    {
        const float TanH = 1.1917536f, TanV = 1.1106125f;      // tan 50 deg, tan 48 deg (Master Plan §8.5)
        public static readonly Vector2Int EyeScreen = new Vector2Int(1398, 1290);
        readonly bool barrel;
        TestGeometry(bool barrel) { this.barrel = barrel; }
        public static TestGeometry Identity => new TestGeometry(false);
        public static TestGeometry Dtest => new TestGeometry(true);

        public Vector4 Frustum => new Vector4(-TanH, TanH, -TanV, TanV);   // (l, r, b, t)

        public Vector2 ScreenToEyeUv(Vector2 q)
        {
            if (!barrel) return q;
            double x = q.x - 0.5, y = q.y - 0.5;
            double r2 = (2 * x) * (2 * x) + (2 * y) * (2 * y);
            double f = 1 + 0.22 * r2 + 0.24 * r2 * r2;
            return new Vector2((float)(0.5 + x * f), (float)(0.5 + y * f));
        }

        /// <summary>View tangent for a per-eye screen uv (Brief §7.1 step 3).</summary>
        public Vector2 ViewTangent(Vector2 q)
        {
            var eu = ScreenToEyeUv(q); var f = Frustum;
            return new Vector2(f.x + eu.x * (f.y - f.x), f.z + eu.y * (f.w - f.z));
        }
    }
}

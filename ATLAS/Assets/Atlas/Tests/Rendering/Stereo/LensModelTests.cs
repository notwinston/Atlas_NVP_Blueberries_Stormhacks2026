using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Rendering.Tests.Stereo
{
    /// <summary>The lens model of the headset (Master Plan D7, task P1-A05).</summary>
    public sealed class LensModelTests
    {
        // Largest radius on the screen, in tangent units, for the default numbers: the far corner of an eye's half.
        static float FieldRadius(LensModel lens)
        {
            float outer = 0.5f * (lens.ScreenWidth - lens.LensSeparation);
            float inner = 0.5f * lens.LensSeparation;
            float half = 0.5f * lens.ScreenHeight + math.abs(lens.VerticalOffset);
            return math.length(new float2(math.max(outer, inner), half)) / lens.ScreenToLens;
        }

        [Test]
        public void Defaults_AreTheCardboardV2ClassPlaceholders()
        {
            var lens = new LensModel();
            Assert.That(lens.K1, Is.EqualTo(0.34f));
            Assert.That(lens.K2, Is.EqualTo(0.55f));
            Assert.That(lens.LensSeparation, Is.EqualTo(0.064f));
            Assert.That(lens.ScreenToLens, Is.EqualTo(0.039f));
            Assert.That(lens.VerticalOffset, Is.EqualTo(0f));
            Assert.That(lens.ScreenWidth, Is.EqualTo(0.1544f));
            Assert.That(lens.ScreenHeight, Is.EqualTo(0.0712f));

            LensParams p = LensParams.Default;
            Assert.That(p.K1, Is.EqualTo(lens.K1));
            Assert.That(p.K2, Is.EqualTo(lens.K2));
            Assert.That(p.LensSeparation, Is.EqualTo(lens.LensSeparation));
            Assert.That(p.ScreenToLens, Is.EqualTo(lens.ScreenToLens));
            Assert.That(p.VerticalOffset, Is.EqualTo(lens.VerticalOffset));
        }

        [Test]
        public void DistortThenUndistort_IsTheIdentityOverTheField()
        {
            var lens = new LensModel();
            float field = FieldRadius(lens);
            Assert.That(field, Is.GreaterThan(1.2f), "the field must reach the corner of the half-screen");
            for (int i = 0; i <= 2000; i++)
            {
                float r = field * i / 2000f;
                Assert.That(lens.Undistort(lens.Distort(r)), Is.EqualTo(r).Within(1e-5f), "r = " + r);
            }
        }

        [Test]
        public void UndistortThenDistort_IsTheIdentityOverTheField()
        {
            var lens = new LensModel();
            float seenMax = lens.Distort(FieldRadius(lens));
            for (int i = 0; i <= 2000; i++)
            {
                float seen = seenMax * i / 2000f;
                Assert.That(lens.Distort(lens.Undistort(seen)), Is.EqualTo(seen).Within(1e-5f * math.max(1f, seen)), "r' = " + seen);
            }
        }

        [Test]
        public void DistortThenUndistort_OnPoints_IsTheIdentity()
        {
            var lens = new LensModel();
            float field = FieldRadius(lens) / math.sqrt(2f);
            for (int y = -10; y <= 10; y++)
            for (int x = -10; x <= 10; x++)
            {
                var p = new float2(x, y) * (field / 10f);
                float2 back = lens.Undistort(lens.Distort(p));
                Assert.That(back.x, Is.EqualTo(p.x).Within(1e-5f));
                Assert.That(back.y, Is.EqualTo(p.y).Within(1e-5f));
            }
        }

        [Test]
        public void Undistort_IsOddAndHandlesZero()
        {
            var lens = new LensModel();
            Assert.That(lens.Undistort(0f), Is.EqualTo(0f));
            Assert.That(lens.Undistort(-1.5f), Is.EqualTo(-lens.Undistort(1.5f)));
            float2 zero = lens.Undistort(new float2(0f, 0f));
            Assert.That(zero.x, Is.EqualTo(0f));
            Assert.That(zero.y, Is.EqualTo(0f));
        }

        [Test]
        public void RadialMap_IsStrictlyMonotonic_AndBarrelPreDistortionCompresses()
        {
            var lens = new LensModel();
            float field = FieldRadius(lens);
            float previous = lens.Distort(0f);
            float previousInverse = 0f;
            Assert.That(previous, Is.EqualTo(0f));
            for (int i = 1; i <= 2000; i++)
            {
                float r = field * i / 2000f;
                float seen = lens.Distort(r);
                Assert.That(seen, Is.GreaterThan(previous), "Distort must grow at r = " + r);
                Assert.That(seen, Is.GreaterThanOrEqualTo(r), "the lens magnifies away from the axis");
                float inverse = lens.Undistort(seen);
                Assert.That(inverse, Is.GreaterThan(previousInverse), "Undistort must grow at r' = " + seen);
                previous = seen;
                previousInverse = inverse;
            }
        }

        [Test]
        public void Distort_MatchesThePolynomial()
        {
            // r (1 + 0.34 r^2 + 0.55 r^4) at r = 1: 1.89.
            Assert.That(LensModel.Distort(1f, 0.34f, 0.55f), Is.EqualTo(1.89f).Within(1e-6f));
            // No coefficients: the identity.
            Assert.That(LensModel.Distort(0.7f, 0f, 0f), Is.EqualTo(0.7f));
            Assert.That(LensModel.Undistort(0.7f, 0f, 0f), Is.EqualTo(0.7f).Within(1e-7f));
        }

        [Test]
        public void Frustum_DefaultHeadset_IsTheContractFieldOfView()
        {
            // Lane A contract section 9.2 and 9.4: symmetric, 96 degrees vertically at aspect 1398 / 1290, so
            // tanV = 1.1106 and tanH = 1.2036 (50.3 degrees to each side, 48 up and down). The screen reaches
            // further on every side (inner 1.2129, outer 2.838, up and down 1.5201) and is clamped.
            var lens = new LensModel();
            foreach (Eye eye in new[] { Eye.Left, Eye.Right })
            {
                EyeFrustum f = lens.GetFrustum(eye);
                Assert.That(f.TanLeft, Is.EqualTo(1.2036f).Within(2e-4f));
                Assert.That(f.TanRight, Is.EqualTo(1.2036f).Within(2e-4f));
                Assert.That(f.TanDown, Is.EqualTo(1.1106f).Within(1e-4f));
                Assert.That(f.TanUp, Is.EqualTo(1.1106f).Within(1e-4f));
                Assert.That(f.HorizontalFovDegrees, Is.EqualTo(100.56f).Within(0.02f));
                Assert.That(f.VerticalFovDegrees, Is.EqualTo(96f).Within(0.01f));
                // The eye texture (1118 x 1032 at scale 0.8) has the same shape, so its pixels are square.
                Assert.That((f.TanLeft + f.TanRight) / (f.TanDown + f.TanUp), Is.EqualTo(1398f / 1290f).Within(2e-4f));
            }
        }

        [Test]
        public void Projection_DefaultHeadset_IsSymmetricAndTheSameForBothEyes()
        {
            var lens = new LensModel();
            float4x4 left = lens.GetProjection(Eye.Left, 0.05f, 60f);
            float4x4 right = lens.GetProjection(Eye.Right, 0.05f, 60f);
            Assert.That(left.c2.x, Is.EqualTo(0f));
            Assert.That(left.c2.y, Is.EqualTo(0f));
            Assert.That(left.c0.x, Is.EqualTo(1f / 1.2036f).Within(2e-4f));
            Assert.That(left.c1.y, Is.EqualTo(1f / 1.1106f).Within(1e-4f));
            Assert.That(right, Is.EqualTo(left));
        }

        [Test]
        public void Blit_DefaultHeadset_MatchesTheContractMapping()
        {
            // Section 9.4: uv = 0.5 + r' / (2 (tanH, tanV)), black outside [0, 1].
            var lens = new LensModel();
            float2 screen = new float2(0.5f + 0.5f * (-0.032f + 0.010f) / (0.5f * 0.1544f), 0.5f + 0.5f * 0.008f / (0.5f * 0.0712f));
            float2 seen = lens.Distort(new float2(0.010f, 0.008f) / 0.039f);
            float2 uv = lens.ScreenToEyeUv(screen, true, out Eye eye);
            Assert.That(eye, Is.EqualTo(Eye.Left));
            Assert.That(uv.x, Is.EqualTo(0.5f + seen.x / (2f * 1.2036f)).Within(2e-4f));
            Assert.That(uv.y, Is.EqualTo(0.5f + seen.y / (2f * 1.1106f)).Within(2e-4f));

            // The top edge of the screen (r' = 1.52) lies beyond 48 degrees: outside the texture, so black.
            float2 top = lens.ScreenToEyeUv(new float2(0.25f, 0.999f), true, out _);
            Assert.That(top.y, Is.GreaterThan(1f));
        }

        [Test]
        public void Frustum_WithoutTheClamp_FollowsTheScreenEdge()
        {
            var lens = new LensModel { MaxFovHorizontalDegrees = 85f, MaxFovVerticalDegrees = 85f };
            EyeFrustum left = lens.GetFrustum(Eye.Left);
            float r = 0.5f * (0.1544f - 0.064f) / 0.039f;
            Assert.That(left.TanLeft, Is.EqualTo(lens.Distort(r)).Within(1e-5f));
            Assert.That(left.TanLeft, Is.GreaterThan(left.TanRight));
            // Inner side: r = 0.032 / 0.039 = 0.82051, r' = 1.21291. Up and down: r = 0.91282, r' = 1.52010.
            Assert.That(left.TanRight, Is.EqualTo(1.21291f).Within(2e-4f));
            Assert.That(left.TanUp, Is.EqualTo(1.52010f).Within(2e-4f));
            Assert.That(left.TanDown, Is.EqualTo(left.TanUp).Within(1e-6f));
        }

        [Test]
        public void Frustum_IsMirroredBetweenTheEyes()
        {
            var lens = new LensModel { VerticalOffset = 0.004f, LensSeparation = 0.061f, MaxFovHorizontalDegrees = 80f, MaxFovVerticalDegrees = 80f };
            EyeFrustum left = lens.GetFrustum(Eye.Left);
            EyeFrustum right = lens.GetFrustum(Eye.Right);
            Assert.That(right.TanLeft, Is.EqualTo(left.TanRight));
            Assert.That(right.TanRight, Is.EqualTo(left.TanLeft));
            Assert.That(right.TanUp, Is.EqualTo(left.TanUp));
            Assert.That(right.TanDown, Is.EqualTo(left.TanDown));
            // The lens centre sits above the screen centre, so less screen is left above it.
            Assert.That(left.TanUp, Is.LessThan(left.TanDown));
        }

        [Test]
        public void Projection_IsOffAxis_AndMirroredBetweenTheEyes()
        {
            // Limits beyond the screen, so the frustum follows the screen: nose side narrower than the outer side.
            var lens = new LensModel { MaxFovHorizontalDegrees = 85f, MaxFovVerticalDegrees = 85f };
            float4x4 left = lens.GetProjection(Eye.Left, 0.05f, 60f);
            float4x4 right = lens.GetProjection(Eye.Right, 0.05f, 60f);

            // Same scale, opposite horizontal shift.
            Assert.That(right.c0.x, Is.EqualTo(left.c0.x));
            Assert.That(right.c1.y, Is.EqualTo(left.c1.y));
            Assert.That(right.c2.x, Is.EqualTo(-left.c2.x).Within(1e-7f));
            // The left eye sees further to the left than to the right, so the axis lies right of the centre:
            // (r + l) / (r - l) with l = -tanLeft is negative.
            Assert.That(left.c2.x, Is.LessThan(0f));
            Assert.That(left.c2.y, Is.EqualTo(0f).Within(1e-7f), "no vertical offset by default");
            // Perspective row.
            Assert.That(left.c2.w, Is.EqualTo(-1f));
            Assert.That(left.c3.w, Is.EqualTo(0f));
        }

        [Test]
        public void Projection_MapsTheFrustumEdgesToTheClipEdges()
        {
            var lens = new LensModel { VerticalOffset = -0.003f };
            const float near = 0.05f, far = 60f;
            foreach (Eye eye in new[] { Eye.Left, Eye.Right })
            {
                EyeFrustum f = lens.GetFrustum(eye);
                float4x4 p = lens.GetProjection(eye, near, far);

                // View space looks down -z. A point on the left edge at depth d has x = -tanLeft * d.
                float2 bottomLeft = Ndc(p, new float3(-f.TanLeft * 3f, -f.TanDown * 3f, -3f));
                float2 topRight = Ndc(p, new float3(f.TanRight * 3f, f.TanUp * 3f, -3f));
                Assert.That(bottomLeft.x, Is.EqualTo(-1f).Within(1e-5f));
                Assert.That(bottomLeft.y, Is.EqualTo(-1f).Within(1e-5f));
                Assert.That(topRight.x, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(topRight.y, Is.EqualTo(1f).Within(1e-5f));

                // Depth: near to -1, far to +1.
                float4 n = math.mul(p, new float4(0f, 0f, -near, 1f));
                float4 fa = math.mul(p, new float4(0f, 0f, -far, 1f));
                Assert.That(n.z / n.w, Is.EqualTo(-1f).Within(1e-4f));
                Assert.That(fa.z / fa.w, Is.EqualTo(1f).Within(1e-4f));
            }
        }

        static float2 Ndc(float4x4 projection, float3 view)
        {
            float4 clip = math.mul(projection, new float4(view, 1f));
            return clip.xy / clip.w;
        }

        [Test]
        public void Blit_LensCentreShowsTheOpticalAxis()
        {
            // The screen point in front of a lens centre must show what lies straight ahead of that eye,
            // which the eye camera draws at the uv its projection gives the axis.
            var lens = new LensModel { VerticalOffset = 0.002f };
            foreach (Eye eye in new[] { Eye.Left, Eye.Right })
            {
                float2 centre = lens.LensCentre(eye);
                var screenUv = new float2(0.5f + centre.x / lens.ScreenWidth, 0.5f + centre.y / lens.ScreenHeight);
                float2 uv = lens.ScreenToEyeUv(screenUv, true, out Eye hit);
                Assert.That(hit, Is.EqualTo(eye));

                float2 axis = Ndc(lens.GetProjection(eye, 0.05f, 60f), new float3(0f, 0f, -1f)) * 0.5f + 0.5f;
                Assert.That(uv.x, Is.EqualTo(axis.x).Within(1e-5f));
                Assert.That(uv.y, Is.EqualTo(axis.y).Within(1e-5f));
            }
        }

        [Test]
        public void Blit_AgreesWithTheProjectionAcrossTheHalfScreen()
        {
            // For any screen point: the direction the eye sees it in, pushed through the eye's projection,
            // is where the blit samples.
            var lens = new LensModel();
            for (int iy = 1; iy < 10; iy++)
            for (int ix = 1; ix < 20; ix++)
            {
                var screenUv = new float2(ix / 20f + 0.001f, iy / 10f);
                float2 uv = lens.ScreenToEyeUv(screenUv, true, out Eye eye);

                float2 metres = (screenUv - 0.5f) * new float2(lens.ScreenWidth, lens.ScreenHeight);
                float2 seen = lens.Distort((metres - lens.LensCentre(eye)) / lens.ScreenToLens);
                float2 expected = Ndc(lens.GetProjection(eye, 0.05f, 60f), new float3(seen, -1f)) * 0.5f + 0.5f;
                Assert.That(uv.x, Is.EqualTo(expected.x).Within(1e-4f));
                Assert.That(uv.y, Is.EqualTo(expected.y).Within(1e-4f));
            }
        }

        [Test]
        public void Blit_IsMirroredBetweenTheEyes()
        {
            var lens = new LensModel();
            for (int i = 1; i < 10; i++)
            {
                var left = new float2(0.5f - i * 0.045f, 0.3f);
                var right = new float2(0.5f + i * 0.045f, 0.3f);
                float2 a = lens.ScreenToEyeUv(left, true, out Eye eyeA);
                float2 b = lens.ScreenToEyeUv(right, true, out Eye eyeB);
                Assert.That(eyeA, Is.EqualTo(Eye.Left));
                Assert.That(eyeB, Is.EqualTo(Eye.Right));
                Assert.That(b.x, Is.EqualTo(1f - a.x).Within(1e-5f));
                Assert.That(b.y, Is.EqualTo(a.y).Within(1e-5f));
            }
        }

        [Test]
        public void Blit_WithoutDistortion_IsAPlainSideBySideCopy()
        {
            var lens = new LensModel();
            float2 a = lens.ScreenToEyeUv(new float2(0.125f, 0.75f), false, out Eye eyeA);
            float2 b = lens.ScreenToEyeUv(new float2(0.625f, 0.75f), false, out Eye eyeB);
            Assert.That(eyeA, Is.EqualTo(Eye.Left));
            Assert.That(eyeB, Is.EqualTo(Eye.Right));
            Assert.That(a.x, Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(b.x, Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(a.y, Is.EqualTo(0.75f));
            Assert.That(b.y, Is.EqualTo(0.75f));
        }

        [Test]
        public void Version_ChangesWithEveryEdit_AndOnlyThen()
        {
            var lens = new LensModel();
            int v = lens.Version;
            lens.K1 = lens.K1;
            Assert.That(lens.Version, Is.EqualTo(v));
            lens.K1 = 0.3f;
            Assert.That(lens.Version, Is.Not.EqualTo(v));
            v = lens.Version;
            lens.Params = LensParams.Default;
            Assert.That(lens.Version, Is.Not.EqualTo(v));
            Assert.That(lens.K1, Is.EqualTo(0.34f));
        }

        [Test]
        public void ScreenSize_FromPixelsAndDensity()
        {
            var lens = new LensModel();
            // Both phones: 2796 x 1290 at 460 px per inch, in either orientation.
            Assert.That(lens.SetScreenFromPixels(2796, 1290, 460f), Is.True);
            Assert.That(lens.ScreenWidth, Is.EqualTo(0.1544f).Within(1e-4f));
            Assert.That(lens.ScreenHeight, Is.EqualTo(0.0712f).Within(1e-4f));
            Assert.That(lens.SetScreenFromPixels(1290, 2796, 460f), Is.True);
            Assert.That(lens.ScreenWidth, Is.EqualTo(0.1544f).Within(1e-4f));

            // A desktop window or an unknown density keeps the phone's size.
            Assert.That(lens.SetScreenFromPixels(1920, 1080, 96f), Is.False);
            Assert.That(lens.ScreenWidth, Is.EqualTo(LensModel.DefaultScreenWidth));
            Assert.That(lens.SetScreenFromPixels(2796, 1290, 0f), Is.False);
            Assert.That(lens.ScreenHeight, Is.EqualTo(LensModel.DefaultScreenHeight));
        }
    }
}

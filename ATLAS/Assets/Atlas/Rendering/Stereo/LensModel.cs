using System;
using Unity.Mathematics;

namespace Atlas.Rendering
{
    /// <summary>Optics of the headset as five numbers. Lengths in metres.</summary>
    public struct LensParams
    {
        /// <summary>Radial distortion, first coefficient.</summary>
        public float K1;
        /// <summary>Radial distortion, second coefficient.</summary>
        public float K2;
        /// <summary>Distance between the two lens centres.</summary>
        public float LensSeparation;
        /// <summary>Distance from the screen to the lens.</summary>
        public float ScreenToLens;
        /// <summary>Lens centre above (+) or below (-) the screen centre.</summary>
        public float VerticalOffset;

        /// <summary>
        /// PLACEHOLDER values of the public Cardboard V2 viewer profile, used as numbers only, until the real
        /// headset is measured (Master Plan section 14, open item 1): K1 0.34, K2 0.55, separation 0.064,
        /// screen to lens 0.039, vertical offset 0.
        /// </summary>
        public static LensParams Default => new LensParams
        {
            K1 = LensModel.DefaultK1,
            K2 = LensModel.DefaultK2,
            LensSeparation = LensModel.DefaultLensSeparation,
            ScreenToLens = LensModel.DefaultScreenToLens,
            VerticalOffset = LensModel.DefaultVerticalOffset,
        };
    }

    /// <summary>
    /// The view frustum of one eye as four tangents, all positive: the half-angles to the left, right, bottom
    /// and top of the eye's optical axis.
    /// </summary>
    public struct EyeFrustum
    {
        public float TanLeft;
        public float TanRight;
        public float TanDown;
        public float TanUp;

        /// <summary>Full horizontal field of view. Degrees.</summary>
        public float HorizontalFovDegrees => math.degrees(math.atan(TanLeft) + math.atan(TanRight));
        /// <summary>Full vertical field of view. Degrees.</summary>
        public float VerticalFovDegrees => math.degrees(math.atan(TanDown) + math.atan(TanUp));
    }

    /// <summary>
    /// The lens model of the headset (Master Plan D7, task P1-A05): the radial distortion polynomial and its
    /// inverse, and the per-eye field of view and off-axis projection that follow from the lens numbers and the
    /// physical size of the screen. Engine-free; every value is a plain settable property so that it can be
    /// tuned live through the lenses. <see cref="Version"/> changes with every edit, so users re-read lazily.
    /// </summary>
    public sealed class LensModel
    {
        // PLACEHOLDERS (open item 1): the Cardboard V2 class viewer profile. Replace once the headset is measured.
        public const float DefaultK1 = 0.34f;
        public const float DefaultK2 = 0.55f;
        public const float DefaultLensSeparation = 0.064f;
        public const float DefaultScreenToLens = 0.039f;
        public const float DefaultVerticalOffset = 0f;
        /// <summary>
        /// Half the field of view to each side: the contract's symmetric projection (lane A contract section
        /// 9.2; Master Plan section 8.5, "about 50 degrees to each side"). tan = tan(48 deg) x 1398 / 1290 = 1.2036.
        /// </summary>
        public const float DefaultMaxFovHorizontalDegrees = 50.28f;
        /// <summary>Half the field of view up and down: 96 degrees in all (section 9.2). tan = 1.1106.</summary>
        public const float DefaultMaxFovVerticalDegrees = 48f;

        /// <summary>2796 x 1290 px at 460 px per inch: both phones (iPhone 14 Pro Max, 15 Pro Max). Metres.</summary>
        public const float DefaultScreenWidth = 0.1544f;
        public const float DefaultScreenHeight = 0.0712f;

        const float MetresPerInch = 0.0254f;
        // A distance on the screen never collapses to zero, so a wrong live value cannot produce a degenerate frustum.
        const float MinExtent = 0.001f;

        float _k1 = DefaultK1;
        float _k2 = DefaultK2;
        float _lensSeparation = DefaultLensSeparation;
        float _screenToLens = DefaultScreenToLens;
        float _verticalOffset = DefaultVerticalOffset;
        float _maxFovHorizontalDegrees = DefaultMaxFovHorizontalDegrees;
        float _maxFovVerticalDegrees = DefaultMaxFovVerticalDegrees;
        float _screenWidth = DefaultScreenWidth;
        float _screenHeight = DefaultScreenHeight;
        int _version;

        /// <summary>Incremented by every change of a value.</summary>
        public int Version => _version;

        /// <summary>Radial distortion, first coefficient.</summary>
        public float K1 { get => _k1; set => Set(ref _k1, value); }
        /// <summary>Radial distortion, second coefficient.</summary>
        public float K2 { get => _k2; set => Set(ref _k2, value); }
        /// <summary>Distance between the two lens centres. Metres.</summary>
        public float LensSeparation { get => _lensSeparation; set => Set(ref _lensSeparation, value); }
        /// <summary>Distance from the screen to the lens. Metres.</summary>
        public float ScreenToLens { get => _screenToLens; set => Set(ref _screenToLens, math.max(value, MinExtent)); }
        /// <summary>Lens centre above (+) or below (-) the screen centre. Metres.</summary>
        public float VerticalOffset { get => _verticalOffset; set => Set(ref _verticalOffset, value); }
        /// <summary>
        /// Largest half-angle drawn to the left and to the right; the frustum is clamped to it. Degrees. The
        /// draw and triangle budgets are planned for the default; a wider value is a contract change.
        /// </summary>
        public float MaxFovHorizontalDegrees { get => _maxFovHorizontalDegrees; set => Set(ref _maxFovHorizontalDegrees, math.clamp(value, 10f, 85f)); }
        /// <summary>Largest half-angle drawn up and down; the frustum is clamped to it. Degrees.</summary>
        public float MaxFovVerticalDegrees { get => _maxFovVerticalDegrees; set => Set(ref _maxFovVerticalDegrees, math.clamp(value, 10f, 85f)); }
        /// <summary>Physical width of the screen in landscape (the long side). Metres.</summary>
        public float ScreenWidth { get => _screenWidth; set => Set(ref _screenWidth, math.max(value, MinExtent)); }
        /// <summary>Physical height of the screen in landscape (the short side). Metres.</summary>
        public float ScreenHeight { get => _screenHeight; set => Set(ref _screenHeight, math.max(value, MinExtent)); }

        /// <summary>The five lens numbers as one value.</summary>
        public LensParams Params
        {
            get => new LensParams
            {
                K1 = _k1, K2 = _k2, LensSeparation = _lensSeparation,
                ScreenToLens = _screenToLens, VerticalOffset = _verticalOffset,
            };
            set
            {
                K1 = value.K1;
                K2 = value.K2;
                LensSeparation = value.LensSeparation;
                ScreenToLens = value.ScreenToLens;
                VerticalOffset = value.VerticalOffset;
            }
        }

        void Set(ref float field, float value)
        {
            if (field == value || float.IsNaN(value))
                return;
            field = value;
            _version++;
        }

        /// <summary>
        /// Takes the physical screen size from its pixel size and pixel density. The longer side becomes the
        /// width. Returns false and keeps the default size (0.1544 x 0.0712 m) when the density is unknown or
        /// the result is not a phone screen (long side outside 0.10 .. 0.20 m), as in the Editor.
        /// </summary>
        public bool SetScreenFromPixels(int widthPixels, int heightPixels, float pixelsPerInch)
        {
            int longSide = math.max(widthPixels, heightPixels);
            int shortSide = math.min(widthPixels, heightPixels);
            if (pixelsPerInch > 0f && shortSide > 0)
            {
                float width = longSide / pixelsPerInch * MetresPerInch;
                float height = shortSide / pixelsPerInch * MetresPerInch;
                if (width >= 0.10f && width <= 0.20f)
                {
                    ScreenWidth = width;
                    ScreenHeight = height;
                    return true;
                }
            }

            ScreenWidth = DefaultScreenWidth;
            ScreenHeight = DefaultScreenHeight;
            return false;
        }

        // ------------------------------------------------------------------ the polynomial and its inverse

        /// <summary>
        /// The lens map on a radius: r is the tangent of the angle before the lens (distance on the screen from
        /// the lens centre, divided by the screen-to-lens distance); the result is the tangent of the angle the
        /// eye sees. r' = r (1 + K1 r^2 + K2 r^4).
        /// </summary>
        public static float Distort(float r, float k1, float k2)
        {
            double x = r;
            double r2 = x * x;
            return (float)(x * (1.0 + r2 * (k1 + k2 * r2)));
        }

        /// <summary>
        /// The inverse of <see cref="Distort(float,float,float)"/> by Newton's method. Exact to float precision
        /// for coefficients that are not negative, where the map is monotonic.
        /// </summary>
        public static float Undistort(float distorted, float k1, float k2)
        {
            double target = math.abs(distorted);
            // The polynomial is convex and increasing for r >= 0, so starting at r = target (never below the
            // root) Newton's method descends to the root without overshooting.
            double r = target;
            for (int i = 0; i < 32; i++)
            {
                double r2 = r * r;
                double f = r * (1.0 + r2 * (k1 + k2 * r2)) - target;
                double slope = 1.0 + r2 * (3.0 * k1 + 5.0 * k2 * r2);
                if (slope <= 1e-9)
                    break;
                double next = r - f / slope;
                if (Math.Abs(next - r) <= 1e-12)
                {
                    r = next;
                    break;
                }
                r = next;
            }
            return (float)(distorted < 0f ? -r : r);
        }

        /// <summary>The lens map on a radius with this model's coefficients.</summary>
        public float Distort(float r) => Distort(r, _k1, _k2);
        /// <summary>The inverse lens map on a radius with this model's coefficients.</summary>
        public float Undistort(float distorted) => Undistort(distorted, _k1, _k2);

        /// <summary>The lens map on a point in tangent space about the lens centre.</summary>
        public float2 Distort(float2 p)
        {
            float r2 = math.dot(p, p);
            return p * (1f + r2 * (_k1 + _k2 * r2));
        }

        /// <summary>The inverse lens map on a point in tangent space about the lens centre.</summary>
        public float2 Undistort(float2 p)
        {
            float rd = math.length(p);
            if (rd < 1e-12f)
                return p;
            return p * (Undistort(rd) / rd);
        }

        // ------------------------------------------------------------------ field of view and projection

        /// <summary>Lens centre of an eye on the screen, from the screen centre (x right, y up). Metres.</summary>
        public float2 LensCentre(Eye eye)
            => new float2(eye == Eye.Left ? -0.5f * _lensSeparation : 0.5f * _lensSeparation, _verticalOffset);

        /// <summary>
        /// The frustum of an eye: how far the eye sees through its lens to each edge of its half of the screen,
        /// clamped to <see cref="MaxFovHorizontalDegrees"/> and <see cref="MaxFovVerticalDegrees"/>. With the
        /// default lens numbers the screen reaches past both limits on every side, so the frustum is the
        /// symmetric 100.6 x 96 degrees of the contract and the screen beyond it is black (section 9.4). It
        /// turns off-axis only where the screen ends before the limit (the nose side of a narrow lens
        /// separation, or a vertical lens offset).
        /// </summary>
        public EyeFrustum GetFrustum(Eye eye)
        {
            float inner = math.max(0.5f * _lensSeparation, MinExtent);
            float outer = math.max(0.5f * (_screenWidth - _lensSeparation), MinExtent);
            float up = math.max(0.5f * _screenHeight - _verticalOffset, MinExtent);
            float down = math.max(0.5f * _screenHeight + _verticalOffset, MinExtent);

            float tanMaxH = math.tan(math.radians(_maxFovHorizontalDegrees));
            float tanMaxV = math.tan(math.radians(_maxFovVerticalDegrees));
            float tanInner = math.min(Distort(inner / _screenToLens), tanMaxH);
            float tanOuter = math.min(Distort(outer / _screenToLens), tanMaxH);

            EyeFrustum f;
            f.TanLeft = eye == Eye.Left ? tanOuter : tanInner;
            f.TanRight = eye == Eye.Left ? tanInner : tanOuter;
            f.TanDown = math.min(Distort(down / _screenToLens), tanMaxV);
            f.TanUp = math.min(Distort(up / _screenToLens), tanMaxV);
            return f;
        }

        /// <summary>
        /// Projection of an eye (off-axis when its frustum is) in Unity's Camera.projectionMatrix convention (OpenGL clip space, the
        /// camera looks down -z). Assign it to Camera.projectionMatrix as it is.
        /// </summary>
        public float4x4 GetProjection(Eye eye, float near, float far) => Projection(GetFrustum(eye), near, far);

        /// <summary>The projection of a frustum given as tangents (see <see cref="GetProjection"/>).</summary>
        public static float4x4 Projection(in EyeFrustum f, float near, float far)
        {
            float width = f.TanLeft + f.TanRight;
            float height = f.TanDown + f.TanUp;
            return new float4x4(
                2f / width, 0f, (f.TanRight - f.TanLeft) / width, 0f,
                0f, 2f / height, (f.TanUp - f.TanDown) / height, 0f,
                0f, 0f, -(far + near) / (far - near), -2f * far * near / (far - near),
                0f, 0f, -1f, 0f);
        }

        // ------------------------------------------------------------------ the lens blit's mapping

        /// <summary>
        /// The constants of the lens pass, in the layout its shader reads:
        /// screen = (screen width, screen height, half the lens separation, vertical offset) / ScreenToLens;
        /// coefficients = (K1, K2, 0, 0);
        /// frustum = (tanLeft, tanDown, 1 / (tanLeft + tanRight), 1 / (tanDown + tanUp)) for each eye.
        /// </summary>
        public void GetBlitConstants(out float4 screen, out float4 coefficients, out float4 frustumLeft, out float4 frustumRight)
        {
            float inv = 1f / _screenToLens;
            screen = new float4(_screenWidth * inv, _screenHeight * inv, 0.5f * _lensSeparation * inv, _verticalOffset * inv);
            coefficients = new float4(_k1, _k2, 0f, 0f);
            frustumLeft = Pack(GetFrustum(Eye.Left));
            frustumRight = Pack(GetFrustum(Eye.Right));
        }

        static float4 Pack(in EyeFrustum f)
            => new float4(f.TanLeft, f.TanDown, 1f / (f.TanLeft + f.TanRight), 1f / (f.TanDown + f.TanUp));

        /// <summary>
        /// What the lens pass computes for one screen position: which eye's half it lies in and where that
        /// eye's texture is sampled. screenUv is (0, 0) at the bottom left and (1, 1) at the top right of the
        /// screen in landscape. A result outside [0, 1] is drawn black. The shader repeats this arithmetic.
        /// </summary>
        public float2 ScreenToEyeUv(float2 screenUv, bool distortion, out Eye eye)
        {
            eye = screenUv.x < 0.5f ? Eye.Left : Eye.Right;
            if (!distortion)
                return new float2(2f * screenUv.x - (eye == Eye.Left ? 0f : 1f), screenUv.y);

            GetBlitConstants(out float4 screen, out _, out float4 frustumLeft, out float4 frustumRight);
            float2 p = (screenUv - 0.5f) * screen.xy;
            float2 centre = new float2(eye == Eye.Left ? -screen.z : screen.z, screen.w);
            float2 seen = Distort(p - centre);
            float4 f = eye == Eye.Left ? frustumLeft : frustumRight;
            return (seen + f.xy) * f.zw;
        }
    }
}

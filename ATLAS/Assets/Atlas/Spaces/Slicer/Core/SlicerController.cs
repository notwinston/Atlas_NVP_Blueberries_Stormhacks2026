using Atlas.Kernel.FourD;
using Unity.Mathematics;

namespace Atlas.Spaces.Slicer
{
    /// <summary>Which input moves the cut at the moment.</summary>
    public enum SlicerCutOwner : byte
    {
        /// <summary>The walker's place on the w lane.</summary>
        Lane = 0,
        /// <summary>The stick, or its gaze handles.</summary>
        Stick = 1,
        /// <summary>A value set from outside: the Station, the on-device menu, a restored preset.</summary>
        Parameter = 2,
    }

    /// <summary>What the controller is told each frame. Axes are -1 to 1, before any dead zone.</summary>
    public struct SlicerInput
    {
        /// <summary>The walker's floor position (x, z), room frame, metres.</summary>
        public float2 Floor;
        public float AxisW;
        public float AxisXw, AxisYw, AxisZw;
        public float DeltaTime;
    }

    /// <summary>
    /// The state of the Slicer and the rule that moves it (Master Plan section 5.1; lane A contract, section
    /// 14.3): the cut offset w0 and the three turns in the planes that contain w. Engine-free: it takes the
    /// walker's position, the sticks and the time step, and never reads a clock or a device.
    /// </summary>
    /// <remarks>
    /// The walker's own motion is never scaled (rule 3): walking the lane moves the cut, not the walker.
    /// The lane maps position to offset absolutely, so the same spot on the floor is always the same slice.
    /// A stick or a value set from outside takes the cut over; the lane takes it back once that input has been
    /// quiet for <see cref="OwnerHoldSeconds"/> and the walker has moved <see cref="LaneReclaimMetres"/> along
    /// the lane, so a value set from the Station holds for as long as the walker stands still.
    /// </remarks>
    public sealed class SlicerController
    {
        public const float StickDeadZone = 0.15f;
        /// <summary>Model units per second at full stick: the whole range in under four seconds.</summary>
        public const float CutRatePerSecond = 0.6f;
        /// <summary>Radians per second at full stick: 60 degrees.</summary>
        public const float TurnRatePerSecond = math.PI / 3f;
        public const float OwnerHoldSeconds = 2f;
        public const float LaneReclaimMetres = 0.05f;
        /// <summary>Steps of the cut over its range with <see cref="SlicerSource.Stepped"/>.</summary>
        public const int SteppedSteps = 32;

        readonly SlicerLayout _layout;
        float _w, _xw, _yw, _zw;
        SlicerCutOwner _owner;
        float _hold;
        float _refZ;
        bool _hasRef;

        public SlicerController(in SlicerLayout layout)
        {
            _layout = layout;
            _owner = SlicerCutOwner.Parameter;
        }

        public SlicerLayout Layout { get { return _layout; } }

        /// <summary>The cut offset w0, model units, within +-<see cref="SlicerObjects.CutRange"/>.</summary>
        public float W { get { return _w; } }
        /// <summary>Turn in the XW plane, radians, within -pi .. pi.</summary>
        public float Xw { get { return _xw; } }
        public float Yw { get { return _yw; } }
        public float Zw { get { return _zw; } }
        public SlicerCutOwner Owner { get { return _owner; } }

        /// <summary>Starts from stored values (angles in radians). The cut keeps the value given until the
        /// walker moves along the lane or touches the stick.</summary>
        public void Reset(float w, float xw, float yw, float zw)
        {
            _w = ClampCut(w);
            _xw = WrapAngle(xw);
            _yw = WrapAngle(yw);
            _zw = WrapAngle(zw);
            _owner = SlicerCutOwner.Parameter;
            _hold = 0f;
            _hasRef = false;
        }

        /// <summary>The cut set from outside. It owns the cut for <see cref="OwnerHoldSeconds"/>.</summary>
        public void SetCut(float w)
        {
            _w = ClampCut(w);
            _owner = SlicerCutOwner.Parameter;
            _hold = OwnerHoldSeconds;
            _hasRef = false;
        }

        /// <summary>A turn set from outside: plane 0 XW, 1 YW, 2 ZW; radians.</summary>
        public void SetAngle(int plane, float radians)
        {
            float a = WrapAngle(radians);
            if (plane == 0) _xw = a;
            else if (plane == 1) _yw = a;
            else if (plane == 2) _zw = a;
        }

        /// <summary>One frame. No allocation.</summary>
        public void Step(in SlicerInput input)
        {
            float dt = math.clamp(input.DeltaTime, 0f, 0.1f);
            float z = input.Floor.y;
            _hold = math.max(0f, _hold - dt);
            if (!_hasRef)
            {
                _refZ = z;
                _hasRef = true;
            }

            float stick = DeadZone(input.AxisW);
            if (stick != 0f)
            {
                _w = ClampCut(_w + stick * CutRatePerSecond * dt);
                _owner = SlicerCutOwner.Stick;
                _hold = OwnerHoldSeconds;
                _refZ = z;
            }
            else if (_layout.HasLane)
            {
                if (_owner != SlicerCutOwner.Lane && _hold <= 0f && math.abs(z - _refZ) >= LaneReclaimMetres)
                    _owner = SlicerCutOwner.Lane;
                if (_owner == SlicerCutOwner.Lane) _w = _layout.CutAt(z);
            }

            _xw = WrapAngle(_xw + DeadZone(input.AxisXw) * TurnRatePerSecond * dt);
            _yw = WrapAngle(_yw + DeadZone(input.AxisYw) * TurnRatePerSecond * dt);
            _zw = WrapAngle(_zw + DeadZone(input.AxisZw) * TurnRatePerSecond * dt);
        }

        /// <summary>The offset handed to the slicer: the cut itself, or its nearest step.</summary>
        public float CutOffset(SlicerSource source)
        {
            if (source != SlicerSource.Stepped) return _w;
            float step = 2f * SlicerObjects.CutRange / SteppedSteps;
            return ClampCut(math.round(_w / step) * step);
        }

        /// <summary>
        /// The body's rotation and translation for <see cref="Slicer4D.Slice"/>: first its rest pose, then the
        /// walker's turns in XW, YW and ZW, in that order. The body turns about its own centre.
        /// </summary>
        public void GetTransform(SlicerObject body, out float4x4 modelToSlice, out float4 translation)
        {
            GetTransform(body, _xw, _yw, _zw, out modelToSlice, out translation);
        }

        /// <summary>As above, for given turns in radians.</summary>
        public static void GetTransform(SlicerObject body, float xw, float yw, float zw,
                                        out float4x4 modelToSlice, out float4 translation)
        {
            double4x4 m = math.mul(Turns(xw, yw, zw), SlicerObjects.BaseRotation(body));
            modelToSlice = new float4x4(m);
            translation = math.mul(modelToSlice, SlicerObjects.Centring(body));
        }

        /// <summary>The three turns alone: XW first, then YW, then ZW. Planes that do not contain w are never
        /// turned here: they would only spin the slice like any solid.</summary>
        public static double4x4 Turns(double xw, double yw, double zw)
        {
            return Rotation4D.FromPlaneAngles(0.0, 0.0, xw, 0.0, yw, zw);
        }

        /// <summary>
        /// Unit normal of the cut in the slice frame. Straight along w for every polytope. For the tube it is
        /// tilted towards z by the scanner angle of Master Plan section 5.4: (0, 0, -sin, cos).
        /// </summary>
        public static float4 CutNormal(SlicerObject body, float tiltRadians)
        {
            if (SlicerObjects.Clamp(body) != SlicerObject.Tube || tiltRadians == 0f) return new float4(0f, 0f, 0f, 1f);
            math.sincos(tiltRadians, out float s, out float c);
            return new float4(0f, 0f, -s, c);
        }

        public static float ClampCut(float w)
        {
            if (float.IsNaN(w)) return 0f;
            return math.clamp(w, -SlicerObjects.CutRange, SlicerObjects.CutRange);
        }

        /// <summary>Brings an angle into -pi .. pi.</summary>
        public static float WrapAngle(float radians)
        {
            if (float.IsNaN(radians) || float.IsInfinity(radians)) return 0f;
            if (radians > math.PI || radians < -math.PI)
            {
                const float Turn = 2f * math.PI;
                radians -= Turn * math.floor((radians + math.PI) / Turn);
            }
            return math.clamp(radians, -math.PI, math.PI);
        }

        /// <summary>Zero inside the dead zone, then linear up to +-1.</summary>
        public static float DeadZone(float axis)
        {
            float magnitude = math.abs(axis);
            if (!(magnitude > StickDeadZone)) return 0f;
            float scaled = math.saturate((magnitude - StickDeadZone) / (1f - StickDeadZone));
            return axis < 0f ? -scaled : scaled;
        }
    }
}

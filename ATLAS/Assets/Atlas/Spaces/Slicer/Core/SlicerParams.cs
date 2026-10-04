using System;
using Atlas.Link;
using Unity.Mathematics;

namespace Atlas.Spaces.Slicer
{
    /// <summary>
    /// The parameters of group "slicer" (lane A contract, section 14.3; README F6) and the two-way traffic
    /// between them and the <see cref="SlicerController"/>. The cut and the three turns are live values: the
    /// space writes them every frame, and a value that differs from the last one written was set by somebody
    /// else (the Station, the menu) and is taken over.
    /// </summary>
    public sealed class SlicerParams
    {
        public const string Group = "slicer";
        public const string Source = "slicer.source", Object = "slicer.object", View = "slicer.view";
        public const string W = "slicer.w", Xw = "slicer.xw", Yw = "slicer.yw", Zw = "slicer.zw";
        public const string Tilt = "slicer.tilt";

        public const float MaxAngleDegrees = 180f, MaxTiltDegrees = 80f;

        static readonly string[] s_sources = { "live", "stepped", "tesseract" };
        static readonly string[] s_views = { "slice", "shadow" };

        readonly ParamEntry _source, _object, _view, _w, _xw, _yw, _zw, _tilt;
        float _wroteW, _wroteXw, _wroteYw, _wroteZw;

        SlicerParams(ParamRegistry registry)
        {
            _source = registry.DeclareEnum(Source, Group, s_sources, 0, null, true);
            _object = registry.DeclareEnum(Object, Group, SlicerObjects.Names(), 0, null, true);
            _view = registry.DeclareEnum(View, Group, s_views, 0);
            _w = registry.DeclareFloat(W, Group, -SlicerObjects.CutRange, SlicerObjects.CutRange, 0f);
            _xw = registry.DeclareFloat(Xw, Group, -MaxAngleDegrees, MaxAngleDegrees, 0f);
            _yw = registry.DeclareFloat(Yw, Group, -MaxAngleDegrees, MaxAngleDegrees, 0f);
            _zw = registry.DeclareFloat(Zw, Group, -MaxAngleDegrees, MaxAngleDegrees, 0f);
            _tilt = registry.DeclareFloat(Tilt, Group, 0f, MaxTiltDegrees, 0f);
            Remember();
        }

        /// <summary>Declares the group, or finds it again with its current values (declaring is idempotent).</summary>
        public static SlicerParams Declare(ParamRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            return new SlicerParams(registry);
        }

        public SlicerSource SourceValue { get { return (SlicerSource)_source.Int; } }

        /// <summary>The body in force: the tesseract when the source says so, else <c>slicer.object</c>.</summary>
        public SlicerObject ObjectValue
        {
            get
            {
                return SourceValue == SlicerSource.TesseractOnly
                    ? SlicerObject.Tesseract
                    : SlicerObjects.Clamp((SlicerObject)_object.Int);
            }
        }

        public SlicerView ViewValue { get { return _view.Int != 0 ? SlicerView.Shadow : SlicerView.Slice; } }
        public float TiltRadians { get { return math.radians(_tilt.Float); } }

        public void SetObject(SlicerObject body) { _object.Set((int)SlicerObjects.Clamp(body)); }
        public void SetView(SlicerView view) { _view.Set((int)view); }

        /// <summary>Starts the controller from the stored values.</summary>
        public void Load(SlicerController controller)
        {
            controller.Reset(_w.Float, math.radians(_xw.Float), math.radians(_yw.Float), math.radians(_zw.Float));
            Remember();
        }

        /// <summary>Hands the controller every value that somebody else has set since the last
        /// <see cref="Store"/>. Call before the controller's step. No allocation.</summary>
        public void TakeOutsideChanges(SlicerController controller)
        {
            if (_w.Float != _wroteW) controller.SetCut(_w.Float);
            if (_xw.Float != _wroteXw) controller.SetAngle(0, math.radians(_xw.Float));
            if (_yw.Float != _wroteYw) controller.SetAngle(1, math.radians(_yw.Float));
            if (_zw.Float != _wroteZw) controller.SetAngle(2, math.radians(_zw.Float));
            Remember();
        }

        /// <summary>Writes the controller's state back, so the Station and the menu show it live. Call after
        /// the controller's step. No allocation.</summary>
        public void Store(SlicerController controller)
        {
            _w.Set(controller.W);
            _xw.Set(math.degrees(controller.Xw));
            _yw.Set(math.degrees(controller.Yw));
            _zw.Set(math.degrees(controller.Zw));
            Remember();
        }

        void Remember()
        {
            _wroteW = _w.Float;
            _wroteXw = _xw.Float;
            _wroteYw = _yw.Float;
            _wroteZw = _zw.Float;
        }
    }

    /// <summary>The Slicer's five values in <c>State.p[3..7]</c> (lane A contract, section 14.3).</summary>
    public static class SlicerState
    {
        /// <summary>p[7]: object + 16 * view.</summary>
        public static float Pack(SlicerObject body, SlicerView view)
        {
            return (int)SlicerObjects.Clamp(body) + 16 * (int)view;
        }

        public static void Unpack(float packed, out SlicerObject body, out SlicerView view)
        {
            int value = float.IsNaN(packed) ? 0 : (int)math.round(math.clamp(packed, 0f, 255f));
            view = (value & 16) != 0 ? SlicerView.Shadow : SlicerView.Slice;
            body = SlicerObjects.Clamp((SlicerObject)(value & 15));
        }
    }
}

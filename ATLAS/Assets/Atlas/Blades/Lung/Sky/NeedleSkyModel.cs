using System;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>
    /// Needle-sky logic: picks, cursor, dwell, read-outs (MP §10.2.2 beat g; §6.3 step 5). Pure: no GameObjects;
    /// no allocation in Step except on a pick (an event string). Brief G7 §5.2 gives the exact order.
    /// </summary>
    public sealed class NeedleSkyModel
    {
        const double Deg = Math.PI / 180.0;
        readonly SkyMaps _maps;
        readonly ISkyEvents _events;
        readonly double3[] _picks = new double3[NeedleSkyConstants.PicksPerCheck];
        readonly float _sharePct;
        int _count;
        double3 _cursor = new double3(0, 0, 1);
        double _cursorAz, _cursorEl;
        bool _attached, _armed, _hasLast;
        double3 _anchor, _lastDwellPick;
        double _dwellT, _sinceThird;
        NeedleSkyReadout _readout;

        public NeedleSkyModel(SkyMaps maps, ISkyEvents events)
        {
            _maps = maps ?? throw new ArgumentNullException(nameof(maps));
            _events = events;
            _sharePct = (float)Math.Round(maps.RobustSharePercent, 1, MidpointRounding.AwayFromZero);
            Reset();
        }

        /// <summary>Idempotent; Enter may be called from any state (ILungBeat).</summary>
        public void Reset()
        {
            _count = 0; _attached = true; _armed = false; _hasLast = false; _dwellT = 0; _sinceThird = 0;
            _cursor = new double3(0, 0, 1); _cursorAz = 0; _cursorEl = 0;
            RefreshReadout();
        }

        public double3 Cursor => _cursor;
        public bool CursorAttached => _attached;
        public int PickCount => _count;
        public double3 Pick(int index) => _picks[index];
        public NeedleSkyReadout Readout => _readout;
        /// <summary>D9: third pick done and 5 s elapsed.</summary>
        public bool Completed => _count == NeedleSkyConstants.PicksPerCheck && _sinceThird >= NeedleSkyConstants.CompleteAfterCheckSeconds - NeedleSkyConstants.TimeEps;

        public void Step(in SkyInput input)
        {
            double dt = input.DeltaTime;
            if (_count == NeedleSkyConstants.PicksPerCheck) _sinceThird += dt;                      // 1

            float sx = input.Stick.x, sy = input.Stick.y;                                           // 2 stick
            if (math.sqrt(sx * sx + sy * sy) > NeedleSkyConstants.StickDeadzone)
            {
                _attached = false; _armed = false;
                _cursorAz += sx * NeedleSkyConstants.StickRateDegPerS * dt;
                _cursorEl = Math.Max(-NeedleSkyConstants.ElClampDeg, Math.Min(NeedleSkyConstants.ElClampDeg,
                    _cursorEl + sy * NeedleSkyConstants.StickRateDegPerS * dt));
                _cursorAz -= 360.0 * Math.Floor((_cursorAz + 180.0) / 360.0);
                _cursor = SkyDirection.FromAzEl(_cursorAz, _cursorEl);
            }
            if (_attached)
            {
                _cursor = input.Gaze;
                SkyDirection.ToAzEl(_cursor, out _cursorAz, out _cursorEl);
            }

            if (input.ConsolePick) TryPick(SkyDirection.FromAzEl(input.ConsoleAzDeg, input.ConsoleElDeg));   // 3

            if (input.Confirm)                                                                       // 4
            {
                TryPick(_cursor);
                _attached = true; _armed = false; _lastDwellPick = _cursor; _hasLast = true;
            }

            if (_attached && _count < NeedleSkyConstants.PicksPerCheck)                              // 5 dwell
            {
                double cone = NeedleSkyConstants.DwellConeDeg * Deg;
                if (!_armed)
                {
                    if (!_hasLast || SkyDirection.AngleRad(input.Gaze, _lastDwellPick) > cone)
                    { _armed = true; _anchor = input.Gaze; _dwellT = 0; }
                }
                else
                {
                    if (SkyDirection.AngleRad(input.Gaze, _anchor) <= cone) _dwellT += dt;
                    else { _anchor = input.Gaze; _dwellT = 0; }
                    if (_dwellT >= NeedleSkyConstants.DwellSeconds - NeedleSkyConstants.TimeEps)
                    {
                        TryPick(input.Gaze);
                        _armed = false; _lastDwellPick = input.Gaze; _hasLast = true;
                    }
                }
            }
            RefreshReadout();                                                                        // 6
        }

        bool TryPick(double3 d)
        {
            if (_count == NeedleSkyConstants.PicksPerCheck) return false;
            double sep = NeedleSkyConstants.MinPickSeparationDeg * Deg;
            for (int k = 0; k < _count; k++)
                if (SkyDirection.AngleRad(d, _picks[k]) < sep || SkyDirection.AngleRad(d, -_picks[k]) < sep) return false;
            _picks[_count++] = d;
            SkyDirection.ToAzEl(d, out double az, out double el);
            SkyDirection.Pixel(az, el, out int i, out int j);
            _events?.Emit("sky_pick", JsonUtility.ToJson(new SkyPickData
            {
                az_deg = (float)az, el_deg = (float)el, clear_mm = Shown(_maps.MinClearanceMm(i, j)), robust = _maps.IsRobust(i, j),   // D15
            }));
            return true;
        }

        static float Shown(float mm) => (float)Math.Round(mm, 1, MidpointRounding.AwayFromZero);

        void RefreshReadout()
        {
            SkyDirection.ToAzEl(_cursor, out double az, out double el);
            SkyDirection.Pixel(az, el, out int i, out int j);
            _readout = new NeedleSkyReadout(Shown(_maps.MinClearanceMm(i, j)), _maps.IsRobust(i, j), _sharePct, _count, float.NaN);
        }
    }
}

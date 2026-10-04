using System;
using Atlas.Rendering;
using Atlas.Telemetry;
using UnityEngine;

namespace Atlas.Blades.FieldFit.BlindSpotFinder
{
    /// <summary>
    /// The ONLY file naming lane A/C/D types. Binds adapters to the placeholder members listed in docs/laneB/placeholders.md.
    /// A gap is reported through <c>missing</c>; this adapter fills none (Brief §2).
    /// </summary>
    public static class BlindSpotPlaceholderSeams
    {
        public static bool TryResolve(out IBlindSpotRig rig, out IBlindSpotLook look, out IBlindSpotTap tap,
            out IBlindSpotEvents events, out string missing)
        {
            string m = "";
            rig = null; look = null; tap = null;
            var sr = StereoRig.Current;
            if (sr != null && sr.LeftEye != null && sr.RightEye != null) rig = new CameraRig(sr);
            else m += "StereoRig.Current with LeftEye/RightEye (lane A P1-A04); ";

            // The placeholders expose no shared AtlasSurface material and no named bright/dark palette swatch
            // (AtlasPalette.CellUV(index) exists, but which cells are lightest/darkest is lane A's, P1-A06).
            m += "IBlindSpotLook: shared AtlasSurface Material and bright/dark palette swatch UVs (lane A P1-A06); ";
            // IActionSource has no live-instance accessor and its gamepad implementation is internal; GazeTarget is dwell-only (D7).
            m += "IBlindSpotTap: live Confirm action source (lane C P1-C10); ";

            events = new EventLogSink();
            missing = m.TrimEnd(' ', ';');
            return false;   // look and tap are blocked by placeholder until lanes A and C land
        }

        sealed class EventLogSink : IBlindSpotEvents
        {
            // Placeholder EventLog.Emit takes (scene, ev, dataJson); the sink fills t_us (PH-31).
            public void Emit(string ev, object data) => EventLog.Emit("fieldfit", ev, data == null ? "{}" : JsonUtility.ToJson(data));
        }

        // Blanking fallback (allowed for blanking only): the placeholder rig exposes the eye Cameras only.
        sealed class CameraRig : IBlindSpotRig
        {
            readonly StereoRig _rig;
            readonly bool[] _blank = new bool[2];
            readonly int[] _mask = new int[2];
            readonly CameraClearFlags[] _flags = new CameraClearFlags[2];
            readonly Color[] _bg = new Color[2];
            public CameraRig(StereoRig rig) { _rig = rig; }
            Camera Cam(EyeSide e) => e == EyeSide.Left ? _rig.LeftEye : _rig.RightEye;
            public Transform EyeTransform(EyeSide eye) => Cam(eye).transform;

            public void SetEyeBlank(EyeSide eye, bool blank)
            {
                int i = (int)eye; var c = Cam(eye);
                if (c == null || _blank[i] == blank) return;
                if (blank)
                {
                    _mask[i] = c.cullingMask; _flags[i] = c.clearFlags; _bg[i] = c.backgroundColor;
                    c.cullingMask = 0; c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = Color.black;
                }
                else { c.cullingMask = _mask[i]; c.clearFlags = _flags[i]; c.backgroundColor = _bg[i]; }
                _blank[i] = blank;
            }
        }
    }
}

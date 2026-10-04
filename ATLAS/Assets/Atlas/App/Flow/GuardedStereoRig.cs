using System;
using Atlas.Rendering;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.App
{
    /// <summary>
    /// The rules of the headset refusal (lane A contract, section 7.2, rule 2): a device build with no Bump
    /// Guard shows "Bump Guard missing" and refuses the headset view.
    /// </summary>
    public static class HeadsetGate
    {
        public const string Message = "Bump Guard missing";

        /// <summary>True on a phone whose Bump Guard could not be created.</summary>
        public static bool MustWarn(bool isDevice, bool guardInstalled)
        {
            return isDevice && !guardInstalled;
        }

        /// <summary>
        /// Whether the overlay component draws: when dev.overlay asks for it, except over the lens view on a
        /// phone; and always while the warning is owed, so a release build shows it too.
        /// </summary>
        public static bool OverlayDrawn(bool wanted, bool isDevice, bool headset, bool guardInstalled)
        {
            if (MustWarn(isDevice, guardInstalled)) return true;
            return wanted && !(isDevice && headset);
        }
    }

    /// <summary>
    /// The stereo rig as the rest of the app sees it (AtlasServices publishes this, not the rig itself). It
    /// passes everything through, except that SetMode(Headset) is refused while <see cref="HeadsetLocked"/>
    /// is set: the one place the refusal is enforced, so the rehearsal flow, onboarding and the menu cannot
    /// differ (lane A contract, sections 7.2 and 8.2).
    /// </summary>
    public sealed class GuardedStereoRig : IStereoRig
    {
        readonly IStereoRig _rig;
        bool _locked;

        public GuardedStereoRig(IStereoRig rig)
        {
            if (rig == null) throw new ArgumentNullException(nameof(rig));
            _rig = rig;
        }

        /// <summary>Called each time the headset view is refused, with the line to show.</summary>
        public Action<string> Refused { get; set; }

        /// <summary>Setting it also brings a rig that is already in the headset view back to the hand.</summary>
        public bool HeadsetLocked
        {
            get { return _locked; }
            set
            {
                _locked = value;
                if (value && _rig.Mode == DisplayMode.Headset) _rig.SetMode(DisplayMode.Hand);
            }
        }

        public DisplayMode Mode { get { return _rig.Mode; } }

        public void SetMode(DisplayMode mode)
        {
            if (_locked && mode == DisplayMode.Headset)
            {
                Action<string> refused = Refused;
                if (refused != null) refused(HeadsetGate.Message);
                return;
            }
            _rig.SetMode(mode);
        }

        public float Ipd
        {
            get { return _rig.Ipd; }
            set { _rig.Ipd = value; }
        }

        public float3 HeadOffset
        {
            get { return _rig.HeadOffset; }
            set { _rig.HeadOffset = value; }
        }

        public quaternion PredictionOffset
        {
            get { return _rig.PredictionOffset; }
            set { _rig.PredictionOffset = value; }
        }

        public Transform Head { get { return _rig.Head; } }
        public Transform Hud { get { return _rig.Hud; } }
        public Camera GetEyeCamera(Eye eye) { return _rig.GetEyeCamera(eye); }
        public float3 GetEyePosition(Eye eye) { return _rig.GetEyePosition(eye); }
        public void GetEyeFrustumPlanes(Eye eye, Plane[] planes) { _rig.GetEyeFrustumPlanes(eye, planes); }
    }
}

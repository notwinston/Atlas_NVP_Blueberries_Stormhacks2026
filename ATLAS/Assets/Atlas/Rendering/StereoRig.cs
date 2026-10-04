// PLACEHOLDER (lane A, P1-A04): contract surface only; replaced by lane A's implementation
using UnityEngine;

namespace Atlas.Rendering
{
    /// <summary>The two eye cameras (Master Plan §8.2): +-32 mm under the head (deviation PH-10).</summary>
    [ExecuteAlways]
    public sealed class StereoRig : MonoBehaviour
    {
        public static StereoRig Current { get; private set; }   // set in OnEnable, cleared in OnDisable

        public const int EyeWidth = 1118, EyeHeight = 1032;     // Master Plan §8.2, at render scale 0.8

        public Camera LeftEye, RightEye;
        public Transform Head;

        bool _overridden;

        void OnEnable()
        {
            Current = this;
            if (Head == null) Head = transform;
            if (LeftEye == null) LeftEye = MakeEye("LeftEye", -0.032f);    // Master Plan §8.2: +-32 mm
            if (RightEye == null) RightEye = MakeEye("RightEye", 0.032f);
        }

        void OnDisable() { if (Current == this) Current = null; }

        Camera MakeEye(string name, float x)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(Head, false);
            go.transform.localPosition = new Vector3(x, 0f, 0f);
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 96f;                                  // approximates §8.5 "about 50 deg each side, 48 deg up and down"
            cam.aspect = (float)EyeWidth / EyeHeight;
            return cam;
        }

        /// <summary>P3-B03 wide source: the thermal ladder must not swap targets while an override is set.</summary>
        public void OverrideEyeTargets(RenderTexture left, RenderTexture right, float verticalFovDeg)
        {
            _overridden = true;
            LeftEye.targetTexture = left; RightEye.targetTexture = right;
            LeftEye.fieldOfView = RightEye.fieldOfView = verticalFovDeg;
        }

        public void ClearEyeTargetOverride()
        {
            if (!_overridden) return;
            _overridden = false;
            LeftEye.targetTexture = null; RightEye.targetTexture = null;
            LeftEye.fieldOfView = RightEye.fieldOfView = 96f;
        }
    }
}

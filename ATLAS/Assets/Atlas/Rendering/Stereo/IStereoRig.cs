using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Rendering
{
    /// <summary>How the phone is being used, and so what draws to the screen.</summary>
    public enum DisplayMode
    {
        /// <summary>Phone in the hand: the AR camera draws full screen, mono, with the camera image (scan, onboarding).</summary>
        Hand = 0,
        /// <summary>Phone in the headset: two eye cameras, eye textures, one lens blit. No camera image.</summary>
        Headset = 1,
    }

    /// <summary>One of the walker's eyes.</summary>
    public enum Eye
    {
        Left = 0,
        Right = 1,
    }

    /// <summary>The two-camera rig under the tracked head (Master Plan D7, task P1-A04).</summary>
    public interface IStereoRig
    {
        DisplayMode Mode { get; }
        /// <summary>Switches between hand and headset display.</summary>
        void SetMode(DisplayMode mode);

        /// <summary>Distance between the eye cameras. Metres. Clamped to 0.055 .. 0.075. Default 0.064.</summary>
        float Ipd { get; set; }
        /// <summary>Position of the eye midpoint in the tracked camera's local frame. Metres. Default (0, 0, 0).</summary>
        float3 HeadOffset { get; set; }
        /// <summary>Extra rotation of the eyes about the head, in head space: the gyro prediction. Identity = off.</summary>
        quaternion PredictionOffset { get; set; }

        /// <summary>"Atlas Head": the eye midpoint, unpredicted. World pose = the raw head pose of the frame.</summary>
        Transform Head { get; }
        /// <summary>Head-locked parent for the warning cue, the HUD and the gaze reticle. Layer AtlasHud.</summary>
        Transform Hud { get; }
        Camera GetEyeCamera(Eye eye);

        /// <summary>World position of an eye for the current frame. Metres.</summary>
        float3 GetEyePosition(Eye eye);
        /// <summary>The six frustum planes of an eye in world space, normals inwards, in Unity's order
        /// (left, right, down, up, near, far). planes must have length 6.</summary>
        void GetEyeFrustumPlanes(Eye eye, Plane[] planes);
    }
}

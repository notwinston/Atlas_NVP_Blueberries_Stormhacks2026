using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>
    /// What stands between the native detector's record and the rest of the hands code
    /// (documents/ATLAS_Hands_Design.md Appendix C): the camera pose turned from ARKit's convention into
    /// Unity's, the check that a record is one, and how the plugin's answers become a fault. It lives here,
    /// not beside the plugin's bindings in Atlas.Hands.ARKit, because that assembly exists on the phone and
    /// in the Editor only and no headless test can reach it; this is plain code and is tested without a phone.
    /// </summary>
    internal static class HandsNativeMath
    {
        /// <summary>
        /// ARKit's camera transform as a pose in Unity's AR session frame. ARKit's world is right-handed and
        /// its camera looks along its own -z; Unity's session frame is ARKit's with z negated (as
        /// Atlas.World.WallBridgeJson turns RoomPlan's walls) and Unity's camera looks along +z. A camera
        /// pose therefore needs the flip on both sides, F * M * F with F = diag(1, 1, -1, 1): negating z on
        /// one side only would put the hands behind the head or mirror them. For the 16 floats the plugin
        /// stores column by column that is five sign changes: elements 2 and 6 (the z of the right and up
        /// axes), 8 and 9 (the x and y of the third column, which becomes the forward axis) and 14 (the z of
        /// the position). The bottom row of a pose is 0 0 0 1 and is left alone.
        /// <para>The result maps a point of Unity's camera space (+X right, +Y up, +Z forward, the space of
        /// <see cref="HandsDetection.Unproject"/>) to the session frame: position (m12, m13, -m14), forward
        /// (-m8, -m9, m10), up (m4, m5, -m6). It holds for an app fixed to Unity's LandscapeLeft, the
        /// orientation ARKit's camera space is defined in. Applying it twice gives the matrix back.</para>
        /// </summary>
        internal static float4x4 ArKitToUnity(in float4x4 cameraToSession)
        {
            float4 right = cameraToSession.c0, up = cameraToSession.c1, back = cameraToSession.c2, position = cameraToSession.c3;
            return new float4x4(
                new float4(right.x, right.y, -right.z, right.w),
                new float4(up.x, up.y, -up.z, up.w),
                new float4(-back.x, -back.y, back.z, back.w),
                new float4(position.x, position.y, -position.z, position.w));
        }

        /// <summary>
        /// True when the bytes the plugin handed over are a record of this version whose counts stay inside
        /// its arrays. The counts index fixed arrays on the reader's side, so a record from a plugin of
        /// another version (one left in an appended Xcode project, say) is refused whole, not read.
        /// </summary>
        internal static bool IsRecord(in HandsDetection detection)
        {
            return detection.Version == HandsDetection.CurrentVersion
                && (uint)detection.HandCount <= HandsDetection.MaxHands
                && (uint)detection.Hand0.PalmCount <= DetectedHand.MaxPalmSamples
                && (uint)detection.Hand1.PalmCount <= DetectedHand.MaxPalmSamples;
        }

        /// <summary>
        /// The source's fault after one frame, from what the plugin's tick and poll answered: 0 while both
        /// are healthy (0 or 1), otherwise the negative code, the poll's first because it knows more (a tick
        /// cannot see a failed request). The plugin answers an error only while its cause holds, so the
        /// fault clears by itself.
        /// </summary>
        internal static int Fault(int ticked, int polled)
        {
            return polled < 0 ? polled : (ticked < 0 ? ticked : 0);
        }
    }
}

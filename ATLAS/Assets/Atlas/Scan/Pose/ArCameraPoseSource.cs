using Atlas.World;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ArTrackingState = UnityEngine.XR.ARSubsystems.TrackingState;
using TrackingState = Atlas.World.TrackingState;

namespace Atlas.Scan
{
    /// <summary>
    /// The head as AR Foundation tracks it: on the phone and in XR Simulation. The TrackedPoseDriver has
    /// already written the tracked camera this frame (Update only, decision D8); this reads the pose of
    /// "Atlas Head" in the AR session frame and the session's live tracking state.
    /// </summary>
    public sealed class ArCameraPoseSource : IPoseSource
    {
        readonly Transform _head;
        readonly Transform _sessionSpace;
        readonly ARSession _session;

        /// <summary>
        /// head is "Atlas Head" (or the tracked camera while there is no stereo rig). sessionSpace is the
        /// transform whose local frame is the AR session frame: the tracked camera's parent. When it is null
        /// the XR Origin is used, which is the same frame while "Camera Offset" is unmoved.
        /// </summary>
        public ArCameraPoseSource(Transform head, Transform xrOrigin, ARSession session, Transform sessionSpace = null)
        {
            _head = head;
            _sessionSpace = sessionSpace != null ? sessionSpace : xrOrigin;
            _session = session;
        }

        public string Name { get { return "arcamera"; } }
        public PoseFrameKind Frame { get { return PoseFrameKind.Session; } }
        public bool DrivesRig { get { return true; } }

        /// <summary>The live tracking state of the session; NotTracking with no session subsystem.</summary>
        public TrackingState Tracking
        {
            get
            {
                XRSessionSubsystem subsystem = _session != null ? _session.subsystem : null;
                // ARSession.state has no value for "lost" and its not-tracking reason is stale: read the subsystem.
                return subsystem != null && subsystem.running ? Map(subsystem.trackingState) : TrackingState.NotTracking;
            }
        }

        public bool TryRead(double time, float deltaTime, out HeadPose pose)
        {
            pose = default(HeadPose);
            if (_head == null || _session == null) return false;
            XRSessionSubsystem subsystem = _session.subsystem;
            if (subsystem == null || !subsystem.running) return false;      // the session has not started

            _head.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
            if (_sessionSpace != null)
            {
                position = _sessionSpace.InverseTransformPoint(position);
                rotation = Quaternion.Inverse(_sessionSpace.rotation) * rotation;
            }
            pose = new HeadPose(time, position, rotation, Map(subsystem.trackingState));
            return true;
        }

        /// <summary>AR Foundation's tracking state as ATLAS names it.</summary>
        public static TrackingState Map(ArTrackingState state)
        {
            switch (state)
            {
                case ArTrackingState.Tracking: return TrackingState.Normal;
                case ArTrackingState.Limited: return TrackingState.Limited;
                default: return TrackingState.NotTracking;
            }
        }
    }
}

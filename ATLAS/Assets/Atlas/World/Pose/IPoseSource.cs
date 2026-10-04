namespace Atlas.World
{
    /// <summary>The frame a pose source delivers its poses in.</summary>
    public enum PoseFrameKind : byte { Session = 0, Room = 1 }

    /// <summary>Where head poses come from (contract README F9). One sample per frame.</summary>
    public interface IPoseSource
    {
        /// <summary>"arcamera", "poselog" or "walker".</summary>
        string Name { get; }
        /// <summary>Session: poses are in the AR session frame and the pipeline maps them through the anchor.
        /// Room: poses are already in the room frame.</summary>
        PoseFrameKind Frame { get; }
        /// <summary>True when the tracked camera's transform is driven by AR Foundation and must not be written.</summary>
        bool DrivesRig { get; }
        /// <summary>The sample for this frame. False when there is none (the log has ended, the session has not started).</summary>
        bool TryRead(double time, float deltaTime, out HeadPose pose);
    }
}

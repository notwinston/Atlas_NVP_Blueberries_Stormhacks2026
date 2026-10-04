namespace Atlas.World
{
    /// <summary>What Bump Guard receives each frame. Built by the pose pipeline in step 1.</summary>
    public struct SensorFrame
    {
        /// <summary>Seconds, app clock.</summary>
        public double Time;
        /// <summary>Seconds since the previous frame; 0 on the first frame.</summary>
        public float DeltaTime;
        /// <summary>The RAW pose: never filtered, never predicted.</summary>
        public HeadPose Head;
        /// <summary>A RoomModel is active and Head is in the room frame.</summary>
        public bool RoomActive;
        /// <summary>The room anchor is tracked this frame. True when the pose source has no anchor (replay, walker).</summary>
        public bool AnchorTracked;
        /// <summary>Depth holds a new image this frame.</summary>
        public bool HasDepth;
        public DepthFrame Depth;
    }
}

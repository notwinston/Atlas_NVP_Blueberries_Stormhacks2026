namespace Atlas.World
{
    /// <summary>Where LiDAR depth images come from (contract README F9).</summary>
    public interface IDepthSource
    {
        /// <summary>False where there is no LiDAR depth: the Editor, XR Simulation, a pose-log replay.</summary>
        bool Available { get; }
        /// <summary>The newest depth image, if one arrived since the last call, with the tracked camera's world
        /// pose of this frame. Called by the pose pipeline in step 1, after the XR Origin has been moved.
        /// Reuses one buffer; allocates nothing after the first call.</summary>
        bool TryRead(double time, out DepthFrame frame);
    }

    /// <summary>No depth: Available is false and TryRead returns false.</summary>
    public sealed class NullDepthSource : IDepthSource
    {
        public bool Available { get { return false; } }

        public bool TryRead(double time, out DepthFrame frame)
        {
            frame = default(DepthFrame);
            return false;
        }
    }
}

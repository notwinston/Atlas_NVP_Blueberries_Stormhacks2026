using Atlas.Link;

namespace Atlas.World
{
    /// <summary>Step 1. Reads sensors once per frame and fills the raw fields of the frame.</summary>
    public interface ISenseStep { void Sense(ref FrameContext frame); }

    /// <summary>Step 2. Bump Guard. Runs before anything can be skipped; never waits.</summary>
    public interface ISafetyStep { void Guard(in FrameContext frame); }

    /// <summary>Step 3. Fills HeadPosition, HeadRotation and PoseJumpRejected.</summary>
    public interface IPoseFilterStep { void Filter(ref FrameContext frame); }

    /// <summary>Step 6. Writes per-frame data to materials, meshes and shader globals.</summary>
    public interface IUploadStep { void Upload(in FrameContext frame); }

    /// <summary>Step 8a. Takes the frame's State for sending. Never blocks.</summary>
    public interface IStateSink { void Send(in StateMessage state); }

    /// <summary>Step 8b. Runs after the frame has been rendered.</summary>
    public interface IAfterFrameStep { void AfterFrame(in FrameContext frame); }

    /// <summary>The frame loop as installers see it (contract README F5).</summary>
    public interface IFrameLoop
    {
        /// <summary>The context of the frame in progress. Valid from step 1.</summary>
        ref readonly FrameContext Current { get; }
        /// <summary>Step 1 hooks run in the order they were added.</summary>
        void AddSense(ISenseStep step);
        /// <summary>Exactly one. A second call replaces the first and logs a warning.</summary>
        void SetGuard(ISafetyStep step);
        void SetPoseFilter(IPoseFilterStep step);
        /// <summary>Step 6 hooks run in the order they were added, after the entered module's own Upload.</summary>
        void AddUpload(IUploadStep step);
        void SetStateSink(IStateSink sink);
        /// <summary>Step 8b hooks run in the order they were added.</summary>
        void AddAfterFrame(IAfterFrameStep step);
        /// <summary>kernel.ResetToFloor(geometry head floor point); the next FloorDelta is zero.</summary>
        void ResetWalkerToFloor();
        /// <summary>kernel.Reset(pose); the next FloorDelta is zero.</summary>
        void ResetWalker(in Atlas.Kernel.WalkerPose pose);
    }

    /// <summary>The six operator commands as local calls (Master Plan section 7.3.2). No link type appears here.</summary>
    public interface IOperatorCommands
    {
        /// <summary>SceneCmd. variant may be null; its meaning is the space's own.</summary>
        void SwitchSpace(SpaceId space, string variant);
        void Home();
        void Recentre();
        /// <summary>-1 automatic, 0 to 3 the ladder rungs, 4 the high tier.</summary>
        void SetQuality(int tier);
        /// <summary>The Console's STOP button: the warning cue at STRONG.</summary>
        void Cue();
        void SetLock(bool on);
    }
}

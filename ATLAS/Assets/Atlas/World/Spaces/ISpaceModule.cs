using Atlas.Kernel;
using Atlas.Link;

namespace Atlas.World
{
    /// <summary>
    /// One space of the catalogue: configured from the room, entered, ticked and left by SpaceLab
    /// (Master Plan section 6.5). SpaceLab is its only caller.
    /// </summary>
    public interface ISpaceModule
    {
        SpaceId Id { get; }
        /// <summary>Does this space match the real room? Constant from the end of Configure until Exit.</summary>
        bool Registered { get; }
        /// <summary>Every size comes from the RoomModel. Declares the space's parameters.</summary>
        void Configure(RoomModel room, ParamRegistry registry);
        void Enter(in WalkerPose start);
        /// <summary>Step 5 of the frame, after Bump Guard and the pose filter.</summary>
        void Tick(in FrameContext frame);
        void Exit();
        /// <summary>What the Spectator needs to redraw this frame.</summary>
        void WriteState(ref StateMessage state);
    }

    /// <summary>Optional. Lets the Spectator drive the module from a received State (Master Plan section 7.3.1).</summary>
    public interface ISpaceStateReader
    {
        /// <summary>Called by SpaceLab in the Spectator build before Tick, with the newest State.</summary>
        void ReadState(in StateMessage state);
    }
}

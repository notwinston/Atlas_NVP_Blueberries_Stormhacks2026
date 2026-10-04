// PLACEHOLDER (lane A, P1-X05): contract surface only; replaced by lane A's implementation
using Atlas.Kernel;
using Atlas.Link;

namespace Atlas.World
{
    /// <summary>One implementation per space. Master Plan §6.5, verbatim.</summary>
    public interface ISpaceModule
    {
        SpaceId Id { get; }
        bool Registered { get; }   // true for S0, S1, S3, S5, S6; false for S2, S4 (§4.9)
        void Configure(RoomModel room, ParamRegistry registry);
        void Enter(in WalkerPose start);
        void Tick(in FrameContext frame);
        void Exit();
        void WriteState(ref StateMessage state);
    }
}

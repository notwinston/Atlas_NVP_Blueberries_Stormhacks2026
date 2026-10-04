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

    /// <summary>
    /// Optional. A space whose world comes from a seeded generator says what fixes that world, so that the link
    /// can name it: the seed and the generator's version go into the world hash (Master Plan section 7.3.1),
    /// and the generator's own hash of what it made is reported with it, to compare two runtimes (P1-B07).
    /// Constant from the end of Configure until Exit; after a change the space calls ISpaceHost.BumpWorldVersion.
    /// </summary>
    public interface ISpaceWorld
    {
        /// <summary>The generator's seed.</summary>
        long WorldSeed { get; }
        /// <summary>The generator's version: 1 in Phase 1.</summary>
        int GeneratorVersion { get; }
        /// <summary>The hash of the generated content as 16 lower-case hexadecimal digits (WorldGenerator.WorldHash),
        /// or empty when the space has none to report. Computed in Configure, never per frame.</summary>
        string ContentHash { get; }
    }

    /// <summary>Optional. Lets the Spectator drive the module from a received State (Master Plan section 7.3.1).</summary>
    public interface ISpaceStateReader
    {
        /// <summary>Called by the Spectator's host before Tick, with the newest State. The phone never calls it.</summary>
        void ReadState(in StateMessage state);
    }
}

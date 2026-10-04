// PLACEHOLDER (lane A, P1-X05): contract surface only; replaced by lane A's implementation
namespace Atlas.World
{
    /// <summary>The seven spaces. The value equals State.spaceId (u8 0-6, App. B.1) and the SceneCmd strings "S0".."S6" (App. B.2).</summary>
    // Master Plan §3.1, App. B.1, B.2
    public enum SpaceId : byte
    {
        S0 = 0, // Home
        S1 = 1, // Glued
        S2 = 2, // Curved Walk
        S3 = 3, // Aligned Cells
        S4 = 4, // Small Planet
        S5 = 5, // Impossible Rooms
        S6 = 6, // Slicer
    }
}

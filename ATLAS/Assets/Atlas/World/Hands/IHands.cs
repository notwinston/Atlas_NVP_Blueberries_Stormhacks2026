using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>
    /// The walker's tracked hands as every other assembly sees them (documents/ATLAS_Hands_Design.md section
    /// 3.6). Published through AtlasServices by Atlas.Hands; absent on the Station and wherever hands are not
    /// installed, so ask with AtlasServices.TryGet. Frozen in step 1 of the frame. Main thread only.
    /// </summary>
    public interface IHands
    {
        /// <summary>The detector is running (section 3.5: headset, no gamepad, enabled, a source, the AR head, the tier).</summary>
        bool Active { get; }
        ref readonly HandPose Pose(HandSide side);
        /// <summary>World, metres. Meaningful while Pose(side).Tracked.</summary>
        float3 Joint(HandSide side, HandJoint joint);
        ref readonly TwoHandPinch TwoHand { get; }
        /// <summary>Hides both hands and suspends touch until the next frame. For a space that cannot show them correctly this frame.</summary>
        void HideThisFrame();
    }

    /// <summary>
    /// Surfaces a fingertip can touch, known without reading the picture back (section 6.5). A space may publish
    /// one through AtlasServices for its own content; the room's walls, floor and furniture boxes are handled by
    /// Atlas.Hands itself.
    /// </summary>
    public interface IHandsSurface
    {
        /// <summary>
        /// The surface point closest to a world point and the unit normal there, pointing to the open side.
        /// False when no surface lies within maxDistance metres.
        /// </summary>
        bool TryClosest(float3 world, float maxDistance, out float3 point, out float3 normal);
    }

    /// <summary>
    /// Whether panels should sit within reach of the hands, and where (section 6.1). Published by Atlas.Hands. A
    /// view that finds none, or finds Near false, places itself as it always has.
    /// </summary>
    public interface IReachLayout
    {
        /// <summary>True in a hands session: panels come to the near anchor and are drawn depth-tested.</summary>
        bool Near { get; }
        /// <summary>World pose of the near anchor: the origin is the panel centre, +Z points away from the walker, +Y up along the panel.</summary>
        float3 Position { get; }
        quaternion Rotation { get; }
        /// <summary>Multiplies a panel's own layout, which was made for its usual distance.</summary>
        float Scale { get; }
    }
}

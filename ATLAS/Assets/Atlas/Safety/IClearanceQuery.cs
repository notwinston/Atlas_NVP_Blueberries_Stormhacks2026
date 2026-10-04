using Unity.Mathematics;

namespace Atlas.Safety
{
    /// <summary>
    /// clear(p) of Master Plan section 4.10 at a floor point: the smaller of the room's static distance field
    /// and the distance to the nearest live dynamic cell, metres. p is (x, z) in the room (anchor) frame.
    /// <see cref="BumpGuard"/> implements it over the live map; a space that places things on the floor (the
    /// Lung valve chambers) asks it before it puts one down, and a test passes a stand-in.
    /// </summary>
    public interface IClearanceQuery
    {
        /// <summary>
        /// Metres; positive infinity when no room is loaded, and zero for a point outside the room's grid
        /// or one that is not a number. Never allocates.
        /// </summary>
        float Clearance(float2 p);
    }
}

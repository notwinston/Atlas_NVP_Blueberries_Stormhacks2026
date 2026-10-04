// PLACEHOLDER (lane C, P1-C07): contract surface only; replaced by lane C's implementation
using System;
using UnityEngine;

namespace Atlas.World
{
    // The Home rebuild's output, crossing Atlas.World because Atlas.Spaces.* may not reference Atlas.Scan
    // (Master Plan §6.2). Placeholder choice PH-25. Lane B reads this interface only.
    public interface IHomeShell
    {
        Mesh Floor { get; }                   // m, anchor frame (relative to the room centre, Master Plan §4.6), 0.25 m grid
        Mesh Walls { get; }                   // walls with door openings, same frame
        int ObjectCount { get; }
        RoomObject ObjectRecord(int i);       // the RoomModel record (pose)
        Mesh ObjectMesh(int i);               // box-local: origin at the centre of the base, +Y up, m (Master Plan §6.4)
        event Action<int> ObjectMeshChanged;  // placeholder to generated / built-in swap (Master Plan §4.5)
    }
}

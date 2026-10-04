using System;
using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.Blades.Playground
{
    /// <summary>
    /// Lane C's port for "where is the start, by the kernel": the geodesic bearing of the walk's start flag from
    /// the walker, in the room's axes (the same frame as the head yaw). Lane B's home beacon (<c>P1-B06</c>,
    /// <c>P1-B10</c>) answers the same question for the beacon, so the real binding at integration may simply
    /// forward to it; <see cref="KernelHomeBearing"/> answers it from <see cref="IWalkerKernel"/> alone.
    /// </summary>
    public interface IHomeBearing
    {
        /// <summary>Remembers the place the walker starts from (the start flag). Call before the first leg.</summary>
        void MarkStart();

        /// <summary>
        /// Azimuth in degrees (see <see cref="ProbeGeometry"/>) and geodesic distance in metres from the walker to
        /// the start. False when the kernel cannot say, for example the start tile is no longer a candidate.
        /// </summary>
        bool TryGet(out double azimuthDeg, out double distanceMetres);
    }

    /// <summary>
    /// The geodesic bearing of the start from the walker's tile, pose and the kernel's candidate list alone. The
    /// kernel integrates the walker's real displacements in room axes, so the walker frame's axes are the room's
    /// axes carried along the path (the holonomy is in the pose's rotation): the bearing of the start in that
    /// frame is the direction in which the walker has to turn the head, as measured in the room.
    /// </summary>
    public sealed class KernelHomeBearing : IHomeBearing
    {
        readonly IWalkerKernel _kernel;
        WalkerPose _start;
        bool _marked;

        public KernelHomeBearing(IWalkerKernel kernel)
        {
            if (kernel == null) throw new ArgumentNullException("kernel");
            _kernel = kernel;
        }

        public void MarkStart()
        {
            _start = _kernel.Pose;
            _marked = _start.IsValid;
        }

        public bool TryGet(out double azimuthDeg, out double distanceMetres)
        {
            azimuthDeg = 0.0;
            distanceMetres = 0.0;
            if (!_marked) return false;
            WalkerPose now = _kernel.Pose;
            if (!now.IsValid) return false;

            var candidates = _kernel.Candidates;
            for (int i = 0; i < candidates.Count; i++)
            {
                VisibleTile t = candidates[i];
                if (t.Tile != _start.Tile) continue;
                // The start in the walker's tile frame (the candidates are expressed in it), then in the walker's own frame.
                double3 inTile = t.Chain.Apply(_start.Local.Position);
                double3 inWalker = now.Local.Inverse().Apply(inTile);
                double kappa = _kernel.Kappa;
                double2 v = KernelMaps.Log(inWalker, kappa);
                double units = math.length(v);
                distanceMetres = units * _kernel.MetresPerUnit;
                azimuthDeg = ProbeGeometry.AzimuthDeg(v.x, v.y);
                return true;
            }
            return false;
        }
    }
}

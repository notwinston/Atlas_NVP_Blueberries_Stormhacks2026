using Unity.Mathematics;

namespace Atlas.Kernel
{
    // P1-B11, Master Plan §4.6: the room's centre is tile zero's centre and room metres are tile zero's chart metres.
    // Placements are isometries in tile zero's frame, so the room's content is rigid under the exp map about the centre.
    public static class TileZeroPlacement
    {
        /// <summary>Registered start pose: Boost(head / l, kappa); flat (kappa 0): Boost(head, 0) in metres.</summary>
        public static Iso2 Registered(double2 headXZm, double kappa, double lengthScaleM)
            => Iso2.Boost(headXZm / Scale(kappa, lengthScaleM), kappa);

        /// <summary>
        /// Placement of a room object: Boost(position / l, kappa) * Rot(-yaw). Rot(theta) is a Unity yaw of -theta (kernel.md §2.4),
        /// so the Unity yaw enters as Rot(-yaw).
        /// </summary>
        public static Iso2 Object(double2 positionXZm, double yawUnityRad, double kappa, double lengthScaleM)
            => Iso2.Boost(positionXZm / Scale(kappa, lengthScaleM), kappa) * Iso2.Rot(-yawUnityRad);

        /// <summary>
        /// The same object laid out by the exp map about origin (room metres) instead of the room centre:
        /// Boost(origin / l) * Boost((position - origin) / l) * Rot(-yaw). Seen from a walker registered at origin, its
        /// distance and bearing are the real ones. Flat, it is Object exactly.
        /// </summary>
        public static Iso2 ObjectAbout(double2 originXZm, double2 positionXZm, double yawUnityRad, double kappa, double lengthScaleM)
        {
            double l = Scale(kappa, lengthScaleM);
            return Iso2.Boost(originXZm / l, kappa) * Iso2.Boost((positionXZm - originXZm) / l, kappa) * Iso2.Rot(-yawUnityRad);
        }

        /// <summary>The lap flag, c0 = (-s/2, -s/2), first heading +x (MP §3.3, §4.4, A.4): Boost(c0 / l, kappa).</summary>
        public static Iso2 LapFlag(double sideM, double kappa, double lengthScaleM)
            => Iso2.Boost(new double2(-sideM / 2, -sideM / 2) / Scale(kappa, lengthScaleM), kappa);

        static double Scale(double kappa, double l) => kappa == 0 ? 1.0 : l;
    }
}

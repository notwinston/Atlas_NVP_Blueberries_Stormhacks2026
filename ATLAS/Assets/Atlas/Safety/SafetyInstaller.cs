using Atlas.World;
using UnityEngine;

namespace Atlas.Safety
{
    /// <summary>
    /// Builds Bump Guard and its warning cue in code (contracts F3; lane A boot row 12). The composition root
    /// calls <see cref="Install"/> once, wraps the returned guard in its safety step, and from then on the frame
    /// loop ticks it first in every frame.
    /// </summary>
    public static class SafetyInstaller
    {
        /// <summary>
        /// Creates Bump Guard, the cue under boot.Hud (on that transform's layer), the phone's haptics and the
        /// gamepad's rumble, and an object "BumpGuard" under boot.Systems that owns their lifetime. Publishes
        /// <see cref="IBumpGuard"/> and <see cref="BumpGuard"/> through AtlasServices. After boot the guard
        /// follows ISpaceHost.RoomChanged by itself. cueMaterial is used only if the cue's own shader is
        /// missing from the build, and may be null.
        /// </summary>
        public static BumpGuard Install(AtlasBootContext boot, Material cueMaterial = null)
        {
            var guard = new BumpGuard();
            var phone = new IosHaptics();
            BumpCue.Initialise(boot.Hud, cueMaterial, phone);
            BumpCue.AddHaptics(new GamepadHaptics());

            var systemObject = new GameObject("BumpGuard");
            if (boot.Systems != null) systemObject.transform.SetParent(boot.Systems, false);
            systemObject.AddComponent<BumpGuardLifetime>().Bind(guard, phone);

            AtlasServices.Register<IBumpGuard>(guard);
            AtlasServices.Register(guard);
            return guard;
        }
    }
}

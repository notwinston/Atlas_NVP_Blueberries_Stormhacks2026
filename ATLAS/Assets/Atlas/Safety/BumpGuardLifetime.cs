using System;
using Atlas.World;
using UnityEngine;

namespace Atlas.Safety
{
    /// <summary>
    /// Ties Bump Guard to the life of the app's systems object (contracts F3): follows the active room, quiets
    /// the rumble while the app is paused, and frees the pinned map and the cue when the object goes. It has no
    /// Update: Bump Guard is ticked by the frame loop only (contracts F5).
    /// </summary>
    public sealed class BumpGuardLifetime : MonoBehaviour
    {
        BumpGuard _guard;
        IosHaptics _phone;
        ISpaceHost _host;
        Action<RoomModel> _onRoomChanged;
        Action _onBooted;

        internal void Bind(BumpGuard guard, IosHaptics phone)
        {
            _guard = guard;
            _phone = phone;
            _onRoomChanged = guard.SetRoom;
            // The space host is published later in the boot order: take the room from it once boot is over.
            if (AtlasServices.BootCompleted) FollowRoom();
            else
            {
                _onBooted = FollowRoom;
                AtlasServices.Booted += _onBooted;
            }
        }

        void FollowRoom()
        {
            if (_onBooted != null)
            {
                AtlasServices.Booted -= _onBooted;
                _onBooted = null;
            }
            if (_guard == null || _host != null) return;
            if (!AtlasServices.TryGet(out ISpaceHost host)) return;
            _host = host;
            _host.RoomChanged += _onRoomChanged;
            if (_host.Room != null) _guard.SetRoom(_host.Room);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) BumpCue.Silence();
        }

        void OnDestroy()
        {
            if (_onBooted != null) AtlasServices.Booted -= _onBooted;
            _onBooted = null;
            if (_host != null) _host.RoomChanged -= _onRoomChanged;
            _host = null;
            if (_guard == null) return;

            AtlasServices.Unregister<IBumpGuard>(_guard);
            AtlasServices.Unregister(_guard);
            BumpCue.Shutdown();
            if (_phone != null) _phone.Stop();
            _guard.Dispose();
            _guard = null;
        }
    }
}

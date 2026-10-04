using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using UnityEngine;

namespace Atlas.Blades.Network
{
    /// <summary>
    /// S7 Network: flat, no warp. The walker stands in the real room; the network floor map is shown over the floor and the
    /// room's furniture is hidden while the space is entered. The net.* parameters are declared at boot by NetworkTwins.
    /// </summary>
    public sealed class NetworkSpace : MonoBehaviour, ISpaceModule
    {
        static bool s_LoggedNoView;
        IRoomContent m_Room;

        public static ISpaceModule Create()
        {
            return new GameObject(SpaceIds.SceneName(SpaceId.Network)).AddComponent<NetworkSpace>();
        }

        /// <summary>Nothing to declare: NetworkTwins declares group "net" at boot.</summary>
        public static void DeclareParams(ParamRegistry registry) { }

        public SpaceId Id { get { return SpaceId.Network; } }
        public bool Registered { get { return true; } }

        public void Configure(RoomModel room, ParamRegistry registry)
        {
            AtlasServices.TryGet(out m_Room);
        }

        public void Enter(in WalkerPose start)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            if (m_Room != null && m_Room.IsBuilt) m_Room.ObjectsVisible = false;
            NetworkFloorView v = NetworkInstaller.View;
            if (v == null)
            {
                if (!s_LoggedNoView) { s_LoggedNoView = true; Debug.LogWarning("[Network] S7 entered without a floor view: nothing to show."); }
                return;
            }
            v.Visible = true;
        }

        public void Tick(in FrameContext frame) { }

        public void Exit()
        {
            NetworkFloorView v = NetworkInstaller.View;
            if (v != null) v.Visible = false;
            if (m_Room != null && m_Room.IsBuilt) m_Room.ObjectsVisible = true;
        }

        public void WriteState(ref StateMessage state) { }

        void OnDestroy()
        {
            NetworkFloorView v = NetworkInstaller.View;
            if (v != null) v.Visible = false;
        }
    }

    static class NetworkSpaceRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void Register()
        {
            SpaceRegistry.Register(SpaceId.Network, NetworkSpace.Create, NetworkSpace.DeclareParams);
        }
    }
}

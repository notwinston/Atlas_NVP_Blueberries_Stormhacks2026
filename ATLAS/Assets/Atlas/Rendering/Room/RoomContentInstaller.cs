using System;
using Atlas.World;
using UnityEngine;

namespace Atlas.Rendering.Room
{
    /// <summary>Creates the room content and wires it to the space host and the frame loop (contract README F3).</summary>
    public static class RoomContentInstaller
    {
        /// <summary>
        /// Row 13 of the creation order: creates RoomContent under boot.RoomRoot with the published
        /// MaterialPool, publishes it as IRoomContent and, once AtlasServices.Booted is raised, rebuilds on
        /// ISpaceHost.RoomChanged and ticks from the frame loop's upload step.
        /// </summary>
        public static RoomContent Install(AtlasBootContext boot)
        {
            return Install(boot, AtlasServices.Get<MaterialPool>());
        }

        /// <summary>As above, with the pool given.</summary>
        public static RoomContent Install(AtlasBootContext boot, MaterialPool pool)
        {
            if (boot == null) throw new ArgumentNullException(nameof(boot));
            Transform root = boot.RoomRoot;
            if (root == null)
            {
                // A composition root with no room node of its own still gets a room.
                root = new GameObject("Room").transform;
                if (boot.Root != null) root.SetParent(boot.Root, false);
                boot.RoomRoot = root;
            }
            // Until the frame loop ticks it, nothing would advance a fade.
            var content = new RoomContent(root, pool) { Instant = true };
            AtlasServices.Register<IRoomContent>(content);
            var wiring = new Wiring(content);
            AtlasServices.Booted += wiring.OnBooted;
            return content;
        }

        sealed class Wiring : IUploadStep
        {
            readonly RoomContent m_Content;

            public Wiring(RoomContent content) { m_Content = content; }

            public void OnBooted()
            {
                if (AtlasServices.TryGet(out IFrameLoop loop))
                {
                    loop.AddUpload(this);
                    m_Content.Instant = false;
                }
                if (AtlasServices.TryGet(out ISpaceHost host))
                {
                    host.RoomChanged += OnRoomChanged;
                    if (host.Room != null && !ReferenceEquals(host.Room, m_Content.Room)) m_Content.Build(host.Room);
                }
            }

            void OnRoomChanged(RoomModel room)
            {
                if (room != null) m_Content.Build(room);
                else m_Content.Clear();
            }

            public void Upload(in FrameContext frame)
            {
                m_Content.Tick(frame.DeltaTime);
            }
        }
    }
}

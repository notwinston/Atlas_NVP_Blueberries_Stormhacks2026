namespace Atlas.World
{
    /// <summary>
    /// What an installer gets from the composition root (contract README F3). Not a core type: it carries
    /// scene transforms, so it is the one place in this folder that names UnityEngine.
    /// </summary>
    public sealed class AtlasBootContext
    {
        /// <summary>"Atlas".</summary>
        public UnityEngine.Transform Root;
        /// <summary>"Atlas/Systems": one child per system, created by its installer.</summary>
        public UnityEngine.Transform Systems;
        /// <summary>"Atlas/Room": identity transform, the room frame.</summary>
        public UnityEngine.Transform RoomRoot;
        /// <summary>"Atlas/Space": parent of a module created by a factory.</summary>
        public UnityEngine.Transform SpaceRoot;
        /// <summary>The XR Origin. Null in the Spectator.</summary>
        public UnityEngine.Transform XrOrigin;
        /// <summary>"Main Camera", the tracked device camera. Null in the Spectator.</summary>
        public UnityEngine.Transform TrackedCamera;
        /// <summary>"Atlas Head": the head every lane reads.</summary>
        public UnityEngine.Transform Head;
        /// <summary>Head-locked parent, layer AtlasHud.</summary>
        public UnityEngine.Transform Hud;
        public Atlas.Link.ParamRegistry Params;
        /// <summary>Built with ATLAS_SPECTATOR.</summary>
        public bool IsSpectator;
        /// <summary>iOS player, not the Editor.</summary>
        public bool IsDevice;
    }
}

using Atlas.Rendering;
using Atlas.World;

namespace Atlas.App
{
    /// <summary>
    /// What SpaceLab changes outside a module (lane A contract, section 4.2): the global world fade, and
    /// between two spaces the look's defaults and the room content's default presentation.
    /// </summary>
    public sealed class SpaceStage : ISpaceStage
    {
        readonly IRoomContent _room;

        /// <param name="room">The room content; may be null (a sandbox with no room).</param>
        public SpaceStage(IRoomContent room) { _room = room; }

        public void SetFade(float fade)
        {
            AtlasLook.SetFade(fade);
        }

        public void ResetForSpace()
        {
            AtlasLook.ResetForSpace();
            if (_room != null && _room.IsBuilt) _room.ResetPresentation();
        }
    }
}

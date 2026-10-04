using System;
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
        readonly FurnitureLevelPolicy _furniture;
        readonly Action _resetLook;
        readonly Action<float> _setFade;

        /// <param name="room">The room content; may be null (a sandbox with no room).</param>
        /// <param name="furniture">Sets the furniture level of the room on entering a space; may be null.</param>
        public SpaceStage(IRoomContent room, FurnitureLevelPolicy furniture = null)
            : this(room, furniture, AtlasLook.ResetForSpace, AtlasLook.SetFade)
        {
        }

        /// <summary>As above, with the look given: for a test, which has no shader globals to write.</summary>
        public SpaceStage(IRoomContent room, FurnitureLevelPolicy furniture, Action resetLook, Action<float> setFade)
        {
            _room = room;
            _furniture = furniture;
            _resetLook = resetLook;
            _setFade = setFade;
        }

        public void SetFade(float fade)
        {
            _setFade(fade);
        }

        public void ResetForSpace(SpaceId? next)
        {
            _resetLook();
            if (_room != null && _room.IsBuilt) _room.ResetPresentation();
            // After the reset, which returns the room to Full: the level of the space about to be entered.
            if (_furniture != null) _furniture.EnterSpace(next);
        }
    }
}

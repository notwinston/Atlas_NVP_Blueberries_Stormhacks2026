using System;
using Atlas.World;

namespace Atlas.App
{
    /// <summary>The values of render.furniture: the space's level and then the ladder, or one level always.</summary>
    public enum FurnitureMode : byte { Auto = 0, Light = 1, Full = 2 }

    /// <summary>
    /// The level of furniture each space asks for (mesh design section 5.4): Full in Home and Curved Walk, Light
    /// in every other space, until the measurement of the full copies in the other spaces says otherwise.
    /// </summary>
    public static class SpaceFurniture
    {
        static readonly FurnitureLevel[] s_levels =
        {
            FurnitureLevel.Full,     // S0 Home
            FurnitureLevel.Light,    // S1 Glued
            FurnitureLevel.Full,     // S2 Curved Walk
            FurnitureLevel.Light,    // S3 Aligned Cells
            FurnitureLevel.Light,    // S4 Small Planet
            FurnitureLevel.Light,    // S5 Impossible Rooms
            FurnitureLevel.Light,    // S6 Slicer
            FurnitureLevel.Light,    // S7 Network
        };

        /// <summary>The space's own level. Full for a value that is not a space.</summary>
        public static FurnitureLevel Level(SpaceId id)
        {
            int index = (int)id;
            return index >= 0 && index < s_levels.Length ? s_levels[index] : FurnitureLevel.Full;
        }
    }

    /// <summary>
    /// Decides the room's furniture level and puts it on the room content: when a space is entered (from the
    /// stage, right after the room's presentation is reset), when the ladder changes tier, when
    /// render.furniture changes and when the room is rebuilt. All of these run between frames.
    /// </summary>
    public sealed class FurnitureLevelPolicy
    {
        readonly IRoomContent _room;
        readonly QualityLadder _ladder;
        FurnitureMode _mode;
        bool _inSpace;
        SpaceId _space;

        /// <summary>room may be null (a sandbox with no room); ladder may be null (no ladder: the space's level stands).</summary>
        public FurnitureLevelPolicy(IRoomContent room, QualityLadder ladder)
        {
            _room = room;
            _ladder = ladder;
            if (ladder != null) ladder.TierChanged += tier => Apply();
            if (room != null) room.Built += model => Apply();
        }

        /// <summary>render.furniture.</summary>
        public FurnitureMode Mode
        {
            get { return _mode; }
            set
            {
                if (value == _mode) return;
                _mode = value;
                Apply();
            }
        }

        /// <summary>The level the policy asks of the room now.</summary>
        public FurnitureLevel Level
        {
            get
            {
                FurnitureLevel own = _inSpace ? SpaceFurniture.Level(_space) : FurnitureLevel.Full;
                return Resolve(_mode, own, _ladder != null ? _ladder.FurnitureCap : FurnitureLevel.Full);
            }
        }

        /// <summary>
        /// auto: the space's level, held down to Light by the ladder. light: Light. full: Full, whatever the
        /// ladder says.
        /// </summary>
        public static FurnitureLevel Resolve(FurnitureMode mode, FurnitureLevel spaceLevel, FurnitureLevel ladderCap)
        {
            switch (mode)
            {
                case FurnitureMode.Light: return FurnitureLevel.Light;
                case FurnitureMode.Full: return FurnitureLevel.Full;
                default: return ladderCap == FurnitureLevel.Light ? FurnitureLevel.Light : spaceLevel;
            }
        }

        /// <summary>A space is being entered (the room's presentation has just been reset). Null: no space.</summary>
        public void EnterSpace(SpaceId? id)
        {
            _inSpace = id.HasValue;
            if (id.HasValue) _space = id.Value;
            Apply();
        }

        void Apply()
        {
            if (_room == null || !_room.IsBuilt) return;
            _room.FurnitureLevel = Level;
        }
    }
}

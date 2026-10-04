namespace Atlas.World
{
    /// <summary>The seven spaces of Phase 1 (Master Plan section 3.1). The value is State.spaceId (Appendix B.1).</summary>
    public enum SpaceId : byte
    {
        Home = 0,
        Glued = 1,
        CurvedWalk = 2,
        AlignedCells = 3,
        SmallPlanet = 4,
        Portal = 5,
        Slicer = 6,
    }

    /// <summary>Names and wire values derived from a <see cref="SpaceId"/>. Every method returns a constant: no allocation.</summary>
    public static class SpaceIds
    {
        public const int Count = 7;
        /// <summary>State.spaceId while no space is entered (onboarding, scan).</summary>
        public const byte NoSpace = 255;
        /// <summary>State.sceneId while no space is entered.</summary>
        public const ushort NoScene = 0xFFFF;

        static readonly string[] s_codes = { "S0", "S1", "S2", "S3", "S4", "S5", "S6" };

        static readonly string[] s_scenes =
        {
            "Space_Home", "Space_Glued", "Space_CurvedWalk", "Space_AlignedCells", "Space_SmallPlanet",
            "Space_Portal", "Space_Slicer"
        };

        static readonly string[] s_groups = { "home", "glued", "curved", "cells", "planet", "portal", "slicer" };

        static readonly string[] s_display =
        {
            "Home", "Glued", "Curved Walk", "Aligned Cells", "Small Planet", "Impossible Rooms", "Slicer"
        };

        /// <summary>True for the seven defined values.</summary>
        public static bool IsDefined(SpaceId id) { return (int)id < Count; }

        /// <summary>"S0" to "S6": the code used by SceneCmd (Appendix B.2) and in logs.</summary>
        public static string Code(SpaceId id) { return IsDefined(id) ? s_codes[(int)id] : "S?"; }

        /// <summary>Parses "S0" to "S6". False for anything else.</summary>
        public static bool TryParseCode(string code, out SpaceId id)
        {
            id = SpaceId.Home;
            if (code == null || code.Length != 2 || code[0] != 'S') return false;
            int n = code[1] - '0';
            if (n < 0 || n >= Count) return false;
            id = (SpaceId)n;
            return true;
        }

        /// <summary>"Space_Home" and so on: the scene name of Appendix C.9.</summary>
        public static string SceneName(SpaceId id) { return IsDefined(id) ? s_scenes[(int)id] : "Space_Unknown"; }

        /// <summary>Lower-case parameter group: "home", "glued", "curved", "cells", "planet", "portal", "slicer".</summary>
        public static string ParamGroup(SpaceId id) { return IsDefined(id) ? s_groups[(int)id] : "unknown"; }

        /// <summary>The name shown to people: "Home", "Glued", "Curved Walk", "Aligned Cells", "Small Planet", "Impossible Rooms", "Slicer".</summary>
        public static string DisplayName(SpaceId id) { return IsDefined(id) ? s_display[(int)id] : "Unknown"; }

        /// <summary>State.sceneId of a SpaceLab space: the numeric value of the id.</summary>
        public static ushort SceneId(SpaceId id) { return (ushort)id; }
    }
}

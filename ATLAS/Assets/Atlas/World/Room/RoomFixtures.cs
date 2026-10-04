using System;
using System.IO;

namespace Atlas.World
{
    /// <summary>
    /// The RoomModel fixtures: the five frozen at gate G0 (Master Plan section 4.3), three sample rooms, the budget
    /// room and a standing-mode room, and budget_room_120k of the mesh design (section 5.4). This file is engine-free; the Resources loader is in RoomFixturesLoader.cs.
    /// </summary>
    public static partial class RoomFixtures
    {
        public const string Sample3x3 = "sample_3x3";
        public const string SampleL3x4 = "sample_L_3x4";
        public const string Sample4x5Table = "sample_4x5_table";
        public const string BudgetRoom = "budget_room";
        public const string TinyStanding = "tiny_standing";
        /// <summary>The budget room with 24 stand-ins, 120,000 triangles in all.</summary>
        public const string BudgetRoom120k = "budget_room_120k";
        /// <summary>The six names, in this order.</summary>
        public static readonly string[] All = { Sample3x3, SampleL3x4, Sample4x5Table, BudgetRoom, TinyStanding, BudgetRoom120k };
        public const string ResourcesFolder = "AtlasRooms";
        /// <summary>Environment variable that names the Unity project folder for headless runs.</summary>
        public const string ProjectDirVariable = "ATLAS_PROJECT_DIR";

        /// <summary>True when the name is one of the fixtures.</summary>
        public static bool IsFixture(string name)
        {
            return Array.IndexOf(All, name) >= 0;
        }

        /// <summary>Path of a fixture file: Assets/Atlas/World/Resources/AtlasRooms/name.json under the folder
        /// named by ATLAS_PROJECT_DIR, or under the current directory.</summary>
        public static string ProjectPath(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("No fixture name", nameof(name));
            string root = Environment.GetEnvironmentVariable(ProjectDirVariable);
            if (string.IsNullOrEmpty(root)) root = Directory.GetCurrentDirectory();
            return Path.Combine(root, "Assets", "Atlas", "World", "Resources", ResourcesFolder, name + ".json");
        }

        /// <summary>For headless tests and tools: the text of a fixture file read from the project folder.</summary>
        public static string ReadTextFromProject(string name)
        {
            string path = ProjectPath(name);
            if (!File.Exists(path)) throw new ArgumentException("No RoomModel fixture at " + path, nameof(name));
            return File.ReadAllText(path);
        }

        /// <summary>For headless tests and tools: reads and parses a fixture from the project folder.</summary>
        public static RoomModel LoadFromProject(string name)
        {
            return RoomModelJson.Read(ReadTextFromProject(name));
        }

        /// <summary>Reads and parses any RoomModel file. Engine-free.</summary>
        public static RoomModel LoadFromFile(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("No file path", nameof(path));
            if (!File.Exists(path)) throw new ArgumentException("No RoomModel file at " + path, nameof(path));
            return RoomModelJson.Read(File.ReadAllText(path));
        }
    }
}

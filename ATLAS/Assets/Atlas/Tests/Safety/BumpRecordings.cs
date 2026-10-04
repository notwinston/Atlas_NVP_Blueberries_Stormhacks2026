using System;
using System.IO;
using Atlas.World;
using NUnit.Framework;

namespace Atlas.Safety.Tests
{
    /// <summary>
    /// The recorded walks of /recordings that the Bump Guard tests replay (contract README F8): both are in the
    /// frame of the fixture room sample_3x3, a 3 x 3 m room, at 60 samples a second.
    /// </summary>
    public static class BumpRecordings
    {
        /// <summary>One lap of the 2.0 m lap square at 0.5 m/s.</summary>
        public const string Lap = "walk_lap_sample_3x3";
        /// <summary>Straight at the wall at z = 1.5 at 1.0 m/s, stopping 0.3 m short of it.</summary>
        public const string WallApproach = "walk_wall_approach";
        /// <summary>Half the side of the room the walks were made in, metres.</summary>
        public const float RoomHalf = 1.5f;

        /// <summary>Reads a pose log of /recordings by its name without the extension.</summary>
        public static PoseLog Load(string name)
        {
            string project = Environment.GetEnvironmentVariable(RoomFixtures.ProjectDirVariable);
            if (string.IsNullOrEmpty(project)) project = Directory.GetCurrentDirectory();
            string path = Path.GetFullPath(Path.Combine(project, "..", "recordings", name + ".poselog"));
            Assert.That(File.Exists(path), "missing recording " + path);
            using (var reader = new StreamReader(path)) return PoseLog.Read(reader);
        }
    }
}

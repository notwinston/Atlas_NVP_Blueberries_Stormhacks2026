using UnityEditor;
using UnityEngine;

namespace Atlas.World.Lint
{
    static class WorldLintMenu
    {
        // Master Plan §9.5.6: three seeds, budget_room dimensions (4 x 5 m, L = 3.0 / 1.0613, A.4). Edits no scene.
        [MenuItem("Atlas/World/Run WorldLint")]
        static void RunDefault()
        {
            var room = RoomFootprint.Rectangle(4, 5, 3.0 / 1.0613);
            foreach (uint seed in new uint[] { 1, 42, 20261003 })
            {
                var report = WorldLint.Run(seed, room);
                if (report.Passed) Debug.Log(report.ToString()); else Debug.LogError(report.ToString());
            }
        }
    }
}

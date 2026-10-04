using System;
using UnityEngine;

namespace Atlas.Blades.Playground.Shell
{
    /// <summary>
    /// <c>Beats.asset</c> (Master Plan section 10.1.2): the ten beats of the run as data, the order the director
    /// walks and the name each <c>pg.beat</c> event carries. The director's logic is in
    /// <see cref="PlaygroundDirectorCore"/>; the exit condition column is the plan's wording, for the Station
    /// operator's checklist and not read by code.
    /// </summary>
    [CreateAssetMenu(fileName = "Beats", menuName = "Atlas/Playground Beats")]
    public sealed class PlaygroundBeatTable : ScriptableObject
    {
        [Serializable]
        public struct Row
        {
            public int Number;
            public string Name;
            public string ExitCondition;
        }

        [SerializeField] Row[] m_Beats = Defaults();

        public Row[] Beats { get { return m_Beats; } }

        /// <summary>The names by beat number, index 1 to 10, for <see cref="PlaygroundDirectorCore"/>; index 0 is unused.</summary>
        public string[] Names()
        {
            var names = new string[PlaygroundBeats.Count + 1];
            if (m_Beats != null)
                foreach (Row row in m_Beats)
                    if (row.Number >= 1 && row.Number <= PlaygroundBeats.Count) names[row.Number] = row.Name;
            return names;
        }

        /// <summary>The table holds the ten beats, numbered 1 to 10 in order, each with a name.</summary>
        public bool IsComplete()
        {
            if (m_Beats == null || m_Beats.Length != PlaygroundBeats.Count) return false;
            for (int i = 0; i < m_Beats.Length; i++)
                if (m_Beats[i].Number != i + 1 || string.IsNullOrEmpty(m_Beats[i].Name)) return false;
            return true;
        }

        /// <summary>The plan's table: names as <see cref="PlaygroundBeats.Name"/>, exit conditions as written.</summary>
        public static Row[] Defaults()
        {
            string[] exits =
            {
                "RoomModel valid",
                "Walker confirms; Local Network prompt answered",
                "Head pose stable",
                "Head within 0.3 m of c0",
                "q = 5 selected",
                "Loop closed at q = 5",
                "Walker opens the mode chip",
                "Walker opens the menu",
                "Exhibit chosen, or done",
                "End of run"
            };
            var rows = new Row[PlaygroundBeats.Count];
            for (int i = 0; i < rows.Length; i++)
                rows[i] = new Row { Number = i + 1, Name = PlaygroundBeats.Name((PlaygroundBeat)(i + 1)), ExitCondition = exits[i] };
            return rows;
        }
    }
}

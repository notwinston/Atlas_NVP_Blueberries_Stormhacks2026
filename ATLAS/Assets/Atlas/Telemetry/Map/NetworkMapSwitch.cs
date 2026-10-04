using System;
using Unity.Mathematics;

namespace Atlas.Telemetry
{
    /// <summary>
    /// Which picture the Spectator's map panel shows (NET-04): the network disc while the phone's network exhibit is
    /// active, else null (the normal flat twin). Engine-free; the file is read through a delegate and each bundled
    /// file is parsed at most once. <see cref="Update"/> allocates nothing after the discs are loaded.
    /// </summary>
    public sealed class NetworkMapSwitch
    {
        /// <summary>The parameter that picks the bundled file (contract section 6.3).</summary>
        public const string FileParam = "net.file";
        /// <summary>net.file option order is ascending seed (contract section 1).</summary>
        static readonly int[] s_seeds = { 7, 83 };

        readonly Func<string, string> _read;
        readonly NetworkDisc[] _discs = new NetworkDisc[2];
        readonly bool[] _failed = new bool[2];
        NetworkDisc _centred;
        int _centredNode = -2;

        /// <param name="read">Returns the text of a file name under StreamingAssets/networks, or null.</param>
        public NetworkMapSwitch(Func<string, string> read) { _read = read; }

        public static string FileName(int file)
        {
            return "network_" + s_seeds[file < 0 || file >= s_seeds.Length ? 0 : file] + ".json";
        }

        /// <summary>The disc to show now, or null for the flat twin.</summary>
        public NetworkDisc Update(bool active, int file, int centreNode)
        {
            if (!active) return null;
            if (file < 0 || file >= s_seeds.Length) file = 0;
            NetworkDisc disc = _discs[file];
            if (disc == null)
            {
                if (_failed[file]) return null;
                try
                {
                    string json = _read != null ? _read(FileName(file)) : null;
                    if (json != null) disc = NetworkDisc.Parse(json);
                }
                catch (Exception) { disc = null; }
                if (disc == null) { _failed[file] = true; return null; }
                _discs[file] = disc;
            }
            if (!ReferenceEquals(_centred, disc) || _centredNode != centreNode)
            {
                _centred = disc;
                _centredNode = centreNode;
                if (centreNode >= 0 && centreNode < disc.NodeCount) disc.RecentreOnNode(centreNode);
                else disc.Recentre(double2.zero);
            }
            return disc;
        }
    }
}

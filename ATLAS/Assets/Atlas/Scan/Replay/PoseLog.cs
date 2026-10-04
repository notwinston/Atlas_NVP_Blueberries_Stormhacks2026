// PLACEHOLDER (lane C, P1-C01): contract surface only; replaced by lane C's implementation
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Atlas.Scan
{
    // Master Plan App. C.8 "walk_*.poselog (timestamped head poses)". Text format (PH-24), one sample per line:
    //   # atlas.poselog/1  t_s x y z qx qy qz qw tracking
    // t_s seconds; x y z metres in the anchor frame; q Unity quaternion; tracking 0 normal, 1 limited, 2 lost.
    public readonly struct PoseSample
    {
        public readonly double Time;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly byte Tracking;

        internal PoseSample(double time, Vector3 position, Quaternion rotation, byte tracking)
        {
            Time = time; Position = position; Rotation = rotation; Tracking = tracking;
        }
    }

    public static class PoseLog
    {
        public static PoseSample[] Read(string path)
        {
            var list = new List<PoseSample>();
            var ci = CultureInfo.InvariantCulture;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var f = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 9) continue;
                list.Add(new PoseSample(
                    double.Parse(f[0], ci),
                    new Vector3(float.Parse(f[1], ci), float.Parse(f[2], ci), float.Parse(f[3], ci)),
                    new Quaternion(float.Parse(f[4], ci), float.Parse(f[5], ci), float.Parse(f[6], ci), float.Parse(f[7], ci)),
                    byte.Parse(f[8], ci)));
            }
            return list.ToArray();
        }
    }
}

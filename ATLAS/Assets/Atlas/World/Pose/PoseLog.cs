using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>
    /// A recorded walk: one raw head pose per line of a text file, "walk_*.poselog". Engine-free.
    /// Line 1 is "ATLAS-POSELOG 1"; then "key value" header lines up to a line that is exactly "data";
    /// then nine fields per line: t x y z qx qy qz qw s.
    /// </summary>
    public sealed class PoseLog
    {
        public const string Magic = "ATLAS-POSELOG";
        public const int FormatVersion = 1;

        static readonly char[] s_space = { ' ' };

        /// <summary>The RoomModel the walk was recorded in; "" when the header has no room line.</summary>
        public string RoomId = "";
        public PoseFrameKind Frame;
        /// <summary>"" when absent.</summary>
        public string CreatedUtc = "";
        /// <summary>"" when absent.</summary>
        public string Device = "";
        /// <summary>Time is t of the file: seconds since the first sample.</summary>
        public HeadPose[] Samples = new HeadPose[0];

        /// <summary>Last t, or 0.</summary>
        public double Duration
        {
            get { return Samples != null && Samples.Length > 0 ? Samples[Samples.Length - 1].Time : 0.0; }
        }

        /// <summary>FormatException on a bad header, a wrong field count or a decreasing time, naming the line.</summary>
        public static PoseLog Read(TextReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            int lineNo = 1;
            string line = reader.ReadLine();
            if (line == null) throw new FormatException("Pose log line 1: the file is empty");
            string[] head = line.Trim().Split(s_space, StringSplitOptions.RemoveEmptyEntries);
            int version;
            if (head.Length != 2 || head[0] != Magic ||
                !int.TryParse(head[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out version))
                throw new FormatException("Pose log line 1: expected \"" + Magic + " " + FormatVersion + "\"");
            if (version != FormatVersion)
                throw new FormatException("Pose log line 1: version " + version + " is not supported");

            var log = new PoseLog();
            bool haveFrame = false, inData = false;
            while (!inData && (line = reader.ReadLine()) != null)
            {
                lineNo++;
                line = line.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line == "data") { inData = true; break; }
                int cut = line.IndexOf(' ');
                string key = cut < 0 ? line : line.Substring(0, cut);
                string value = cut < 0 ? "" : line.Substring(cut + 1).Trim();
                switch (key)
                {
                    case "frame":
                        if (value == "room") log.Frame = PoseFrameKind.Room;
                        else if (value == "session") log.Frame = PoseFrameKind.Session;
                        else throw new FormatException("Pose log line " + lineNo + ": frame must be room or session");
                        haveFrame = true;
                        break;
                    case "room": log.RoomId = value; break;
                    case "created": log.CreatedUtc = value; break;
                    case "device": log.Device = value; break;
                    // Unknown keys are ignored, so a newer recorder can add some.
                }
            }
            if (!inData) throw new FormatException("Pose log line " + lineNo + ": the header has no data line");
            if (!haveFrame) throw new FormatException("Pose log line " + lineNo + ": the header has no frame line");

            var samples = new List<HeadPose>(1024);
            double last = 0.0;
            var v = new double[8];
            while ((line = reader.ReadLine()) != null)
            {
                lineNo++;
                line = line.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] f = line.Split(s_space, StringSplitOptions.RemoveEmptyEntries);
                if (f.Length != 9)
                    throw new FormatException("Pose log line " + lineNo + ": expected 9 fields, found " + f.Length);
                for (int k = 0; k < 8; k++)
                {
                    if (!double.TryParse(f[k], NumberStyles.Float, CultureInfo.InvariantCulture, out v[k]) ||
                        double.IsNaN(v[k]) || double.IsInfinity(v[k]))
                        throw new FormatException("Pose log line " + lineNo + ": field " + (k + 1) + " is not a number");
                }
                int s;
                if (!int.TryParse(f[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out s) || s < 0 || s > 2)
                    throw new FormatException("Pose log line " + lineNo + ": tracking must be 0, 1 or 2");
                if (v[0] < last)
                    throw new FormatException("Pose log line " + lineNo + ": time decreases");
                last = v[0];
                samples.Add(new HeadPose(v[0], new float3((float)v[1], (float)v[2], (float)v[3]),
                    new quaternion((float)v[4], (float)v[5], (float)v[6], (float)v[7]), (TrackingState)s));
            }
            log.Samples = samples.ToArray();
            return log;
        }

        /// <summary>Writes the header and every sample, with line feeds.</summary>
        public static void Write(TextWriter writer, PoseLog log)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (log == null) throw new ArgumentNullException(nameof(log));
            WriteHeader(writer, log.RoomId, log.Frame, log.CreatedUtc, log.Device);
            HeadPose[] samples = log.Samples;
            if (samples == null) return;
            for (int i = 0; i < samples.Length; i++)
            {
                writer.Write(FormatLine(samples[i].Time, in samples[i]));
                writer.Write('\n');
            }
        }

        /// <summary>The header up to and including the "data" line. Used by the recorder before it appends lines.</summary>
        public static void WriteHeader(TextWriter writer, string roomId, PoseFrameKind frame, string createdUtc,
            string device = null)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            writer.Write(Magic + " " + FormatVersion.ToString(CultureInfo.InvariantCulture) + "\n");
            if (!string.IsNullOrEmpty(roomId)) writer.Write("room " + roomId + "\n");
            writer.Write(frame == PoseFrameKind.Room ? "frame room\n" : "frame session\n");
            if (!string.IsNullOrEmpty(createdUtc)) writer.Write("created " + createdUtc + "\n");
            if (!string.IsNullOrEmpty(device)) writer.Write("device " + device + "\n");
            writer.Write("data\n");
        }

        /// <summary>One data line, without the line feed. Used by the recorder (off the frame path: it allocates).</summary>
        public static string FormatLine(double t, in HeadPose pose)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            float4 q = pose.Rotation.value;
            return string.Concat(
                t.ToString("F6", inv), " ",
                Fixed(pose.Position.x) + " " + Fixed(pose.Position.y) + " " + Fixed(pose.Position.z), " ",
                Fixed(q.x) + " " + Fixed(q.y) + " " + Fixed(q.z) + " " + Fixed(q.w) + " " +
                ((int)pose.Tracking).ToString(inv));
        }

        static string Fixed(float value)
        {
            string text = ((double)value).ToString("F6", CultureInfo.InvariantCulture);
            return text == "-0.000000" ? "0.000000" : text;
        }
    }
}

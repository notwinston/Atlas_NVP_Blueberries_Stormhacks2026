using System;
using System.IO;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Pose
{
    /// <summary>
    /// Generates the sample pose logs of /recordings (contract README F8): synthetic walks in the room frame of
    /// the fixture sample_3x3, at 60 samples a second and an eye height of 1.6 m. The files on disk are this
    /// generator's output; SampleWalkFilesTests keeps the two in step.
    /// </summary>
    public static class SampleWalks
    {
        public const double Rate = 60.0;
        public const float EyeHeight = 1.6f;
        public const string RoomId = RoomFixtures.Sample3x3;
        public const string CreatedUtc = "2026-10-04T00:00:00Z";
        public const string Device = "generated";

        public const string LapName = "walk_lap_sample_3x3";
        public const string WallName = "walk_wall_approach";
        public const string JumpName = "walk_jump_and_loss";
        public static readonly string[] Names = { LapName, WallName, JumpName };

        /// <summary>Metres per second.</summary>
        public const double LapSpeed = 0.5;
        public const double WallSpeed = 1.0;
        public const double JumpWalkSpeed = 0.5;

        /// <summary>Metres: how close to the wall the approach stops.</summary>
        public const double WallStopDistance = 0.3;
        /// <summary>Frames the approach stands still at its end.</summary>
        public const int WallStandFrames = 60;

        /// <summary>The frame whose pose has jumped, and the jump: half a metre along +Z.</summary>
        public const int JumpFrame = 90;
        public const float JumpMetres = 0.5f;
        /// <summary>The first frame with tracking Limited, and how many there are: one second.</summary>
        public const int LossFirstFrame = 150;
        public const int LossFrames = 60;
        public const int JumpWalkFrames = 240;

        public static PoseLog ByName(string name, RoomModel room)
        {
            switch (name)
            {
                case LapName: return Lap(room.LapSide);
                case WallName: return WallApproach(HalfDepth(room));
                case JumpName: return JumpAndLoss();
                default: throw new ArgumentException("No sample walk named " + name, nameof(name));
            }
        }

        /// <summary>The largest z of the room's outline: where the far wall stands.</summary>
        public static float HalfDepth(RoomModel room)
        {
            float z = 0f;
            for (int i = 0; i < room.Outline.Length; i++) z = math.max(z, room.Outline[i].y);
            return z;
        }

        /// <summary>One lap of the lap square at 0.5 m/s, starting and ending at the corner (-s/2, -s/2), looking along the path.</summary>
        public static PoseLog Lap(float lapSide)
        {
            double side = lapSide, h = 0.5 * side, total = 4.0 * side;
            const double blend = 0.2;      // metres before a corner over which the head turns into the next side
            int frames = (int)Math.Round(total / LapSpeed * Rate);
            var samples = new HeadPose[frames + 1];
            for (int k = 0; k <= frames; k++)
            {
                double t = k / Rate;
                double s = Math.Min(LapSpeed * t, total);
                int leg = Math.Min((int)(s / side), 3);
                double along = s - leg * side;
                double x, z;
                switch (leg)
                {
                    case 0: x = -h; z = -h + along; break;      // +Z
                    case 1: x = -h + along; z = h; break;       // +X
                    case 2: x = h; z = h - along; break;        // -Z
                    default: x = h - along; z = -h; break;      // -X
                }
                double u = Math.Max(0.0, Math.Min(1.0, (along - (side - blend)) / blend));
                double yaw = (leg + u * u * (3.0 - 2.0 * u)) * (Math.PI / 2.0);
                samples[k] = Sample(t, x, z, yaw, TrackingState.Normal);
            }
            return Make(samples);
        }

        /// <summary>A straight walk at 1.0 m/s from (0, -1) towards the wall at z = halfDepth, stopping 0.3 m short, then standing for a second.</summary>
        public static PoseLog WallApproach(float halfDepth)
        {
            const double startZ = -1.0;
            double stopZ = halfDepth - WallStopDistance;
            int walk = (int)Math.Round((stopZ - startZ) / WallSpeed * Rate);
            var samples = new HeadPose[walk + WallStandFrames + 1];
            for (int k = 0; k < samples.Length; k++)
            {
                double t = k / Rate;
                double z = Math.Min(startZ + WallSpeed * t, stopZ);
                samples[k] = Sample(t, 0.0, z, 0.0, TrackingState.Normal);
            }
            return Make(samples);
        }

        /// <summary>
        /// A walk along +X at 0.5 m/s from (-1, 0). At JumpFrame the pose jumps 0.5 m along +Z and stays
        /// there, as after a relocalisation. For one second from LossFirstFrame tracking is Limited while the
        /// pose keeps moving.
        /// </summary>
        public static PoseLog JumpAndLoss()
        {
            var samples = new HeadPose[JumpWalkFrames + 1];
            for (int k = 0; k <= JumpWalkFrames; k++)
            {
                double t = k / Rate;
                double x = -1.0 + JumpWalkSpeed * t;
                double z = k >= JumpFrame ? JumpMetres : 0.0;
                bool lost = k >= LossFirstFrame && k < LossFirstFrame + LossFrames;
                samples[k] = Sample(t, x, z, Math.PI / 2.0, lost ? TrackingState.Limited : TrackingState.Normal);
            }
            return Make(samples);
        }

        public static string ToText(PoseLog log)
        {
            var writer = new StringWriter();
            PoseLog.Write(writer, log);
            return writer.ToString();
        }

        /// <summary>/recordings in the repository: beside the Unity project folder.</summary>
        public static string RecordingsDirectory()
        {
            string project = Environment.GetEnvironmentVariable(RoomFixtures.ProjectDirVariable);
            if (string.IsNullOrEmpty(project)) project = Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(project, "..", "recordings"));
        }

        static HeadPose Sample(double t, double x, double z, double yaw, TrackingState tracking)
        {
            return new HeadPose(t, new float3((float)x, EyeHeight, (float)z), quaternion.RotateY((float)yaw), tracking);
        }

        static PoseLog Make(HeadPose[] samples)
        {
            return new PoseLog
            {
                RoomId = RoomId,
                Frame = PoseFrameKind.Room,
                CreatedUtc = CreatedUtc,
                Device = Device,
                Samples = samples,
            };
        }
    }
}

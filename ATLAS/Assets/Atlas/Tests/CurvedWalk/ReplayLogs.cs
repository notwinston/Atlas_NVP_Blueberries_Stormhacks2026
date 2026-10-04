using System;
using System.Collections.Generic;
using System.IO;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.CurvedWalk.Tests
{
    // Test helpers for P1-B09 / P1-B11 (brief §7): recorded walks in the real pose-log format, generated laps, and a rig
    // that plays the frame loop for S2: the app's one kernel stepped at step 4, then the space's logic at step 5.
    public static class ReplayLogs
    {
        public const double Dt = 1.0 / 60.0;

        static string ProjectDir => Environment.GetEnvironmentVariable(RoomFixtures.ProjectDirVariable) ?? Directory.GetCurrentDirectory();
        public static string RecordingsDir => Path.Combine(ProjectDir, "..", "recordings");

        /// <summary>A recorded walk (recordings/*.poselog, ATLAS-POSELOG 1) through the real reader.</summary>
        public static List<HeadPose> Load(string name)
        {
            using (var reader = File.OpenText(Path.Combine(RecordingsDir, name)))
                return new List<HeadPose>(PoseLog.Read(reader).Samples);
        }

        public static RoomModel Room(string fixture) => RoomFixtures.LoadFromProject(fixture);

        static HeadPose Sample(double t, double2 p) =>
            new HeadPose(t, new float3((float)p.x, 1.6f, (float)p.y), quaternion.identity, TrackingState.Normal);

        public static List<HeadPose> Still(double2 at, int frames, double t0 = 0)
        {
            var l = new List<HeadPose>();
            for (int i = 0; i < frames; i++) l.Add(Sample(t0 + i * Dt, at));
            return l;
        }

        static void Stop(List<HeadPose> l, double2 p, int frames)
        {
            double t = l.Count > 0 ? l[l.Count - 1].Time : 0;
            for (int i = 0; i < frames; i++) l.Add(Sample(t + (i + 1) * Dt, p));
        }

        /// <summary>Brief §7: still 5 s at (0,0), walk to start at 0.7 m/s, stop 0.5 s.</summary>
        public static List<HeadPose> Prefix(double2 start, double v = 0.7, int still = 300, int stop = 30)
        {
            var l = Still(double2.zero, still);
            double len = math.length(start);
            int n = Math.Max(1, (int)Math.Round(len / v / Dt));
            double t = l[l.Count - 1].Time;
            for (int i = 1; i <= n; i++) l.Add(Sample(t + i * Dt, start * ((double)i / n)));
            Stop(l, start, stop);
            return l;
        }

        /// <summary>Brief §7: a lap of `sides` sides of length s from start, turning left; lateral sway sin(2 pi 5 tau/T) per side, zero at both ends.</summary>
        public static List<HeadPose> Lap(int sides, double s, double2 start, double swayM, double v = 0.7, int stop = 30, double t0 = 0)
        {
            var l = new List<HeadPose>();
            double2 corner = start;
            double t = t0;
            for (int k = 0; k < sides; k++)
            {
                double2 d = (k & 3) switch { 0 => new double2(1, 0), 1 => new double2(0, 1), 2 => new double2(-1, 0), _ => new double2(0, -1) };
                double2 left = new double2(-d.y, d.x);
                int n = (int)Math.Round(s / v / Dt);
                for (int i = 1; i <= n; i++)
                {
                    double tau = (double)i / n;
                    l.Add(Sample(t + i * Dt, corner + d * (s * tau) + left * (swayM * Math.Sin(2 * Math.PI * 5 * tau))));
                }
                t += n * Dt;
                corner += d * s;
                for (int i = 1; i <= stop; i++) l.Add(Sample(t + i * Dt, corner));
                t += stop * Dt;
            }
            return l;
        }

        public static List<HeadPose> Concat(params List<HeadPose>[] parts)
        {
            var all = new List<HeadPose>();
            double t = 0; bool first = true;
            foreach (var part in parts)
            {
                if (part.Count == 0) continue;
                double base0 = part[0].Time;
                foreach (var s in part)
                    all.Add(new HeadPose(first ? s.Time : t + Dt + (s.Time - base0), s.Position, s.Rotation, s.Tracking));
                t = all[all.Count - 1].Time; first = false;
            }
            return all;
        }

        public static List<HeadPose> Shift(List<HeadPose> src, float dx, float dz)
        {
            var l = new List<HeadPose>(src.Count);
            foreach (var s in src) l.Add(new HeadPose(s.Time, s.Position + new float3(dx, 0f, dz), s.Rotation, s.Tracking));
            return l;
        }

        /// <summary>Step 4 of the frame loop on the kernel given, and the frame's context as the loop fills it.</summary>
        public static FrameContext StepKernel(IWalkerKernel kernel, int frame, in HeadPose s)
        {
            KernelStep step = kernel.Step(new double2(s.Position.x, s.Position.z), s.Tracking == TrackingState.Normal, s.Time);
            return new FrameContext
            {
                FrameIndex = frame, Time = s.Time, DeltaTime = (float)Dt,
                RawHeadPosition = s.Position, RawHeadRotation = s.Rotation, Tracking = s.Tracking,
                HeadPosition = s.Position, HeadRotation = s.Rotation,
                Walker = kernel.Pose, Kernel = step,
            };
        }

        /// <summary>
        /// S2 as the app runs it, without the engine: the composition root's kernel (HolonomyWalkerKernel, flat for the room
        /// as SpaceLab.SetRoom leaves it), the real registry with the dial's parameters, the real event log, and the space's
        /// logic entered on that kernel. Step is one frame: the kernel at step 4, the space at step 5.
        /// </summary>
        public sealed class Rig : IDisposable
        {
            public readonly HolonomyWalkerKernel Kernel = new HolonomyWalkerKernel();
            public readonly CurvedWalkCore S = new CurvedWalkCore();
            public readonly ParamRegistry Reg = new ParamRegistry();
            public readonly EventLog Log = new EventLog(new ManualClock(), null);
            public readonly List<EventMessage> Events = new List<EventMessage>();
            public readonly List<SpaceId> Requests = new List<SpaceId>();
            public readonly ParamEntry Q, Strong, Mode;
            public CurvedWalkInput Input;
            public int WorldBumps, FrameNo;
            public FrameContext Last;

            public Rig(RoomModel room, int q = 5, bool strong = false)
            {
                CurvedWalkCore.DeclareParams(Reg);   // SpaceRegistry.DeclareAllParams at boot
                Mode = Reg.DeclareEnum(CurvedWalkCore.ParamMode, "curved", new[] { "Mode 1 Curved Walk", "Mode 2 Aligned Cells" }, 0, null, true);
                Q = Reg.Find(CurvedWalkCore.ParamQ);
                Strong = Reg.Find(CurvedWalkCore.ParamStrong);
                Strong.Set(strong);
                Q.Set(q);
                Log.Scene = SpaceIds.Code(SpaceId.CurvedWalk);   // LinkPump names the entered space
                Log.Logged += Events.Add;
                Kernel.Configure(KernelConfig.Flat(room.LapSide));
                S.RequestSwitch = id => { Requests.Add(id); return true; };
                S.WorldChanged = () => WorldBumps++;
                S.Configure(room, Reg, Log);
                S.Enter(Kernel);
            }

            public void Step(in HeadPose s)
            {
                Last = StepKernel(Kernel, FrameNo++, s);
                S.Tick(in Last, in Input);
            }

            public void Run(List<HeadPose> samples, Action<int> after = null)
            {
                for (int i = 0; i < samples.Count; i++) { Step(samples[i]); after?.Invoke(i); }
            }

            public int Count(string ev) { int n = 0; foreach (var e in Events) if (e.Ev == ev) n++; return n; }

            public void Dispose() { Log.Logged -= Events.Add; }
        }
    }
}

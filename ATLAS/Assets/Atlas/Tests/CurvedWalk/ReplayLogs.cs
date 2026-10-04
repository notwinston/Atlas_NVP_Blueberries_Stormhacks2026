using System;
using System.Collections.Generic;
using System.IO;
using Atlas.Input;
using Atlas.Scan;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.CurvedWalk.Tests
{
    // Test helpers for P1-B09 / P1-B11 (brief §7): replay logs, generated laps, a settable action source.
    public static class ReplayLogs
    {
        public const double Dt = 1.0 / 60.0;

        public readonly struct Sample
        {
            public readonly double T; public readonly float X, Z;
            public Sample(double t, float x, float z) { T = t; X = x; Z = z; }
        }

        public static string RecordingsDir => Path.Combine(Application.dataPath, "../../recordings");

        public static List<Sample> Load(string name)
        {
            var list = new List<Sample>();
            foreach (var p in PoseLog.Read(Path.Combine(RecordingsDir, name))) list.Add(new Sample(p.Time, p.Position.x, p.Position.z));
            return list;
        }

        public static RoomModel Room(string name) => RoomModel.Load(Path.Combine(RecordingsDir, name));

        public static List<Sample> Still(double2 at, int frames, double t0 = 0)
        {
            var l = new List<Sample>();
            for (int i = 0; i < frames; i++) l.Add(new Sample(t0 + i * Dt, (float)at.x, (float)at.y));
            return l;
        }

        static void Stop(List<Sample> l, double2 p, int frames)
        {
            double t = l.Count > 0 ? l[l.Count - 1].T : 0;
            for (int i = 0; i < frames; i++) l.Add(new Sample(t + (i + 1) * Dt, (float)p.x, (float)p.y));
        }

        /// <summary>Brief §7: still 5 s at (0,0), walk to start at 0.7 m/s, stop 0.5 s.</summary>
        public static List<Sample> Prefix(double2 start, double v = 0.7, int still = 300, int stop = 30)
        {
            var l = Still(double2.zero, still);
            double len = math.length(start);
            int n = Math.Max(1, (int)Math.Round(len / v / Dt));
            double t = l[l.Count - 1].T;
            for (int i = 1; i <= n; i++)
            {
                double2 p = start * ((double)i / n);
                l.Add(new Sample(t + i * Dt, (float)p.x, (float)p.y));
            }
            Stop(l, start, stop);
            return l;
        }

        /// <summary>Brief §7: a lap of `sides` sides of length s from start, turning left; lateral sway sin(2 pi 5 tau/T) per side, zero at both ends.</summary>
        public static List<Sample> Lap(int sides, double s, double2 start, double swayM, double v = 0.7, int stop = 30, double t0 = 0)
        {
            var l = new List<Sample>();
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
                    double2 p = corner + d * (s * tau) + left * (swayM * Math.Sin(2 * Math.PI * 5 * tau));
                    l.Add(new Sample(t + i * Dt, (float)p.x, (float)p.y));
                }
                t += n * Dt;
                corner += d * s;
                for (int i = 1; i <= stop; i++) l.Add(new Sample(t + i * Dt, (float)corner.x, (float)corner.y));
                t += stop * Dt;
            }
            return l;
        }

        public static List<Sample> Concat(params List<Sample>[] parts)
        {
            var all = new List<Sample>();
            double t = 0; bool first = true;
            foreach (var part in parts)
            {
                if (part.Count == 0) continue;
                double base0 = part[0].T;
                foreach (var s in part)
                {
                    double tt = first ? s.T : t + Dt + (s.T - base0);
                    all.Add(new Sample(tt, s.X, s.Z));
                }
                t = all[all.Count - 1].T; first = false;
            }
            return all;
        }

        public static List<Sample> Shift(List<Sample> src, float dx, float dz)
        {
            var l = new List<Sample>(src.Count);
            foreach (var s in src) l.Add(new Sample(s.T, s.X + dx, s.Z + dz));
            return l;
        }

        public static FrameContext Frame(long frame, in Sample s, double dtOverride = Dt)
            => new FrameContext(frame, s.T, (float)dtOverride, new float3(s.X, 1.6f, s.Z), quaternion.identity, true, false);

        /// <summary>Test double for lane C's action map: set a trigger for the next call(s).</summary>
        public sealed class TestActions : IActionSource
        {
            public bool Prev, Next, Home;
            public Vector2 Stick => Vector2.zero;
            public bool Triggered(AtlasAction a) => a == AtlasAction.DialPrev ? Prev : a == AtlasAction.DialNext ? Next : a == AtlasAction.ReturnHome && Home;
        }

        public sealed class Events : IDisposable
        {
            public readonly List<(string scene, string ev, string data)> List = new List<(string, string, string)>();
            public Events() { EventLog.Emitted += On; }
            void On(Atlas.Link.EventMessage m) => List.Add((m.Scene, m.Ev, m.Data));
            public int Count(string ev) { int n = 0; foreach (var e in List) if (e.ev == ev) n++; return n; }
            public void Dispose() { EventLog.Emitted -= On; }
        }
    }
}

// Dependency-free tests of Atlas.Lung.Core (no test framework, like Atlas.Cases.Tests). Exit code 0 = all passed.
//
//   Atlas.Lung.Core.Tests selftest                      generated bodies only (tesseract, test spheres), no files
//   Atlas.Lung.Core.Tests case <caseDir> <twin|real>    checks against one built case package
//   Atlas.Lung.Core.Tests m2 <caseDir> <points.txt>     prints "hit best" per line of "x y z" (patient mm)
//   Atlas.Lung.Core.Tests gate <caseDir>                prints "start len itv_cm3 duty best_cm3 best_start reduction score" for the 100 arcs
//   Atlas.Lung.Core.Tests chest <dump.bin> <fps> <period_s>   largest per-frame vertex step of a blend-shape dump
//
// Lines starting with "MEASURE " carry measured values for the report: MEASURE <name> <value>.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Atlas.Cases;

namespace Atlas.Lung.Core.Tests
{
    public static class Program
    {
        static int passed, failed;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static void Check(bool ok, string name)
        {
            if (ok) passed++;
            else { failed++; Console.Error.WriteLine("FAIL " + name); }
        }

        static void Near(double got, double want, double tol, string name)
        {
            Check(Math.Abs(got - want) <= tol, name + " (got " + got.ToString("R", Inv) + ", want " + want.ToString("R", Inv) + " +/- " + tol.ToString("R", Inv) + ")");
        }

        static void Measure(string name, double value) { Console.WriteLine("MEASURE " + name + " " + value.ToString("R", Inv)); }

        static void Throws(Action a, string name)
        {
            try { a(); Check(false, name + " (no exception)"); }
            catch (Exception) { Check(true, name); }
        }

        public static int Main(string[] args)
        {
            if (args.Length == 0) { Console.Error.WriteLine("usage: selftest | case <dir> <twin|real> | m2 <dir> <points> | gate <dir> | chest <dump> <fps> <period>"); return 2; }
            switch (args[0])
            {
                case "selftest": SelfTest(); break;
                case "case": CaseTests(args[1], args[2]); break;
                case "m2": return M2Dump(args[1], args[2]);
                case "gate": return GateDump(args[1]);
                case "chest": return Chest(args[1], double.Parse(args[2], Inv), double.Parse(args[3], Inv));
                default: Console.Error.WriteLine("unknown mode " + args[0]); return 2;
            }
            Console.WriteLine(passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------------------------------

        /// <summary>Slices the whole mesh, packed, into a fresh buffer.</summary>
        static float[] SliceAll(TubeMesh mesh, in SliceFrame frame, out int vertexCount, out int tetsCut)
        {
            var pos = new float[3 * mesh.MaxSliceVertices];
            vertexCount = ReferenceSlicer4D.Slice(mesh.Vertices, mesh.Tetrahedra, 0, mesh.TetCount, mesh.TetCell, mesh.VertexData,
                                                  frame, pos, null, null, 0, false, out tetsCut);
            return pos;
        }

        /// <summary>
        /// Closed-surface check on a triangle soup, points matched bit for bit, triangles with two equal corners
        /// skipped. Undirected: the number of edges not used by exactly two triangles (0 = closed).
        /// Directed: the number of directed edges without exactly one partner in the opposite direction
        /// (0 = closed and consistently wound). Sliver triangles a rounding step wide, which appear when the cut
        /// passes within a float step of a ring, have no reliable winding, so the directed check is only used
        /// on cuts away from the rings (and on cuts exactly on a ring, where the slivers vanish).
        /// </summary>
        static int UnpairedEdges(float[] pos, int firstVertex, int vertexCount, bool directed = true)
        {
            var edges = new Dictionary<(int, int, int, int, int, int), int>();
            for (int i = firstVertex; i + 2 < firstVertex + vertexCount; i += 3)
            {
                var a = Key(pos, i); var b = Key(pos, i + 1); var c = Key(pos, i + 2);
                if (a.Equals(b) || b.Equals(c) || a.Equals(c)) continue;      // degenerate: no area, no edges
                Count(edges, a, b, directed); Count(edges, b, c, directed); Count(edges, c, a, directed);
            }
            int bad = 0;
            foreach (var pair in edges)
            {
                var k = pair.Key;
                if (!directed) { if (pair.Value != 2) bad++; continue; }
                int back;
                edges.TryGetValue((k.Item4, k.Item5, k.Item6, k.Item1, k.Item2, k.Item3), out back);
                if (pair.Value != 1 || back != 1) bad++;
            }
            return bad;
        }

        static (int, int, int) Key(float[] pos, int v)
        {
            return (BitConverter.SingleToInt32Bits(pos[3 * v] + 0f), BitConverter.SingleToInt32Bits(pos[3 * v + 1] + 0f),
                    BitConverter.SingleToInt32Bits(pos[3 * v + 2] + 0f));
        }

        static void Count(Dictionary<(int, int, int, int, int, int), int> edges, (int, int, int) a, (int, int, int) b, bool directed)
        {
            if (!directed && a.CompareTo(b) > 0) { var x = a; a = b; b = x; }
            var key = (a.Item1, a.Item2, a.Item3, b.Item1, b.Item2, b.Item3);
            int n;
            edges.TryGetValue(key, out n);
            edges[key] = n + 1;
        }

        /// <summary>Triangles with three different corners (the rest of a fixed-slot buffer is degenerate).</summary>
        static int RealTriangles(float[] pos, int firstVertex, int vertexCount)
        {
            int n = 0;
            for (int i = firstVertex; i + 2 < firstVertex + vertexCount; i += 3)
            {
                var a = Key(pos, i); var b = Key(pos, i + 1); var c = Key(pos, i + 2);
                if (!(a.Equals(b) || b.Equals(c) || a.Equals(c))) n++;
            }
            return n;
        }

        static double Percentile95(double[] samples)
        {
            var s = (double[])samples.Clone();
            Array.Sort(s);
            return s[(int)Math.Ceiling(0.95 * s.Length) - 1];
        }

        // ------------------------------------------------------------------------------------------
        // selftest: generated bodies
        // ------------------------------------------------------------------------------------------
        static void SelfTest()
        {
            Tesseract();
            ConstantSpeed();
            ScannerTiltAcceptance();
            Detents();
            Colour(PhaseColour.Default(), 1.5f, "default row");
            BlendWeights();
            GatingSynthetic();
            OccupancySynthetic();
            M3();
            SlabsAndAllocation(TubeGenerator.LujanSphere(0.3, 0.225, 4.0, 10, 1, 1.5 / 4.0), "generated tube");
        }

        // Appendix A.8: the unit tesseract cut by x + y + z + w = c is a regular tetrahedron of area 0.8660 at
        // c = 0.5 and a regular octahedron of area 6.9282 at c = 2 (to 4 decimals).
        static void Tesseract()
        {
            TubeMesh t = TubeGenerator.Tesseract();
            Check(t.TetCount == 48, "tesseract: 48 tetrahedra");
            Check(t.VertexCount == 16, "tesseract: 16 vertices");
            int n, cut;
            float[] pos = SliceAll(t, SliceFrame.InPlane(1, 1, 1, 1, 0.5), out n, out cut);
            double area = ReferenceSlicer4D.Area(pos, 0, n);
            Measure("tesseract_area_c0.5", area);
            Check(Math.Round(area, 4) == 0.8660, "A.8 tesseract: area at c = 0.5 is 0.8660 to 4 decimals (got " + area.ToString("F6", Inv) + ")");
            Check(UnpairedEdges(pos, 0, n) == 0, "tesseract c = 0.5: closed and consistently wound");
            // regular tetrahedron of edge c sqrt 2: volume e^3 / (6 sqrt 2); positive = wound outward
            Near(ReferenceSlicer4D.SignedVolume(pos, 0, n), Math.Pow(0.5 * Math.Sqrt(2), 3) / (6 * Math.Sqrt(2)), 1e-5, "tesseract c = 0.5: outward volume");

            pos = SliceAll(t, SliceFrame.InPlane(1, 1, 1, 1, 2.0), out n, out cut);
            area = ReferenceSlicer4D.Area(pos, 0, n);
            Measure("tesseract_area_c2", area);
            Check(Math.Round(area, 4) == 6.9282, "A.8 tesseract: area at c = 2 is 6.9282 to 4 decimals (got " + area.ToString("F6", Inv) + ")");
            Check(UnpairedEdges(pos, 0, n) == 0, "tesseract c = 2: closed and consistently wound");
            // regular octahedron of edge sqrt 2: volume sqrt 2 / 3 e^3 = 4/3
            Near(ReferenceSlicer4D.SignedVolume(pos, 0, n), 4.0 / 3.0, 1e-5, "tesseract c = 2: outward volume");

            // packed output never passes the capacity, and a tetrahedron is written whole or not at all
            var small = new float[3 * 7];
            int cut2;
            int wrote = ReferenceSlicer4D.Slice(t.Vertices, t.Tetrahedra, 0, t.TetCount, null, null, SliceFrame.InPlane(1, 1, 1, 1, 2.0),
                                                small, null, null, 0, false, out cut2);
            Check(wrote <= 7 && wrote % 3 == 0 && wrote > 0, "packed slice stops at the capacity");
        }

        // A.8 unit test: a sphere of diameter D moving at constant speed u appears with length D v / (v - u).
        static void ConstantSpeed()
        {
            const double D = 20.0, V = 20.0, C = 10.0, T = 8.0;
            foreach (double u in new[] { 5.0, -5.0, 0.0, 10.0 })
            {
                TubeMesh tube = TubeGenerator.ConstantSpeedSphere(D, u, T, 41, C);
                double theta = ScannerTilt.Theta(C, V);
                SliceFrame f = ScannerTilt.Frame(theta, 2, u * 4.0, C * 4.0);
                int n, cut;
                float[] pos = SliceAll(tube, f, out n, out cut);
                float lo, hi;
                Check(ReferenceSlicer4D.Extent(pos, 0, n, 2, out lo, out hi), "constant speed u = " + u + ": slice not empty");
                double want = ScannerTilt.ConstantSpeedLength(D, V, u);
                Near(hi - lo, want, 0.02 * want, "A.8 constant speed: apparent length D v / (v - u) at u = " + u);
                Check(ReferenceSlicer4D.Extent(pos, 0, n, 0, out lo, out hi), "x extent");
                Near(hi - lo, D, 0.02 * D, "constant speed: true width across the table at u = " + u);
            }
        }

        // P2-A28 acceptance. Set-up of documents/research/scripts/R2_slicer_proto.py:
        //   sphere of diameter 20 mm (icosphere, 2 subdivisions: 320 triangles), z(t) = -15 cos^4(pi t / 4 s) mm,
        //   10 phases per period joined by straight prisms, 3 periods (t = 0 .. 12 s), time scale c = 10 mm/s;
        //   table speed v = 20 mm/s; 41 scans, scan i meeting the sphere's centre z(ts) at ts = 4.0 + 0.1 i s
        //   (one whole breathing period, ts = 4 .. 8 s).
        // "Apparent length" of one scan = extent along z of that tilted slice; "spans 11.9-34.8 mm" = the
        // smallest and the largest apparent length over the 41 scans, each within 0.5 mm.
        static void ScannerTiltAcceptance()
        {
            const double D = 20.0, B = 15.0, Period = 4.0, V = 20.0, WantMin = 11.9, WantMax = 34.8, Tol = 0.5;

            // (1) the prototype's own units: millimetres, c = 10 mm/s
            double min, max;
            ScanSpan(TubeGenerator.LujanSphere(D, B, Period, 10, 3, 10.0), 10.0, V, 1.0, B, Period, false, out min, out max);
            Measure("scanner_apparent_min_mm", min);
            Measure("scanner_apparent_max_mm", max);
            Near(min, WantMin, Tol, "P2-A28: smallest apparent length at v = 20 mm/s");
            Near(max, WantMax, Tol, "P2-A28: largest apparent length at v = 20 mm/s");

            // (2) the same sphere in the display units of a case package: M = 15, Lambda = 1.5 m per breath
            const double M = 15.0, Lambda = 1.5, Units = M / 1000.0;
            double c = ScannerTilt.TimeScale(Lambda, Period);
            double minD, maxD;
            ScanSpan(TubeGenerator.LujanSphere(D * Units, B * Units, Period, 10, 3, c, 2, Units), c, V * Units, Units, B * Units, Period, false, out minD, out maxD);
            Measure("scanner_apparent_min_mm_display_units", minD);
            Measure("scanner_apparent_max_mm_display_units", maxD);
            Near(minD, WantMin, Tol, "P2-A28 in display units: smallest apparent length");
            Near(maxD, WantMax, Tol, "P2-A28 in display units: largest apparent length");

            // (3) one stored period, as a case tube is, sliced as periodic copies
            double minP, maxP;
            ScanSpan(TubeGenerator.LujanSphere(D * Units, B * Units, Period, 10, 1, c, 2, Units), c, V * Units, Units, B * Units, Period, true, out minP, out maxP);
            Measure("scanner_apparent_min_mm_periodic_copies", minP);
            Measure("scanner_apparent_max_mm_periodic_copies", maxP);
            Near(minP, minD, 0.01, "one period sliced as copies equals three stored periods (min)");
            Near(maxP, maxD, 0.01, "one period sliced as copies equals three stored periods (max)");
            Near(minP, WantMin, Tol, "P2-A28 with periodic copies: smallest apparent length");
            Near(maxP, WantMax, Tol, "P2-A28 with periodic copies: largest apparent length");

            // never more than D + b (A.8)
            Check(max <= D + B + 1e-3, "apparent length never exceeds D + b");
        }

        /// <summary>Smallest and largest apparent length (mm) over 41 scans meeting the sphere's centre at ts = 4.0 .. 8.0 s.</summary>
        static void ScanSpan(TubeMesh tube, double c, double v, double unitsPerMm, double b, double period, bool copies, out double min, out double max)
        {
            double theta = ScannerTilt.Theta(c, v);
            var pos = new float[3 * tube.MaxSliceVertices];
            min = double.MaxValue; max = 0.0;
            for (int i = 0; i <= 40; i++)
            {
                double ts = 4.0 + 0.1 * i;
                SliceFrame f = ScannerTilt.Frame(theta, 2, TubeGenerator.LujanZ(ts, b, period), c * ts);
                float lo = float.MaxValue, hi = float.MinValue;
                bool any = false;
                for (int j = copies ? 0 : 0; j <= (copies ? 2 : 0); j++)
                {
                    SliceFrame fj = copies ? ScannerTilt.ForCopy(f, j, tube.WPeriod) : f;
                    int cut;
                    int n = ReferenceSlicer4D.Slice(tube.Vertices, tube.Tetrahedra, 0, tube.TetCount, null, null, fj, pos, null, null, 0, false, out cut);
                    float a, z;
                    if (ReferenceSlicer4D.Extent(pos, 0, n, 2, out a, out z))
                    {
                        any = true;
                        if (a < lo) lo = a;
                        if (z > hi) hi = z;
                    }
                }
                Check(any, "scan " + i + ": slice not empty");
                double len = ScannerTilt.ApparentLengthMm(lo, hi, unitsPerMm);
                if (len < min) min = len;
                if (len > max) max = len;
            }
        }

        static void Detents()
        {
            Check(ScannerTilt.DetentCount == 5 && ScannerTilt.DefaultDetentSpeedsMmPerS.Length == 5, "5 detents");
            Check(ScannerTilt.Theta(10.0, double.PositiveInfinity) == 0.0, "detent 0 is the snapshot, theta = 0");
            Near(Math.Tan(ScannerTilt.Theta(10.0, 20.0)), 0.5, 1e-12, "tan theta = c / v");
            Check(double.IsPositiveInfinity(ScannerTilt.Speed(10.0, 0.0)), "v read-out at theta = 0 is infinite");
            bool has20 = false;
            for (int d = 0; d < 5; d++)
            {
                double v = ScannerTilt.DefaultDetentSpeedsMmPerS[d];
                if (v == 20.0) has20 = true;
                double theta = ScannerTilt.DetentTheta(d, ScannerTilt.DefaultDetentSpeedsMmPerS, 1.5, 4.0, 0.015);
                double back = ScannerTilt.Speed(ScannerTilt.TimeScale(1.5, 4.0), theta) / 0.015;
                if (d == 0) Check(theta == 0.0 && double.IsPositiveInfinity(back), "detent 0 read-out");
                else Near(back, v, 1e-9 * v, "v read-out of detent " + d + " in mm/s");
                if (d > 0) Check(theta > ScannerTilt.DetentTheta(d - 1, ScannerTilt.DefaultDetentSpeedsMmPerS, 1.5, 4.0, 0.015), "tilt grows with the detent");
                Check(ScannerTilt.DetentFromDial(d / 4.0) == d, "dial position " + d + "/4 is detent " + d);
            }
            Check(has20, "20 mm/s is a detent");
            Check(ScannerTilt.DetentFromDial(-1.0) == 0 && ScannerTilt.DetentFromDial(2.0) == 4, "dial clamps");
            Check(ScannerTilt.DetentFromDial(0.37) == 1 && ScannerTilt.DetentFromDial(0.38) == 2, "dial snaps to the nearest detent");
            Check(ScannerTilt.StepDetent(4, 1) == 4 && ScannerTilt.StepDetent(0, -1) == 0 && ScannerTilt.StepDetent(2, 1) == 3, "detent steps clamp");
            // the frame at theta = 0 is the snapshot w = wAt, normal +w
            SliceFrame f = ScannerTilt.Frame(0.0, 2, 0.123, 0.45);
            Check(f.Nx == 0f && f.Ny == 0f && f.Nz == 0f && f.Nw == 1f && f.Offset == 0.45f, "theta = 0 frame is the snapshot");
            Check(f.Same(SliceFrame.AtW(0.45f)), "theta = 0 frame equals AtW");
        }

        // P2-A22 acceptance: colour at tau = 0 and tau = Lambda identical (delta RGB = 0).
        static void Colour(PhaseColour row, float lambda, string what)
        {
            byte r0, g0, b0, r1, g1, b1;
            row.SampleNearest(0.0, lambda, out r0, out g0, out b0);
            row.SampleNearest(lambda, lambda, out r1, out g1, out b1);
            int dNearest = Math.Abs(r0 - r1) + Math.Abs(g0 - g1) + Math.Abs(b0 - b1);
            float fr0, fg0, fb0, fr1, fg1, fb1;
            row.SampleLinear(0.0, lambda, out fr0, out fg0, out fb0);
            row.SampleLinear(lambda, lambda, out fr1, out fg1, out fb1);
            float dLinear = Math.Abs(fr0 - fr1) + Math.Abs(fg0 - fg1) + Math.Abs(fb0 - fb1);
            Measure("phase_colour_delta_rgb_" + what.Replace(' ', '_'), dNearest + dLinear);
            Check(dNearest == 0, "P2-A22 " + what + ": colour at tau = 0 and tau = Lambda identical, nearest texel (delta RGB = " + dNearest + ")");
            Check(dLinear == 0f, "P2-A22 " + what + ": colour at tau = 0 and tau = Lambda identical, linear (delta RGB = " + dLinear + ")");
            Check(PhaseColour.U(0.0, lambda) == 0.0 && PhaseColour.U(lambda, lambda) == 0.0, what + ": u = 0 at tau = 0 and at tau = Lambda");
            for (int k = -3; k <= 3; k++)
            {
                row.SampleLinear(k * (double)lambda, lambda, out fr1, out fg1, out fb1);
                Check(fr1 == fr0 && fg1 == fg0 && fb1 == fb0, what + ": colour at tau = " + k + " Lambda equals colour at 0");
            }
            Check(PhaseColour.U(-1e-20, 1.5) == 0.0, "u stays below 1 for a tiny negative tau");
            Near(PhaseColour.U(0.375, 1.5), 0.25, 1e-15, "u = frac(tau / Lambda)");
            Near(PhaseColour.U(1.875, 1.5), 0.25, 1e-12, "u wraps");
            // cyclic: no jump across the seam. The step from just below Lambda to Lambda is no larger than a texel step.
            float worstSeam = 0f, worstInside = 0f;
            for (int i = 0; i < row.Texels; i++)
            {
                int a = 3 * i, c = 3 * ((i + 1) % row.Texels);
                float step = Math.Abs(row.Row[a] - row.Row[c]) + Math.Abs(row.Row[a + 1] - row.Row[c + 1]) + Math.Abs(row.Row[a + 2] - row.Row[c + 2]);
                if (i == row.Texels - 1) worstSeam = step; else if (step > worstInside) worstInside = step;
            }
            Check(worstSeam <= worstInside, what + ": the seam between the last texel and texel 0 is no larger than any other step");
            row.SampleLinear(lambda * (1.0 - 1e-7), lambda, out fr1, out fg1, out fb1);
            Check(Math.Abs(fr1 - fr0) + Math.Abs(fg1 - fg0) + Math.Abs(fb1 - fb0) < 0.01f, what + ": continuous just below tau = Lambda");
            // the two halves of the breath differ (lightness varied), and inhale is cooler than exhale
            row.SampleLinear(0.5 * lambda, lambda, out fr1, out fg1, out fb1);
            Check(fb0 > fr0 && fr1 > fb1, what + ": phase 0 cool, phase 50 % warm");
            float ar, ag, ab, br, bg, bb;
            row.SampleLinear(0.25 * lambda, lambda, out ar, out ag, out ab);
            row.SampleLinear(0.75 * lambda, lambda, out br, out bg, out bb);
            Check(Math.Abs((ar + ag + ab) - (br + bg + bb)) > 20f, what + ": 25 % and 75 % differ in lightness");
        }

        // P2-A24: weights of 10 phases from a continuous phase, wrap 9 -> 0.
        static void BlendWeights()
        {
            var w = new float[10];
            var w2 = new float[10];
            for (int i = 0; i <= 1000; i++)
            {
                double phase = i / 100.0;       // 0 .. 10
                BlendShapePlayer.Weights(phase, w);
                double sum = 0.0; int nonZero = 0; bool neighbours = true; bool range = true;
                int first = -1;
                for (int k = 0; k < 10; k++)
                {
                    sum += w[k];
                    if (w[k] < 0f || w[k] > 1f) range = false;
                    if (w[k] != 0f) { nonZero++; if (first < 0) first = k; }
                }
                int kk = (int)Math.Floor(phase) % 10;
                if (nonZero == 2) neighbours = w[kk] != 0f && w[(kk + 1) % 10] != 0f;
                if (Math.Abs(sum - 1.0) > 1e-6 || nonZero < 1 || nonZero > 2 || !neighbours || !range)
                {
                    Check(false, "weights at phase " + phase + ": sum " + sum + ", non-zero " + nonZero);
                    return;
                }
                bool whole = i % 100 == 0;
                if (whole && !(nonZero == 1 && w[kk] == 1f)) { Check(false, "whole phase " + phase + " is one shape at weight 1"); return; }
                if (!whole && nonZero != 2) { Check(false, "between phases two neighbours are non-zero, phase " + phase); return; }
            }
            Check(true, "weights sum to 1, lie in [0, 1], and two neighbours are non-zero between phases (1,001 phases)");

            BlendShapePlayer.Weights(9.5, w);
            Check(w[9] == 0.5f && w[0] == 0.5f, "phase 9.5 blends phase 9 and phase 0");
            BlendShapePlayer.Weights(10.0, w); BlendShapePlayer.Weights(0.0, w2);
            bool same = true;
            for (int k = 0; k < 10; k++) same &= w[k] == w2[k];
            Check(same && w[0] == 1f, "phase 10 equals phase 0");
            BlendShapePlayer.Weights(-0.25, w);
            Check(w[9] == 0.25f && w[0] == 0.75f, "negative phases wrap (-0.25 is 9.75)");
            // continuity at the wrap: the weights just below 10 approach those at 0
            BlendShapePlayer.Weights(10.0 - 1e-6, w);
            double jump = 0.0;
            for (int k = 0; k < 10; k++) jump = Math.Max(jump, Math.Abs(w[k] - w2[k]));
            Check(jump <= 2e-6, "weights are continuous across the wrap 9 -> 0 (largest change " + jump + ")");
            // and everywhere: a phase step d changes no weight by more than d
            double worst = 0.0;
            for (int i = 0; i < 4000; i++)
            {
                BlendShapePlayer.Weights(i / 400.0, w); BlendShapePlayer.Weights((i + 1) / 400.0, w2);
                for (int k = 0; k < 10; k++) worst = Math.Max(worst, Math.Abs(w[k] - w2[k]));
            }
            Check(worst <= 1.0 / 400.0 + 1e-6, "a phase step of 1/400 changes no weight by more than 1/400");

            Near(BlendShapePlayer.PhaseAt(1.0, 4.0), 2.5, 1e-12, "phase at 1 s of a 4 s breath");
            Near(BlendShapePlayer.PhaseAt(4.0, 4.0), 0.0, 1e-12, "phase wraps at the period");
            Near(BlendShapePlayer.PhaseAt(9.0, 4.0), 2.5, 1e-12, "phase at 9 s");

            // vertex step: two vertices, shape k moves vertex 0 by k mm along x; shape 9 also moves vertex 1 by 30 mm
            var targets = new float[10][];
            for (int k = 0; k < 10; k++) targets[k] = new float[] { 0.001f * k, 0f, 0f, 0f, 0f, 0f };
            targets[9][4] = 0.030f;
            BlendShapePlayer.Weights(3.0, w); BlendShapePlayer.Weights(3.5, w2);
            Near(BlendShapePlayer.MaxVertexStep(targets, w, w2), 0.0005, 1e-9, "vertex step inside one interval");
            BlendShapePlayer.Weights(9.0, w); BlendShapePlayer.Weights(0.0, w2);
            Near(BlendShapePlayer.MaxVertexStep(targets, w, w2), 0.030, 1e-9, "vertex step across the wrap");
            double atWrap; int frame;
            double max = BlendShapePlayer.MaxStepOverBreath(targets, 60.0, 4.0, w, w2, out atWrap, out frame);
            // 24 frames per phase interval; the worst interval is 9 -> 0, where vertex 1 travels 30 mm: 30 / 24 mm per frame
            Near(max, 0.030 / 24.0, 1e-7, "largest step per frame over a breath at 60 fps");
            Near(atWrap, max, 1e-12, "the worst step is found at the wrap");
            Check(frame >= 216, "worst frame lies in the last phase interval");
        }

        static CaseTable SyntheticTable()
        {
            var t = new CaseTable();
            t.Id = "synthetic"; t.Substeps = 0;
            t.GtvCm3 = new double[10];
            t.GatingCm3 = new double[10][];
            for (int s = 0; s < 10; s++)
            {
                t.GatingCm3[s] = new double[10];
                for (int l = 0; l < 10; l++) t.GatingCm3[s][l] = l == 9 ? 20.0 : 1.0 + (l + 1) + 0.1 * ((s + 7) % 10);
                t.GtvCm3[s] = t.GatingCm3[s][0];
            }
            t.ItvCm3 = 20.0; t.CoreCm3 = 0.5;
            t.CentroidMm = new double[10][];
            for (int s = 0; s < 10; s++) t.CentroidMm[s] = new double[3];
            t.CentroidRangeMm = new double[3]; t.ArgmaxRhoMm = new double[3]; t.OriginMm = new double[] { 10, 20, 30 };
            t.MaxRho = 0.8;
            t.OccDims = new long[] { 4, 5, 6 };
            t.OccBboxMinMm = new double[] { -4, -5, -6 }; t.OccBboxMaxMm = new double[] { 4, 5, 6 };
            t.Validate();
            return t;
        }

        // P2-A27: volume = table entry, duty = length / 10, best arc of equal duty, ITV reduction; M4 payload.
        static void GatingSynthetic()
        {
            CaseTable t = SyntheticTable();
            Check(Gating.VolumeCm3(t, 3, 5) == t.GatingCm3[3][4], "gated volume is gating_cm3[start][length - 1]");
            Check(Gating.Duty(4) == 0.4 && Gating.Duty(10) == 1.0 && Gating.Duty(1) == 0.1, "duty = arc length / 10");
            int bs; double bv;
            Gating.BestArc(t, 5, out bs, out bv);
            Check(bs == 3 && bv == 6.0, "best arc of equal duty: smallest volume");     // (s + 7) % 10 == 0 at s = 3
            Near(Gating.ItvReduction(t, 3, 5), 1.0 - 6.0 / 20.0, 1e-15, "ITV reduction = 1 - V / ITV");
            double score;
            M4Gate e = LungScoring.M4(t, 5, 5, out score);     // (5 + 7) % 10 = 2 -> 6.2
            Check(e.Start == 5 && e.Len == 5 && e.ItvCm3 == 6.2 && e.Duty == 0.5 && e.BestCm3 == 6.0, "m4_gate payload {start, len, itv_cm3, duty, best_cm3}");
            Near(score, (20.0 - 6.2) / (20.0 - 6.0), 1e-15, "M4 score = reduction of the arc / reduction of the best arc");
            LungScoring.M4(t, 3, 5, out score);
            Check(score == 1.0, "M4 score is 1 for the best arc");
            LungScoring.M4(t, 0, 10, out score);
            Check(score == 1.0, "M4 score is 1 when no arc of that duty reduces the ITV");
            Throws(() => Gating.VolumeCm3(t, 0, 0), "arc length 0 rejected");
            Throws(() => Gating.VolumeCm3(t, 10, 1), "start bin 10 rejected");
            Throws(() => Gating.VolumeCm3(t, 0, 11), "arc length 11 rejected");
            Check(Gating.Contains(8, 4, 8) && Gating.Contains(8, 4, 9) && Gating.Contains(8, 4, 0) && Gating.Contains(8, 4, 1)
                  && !Gating.Contains(8, 4, 2) && !Gating.Contains(8, 4, 7), "arc membership wraps 9 -> 0");
            int start, len;
            Gating.ArcFromFlags(0.45, 1.05, 1.5, out start, out len);       // bins 3 and 7: the 30-70 % gate
            Check(start == 3 && len == 5, "flags at 0.45 m and 1.05 m of a 1.5 m lane give the arc (3, 5)");
            Gating.ArcFromFlags(1.30, 0.20, 1.5, out start, out len);       // bins 8 and 1
            Check(start == 8 && len == 4, "an arc across the wrap");
            Gating.ArcFromFlags(0.31, 0.44, 1.5, out start, out len);
            Check(start == 2 && len == 1, "two flags in one bin give length 1");
            Gating.ArcFromFlags(1.5, 3.0 + 0.01, 1.5, out start, out len);
            Check(start == 0 && len == 1, "flags on later copies of the lane wrap");
        }

        // P2-A29 M2: trilinear sample of the occupancy grid.
        static void OccupancySynthetic()
        {
            CaseTable t = SyntheticTable();
            int nx = 4, ny = 5, nz = 6;
            var vox = new byte[nx * ny * nz];
            for (int z = 0; z < nz; z++) for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++)
                vox[x + nx * (y + ny * z)] = (byte)(10 * x + 20 * y + 5 * z);      // linear: trilinear reproduces it exactly
            var g = new OccupancyGrid(vox, t);
            // voxel (ix, iy, iz) is centred at min + (i + 0.5) * size / n ; voxel size 2 mm on every axis here
            Near(g.Sample(-4 + 2 * 1.5, -5 + 2 * 2.5, -6 + 2 * 3.5), (10 * 1 + 20 * 2 + 5 * 3) / 255.0, 1e-12, "sample at a voxel centre is the stored value");
            Near(g.Sample(-4 + 2 * 2.0, -5 + 2 * 2.75, -6 + 2 * 4.1), (10 * 1.5 + 20 * 2.25 + 5 * 3.6) / 255.0, 1e-12, "trilinear between centres");
            Near(g.Sample(-4.0, -5.0, -6.0), 0.0, 1e-12, "clamped to the first centre at the box corner");
            Near(g.Sample(4.0, 5.0, 6.0), (10 * 3 + 20 * 4 + 5 * 5) / 255.0, 1e-12, "clamped to the last centre at the far corner");
            Check(g.Sample(4.001, 0, 0) == 0.0 && g.Sample(0, -5.001, 0) == 0.0, "0 outside the box");
            Near(g.MaxRho(), (10 * 3 + 20 * 4 + 5 * 5) / 255.0, 1e-12, "largest stored rho");
            M2Place e = LungScoring.M2(g, t, -1.0, -0.5, 0.25);
            Check(e.PxMm == -1.0 && e.PyMm == -0.5 && e.PzMm == 0.25 && e.Best == 0.8 && e.Hit == g.Sample(-1.0, -0.5, 0.25), "m2_place payload {p_mm, hit, best}");
            // display frame: p_mm = p * 1000 / M + origin_mm
            M2Place d = LungScoring.M2FromDisplay(g, t, 15.0, (-1.0 - 10) * 0.015, (-0.5 - 20) * 0.015, (0.25 - 30) * 0.015);
            Near(d.PxMm, -1.0, 1e-9, "display to patient x"); Near(d.PyMm, -0.5, 1e-9, "display to patient y"); Near(d.PzMm, 0.25, 1e-9, "display to patient z");
            Near(d.Hit, e.Hit, 1e-9, "same hit from the display frame");
            Throws(() => new OccupancyGrid(new byte[7], t), "wrong occ.r8 size rejected");
        }

        static void M3()
        {
            M3Pick e = LungScoring.M3(1, 1, false);
            Check(e.Choice == 1 && e.Correct && e.When == "before", "m3_pick payload, correct, before");
            e = LungScoring.M3(2, 1, true);
            Check(e.Choice == 2 && !e.Correct && e.When == "after", "m3_pick payload, wrong, after");
            Throws(() => LungScoring.M3(3, 1, true), "choice 3 rejected");
            Check(LungEvents.Scene == "lung" && LungEvents.M2Place == "m2_place" && LungEvents.M4Gate == "m4_gate" && LungEvents.M3Pick == "m3_pick", "event names of 10.2.6");
        }

        // Slab index, fixed slots, dirty flag, allocation.
        static void SlabsAndAllocation(TubeMesh tube, string what)
        {
            var state = new TubeSliceState(tube);
            int slabs = tube.CellCount;
            bool one = true, closed = true, wound = true, dataOk = true;
            for (int i = 0; i < 10 * slabs; i++)
            {
                double phase = i / 10.0;
                float w = tube.PhaseW(phase);
                if (!state.Update(SliceFrame.AtW(w))) Check(false, what + ": frame " + i + " re-sliced");
                if (state.SlabsTouched != 1 || state.TetsVisited * slabs != tube.TetCount) one = false;
                if (state.TriangleCount == 0 || UnpairedEdges(state.Positions, 0, tube.MaxSliceVertices, false) != 0) closed = false;
                if (i % 10 != 0 && UnpairedEdges(state.Positions, 0, tube.MaxSliceVertices, true) != 0) wound = false;
                // the interpolated vertex channel is the phase of the cut
                for (int v = 0; v < tube.MaxSliceVertices; v += 3)
                {
                    bool real = state.Positions[3 * v] != state.Positions[3 * v + 3] || state.Positions[3 * v + 1] != state.Positions[3 * v + 4] || state.Positions[3 * v + 2] != state.Positions[3 * v + 5];
                    if (real && Math.Abs(state.Data[v] - w / tube.WPeriod) > 1e-5) dataOk = false;
                }
            }
            Check(one, what + ": a pure phase slice touches one slab in " + slabs + " (" + tube.TetCount / slabs + " of " + tube.TetCount + " tetrahedra), at 100 phases");
            Check(closed, what + ": every phase slice is a closed surface (fixed slots, old slabs cleared)");
            Check(wound, what + ": every phase slice between the rings is consistently wound");
            Check(dataOk, what + ": the slice's vertex channel equals tau / Lambda of the cut");

            // unchanged hyperplane: no work
            SliceFrame f = SliceFrame.AtW(tube.PhaseW(3.3));
            state.Update(f);
            Check(!state.Update(f) && state.TetsVisited == 0 && state.DirtyEnd == 0, what + ": an unchanged hyperplane does no work");

            // fixed slots agree with the packed slice of the whole mesh
            int n, cut;
            float[] packed = SliceAll(tube, f, out n, out cut);
            Near(ReferenceSlicer4D.Area(state.Positions, 0, tube.MaxSliceVertices), ReferenceSlicer4D.Area(packed, 0, n), 1e-9, what + ": fixed slots and packed output have the same area");
            Check(state.TriangleCount * 3 == n && state.TetsCut == cut, what + ": fixed slots and packed output have the same triangles");
            Check(cut <= tube.TetCount / slabs, what + ": only tetrahedra of one slab are cut");

            // 1,000 moving-slice frames: no allocation; time reported (desktop CPU, not the phone: P2-A21 needs the phone)
            for (int i = 0; i < 200; i++) state.Update(SliceFrame.AtW(tube.PhaseW(i * 0.05)));      // warm up the JIT
            var ms = new double[1000];
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                state.Update(SliceFrame.AtW(tube.PhaseW(i * 0.01 + 0.005)));
                ms[i] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0, what + ": 0 bytes allocated over 1,000 moving-slice frames (got " + allocated + ")");
            Measure("slice_ms_p95_desktop_" + what.Replace(' ', '_'), Percentile95(ms));
            Measure("slice_alloc_bytes_1000_frames_" + what.Replace(' ', '_'), allocated);
        }

        // ------------------------------------------------------------------------------------------
        // case package checks
        // ------------------------------------------------------------------------------------------
        static void CaseTests(string dir, string kind)
        {
            bool twin = kind == "twin";
            CaseTable table = CaseTable.Read(File.ReadAllBytes(Path.Combine(dir, "table.json")));
            string id = table.Id;
            foreach (string name in new[] { "tube4d.bin", "tube4d_rigid.bin" })
            {
                Tube4D raw = Tube4D.Read(File.ReadAllBytes(Path.Combine(dir, name)));
                TubeMesh tube = TubeLoader.Load(raw);
                string what = id + " " + name;
                string tag = id + "_" + name.Replace(".bin", "");

                // ---- layout: what a Mesh4D needs
                Check(tube.VertexCount == raw.VertexCount && tube.TetCount == raw.TetCount, what + ": counts kept");
                Check(tube.TetCount == 18000, what + ": 18,000 tetrahedra per period (10.2.5)");
                Check(tube.CellCount == 10 && tube.SlabStart.Length == 11, what + ": 10 slabs");
                bool even = true, cellOk = true;
                for (int k = 0; k < 10; k++) even &= tube.SlabStart[k + 1] - tube.SlabStart[k] == 1800;
                for (int t = 0; t < tube.TetCount; t++) cellOk &= tube.TetCell[t] == raw.Slab[t];
                Check(even, what + ": 1,800 tetrahedra per slab");
                Check(cellOk, what + ": TetCell is the file's slab id, order unchanged");
                Check(tube.WPeriod == raw.Lambda && tube.WPeriod == 1.5f && tube.M == 15f && tube.UnitsPerMm == 0.015f, what + ": Lambda 1.5 m, M 15");
                bool same = true, data = true;
                for (int i = 0; i < raw.P.Length; i++) same &= tube.Vertices[i] == raw.P[i];
                for (int i = 0; i < tube.VertexCount; i++) data &= tube.VertexData[i] == (float)((double)raw.P[4 * i + 3] / raw.Lambda);
                Check(same, what + ": vertices are the file's P, bit for bit");
                Check(data, what + ": vertex data = tau / Lambda");
                // orientation: same tetrahedra as the file (as sets), all normals outward
                bool sets = true;
                for (int t = 0; t < tube.TetCount && sets; t++)
                {
                    var a = new int[4]; var b = new int[4];
                    Array.Copy(raw.T, 4 * t, a, 0, 4); Array.Copy(tube.Tetrahedra, 4 * t, b, 0, 4);
                    Array.Sort(a); Array.Sort(b);
                    for (int k = 0; k < 4; k++) sets &= a[k] == b[k];
                }
                Check(sets, what + ": orientation only reorders the four indices of a tetrahedron");
                Check(Orientation.OrientOutward((float[])tube.Vertices.Clone(), (int[])tube.Tetrahedra.Clone()) == 0, what + ": orienting twice changes nothing");

                // ---- phase slices at theta = 0
                var state = new TubeSliceState(tube);
                double unit = tube.UnitsPerMm;
                double sphere = 4.0 * Math.PI * 5.0 * 5.0;      // 4 pi R^2, R = 5 mm (twin)
                double worstArea = 0.0, worstVol = 0.0;
                bool one = true, closed = true, wound = true, outward = true;
                for (int i = 0; i < 100; i++)
                {
                    double phase = i / 10.0;
                    state.Update(SliceFrame.AtW(tube.PhaseW(phase)));
                    if (state.SlabsTouched != 1 || state.TetsVisited != 1800) one = false;
                    if (UnpairedEdges(state.Positions, 0, tube.MaxSliceVertices, false) != 0) closed = false;
                    if (UnpairedEdges(state.Positions, 0, tube.MaxSliceVertices, true) != 0) wound = false;
                    double areaMm2 = ReferenceSlicer4D.Area(state.Positions, 0, tube.MaxSliceVertices) / (unit * unit);
                    double volCm3 = ReferenceSlicer4D.SignedVolume(state.Positions, 0, tube.MaxSliceVertices) / (unit * unit * unit) / 1000.0;
                    if (!(volCm3 > 0.0)) outward = false;
                    if (i % 10 == 0)
                    {
                        int k = i / 10;
                        if (name == "tube4d.bin") worstVol = Math.Max(worstVol, Math.Abs(volCm3 / table.GtvCm3[k] - 1.0));
                        if (k == 0)
                        {
                            Measure(tag + "_slice_phase0_area_mm2", areaMm2);
                            Measure(tag + "_slice_phase0_volume_cm3", volCm3);
                            int real0 = RealTriangles(state.Positions, 0, tube.MaxSliceVertices);
                            Measure(tag + "_slice_phase0_triangles", real0);
                            Check(real0 == 600, what + ": the phase-0 slice is the 600-triangle template");
                            if (twin)
                            {
                                Measure(tag + "_slice_phase0_area_over_4piR2", areaMm2 / sphere);
                                Near(areaMm2, sphere, 0.02 * sphere, "P2-A20 " + what + ": slice at theta = 0, phase 0, area within 2 % of 4 pi R^2 (mm2)");
                            }
                        }
                    }
                    if (twin) worstArea = Math.Max(worstArea, Math.Abs(areaMm2 / sphere - 1.0));
                }
                Check(one, "P2-A20 " + what + ": a pure phase slice touches one slab in ten (1,800 of 18,000 tetrahedra), at 100 phases");
                Check(closed, what + ": every phase slice is a closed surface");
                Check(wound, what + ": every phase slice is consistently wound (also exactly on the rings)");
                Check(outward, what + ": every phase slice is wound outward (positive volume)");
                if (twin)
                {
                    Measure(tag + "_slice_area_worst_rel_dev_100_phases", worstArea);
                    Check(worstArea <= 0.02, "P2-A20 " + what + ": area within 2 % of 4 pi R^2 at all 100 phase positions (worst " + worstArea.ToString("F5", Inv) + ")");
                }
                if (name == "tube4d.bin") Measure(tag + "_slice_volume_vs_gtv_worst_rel_dev", worstVol);

                // ---- the slice at phase k equals ring k of the file (the contract's phase mesh), bit for bit
                {
                    state.Update(SliceFrame.AtW(tube.PhaseW(4.0)));
                    int nv = raw.VertexCount / 11;
                    var ring = new HashSet<(int, int, int)>();
                    for (int i = 0; i < nv; i++)
                        ring.Add((BitConverter.SingleToInt32Bits(raw.P[4 * (4 * nv + i)] + 0f), BitConverter.SingleToInt32Bits(raw.P[4 * (4 * nv + i) + 1] + 0f), BitConverter.SingleToInt32Bits(raw.P[4 * (4 * nv + i) + 2] + 0f)));
                    bool onRing = true; int real = 0;
                    for (int v = 0; v < tube.MaxSliceVertices; v += 3)
                    {
                        var a = Key(state.Positions, v); var b = Key(state.Positions, v + 1); var c = Key(state.Positions, v + 2);
                        if (a.Equals(b) || b.Equals(c) || a.Equals(c)) continue;
                        real++;
                        onRing &= ring.Contains(a) && ring.Contains(b) && ring.Contains(c);
                    }
                    Check(onRing && real == 600, what + ": the slice at phase 4 is ring 4 of the file, 600 triangles, bit for bit");
                }

                // ---- phase colour on the real Lambda and on the slice's own channel
                Colour(PhaseColour.Default(), tube.WPeriod, what);
                {
                    PhaseColour row = PhaseColour.Default();
                    state.Update(SliceFrame.AtW(tube.PhaseW(0.0)));     // the cut at ring 10: the channel reads 1.0
                    byte r0, g0, b0; row.SampleNearest(0.0, tube.WPeriod, out r0, out g0, out b0);
                    int worst = 0;
                    for (int v = 0; v < tube.MaxSliceVertices; v++)
                    {
                        byte r, g, b; row.SampleNearest(state.Data[v], 1.0, out r, out g, out b);
                        if (state.Positions[3 * v] == 0f && state.Positions[3 * v + 1] == 0f && state.Positions[3 * v + 2] == 0f && state.Data[v] == 0f) continue;
                        worst = Math.Max(worst, Math.Abs(r - r0) + Math.Abs(g - g0) + Math.Abs(b - b0));
                    }
                    Check(worst == 0, "P2-A22 " + what + ": the phase-0 slice (cut at tau = Lambda) has the colour of tau = 0 at every vertex (delta RGB = " + worst + ")");
                }

                // ---- scanner tilt on the package tube: reported, not an acceptance number
                {
                    double c = ScannerTilt.TimeScale(tube.WPeriod, 4.0);
                    double theta = ScannerTilt.DetentTheta(3, ScannerTilt.DefaultDetentSpeedsMmPerS, tube.WPeriod, 4.0, unit);
                    var pos = new float[3 * tube.MaxSliceVertices];
                    double min = double.MaxValue, max = 0.0;
                    float zlo, zhi;
                    state.Update(SliceFrame.AtW(tube.PhaseW(0.0)));
                    ReferenceSlicer4D.Extent(state.Positions, 0, tube.MaxSliceVertices, 2, out zlo, out zhi);
                    double trueLen = (zhi - zlo) / unit;
                    for (int i = 0; i < 40; i++)
                    {
                        // meet the lesion's centroid height of phase bin k at that phase
                        double phase = i / 4.0;
                        int k = (int)phase % 10;
                        double frac = phase - Math.Floor(phase);
                        double zc = ((1 - frac) * table.CentroidMm[k][2] + frac * table.CentroidMm[(k + 1) % 10][2] - table.OriginMm[2]) * unit;
                        SliceFrame f = ScannerTilt.Frame(theta, 2, zc, tube.WPeriod * (1.0 + phase / 10.0));
                        float lo = float.MaxValue, hi = float.MinValue; bool any = false;
                        for (int j = -1; j <= 3; j++)
                        {
                            int cut;
                            int n = ReferenceSlicer4D.Slice(tube.Vertices, tube.Tetrahedra, 0, tube.TetCount, null, null, ScannerTilt.ForCopy(f, j, tube.WPeriod), pos, null, null, 0, false, out cut);
                            float a, z;
                            if (ReferenceSlicer4D.Extent(pos, 0, n, 2, out a, out z)) { any = true; lo = Math.Min(lo, a); hi = Math.Max(hi, z); }
                        }
                        if (!any) continue;
                        double len = (hi - lo) / unit;
                        min = Math.Min(min, len); max = Math.Max(max, len);
                    }
                    Check(max > 0.0, what + ": tilted slices at v = 20 mm/s are not empty");
                    Measure(tag + "_si_length_phase0_mm", trueLen);
                    Measure(tag + "_scan_v20_apparent_min_mm", min);
                    Measure(tag + "_scan_v20_apparent_max_mm", max);
                }

                if (name == "tube4d.bin") SlabsAndAllocation(tube, what);
            }

            // ---- P2-A27 gating and M4
            bool exact = true, duty = true, best = true, scoreOk = true;
            double worstRel = 0.0;
            for (int s = 0; s < 10; s++)
            {
                for (int len = 1; len <= 10; len++)
                {
                    double v = Gating.VolumeCm3(table, s, len);
                    double want = table.GatingCm3[s][len - 1];
                    worstRel = Math.Max(worstRel, Math.Abs(v / want - 1.0));
                    if (v != want) exact = false;
                    if (Gating.Duty(len) != len / 10.0) duty = false;
                    int bs; double bv;
                    Gating.BestArc(table, len, out bs, out bv);
                    for (int s2 = 0; s2 < 10; s2++) if (table.GatingCm3[s2][len - 1] < bv) best = false;
                    if (bv != table.GatingCm3[bs][len - 1]) best = false;
                    double score;
                    M4Gate e = LungScoring.M4(table, s, len, out score);
                    if (e.Start != s || e.Len != len || e.ItvCm3 != want || e.Duty != len / 10.0 || e.BestCm3 != bv) scoreOk = false;
                    if (score < 0.0 || score > 1.0 || (s == bs && score != 1.0)) scoreOk = false;
                }
            }
            Measure(id + "_gating_worst_rel_dev_from_table", worstRel);
            Check(worstRel <= 0.01, "P2-A27 " + id + ": gated volume equals the table entry within 1 % for all 100 arcs (worst " + worstRel + ")");
            Check(exact, id + ": gated volume is the table entry exactly");
            Check(duty, "P2-A27 " + id + ": duty = arc length / 10");
            Check(best, id + ": the best arc of equal duty has the smallest table volume");
            Check(scoreOk, id + ": m4_gate payload and score for all 100 arcs");
            {
                int bs; double bv;
                Gating.BestArc(table, 5, out bs, out bv);
                Measure(id + "_itv_cm3", table.ItvCm3);
                Measure(id + "_gate_30_70_cm3", Gating.VolumeCm3(table, 3, 5));
                Measure(id + "_gate_30_70_itv_reduction", Gating.ItvReduction(table, 3, 5));
                Measure(id + "_best_arc_len5_start", bs);
                Measure(id + "_best_arc_len5_cm3", bv);
                Measure(id + "_best_arc_len5_itv_reduction", Gating.ItvReduction(table, bs, 5));
            }
            double worstSingle = 0.0;
            for (int s = 0; s < 10; s++) worstSingle = Math.Max(worstSingle, Math.Abs(Gating.VolumeCm3(table, s, 1) / table.GtvCm3[s] - 1.0));
            Check(worstSingle <= 0.01, id + ": an arc of length 1 is the single phase, gtv_cm3, within 1 % (worst " + worstSingle + ")");

            // ---- P2-A29 M2 on occ.r8
            var occ = new OccupancyGrid(File.ReadAllBytes(Path.Combine(dir, "occ.r8")), table);
            Check(occ.Nx == 64 && occ.Ny == 64 && occ.Nz == 64, id + ": occ.r8 is 64^3");
            Near(occ.MaxRho(), table.MaxRho, 0.01, id + ": largest occ.r8 value equals table max_rho");
            M2Place at = LungScoring.M2(occ, table, table.ArgmaxRhoMm[0], table.ArgmaxRhoMm[1], table.ArgmaxRhoMm[2]);
            Measure(id + "_m2_hit_at_argmax", at.Hit);
            Measure(id + "_m2_best", at.Best);
            Check(at.Best == table.MaxRho, id + ": m2 best = max rho");
            Near(at.Hit, table.MaxRho, 0.01, id + ": hit at the argmax-rho point equals max rho");
            // every voxel centre returns its own byte
            bool centres = true;
            for (int iz = 0; iz < 64; iz += 3) for (int iy = 0; iy < 64; iy += 3) for (int ix = 0; ix < 64; ix += 3)
            {
                double x = occ.MinX + (ix + 0.5) * (occ.MaxX - occ.MinX) / 64, y = occ.MinY + (iy + 0.5) * (occ.MaxY - occ.MinY) / 64, z = occ.MinZ + (iz + 0.5) * (occ.MaxZ - occ.MinZ) / 64;
                if (Math.Abs(occ.Sample(x, y, z) - occ.Voxels[ix + 64 * (iy + 64 * iz)] / 255.0) > 1e-9) centres = false;
            }
            Check(centres, id + ": the sample at a voxel centre is that voxel's value");
            // a point picked in the display frame at the display origin is origin_mm
            M2Place o = LungScoring.M2FromDisplay(occ, table, 15.0, 0, 0, 0);
            Check(o.PxMm == table.OriginMm[0] && o.PyMm == table.OriginMm[1] && o.PzMm == table.OriginMm[2], id + ": display origin maps to origin_mm");
        }

        static int M2Dump(string dir, string pointsFile)
        {
            CaseTable table = CaseTable.Read(File.ReadAllBytes(Path.Combine(dir, "table.json")));
            var occ = new OccupancyGrid(File.ReadAllBytes(Path.Combine(dir, "occ.r8")), table);
            foreach (string line in File.ReadAllLines(pointsFile))
            {
                if (line.Trim().Length == 0) continue;
                string[] p = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                M2Place e = LungScoring.M2(occ, table, double.Parse(p[0], Inv), double.Parse(p[1], Inv), double.Parse(p[2], Inv));
                Console.WriteLine(e.Hit.ToString("R", Inv) + " " + e.Best.ToString("R", Inv));
            }
            return 0;
        }

        static int GateDump(string dir)
        {
            CaseTable table = CaseTable.Read(File.ReadAllBytes(Path.Combine(dir, "table.json")));
            for (int s = 0; s < 10; s++)
            {
                for (int len = 1; len <= 10; len++)
                {
                    double score; int bs; double bv;
                    M4Gate e = LungScoring.M4(table, s, len, out score);
                    Gating.BestArc(table, len, out bs, out bv);
                    Console.WriteLine(string.Join(" ", e.Start.ToString(Inv), e.Len.ToString(Inv), e.ItvCm3.ToString("R", Inv), e.Duty.ToString("R", Inv),
                                                  e.BestCm3.ToString("R", Inv), bs.ToString(Inv), Gating.ItvReduction(table, s, len).ToString("R", Inv), score.ToString("R", Inv)));
                }
            }
            return 0;
        }

        // dump: int32 targets, int32 vertices, then targets x vertices x 3 float32 displacements (little-endian)
        static int Chest(string dump, double fps, double period)
        {
            byte[] raw = File.ReadAllBytes(dump);
            int n = BitConverter.ToInt32(raw, 0), verts = BitConverter.ToInt32(raw, 4);
            var targets = new float[n][];
            for (int k = 0; k < n; k++)
            {
                targets[k] = new float[3 * verts];
                Buffer.BlockCopy(raw, 8 + 12 * verts * k, targets[k], 0, 12 * verts);
            }
            var a = new float[n]; var b = new float[n];
            double atWrap; int frame;
            double max = BlendShapePlayer.MaxStepOverBreath(targets, fps, period, a, b, out atWrap, out frame);
            Measure("chest_vertices", verts);
            Measure("chest_max_step_per_frame_m", max);
            Measure("chest_max_step_across_wrap_m", atWrap);
            Measure("chest_worst_frame", frame);
            return 0;
        }
    }
}

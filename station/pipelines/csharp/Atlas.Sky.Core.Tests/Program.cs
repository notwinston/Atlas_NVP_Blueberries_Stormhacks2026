// Dependency-free tests of Atlas.Sky.Core (no test framework, so the library stays movable into Unity and the build needs
// no third-party packages). Exit code 0 = all passed and nothing failed; a section that could not run says "NOT RUN".
//
//   Atlas.Sky.Core.Tests --fixture <dir with sky/ (sky.json + 11 .r8)> --healpix <ref file> [--healpix <ref file> ...]
//   env ATLAS_CASES_DIR  the cases directory holding lung101/sky/ (the 20-direction acceptance on the real package)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Atlas.Cases;
using Atlas.Sky;

namespace Atlas.Sky.Tests
{
    public static class Program
    {
        static int passed, failed;

        static void Check(bool ok, string name)
        {
            if (ok) passed++;
            else { failed++; Console.Error.WriteLine("FAIL " + name); }
        }

        static void Near(double got, double want, double tol, string name)
        {
            Check(Math.Abs(got - want) <= tol, name + " (got " + got.ToString("R", CultureInfo.InvariantCulture) + ", want "
                + want.ToString("R", CultureInfo.InvariantCulture) + " +/- " + tol.ToString("R", CultureInfo.InvariantCulture) + ")");
        }

        static void Throws<T>(Action a, string name) where T : Exception
        {
            try { a(); Check(false, name + " (no exception)"); }
            catch (T) { Check(true, name); }
        }

        static ulong rng = 0x9E3779B97F4A7C15UL;
        static double NextUnit() // splitmix64 -> [0, 1)
        {
            rng += 0x9E3779B97F4A7C15UL;
            ulong z = rng;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (z >> 11) * (1.0 / 9007199254740992.0);
        }
        static Dir3 RandomDir()
        {
            double z = 2.0 * NextUnit() - 1.0, phi = 2.0 * Math.PI * NextUnit(), s = Math.Sqrt(1.0 - z * z);
            return new Dir3(s * Math.Cos(phi), s * Math.Sin(phi), z);
        }

        public static int Main(string[] args)
        {
            string fixture = null;
            var healpixRefs = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--fixture" && i + 1 < args.Length) fixture = args[++i];
                else if (args[i] == "--healpix" && i + 1 < args.Length) healpixRefs.Add(args[++i]);
                else { Console.Error.WriteLine("unknown argument " + args[i]); return 2; }
            }

            TestHealpixBasics();
            foreach (string f in healpixRefs) TestHealpixReference(f);
            TestFrame();
            TestGreatCircle();
            if (fixture != null)
            {
                TestInfoRejections(Path.Combine(fixture, "sky"));
                TestSkyMaps(Path.Combine(fixture, "sky"), "fixture", false);
                TestMapRejections(Path.Combine(fixture, "sky"));
                TestNoAllocation(Path.Combine(fixture, "sky"));
            }
            else Console.WriteLine("NOT RUN: fixture tests (no --fixture argument)");
            TestRealCase();

            Console.WriteLine("PASSED " + passed + " FAILED " + failed);
            return failed == 0 ? 0 : 1;
        }

        // ============================================================ HEALPix
        static void TestHealpixBasics()
        {
            Check(HealpixRing.NPix(64) == 49152, "npix of nside 64");
            Check(HealpixRing.Ang2PixRing(64, 0.0, 0.0) == 0, "north pole is pixel 0 region");
            Check(HealpixRing.Ang2PixRing(1, 0.5 * Math.PI, 0.0) >= 4 && HealpixRing.Ang2PixRing(1, 0.5 * Math.PI, 0.0) < 8, "nside 1 equator is in the belt");
            Check(HealpixRing.Ang2PixRing(64, Math.PI, 1.0) >= HealpixRing.NPix(64) - 4 * 64, "south pole is in the last ring region");
            Check(HealpixRing.Ang2PixRing(64, 1.0, -0.5) == HealpixRing.Ang2PixRing(64, 1.0, 2.0 * Math.PI - 0.5), "negative phi wraps");
            Throws<ArgumentOutOfRangeException>(() => HealpixRing.Ang2PixRing(64, -0.1, 0.0), "theta below 0 rejected");
            Throws<ArgumentOutOfRangeException>(() => HealpixRing.Ang2PixRing(64, 3.2, 0.0), "theta above pi rejected");
            Throws<ArgumentOutOfRangeException>(() => HealpixRing.Ang2PixRing(0, 1.0, 0.0), "nside 0 rejected");
            // every index is in range and the four poles' neighbours are distinct
            int bad = 0;
            for (int i = 0; i < 20000; i++)
            {
                Dir3 d = RandomDir();
                int p = HealpixRing.Vec2PixRing(64, d.X, d.Y, d.Z);
                if (p < 0 || p >= 49152) bad++;
            }
            Check(bad == 0, "20,000 random directions all give an index in range");
        }

        static void TestHealpixReference(string file)
        {
            string name = Path.GetFileName(file);
            if (!File.Exists(file)) { Check(false, "healpix reference file exists: " + file); return; }
            int n = 0, mismatch = 0;
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] f = line.Split(' ');
                double theta = double.Parse(f[0], CultureInfo.InvariantCulture), phi = double.Parse(f[1], CultureInfo.InvariantCulture);
                int want = int.Parse(f[2], CultureInfo.InvariantCulture);
                int got = HealpixRing.Ang2PixRing(64, theta, phi);
                n++;
                if (got != want)
                {
                    mismatch++;
                    if (mismatch <= 5) Console.Error.WriteLine("  HEALPIX MISMATCH theta=" + f[0] + " phi=" + f[1] + " got " + got + " astropy " + want);
                }
            }
            Console.WriteLine("HEALPIX " + name + ": " + n + " directions, " + mismatch + " mismatches against astropy-healpix");
            Check(n > 0, "healpix reference " + name + " is not empty");
            Check(mismatch == 0, "healpix " + name + ": 0 mismatches against astropy-healpix (" + mismatch + " of " + n + ")");
        }

        // ============================================================ frame
        static void TestFrame()
        {
            double az, el;
            SkyFrame.DirectionToAzEl(1, 0, 0, out az, out el); Near(az, 0, 1e-9, "+x az"); Near(el, 0, 1e-9, "+x el");
            SkyFrame.DirectionToAzEl(0, 1, 0, out az, out el); Near(az, 90, 1e-9, "+y az"); Near(el, 0, 1e-9, "+y el");
            SkyFrame.DirectionToAzEl(-1, 0, 0, out az, out el); Near(az, 180, 1e-9, "-x az");
            SkyFrame.DirectionToAzEl(0, -1, 0, out az, out el); Near(az, 270, 1e-9, "-y az is 270, not -90");
            SkyFrame.DirectionToAzEl(0, 0, 1, out az, out el); Near(el, 90, 1e-9, "+z el");
            SkyFrame.DirectionToAzEl(0, 0, -5, out az, out el); Near(el, -90, 1e-9, "-z el (non-unit vector)");
            SkyFrame.DirectionToAzEl(3, 3, 0, out az, out el); Near(az, 45, 1e-9, "45 degrees az");
            Dir3 d = SkyFrame.AzElToDirection(90, 0); Near(d.X, 0, 1e-12, "az 90 x"); Near(d.Y, 1, 1e-12, "az 90 y"); Near(d.Z, 0, 1e-12, "az 90 z");
            d = SkyFrame.AzElToDirection(0, 90); Near(d.Z, 1, 1e-12, "el 90 is +z");
            double worst = 0;
            for (int i = 0; i < 2000; i++)
            {
                Dir3 rd = RandomDir();
                SkyFrame.DirectionToAzEl(rd.X, rd.Y, rd.Z, out az, out el);
                Dir3 b = SkyFrame.AzElToDirection(az, el);
                worst = Math.Max(worst, Math.Max(Math.Abs(b.X - rd.X), Math.Max(Math.Abs(b.Y - rd.Y), Math.Abs(b.Z - rd.Z))));
                Check(az >= 0.0 && az < 360.0 && el >= -90.0 && el <= 90.0, "az/el ranges");
            }
            Check(worst < 1e-12, "az/el round trip, worst component error " + worst);
            // Unity mapping (informative; needs the Editor): (L, P, S) -> (L, S, P); an involution; left-handed, anatomy not mirrored
            Dir3 u = SkyFrame.PatientToUnity(1, 2, 3);
            Check(u.X == 1 && u.Y == 3 && u.Z == 2, "patient (x,y,z) -> unity (x,z,y)");
            Dir3 p = SkyFrame.UnityToPatient(u.X, u.Y, u.Z);
            Check(p.X == 1 && p.Y == 2 && p.Z == 3, "unity -> patient is the inverse");
            Dir3 sup = SkyFrame.PatientToUnity(0, 0, 1);
            Check(sup.Y == 1, "superior maps to Unity up");
            // pixel mapping
            int c, r;
            SkyFrame.AnglesToPixel(0.0, 0.0, 512, 256, out c, out r); Check(c == 0 && r == 0, "pixel of +z");
            SkyFrame.AnglesToPixel(Math.PI, 2.0 * Math.PI - 1e-12, 512, 256, out c, out r); Check(c == 511 && r == 255, "pixel of -z, phi just below 2 pi");
            SkyFrame.AnglesToPixel(1.0, -1e-17, 512, 256, out c, out r); Check(c >= 0 && c < 512, "tiny negative phi stays in range");
            double th, ph;
            SkyFrame.PixelCenterAngles(0, 25, 512, 256, out th, out ph);
            Near(Math.Sin(th) * Math.Cos(ph), 0.307844, 2e-6, "pixel (0,25) centre matches sky.json check x");
        }

        // ============================================================ great circle
        static void TestGreatCircle()
        {
            var x = new Dir3(1, 0, 0); var y = new Dir3(0, 1, 0); var z = new Dir3(0, 0, 1);
            SphericalTriangle t = GreatCircle.Triangle(x, y, z);
            Check(t.Valid, "octant triangle is valid");
            Near(t.AngleADeg, 90, 1e-9, "octant angle A"); Near(t.AngleBDeg, 90, 1e-9, "octant angle B"); Near(t.AngleCDeg, 90, 1e-9, "octant angle C");
            Near(t.AngleSumDeg, 270.0, 0.5, "octant triangle angle sum is 270 +/- 0.5");
            Near(t.AngleSumDeg, 270.0, 1e-9, "octant triangle angle sum, tight");
            Near(t.AreaSr, Math.PI / 2.0, 1e-12, "octant area is pi/2 sr (1/8 of the sphere)");
            Console.WriteLine("GREATCIRCLE octant angle sum " + t.AngleSumDeg.ToString("F6", CultureInfo.InvariantCulture) + " deg (plan: 270 +/- 0.5)");
            Check(t.ConsistencyErrorDeg < 1e-9, "octant: sum = 180 + 180 * area / pi");
            // vertex order and scale do not matter
            Near(GreatCircle.Triangle(z, x, y).AngleSumDeg, 270.0, 1e-9, "octant, rotated vertex order");
            Near(GreatCircle.Triangle(new Dir3(0, 0, 7), new Dir3(0, 3, 0), new Dir3(5, 0, 0)).AngleSumDeg, 270.0, 1e-9, "octant, reversed and non-unit");
            // via the picks the walker marks (az, el)
            Dir3 a = SkyFrame.AzElToDirection(0, 0), b = SkyFrame.AzElToDirection(90, 0), c = SkyFrame.AzElToDirection(0, 90);
            GcCheck g;
            Check(GreatCircle.TryCheck(a, b, c, out g), "octant from three az/el picks");
            Near(g.AngleSumDeg, 270.0, 0.5, "octant from az/el picks is 270 +/- 0.5");
            Check(g.ToJson() == "{\"angle_sum_deg\":270.00}", "gc_check json " + g.ToJson());

            // random triangles: sum > 180, and equal to 180 + 180 * area / pi, with the area from a third formula (L'Huilier)
            double worstVos = 0, worstLh = 0; int valid = 0, tinyOver = 0;
            for (int i = 0; i < 20000; i++)
            {
                Dir3 p = RandomDir(), q = RandomDir(), r = RandomDir();
                SphericalTriangle s = GreatCircle.Triangle(p, q, r);
                if (!s.Valid) continue;
                valid++;
                if (!(s.AngleSumDeg > 180.0 - 1e-9)) tinyOver++;
                worstVos = Math.Max(worstVos, s.ConsistencyErrorDeg);
                double sa = GreatCircle.ArcLengthRad(q, r), sb = GreatCircle.ArcLengthRad(r, p), sc = GreatCircle.ArcLengthRad(p, q);
                double sp = 0.5 * (sa + sb + sc);
                double inner = Math.Tan(0.5 * sp) * Math.Tan(0.5 * (sp - sa)) * Math.Tan(0.5 * (sp - sb)) * Math.Tan(0.5 * (sp - sc));
                double areaLh = 4.0 * Math.Atan(Math.Sqrt(Math.Max(0.0, inner)));
                worstLh = Math.Max(worstLh, Math.Abs((s.AngleSumDeg - 180.0) - areaLh * 180.0 / Math.PI));
            }
            Check(valid > 19990, "random triangles are valid (" + valid + " of 20000)");
            Check(tinyOver == 0, "angle sum of every random triangle is above 180 degrees");
            Check(worstVos < 1e-6, "sum = 180 + 180 area/pi (Van Oosterom-Strakhov), worst " + worstVos + " deg");
            Check(worstLh < 1e-6, "sum = 180 + 180 area/pi (L'Huilier), worst " + worstLh + " deg");
            Console.WriteLine("GREATCIRCLE 20000 random triangles: worst |sum - (180 + 180 area/pi)| = " + worstVos.ToString("E2", CultureInfo.InvariantCulture)
                + " deg (VOS), " + worstLh.ToString("E2", CultureInfo.InvariantCulture) + " deg (L'Huilier); plan allows 0.5");

            // a small triangle is nearly flat; a large one (almost a hemisphere) is nearly 540
            SphericalTriangle small = GreatCircle.Triangle(new Dir3(1, 0, 0), new Dir3(1, 1e-3, 0), new Dir3(1, 0, 1e-3));
            Check(small.Valid && small.AngleSumDeg > 180.0 && small.AngleSumDeg < 180.001, "small triangle: sum just above 180 (" + small.AngleSumDeg + ")");
            SphericalTriangle big = GreatCircle.Triangle(new Dir3(1, 0.01, 0), new Dir3(-0.5, 0.866, 0.01), new Dir3(-0.5, -0.866, 0.01));
            Check(big.Valid && big.AngleSumDeg > 500.0, "large triangle (near a hemisphere): sum near 540 (" + big.AngleSumDeg + ")");
            // degenerate ones
            Check(!GreatCircle.Triangle(x, x, y).Valid, "coincident vertices are invalid");
            Check(!GreatCircle.Triangle(x, new Dir3(-1, 0, 0), y).Valid, "antipodal vertices are invalid");
            Check(!GreatCircle.Triangle(x, y, new Dir3(0, 0, 0)).Valid, "a zero vector is invalid");
            Check(!GreatCircle.Triangle(x, y, new Dir3(double.NaN, 0, 1)).Valid, "NaN is invalid");
            SphericalTriangle flat = GreatCircle.Triangle(new Dir3(1, 0, 0), new Dir3(Math.Cos(0.3), Math.Sin(0.3), 0), new Dir3(Math.Cos(0.8), Math.Sin(0.8), 0));
            Check(flat.Valid, "three points on one great circle are valid");
            Near(flat.AngleSumDeg, 180.0, 1e-9, "three points on one great circle: sum 180"); Near(flat.AreaSr, 0.0, 1e-12, "...with area 0");
            Near(GreatCircle.ArcLengthRad(x, y), 0.5 * Math.PI, 1e-12, "arc length x to y");
        }

        // ============================================================ sky.json and the maps
        static byte[] Bytes(string dir, string name) { return File.ReadAllBytes(Path.Combine(dir, name)); }

        static void TestInfoRejections(string skyDir)
        {
            string text = System.Text.Encoding.UTF8.GetString(Bytes(skyDir, "sky.json"));
            SkyInfo ok = SkyInfo.Parse(text);
            Check(ok.Phases == 10 && ok.Checks.Length == 20, "fixture sky.json has 10 phases and 20 checks");
            Check(ok.ThresholdLabel == "Teaching threshold" && ok.ThresholdValue == 50, "threshold read");
            Throws<ContractException>(() => SkyInfo.Parse(text.Replace("atlas.sky/1", "atlas.sky/2")), "other schema rejected");
            Throws<ContractException>(() => SkyInfo.Parse(text.Replace("\"ordering\":\"ring\"", "\"ordering\":\"nested\"")), "nested ordering rejected");
            Throws<ContractException>(() => SkyInfo.Parse(text.Replace("risk_p3.r8", "../risk_p3.r8")), "path in a file name rejected");
            Throws<ContractException>(() => SkyInfo.Parse(text.Replace("\"rounding\":\"floor\"", "\"rounding\":\"round\"")), "other rounding rejected");
            Throws<ContractException>(() => SkyInfo.Parse(text.Replace("\"npix\":192", "\"npix\":193")), "wrong npix rejected");
            Throws<ContractException>(() => SkyInfo.Parse("[1,2]"), "non-object rejected");
            Throws<ContractException>(() => SkyInfo.Parse("{}"), "missing keys rejected");
        }

        static void TestMapRejections(string skyDir)
        {
            SkyInfo info = SkyInfo.Parse(Bytes(skyDir, "sky.json"));
            var risk = new byte[10][];
            for (int k = 0; k < 10; k++) risk[k] = Bytes(skyDir, info.RiskFiles[k]);
            byte[] robust = Bytes(skyDir, info.RobustFile);
            Check(SkyMaps.FromBytes(info, risk, robust) != null, "FromBytes accepts the fixture bytes");
            var shortRisk = (byte[][])risk.Clone(); shortRisk[4] = new byte[100];
            Throws<ContractException>(() => SkyMaps.FromBytes(info, shortRisk, robust), "a short phase map is rejected");
            Throws<ContractException>(() => SkyMaps.FromBytes(info, risk, new byte[3]), "a short robust map is rejected");
            var badRobust = (byte[])robust.Clone(); badRobust[7] = 7;
            Throws<ContractException>(() => SkyMaps.FromBytes(info, risk, badRobust), "a robust map holding 7 is rejected");
            Throws<ContractException>(() => SkyMaps.FromBytes(info, new byte[9][], robust), "nine maps are rejected");
            // the AND rule is detected when broken
            var flipped = (byte[])robust.Clone();
            for (int i = 0; i < flipped.Length; i++) flipped[i] = (byte)(flipped[i] == 0 ? 255 : 0);
            Check(SkyMaps.FromBytes(info, risk, flipped).RobustRuleViolations() == flipped.Length, "a flipped robust map violates the rule at every pixel");
        }

        /// <summary>The 20-direction acceptance, on a fixture or on the real package. Returns the worst deviation in mm.</summary>
        static double TestSkyMaps(string skyDir, string label, bool real)
        {
            SkyMaps maps = SkyMaps.Load(skyDir);
            SkyInfo info = maps.Info;
            Check(info.Checks.Length == 20, label + ": sky.json carries 20 test directions");
            Check(maps.RobustRuleViolations() == 0, label + ": robust map equals the AND of the phase maps (" + maps.RobustRuleViolations() + " differing pixels)");
            if (info.RobustShare >= 0.0) Near(maps.RobustShare(), info.RobustShare, real ? 1e-4 : 1e-5, label + ": robust share of the sphere matches sky.json stats");

            // every pixel centre goes back to its own pixel, and its direction's HEALPix index is the one the baker used
            int bad = 0;
            for (int row = 0; row < maps.Height; row++)
                for (int col = 0; col < maps.Width; col++)
                {
                    double th, ph; int c, r;
                    SkyFrame.PixelCenterAngles(col, row, maps.Width, maps.Height, out th, out ph);
                    Dir3 d = SkyFrame.AnglesToDirection(th, ph);
                    if (!maps.TryPixelOf(d.X, d.Y, d.Z, out c, out r) || c != col || r != row) bad++;
                }
            Check(bad == 0, label + ": every pixel centre maps back to its own pixel (" + bad + " wrong)");

            double worst = 0.0; int within = 0, robustOk = 0, pixelOk = 0, healpixOk = 0;
            for (int i = 0; i < info.Checks.Length; i++)
            {
                SkyCheck k = info.Checks[i];
                double[] d = k.Dir;
                int c, r;
                bool found = maps.TryPixelOf(d[0], d[1], d[2], out c, out r);
                if (found && c == k.Col && r == k.Row) pixelOk++;
                else Console.Error.WriteLine("  check " + i + ": pixel (" + c + "," + r + ") but sky.json says (" + k.Col + "," + k.Row + ")");
                if (maps.HealpixIndex(d[0], d[1], d[2]) == k.Healpix) healpixOk++;
                else Console.Error.WriteLine("  check " + i + ": healpix " + maps.HealpixIndex(d[0], d[1], d[2]) + " but sky.json says " + k.Healpix);
                bool allPhases = true;
                for (int ph = 0; ph < info.Phases; ph++)
                {
                    double mm = maps.ClearanceMm(ph, d[0], d[1], d[2]);
                    double dev = Math.Abs(mm - k.ClearanceMm[ph]);
                    worst = Math.Max(worst, dev);
                    if (dev > 0.1) allPhases = false;
                    if (maps.ClearanceMm(ph, k.Col, k.Row) != mm) allPhases = false; // by direction and by pixel agree
                }
                double minMm = maps.MinClearanceMm(d[0], d[1], d[2]);
                worst = Math.Max(worst, Math.Abs(minMm - k.MinClearanceMm));
                if (Math.Abs(minMm - k.MinClearanceMm) > 0.1) allPhases = false;
                SkyPick pick;
                if (!maps.TryPick(d[0], d[1], d[2], out pick)) allPhases = false;
                else
                {
                    if (Math.Abs(pick.ClearMm - k.MinClearanceMm) > 0.1) allPhases = false;
                    if (pick.Robust != k.Robust) allPhases = false; else robustOk++;
                }
                if (allPhases) within++;
                else Console.Error.WriteLine("  check " + i + " (" + k.Col + "," + k.Row + ") outside 0.1 mm or robust mismatch");
            }
            double worstRounded = Math.Round(worst, 6);
            Check(pixelOk == 20, label + ": the 20 test directions fall in the pixels sky.json lists (" + pixelOk + "/20)");
            Check(healpixOk == 20, label + ": the 20 HEALPix indices equal sky.json's (" + healpixOk + "/20)");
            Check(robustOk == 20, label + ": the 20 robust flags equal sky.json's (" + robustOk + "/20)");
            Check(within == 20, label + ": clearance equals sky.json within 0.1 mm at 20 directions, per phase and minimum (" + within + "/20, worst " + worstRounded + " mm)");
            Check(worstRounded <= 0.1, label + ": worst deviation " + worstRounded + " mm <= 0.1 mm");

            // robust flag and clearance agree across the whole map: robust <=> min byte >= threshold
            int inconsistent = 0;
            for (int row = 0; row < maps.Height; row++)
                for (int col = 0; col < maps.Width; col++)
                    if (maps.IsRobust(col, row) != (maps.MinRiskValue(col, row) >= info.ThresholdValue)) inconsistent++;
            Check(inconsistent == 0, label + ": robust <=> min clearance >= threshold at every pixel (" + inconsistent + " differ)");

            // seams and poles
            SkyPick p;
            Check(maps.TryPick(0, 0, 1, out p) && p.ElDeg == 90.0, label + ": +z can be picked (pole row)");
            Check(maps.TryPick(0, 0, -1, out p) && p.ElDeg == -90.0, label + ": -z can be picked");
            Check(maps.TryPick(1, -1e-12, 0, out p) && p.AzDeg > 359.0, label + ": a direction just below +x wraps to the last column");
            int c0, r0, c1, r1;
            maps.TryPixelOf(37.0, 11.0, -5.0, out c0, out r0); maps.TryPixelOf(3.7, 1.1, -0.5, out c1, out r1);
            Check(c0 == c1 && r0 == r1, label + ": the length of the direction vector does not matter");
            Check(!maps.TryPick(0, 0, 0, out p), label + ": a zero vector gives no pick");
            Check(!maps.TryPick(double.NaN, 0, 1, out p), label + ": a NaN vector gives no pick");
            Check(double.IsNaN(maps.MinClearanceMm(0, 0, 0)), label + ": a zero vector gives NaN clearance");
            Throws<ArgumentOutOfRangeException>(() => maps.ClearanceMm(10, 0, 0), label + ": phase 10 rejected");
            Throws<ArgumentOutOfRangeException>(() => maps.ClearanceMm(0, maps.Width, 0), label + ": column out of range rejected");
            Check(maps.TryPick(0.3, 0.2, 0.9, out p) && p.ToJson().StartsWith("{\"az_deg\":") && p.ToJson().EndsWith("}") && p.ToJson().Contains("\"robust\":"), label + ": sky_pick json " + p.ToJson());
            Check(new SkyPick(33.333, -12.346, 7.2, true).ToJson() == "{\"az_deg\":33.33,\"el_deg\":-12.35,\"clear_mm\":7.2,\"robust\":true}", label + ": sky_pick json formatting " + new SkyPick(33.333, -12.346, 7.2, true).ToJson());
            Check(new SkyPick(1, -0.0001, 0.0, false).ToJson() == "{\"az_deg\":1.00,\"el_deg\":0.00,\"clear_mm\":0.0,\"robust\":false}", label + ": no negative zero in the payload");
            if (real) Console.WriteLine("REAL-CASE " + label + ": " + within + "/20 directions within 0.1 mm of sky.json (worst deviation " + worstRounded.ToString("F6", CultureInfo.InvariantCulture)
                + " mm), robust flags " + robustOk + "/20, HEALPix indices " + healpixOk + "/20, robust share " + maps.RobustShare().ToString("F6", CultureInfo.InvariantCulture));
            else Console.WriteLine("FIXTURE " + label + ": " + within + "/20 directions within 0.1 mm, robust flags " + robustOk + "/20, HEALPix " + healpixOk + "/20");
            return worst;
        }

        static void TestNoAllocation(string skyDir)
        {
            SkyMaps maps = SkyMaps.Load(skyDir);
            double sink = 0; SkyPick p;
            for (int i = 0; i < 1000; i++) { maps.TryPick(0.1 * (i % 7) - 0.3, 0.2, 0.5, out p); sink += p.ClearMm; } // warm up the JIT
            var sw = System.Diagnostics.Stopwatch.StartNew(); // allocated before the counter is read
            long before = GC.GetAllocatedBytesForCurrentThread();
            const int N = 1000000;
            for (int i = 0; i < N; i++)
            {
                double a = i * 0.001;
                if (maps.TryPick(Math.Cos(a), Math.Sin(a), 0.3 * Math.Sin(0.37 * a), out p)) sink += p.ClearMm + (p.Robust ? 1 : 0);
                sink += maps.MinClearanceMm(Math.Sin(a), 0.2, Math.Cos(a)) + maps.ClearanceMm(i % 10, 0.5, Math.Cos(a), Math.Sin(a));
                sink += HealpixRing.Ang2PixRing(64, 0.5 + 0.001 * (i % 2000), a);
            }
            sw.Stop();
            long after = GC.GetAllocatedBytesForCurrentThread();
            Check(after - before == 0, "no allocation in " + N + " pick + clearance + healpix lookups (" + (after - before) + " bytes)");
            Console.WriteLine("ALLOC lookups: " + (after - before) + " bytes over " + N + " iterations; " + (sw.Elapsed.TotalMilliseconds * 1e6 / N).ToString("F0", CultureInfo.InvariantCulture)
                + " ns per iteration (pick + 2 lookups + healpix), sink " + (sink > 0 ? "ok" : "zero"));
            // great circle too
            before = GC.GetAllocatedBytesForCurrentThread();
            double s2 = 0;
            for (int i = 0; i < 100000; i++) s2 += GreatCircle.Triangle(new Dir3(1, 0, 0), new Dir3(0, 1, 0), new Dir3(0, 0.1 * (i % 9) + 0.1, 1)).AngleSumDeg;
            after = GC.GetAllocatedBytesForCurrentThread();
            Check(after - before == 0, "no allocation in 100,000 triangle checks (" + (after - before) + " bytes)");
            if (s2 < 0) Console.WriteLine();
        }

        // ============================================================ real package
        static void TestRealCase()
        {
            string cases = Environment.GetEnvironmentVariable("ATLAS_CASES_DIR");
            string sky = string.IsNullOrEmpty(cases) ? null : Path.Combine(cases, "lung101", "sky");
            if (sky == null || !File.Exists(Path.Combine(sky, "sky.json")))
            {
                Console.WriteLine("NOT RUN: real-case acceptance (P2-B10, 20 test directions on lung101): "
                    + (string.IsNullOrEmpty(cases) ? "ATLAS_CASES_DIR is not set" : "no lung101/sky/sky.json under " + cases));
                return;
            }
            SkyMaps maps = SkyMaps.Load(sky);
            Check(maps.Width == 512 && maps.Height == 256 && maps.Phases == 10 && maps.Info.Nside == 64, "real sky: 512 x 256, 10 phases, nside 64");
            Check(maps.Info.Id == "lung101", "real sky: id lung101");
            TestSkyMaps(sky, "lung101", true);
        }
    }
}

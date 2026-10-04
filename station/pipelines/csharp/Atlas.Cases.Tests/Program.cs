// Dependency-free tests of Atlas.Cases (no test framework, so the library stays movable into Unity
// and the build needs no third-party packages). Exit code 0 = all passed.
//
//   Atlas.Cases.Tests [fixture directory holding tube4d.bin, table.json, manifest.json]
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Atlas.Cases.Tests
{
    public static class Program
    {
        static int passed, failed;

        static void Check(bool ok, string name)
        {
            if (ok) passed++;
            else { failed++; Console.Error.WriteLine("FAIL " + name); }
        }

        static void Eq(string got, string want, string name)
        {
            Check(got == want, name + " (got " + got + ", want " + want + ")");
        }

        static void Throws(Action a, string name)
        {
            try { a(); Check(false, name + " (no exception)"); }
            catch (ContractException) { Check(true, name); }
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static string Text(object v) { return Encoding.UTF8.GetString(CanonicalJson.Write(v)); }

        public static int Main(string[] args)
        {
            // --- canonical floats
            Eq(CanonicalJson.FormatFloat(15.0), "15.0", "float 15");
            Eq(CanonicalJson.FormatFloat(1.5), "1.5", "float 1.5");
            Eq(CanonicalJson.FormatFloat(0.0), "0.0", "float 0");
            Eq(CanonicalJson.FormatFloat(-0.0), "0.0", "float -0");
            Eq(CanonicalJson.FormatFloat(-0.25), "-0.25", "float -0.25");
            Eq(CanonicalJson.FormatFloat(0.1), "0.1", "float 0.1");
            Eq(CanonicalJson.FormatFloat(47.4), "47.4", "float 47.4");
            Eq(CanonicalJson.FormatFloat(1234567.891234), "1234567.891234", "float 6 decimals");
            Eq(CanonicalJson.FormatFloat(0.0000004), "0.0", "float below resolution");
            Eq(CanonicalJson.FormatFloat(-0.0000004), "0.0", "float below resolution, negative");
            Eq(CanonicalJson.FormatFloat(0.0000015), "0.000002", "float 1.5e-6");
            Eq(CanonicalJson.FormatFloat(2.5), "2.5", "float 2.5");
            Throws(() => CanonicalJson.FormatFloat(double.NaN), "NaN rejected");
            Throws(() => CanonicalJson.FormatFloat(1e10), "out of range rejected");

            // --- canonical structure
            var o = new Dictionary<string, object>();
            o["b"] = new List<object> { 1L, 2.0, true, null, "x" };
            o["a"] = "q\"\\\n\u0001é";
            o["B"] = -7L;
            Eq(Text(o), "{\"B\":-7,\"a\":\"q\\\"\\\\\\n\\u0001é\",\"b\":[1,2.0,true,null,\"x\"]}\n", "canonical object");
            object parsed = CanonicalJson.Parse(" { \"k\" : [ 1 , 1.0 , 1e2 , -0 , \"\\u00e9\\ud83d\\ude00\" ] } ");
            Eq(Text(parsed), "{\"k\":[1,1.0,100.0,0,\"é\U0001F600\"]}\n", "parse and re-emit");
            Throws(() => CanonicalJson.Parse("{\"a\":1,\"a\":2}"), "duplicate key rejected");
            Throws(() => CanonicalJson.Parse("[1,]"), "trailing comma rejected");
            Throws(() => CanonicalJson.Parse("01"), "leading zero rejected");
            Throws(() => CanonicalJson.Parse("NaN"), "NaN token rejected");
            Throws(() => CanonicalJson.Parse(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'1' }), "BOM rejected");
            var badKey = new Dictionary<string, object>();
            badKey["é"] = 1L;
            Throws(() => CanonicalJson.Write(badKey), "non-ASCII key rejected");

            // --- tube4d: two tetrahedra sharing a face
            var t = new Tube4D();
            t.Layers = 2; t.M = 15f; t.Lambda = 1.5f;
            t.P = new float[] { 0f, 0f, 0f, 0f, 0.1f, 0f, 0f, 0f, 0f, 0.1f, 0f, 0.75f, 0f, 0f, 0.1f, 0.75f, -0.1f, -0.2f, 0.3f, 1.5f };
            t.T = new int[] { 0, 1, 2, 3, 1, 2, 3, 4 };
            t.Slab = new byte[] { 0, 1 };
            byte[] bytes = t.Write();
            Check(bytes.Length == 24 + 16 * 5 + 17 * 2, "tube4d size = 24 + 16 n + 17 m");
            Check(bytes[0] == 0x41 && bytes[1] == 0x54 && bytes[2] == 0x4C && bytes[3] == 0x34, "tube4d magic ATL4");
            Check(bytes[4] == 1 && bytes[5] == 0, "tube4d version 1");
            Check(bytes[6] == 5 && bytes[10] == 2 && bytes[14] == 2, "tube4d counts");
            Check(bytes[16] == 0 && bytes[17] == 0 && bytes[18] == 0x70 && bytes[19] == 0x41, "tube4d M = 15f little-endian");
            Check(bytes[20] == 0 && bytes[21] == 0 && bytes[22] == 0xC0 && bytes[23] == 0x3F, "tube4d Lambda = 1.5f little-endian");
            Check(Same(Tube4D.Read(bytes).Write(), bytes), "tube4d round trip");
            Throws(() => { var b2 = (byte[])bytes.Clone(); b2[0] = (byte)'X'; Tube4D.Read(b2); }, "tube4d bad magic");
            Throws(() => { var b2 = new byte[bytes.Length - 1]; Array.Copy(bytes, b2, b2.Length); Tube4D.Read(b2); }, "tube4d truncated");
            Throws(() => { var b2 = (byte[])bytes.Clone(); b2[b2.Length - 1] = 2; Tube4D.Read(b2); }, "tube4d slab >= layers");
            Throws(() => { var b2 = (byte[])bytes.Clone(); b2[24 + 80] = 9; Tube4D.Read(b2); }, "tube4d index out of range");
            Throws(() => { var b2 = (byte[])bytes.Clone(); b2[4] = 2; Tube4D.Read(b2); }, "tube4d unknown version");

            // --- manifest
            var m = new CaseManifest();
            m.Id = "x"; m.Kind = CaseManifest.KindIllustrative; m.Label = "Illustrative model."; m.Footer = "f";
            m.Licence = "l"; m.Credit = "c"; m.M = 15.0; m.Lambda = 1.5; m.OriginMm = new double[] { 0, 0, 0 };
            m.Files.Add(new CaseFile { Path = "tube4d.bin", Sha256 = CaseManifest.Sha256Hex(bytes), Size = bytes.Length });
            Throws(() => m.Write(), "manifest without a label per file rejected");
            m.Labels["tube4d.bin"] = "Illustrative model.";
            byte[] mb = m.Write();
            Check(Same(CaseManifest.Read(mb).Write(), mb), "manifest round trip");
            Eq(CaseManifest.Sha256Hex(new byte[0]), "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "sha256 of empty");
            m.Files[0].Path = "../x";
            Throws(() => m.Write(), "manifest path escape rejected");

            // --- committed fixtures
            if (args.Length > 0)
            {
                string dir = args[0];
                byte[] ft = File.ReadAllBytes(Path.Combine(dir, "tube4d.bin"));
                byte[] fj = File.ReadAllBytes(Path.Combine(dir, "table.json"));
                byte[] fm = File.ReadAllBytes(Path.Combine(dir, "manifest.json"));
                var tube = Tube4D.Read(ft);
                Check(tube.TetCount == 2, "fixture has 2 tetrahedra");
                Check(tube.M == 15f && tube.Lambda == 1.5f, "fixture M = 15, Lambda = 1.5");
                Check(Same(tube.Write(), ft), "fixture tube4d.bin byte-identical");
                var table = CaseTable.Read(fj);
                Check(table.GatingCm3.Length == 10 && table.GatingCm3[9].Length == 10, "fixture gating table 10 x 10");
                Check(Same(table.Write(), fj), "fixture table.json byte-identical");
                var man = CaseManifest.Read(fm);
                Check(man.M == 15.0 && man.Lambda == 1.5, "fixture manifest M = 15, Lambda = 1.5");
                Check(Same(man.Write(), fm), "fixture manifest.json byte-identical");
                Check(man.Verify(dir).Count == 0, "fixture hash check passes");
            }

            Console.WriteLine("Atlas.Cases.Tests: " + passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }
    }
}

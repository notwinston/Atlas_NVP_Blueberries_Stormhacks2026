// ATLAS case contract (docs/case_contract.md, section 6): manifest.json reader, writer and hash check.
// Plain C#, System only, no Unity references.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Atlas.Cases
{
    public sealed class CaseFile
    {
        /// <summary>Relative to the package directory, '/' separators.</summary>
        public string Path;
        /// <summary>64 lower-case hex digits.</summary>
        public string Sha256;
        /// <summary>Bytes.</summary>
        public long Size;
    }

    /// <summary>manifest.json, schema atlas.case/1.</summary>
    public sealed class CaseManifest
    {
        public const string Schema = "atlas.case/1";
        public const string FileName = "manifest.json";
        public const string KindReal = "real";
        public const string KindIllustrative = "illustrative";

        public string Id;
        public string Kind;
        public string Label;
        public string Footer;
        /// <summary>Exactly one on-screen label per entry of Files, keyed by path.</summary>
        public Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal);
        public string Licence;
        public string Credit;
        public List<string> Citations = new List<string>();
        public double M;
        public double Lambda;
        public double[] OriginMm;
        public List<CaseFile> Files = new List<CaseFile>();
        /// <summary>Optional free-form object, preserved as parsed. Null when absent.</summary>
        public Dictionary<string, object> Extra;

        static readonly string[] Required = {
            "schema", "id", "kind", "label", "footer", "labels", "licence", "credit", "citations",
            "M", "Lambda", "origin_mm", "files" };
        static readonly string[] Optional = { "extra" };
        static readonly string[] FileRequired = { "path", "sha256", "size" };
        static readonly string[] None = { };

        public static CaseManifest Read(byte[] utf8) { return FromJson(CanonicalJson.Parse(utf8)); }

        public byte[] Write() { return CanonicalJson.Write(ToJson()); }

        public static CaseManifest FromJson(object json)
        {
            const string W = "manifest.json";
            var o = CanonicalJson.AsObject(json, W);
            CanonicalJson.CheckKeys(o, Required, Optional, W);
            if (CanonicalJson.AsString(o["schema"], "schema") != Schema)
                throw new ContractException(W + ": schema must be " + Schema);
            var m = new CaseManifest();
            m.Id = CanonicalJson.AsString(o["id"], "id");
            m.Kind = CanonicalJson.AsString(o["kind"], "kind");
            m.Label = CanonicalJson.AsString(o["label"], "label");
            m.Footer = CanonicalJson.AsString(o["footer"], "footer");
            foreach (var kv in CanonicalJson.AsObject(o["labels"], "labels"))
                m.Labels[kv.Key] = CanonicalJson.AsString(kv.Value, "labels");
            m.Licence = CanonicalJson.AsString(o["licence"], "licence");
            m.Credit = CanonicalJson.AsString(o["credit"], "credit");
            foreach (object c in CanonicalJson.AsArray(o["citations"], "citations"))
                m.Citations.Add(CanonicalJson.AsString(c, "citations"));
            m.M = CanonicalJson.AsDouble(o["M"], "M");
            m.Lambda = CanonicalJson.AsDouble(o["Lambda"], "Lambda");
            m.OriginMm = CanonicalJson.AsDoubles(o["origin_mm"], 3, "origin_mm");
            foreach (object f in CanonicalJson.AsArray(o["files"], "files"))
            {
                var fo = CanonicalJson.AsObject(f, "files[]");
                CanonicalJson.CheckKeys(fo, FileRequired, None, W + " files[]");
                var cf = new CaseFile();
                cf.Path = CanonicalJson.AsString(fo["path"], "path");
                cf.Sha256 = CanonicalJson.AsString(fo["sha256"], "sha256");
                cf.Size = CanonicalJson.AsLong(fo["size"], "size");
                m.Files.Add(cf);
            }
            object extra;
            if (o.TryGetValue("extra", out extra)) m.Extra = CanonicalJson.AsObject(extra, "extra");
            m.Validate();
            return m;
        }

        public void Validate()
        {
            const string W = "manifest.json";
            if (Kind != KindReal && Kind != KindIllustrative) throw new ContractException(W + ": kind must be real or illustrative");
            if (Id == null || Label == null || Footer == null || Licence == null || Credit == null)
                throw new ContractException(W + ": missing string");
            if (OriginMm == null || OriginMm.Length != 3) throw new ContractException(W + ": origin_mm needs 3 values");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (CaseFile f in Files)
            {
                CheckPath(f.Path);
                if (!seen.Add(f.Path)) throw new ContractException(W + ": duplicate file " + f.Path);
                if (f.Sha256 == null || f.Sha256.Length != 64) throw new ContractException(W + ": bad sha256 for " + f.Path);
                foreach (char c in f.Sha256)
                    if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) throw new ContractException(W + ": bad sha256 for " + f.Path);
                if (f.Size < 0) throw new ContractException(W + ": negative size for " + f.Path);
            }
            if (Labels.Count != seen.Count) throw new ContractException(W + ": labels must have exactly one entry per file");
            foreach (var kv in Labels)
            {
                if (!seen.Contains(kv.Key)) throw new ContractException(W + ": labels must have exactly one entry per file");
                if (kv.Value == null) throw new ContractException(W + ": null label");
            }
        }

        static void CheckPath(string p)
        {
            bool bad = string.IsNullOrEmpty(p) || p[0] == '/' || p == FileName;
            if (!bad)
            {
                foreach (char c in p) if (c < ' ' || c > '~' || c == '\\') bad = true;
                foreach (string part in p.Split('/')) if (part.Length == 0 || part == "." || part == "..") bad = true;
            }
            if (bad) throw new ContractException("manifest.json: bad file path " + p);
        }

        public Dictionary<string, object> ToJson()
        {
            Validate();
            var labels = new Dictionary<string, object>();
            foreach (var kv in Labels) labels[kv.Key] = kv.Value;
            var cites = new List<object>();
            foreach (string c in Citations) cites.Add(c);
            var sorted = new List<CaseFile>(Files);
            sorted.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            var files = new List<object>();
            foreach (CaseFile f in sorted)
            {
                var fo = new Dictionary<string, object>();
                fo["path"] = f.Path;
                fo["sha256"] = f.Sha256;
                fo["size"] = f.Size;
                files.Add(fo);
            }
            var o = new Dictionary<string, object>();
            o["schema"] = Schema;
            o["id"] = Id;
            o["kind"] = Kind;
            o["label"] = Label;
            o["footer"] = Footer;
            o["labels"] = labels;
            o["licence"] = Licence;
            o["credit"] = Credit;
            o["citations"] = cites;
            o["M"] = M;
            o["Lambda"] = Lambda;
            o["origin_mm"] = CanonicalJson.FromDoubles(OriginMm);
            o["files"] = files;
            if (Extra != null) o["extra"] = Extra;
            return o;
        }

        /// <summary>Lower-case hex SHA-256 of a byte array.</summary>
        public static string Sha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(data);
                var sb = new StringBuilder(64);
                foreach (byte b in h) sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>
        /// The hash check: every listed file exists under <paramref name="caseDir"/> with the recorded size and SHA-256.
        /// Returns the list of problems; empty means the package is intact.
        /// </summary>
        public List<string> Verify(string caseDir)
        {
            var problems = new List<string>();
            foreach (CaseFile f in Files)
            {
                string full = System.IO.Path.Combine(caseDir, f.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (!File.Exists(full)) { problems.Add(f.Path + ": missing"); continue; }
                byte[] data = File.ReadAllBytes(full);
                if (data.LongLength != f.Size) problems.Add(f.Path + ": size differs");
                else if (Sha256Hex(data) != f.Sha256) problems.Add(f.Path + ": sha256 mismatch");
            }
            // The package holds no unlisted file (manifest.json itself is never listed).
            var listed = new HashSet<string>();
            foreach (CaseFile f in Files) listed.Add(f.Path);
            if (Directory.Exists(caseDir))
            {
                string root = System.IO.Path.GetFullPath(caseDir).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
                var extra = new List<string>();
                foreach (string full in Directory.GetFiles(caseDir, "*", SearchOption.AllDirectories))
                {
                    string rel = System.IO.Path.GetFullPath(full).Substring(root.Length).Replace(System.IO.Path.DirectorySeparatorChar, '/');
                    if (rel == "manifest.json" || listed.Contains(rel)) continue;
                    if (rel.StartsWith(".", StringComparison.Ordinal) || rel.Contains("/.")) continue;   // dot files are not package files
                    extra.Add(rel);
                }
                extra.Sort(string.CompareOrdinal);
                foreach (string rel in extra) problems.Add(rel + ": present but not listed in manifest.json");
            }
            return problems;
        }
    }
}

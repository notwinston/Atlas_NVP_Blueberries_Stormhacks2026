using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Atlas.Link;

namespace Atlas.Station
{
    /// <summary>
    /// <c>perf.csv</c> as a file of rows keyed by their <c>seq</c> (lane D contract 3.4 and 5.3): reading one back,
    /// and merging rows into one. The Hub merges when the phone uploads its own CSV after a session
    /// (<c>PUT /logs/{session}/perf.csv</c>, Master Plan section 12.2) and when a row arrives out of order. The
    /// lines themselves are written by <see cref="TelemetryCsv"/> on both ends and are never reformatted here, so
    /// a merged file is byte for byte the phone's file.
    /// </summary>
    public static class PerfCsv
    {
        static readonly UTF8Encoding s_utf8 = new UTF8Encoding(false);

        /// <summary>
        /// The <c>seq</c> of a row, its second column. False for anything that is not a well-formed row: 34
        /// columns of digits, with a sign or a decimal point where a number has one.
        /// </summary>
        public static bool TryReadSeq(string line, out uint seq)
        {
            seq = 0;
            if (string.IsNullOrEmpty(line) || line.Length > TelemetryCsv.MaxRowLength) return false;
            int columns = 1, fieldStart = 0;
            ulong value = 0;
            for (int i = 0; i <= line.Length; i++)
            {
                char ch = i < line.Length ? line[i] : ',';
                if (ch == ',')
                {
                    if (i == fieldStart) return false;                    // an empty field
                    if (i < line.Length) columns++;
                    fieldStart = i + 1;
                    continue;
                }
                bool digit = ch >= '0' && ch <= '9';
                if (!digit && ch != '.' && ch != '-') return false;
                if (columns == 2)
                {
                    if (!digit) return false;
                    value = value * 10 + (ulong)(ch - '0');
                    if (value > uint.MaxValue) return false;
                }
            }
            if (columns != TelemetryCsv.ColumnCount) return false;
            seq = (uint)value;
            return true;
        }

        /// <summary>
        /// The rows of a CSV text, in the order they stand. <paramref name="hasHeader"/> tells whether the first
        /// line is the header; lines that are not rows are left out and counted in <paramref name="skipped"/>.
        /// </summary>
        public static List<string> Rows(string text, out bool hasHeader, out int skipped)
        {
            var rows = new List<string>();
            hasHeader = false;
            skipped = 0;
            if (string.IsNullOrEmpty(text)) return rows;
            int start = 0;
            bool first = true;
            while (start < text.Length)
            {
                int end = text.IndexOf('\n', start);
                if (end < 0) end = text.Length;
                int length = end - start;
                if (length > 0 && text[end - 1] == '\r') length--;       // a file that went through a Windows editor
                string line = text.Substring(start, length);
                start = end + 1;
                if (first)
                {
                    first = false;
                    if (string.Equals(line, TelemetryCsv.Header, StringComparison.Ordinal))
                    {
                        hasHeader = true;
                        continue;
                    }
                }
                if (line.Length == 0) continue;
                uint seq;
                if (TryReadSeq(line, out seq)) rows.Add(line);
                else skipped++;
            }
            return rows;
        }

        /// <summary>
        /// Reads a <c>perf.csv</c>. <paramref name="intact"/> is false when the file has to be written again to be
        /// well formed: a missing header, a torn last line, a line that is not a row. A file that does not exist
        /// reads as no rows and not intact.
        /// </summary>
        public static List<string> Read(string path, out bool intact)
        {
            intact = false;
            if (!File.Exists(path)) return new List<string>();
            string text;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, s_utf8))
                text = reader.ReadToEnd();
            bool hasHeader;
            int skipped;
            List<string> rows = Rows(text, out hasHeader, out skipped);
            intact = hasHeader && skipped == 0 && text.Length > 0 && text[text.Length - 1] == '\n' && text.IndexOf('\r') < 0;
            return rows;
        }

        /// <summary>True when each row's <c>seq</c> is above the one before it: the file is in <c>seq</c> order.</summary>
        public static bool IsAscending(List<string> rows)
        {
            uint previous = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                uint seq;
                TryReadSeq(rows[i], out seq);
                if (i > 0 && seq <= previous) return false;
                previous = seq;
            }
            return true;
        }

        /// <summary>
        /// Merges rows into a <c>perf.csv</c>, creating it when it does not exist. A row whose <c>seq</c> the file
        /// already holds is skipped; the others take their place in <c>seq</c> order. Nothing already in the file
        /// is ever dropped: a file whose rows are not in <c>seq</c> order (a phone that named no session and
        /// started again from 0) keeps its rows as they stand and gets the new ones at its end. The file is
        /// replaced in one step, so a reader sees the old file or the new one. Returns how many rows were added.
        /// </summary>
        public static int MergeFile(string path, List<string> incoming)
        {
            bool intact;
            List<string> existing = Read(path, out intact);
            var known = new HashSet<uint>();
            for (int i = 0; i < existing.Count; i++)
            {
                uint seq;
                TryReadSeq(existing[i], out seq);
                known.Add(seq);
            }

            var fresh = new List<KeyValuePair<uint, string>>();
            if (incoming != null)
            {
                for (int i = 0; i < incoming.Count; i++)
                {
                    uint seq;
                    if (TryReadSeq(incoming[i], out seq) && known.Add(seq)) fresh.Add(new KeyValuePair<uint, string>(seq, incoming[i]));
                }
            }
            if (fresh.Count == 0 && intact) return 0;
            fresh.Sort((a, b) => a.Key.CompareTo(b.Key));

            var text = new StringBuilder((existing.Count + fresh.Count + 1) * 160);
            TelemetryCsv.AppendHeader(text);
            if (IsAscending(existing))
            {
                int e = 0, f = 0;
                while (e < existing.Count || f < fresh.Count)
                {
                    uint seq = 0;
                    if (e < existing.Count) TryReadSeq(existing[e], out seq);
                    bool takeExisting = f >= fresh.Count || (e < existing.Count && seq < fresh[f].Key);
                    text.Append(takeExisting ? existing[e++] : fresh[f++].Value).Append('\n');
                }
            }
            else
            {
                for (int i = 0; i < existing.Count; i++) text.Append(existing[i]).Append('\n');
                for (int i = 0; i < fresh.Count; i++) text.Append(fresh[i].Value).Append('\n');
            }

            string temp = path + ".merge";
            File.WriteAllText(temp, text.ToString(), s_utf8);
            File.Move(temp, path, true);
            return fresh.Count;
        }
    }
}

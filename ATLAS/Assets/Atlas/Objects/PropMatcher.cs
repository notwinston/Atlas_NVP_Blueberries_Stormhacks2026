using System;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Objects
{
    /// <summary>
    /// Chooses the library prop that stands for a scanned object (standalone-phone plan D6 and section 6.4,
    /// "Matcher rule"). Candidates are the props of the object's category, each tried at the quarter turns its
    /// facing rule allows. A candidate's score is the profile fit F (the Forge's fit_score against the turned
    /// prop profile, when the scan's profile covers at least <see cref="MinCoverage"/>), plus
    /// <see cref="AspectWeight"/> times how unevenly the three axes must be stretched, minus a bonus for shared
    /// RoomPlan attributes. The lowest score wins; every candidate within <see cref="Tie"/> of it is tied and the
    /// smaller turn, then the ordinal-smaller id, breaks the tie. Engine-free, deterministic, allocation-free.
    /// tools/props/reference.py holds the Python reference of this rule.
    /// </summary>
    public static class PropMatcher
    {
        public const float AspectWeight = 0.5f, TagBonus = 0.03f, MaxTagBonus = 0.06f, Tie = 0.02f, PoorFit = 0.20f, MinCoverage = 0.30f;

        /// <summary>Cells along each side of a profile.</summary>
        public const int ProfileSide = 16;
        /// <summary>Bytes of a profile.</summary>
        public const int ProfileLength = ProfileSide * ProfileSide;

        // A size or extent is taken within these bounds when its logarithm is needed (the lower one is the Forge's
        // rule), so every score is a finite number and pass 2 always finds the lowest again.
        const double MinLength = 1e-9, MaxLength = 1e9;

        /// <summary>
        /// The best prop of the query's category, or false (with match.Index = -1) when the library has no prop of
        /// that category. Deterministic: the same query and library give the same match.
        /// </summary>
        public static bool Choose(in ObjectQuery q, PropEntry[] library, out PropMatch match)
        {
            match = new PropMatch { Index = -1 };
            if (library == null) return false;
            bool useProfile = Coverage(q.Profile) >= MinCoverage;
            int maxTurn = q.Facing == ObjectFacingRule.Box || q.Facing == ObjectFacingRule.Table ? 3 : 0;

            // Pass 1: the lowest score. Pass 2 recomputes the same numbers and picks among those tied with it,
            // so nothing is stored per candidate.
            double best = double.PositiveInfinity;
            bool any = false;
            for (int p = 0; p < library.Length; p++)
            {
                PropEntry prop = library[p];
                if (prop == null || prop.Category != q.Category) continue;
                for (int turn = 0; turn <= maxTurn; turn++)
                {
                    Score(in q, prop, turn, useProfile, out double fit, out double score);
                    if (!any || score < best) best = score;
                    any = true;
                }
            }
            if (!any) return false;

            int bestIndex = -1, bestTurn = 0;
            double bestFit = 0.0, bestScore = 0.0;
            for (int p = 0; p < library.Length; p++)
            {
                PropEntry prop = library[p];
                if (prop == null || prop.Category != q.Category) continue;
                for (int turn = 0; turn <= maxTurn; turn++)
                {
                    Score(in q, prop, turn, useProfile, out double fit, out double score);
                    if (!(score - best < Tie)) continue;
                    if (bestIndex < 0 || Before(turn, prop.Id, p, bestTurn, library[bestIndex].Id, bestIndex))
                    {
                        bestIndex = p;
                        bestTurn = turn;
                        bestFit = fit;
                        bestScore = score;
                    }
                }
            }
            match.Index = bestIndex;
            match.QuarterTurns = bestTurn;
            match.Fit = (float)bestFit;
            match.Score = (float)bestScore;
            return true;
        }

        /// <summary>F, and F + AspectWeight * A - tags, of one prop at one quarter turn (plan section 6.4, step 3).</summary>
        static void Score(in ObjectQuery q, PropEntry prop, int turn, bool useProfile, out double fit, out double score)
        {
            // The prop's size turned: odd turns swap w and d.
            float3 e = (turn & 1) != 0 ? new float3(prop.Size.z, prop.Size.y, prop.Size.x) : prop.Size;
            double l0 = Math.Log(Length(q.Size.x) / Length(e.x));
            double l1 = Math.Log(Length(q.Size.y) / Length(e.y));
            double l2 = Math.Log(Length(q.Size.z) / Length(e.z));
            double mean = (l0 + l1 + l2) / 3.0;
            double aspect = (Math.Abs(l0 - mean) + Math.Abs(l1 - mean) + Math.Abs(l2 - mean)) / 3.0;

            fit = useProfile ? FitScoreDouble(q.Profile, prop.Profile, turn) : 0.0;
            double tags = Math.Min(MaxTagBonus, TagBonus * (double)SharedTags(q.Tags, prop.Tags));
            score = fit + AspectWeight * aspect - tags;
        }

        static double Length(float value)
        {
            double v = value;
            if (!(v > MinLength)) return MinLength;         // NaN too
            return v < MaxLength ? v : MaxLength;
        }

        // Tie order: the smaller turn, then the ordinal-smaller id, then the smaller library index.
        static bool Before(int turn, string id, int index, int otherTurn, string otherId, int otherIndex)
        {
            if (turn != otherTurn) return turn < otherTurn;
            int byId = string.CompareOrdinal(id, otherId);
            if (byId != 0) return byId < 0;
            return index < otherIndex;
        }

        /// <summary>The number of distinct strings both lists hold (ordinal). Null lists and null strings count as none.</summary>
        public static int SharedTags(string[] a, string[] b)
        {
            if (a == null || b == null || a.Length == 0 || b.Length == 0) return 0;
            int shared = 0;
            for (int i = 0; i < a.Length; i++)
            {
                string tag = a[i];
                if (tag == null) continue;
                bool seen = false;
                for (int k = 0; k < i && !seen; k++) seen = string.Equals(a[k], tag, StringComparison.Ordinal);
                if (seen) continue;                         // a repeated query tag counts once
                for (int k = 0; k < b.Length; k++)
                {
                    if (string.Equals(b[k], tag, StringComparison.Ordinal)) { shared++; break; }
                }
            }
            return shared;
        }

        // ---- The profile, as station/forge/forge/profile.py reads it ------------------------------------------

        /// <summary>Cells that are not 0, divided by 256 (profile.py coverage). 0 for null or a wrong length.</summary>
        public static float Coverage(byte[] profile)
        {
            return (float)CoverageDouble(profile);
        }

        static double CoverageDouble(byte[] profile)
        {
            if (profile == null || profile.Length != ProfileLength) return 0.0;
            int seen = 0;
            for (int c = 0; c < ProfileLength; c++) if (profile[c] != 0) seen++;
            return seen / (double)ProfileLength;
        }

        /// <summary>
        /// profile.py fit_score(scan, rotate(generated, generatedTurns)) without building the turned profile: the
        /// mean, over the cells the scan saw, of |generated - scan| / 255. A null or short generated profile has no
        /// surface anywhere (height 0). Positive infinity when the scan saw no cell (or is null).
        /// </summary>
        public static float FitScore(byte[] scan, byte[] generated, int generatedTurns = 0)
        {
            return (float)FitScoreDouble(scan, generated, generatedTurns);
        }

        static double FitScoreDouble(byte[] scan, byte[] generated, int turns)
        {
            if (scan == null || scan.Length != ProfileLength) return double.PositiveInfinity;
            bool hasGenerated = generated != null && generated.Length == ProfileLength;
            int k = ((turns % 4) + 4) % 4;
            long sum = 0;
            int cells = 0;
            for (int j = 0; j < ProfileSide; j++)
            {
                for (int i = 0; i < ProfileSide; i++)
                {
                    int s = scan[j * ProfileSide + i];
                    if (s == 0) continue;
                    cells++;
                    int g = hasGenerated ? generated[SourceCell(i, j, k)] : 0;
                    sum += g > s ? g - s : s - g;
                }
            }
            if (cells == 0) return double.PositiveInfinity;
            return sum / (255.0 * cells);
        }

        /// <summary>
        /// The profile after the box's yaw has grown by quarterTurns quarter turns (profile.py rotate,
        /// ObjectProfile.Rotate): new cell (i, j) takes old cell (j, 15 - i) for one turn, (15 - i, 15 - j) for two,
        /// (15 - j, i) for three. Writes into <paramref name="into"/> (256 bytes), which must not be the source.
        /// </summary>
        public static void RotateProfile(byte[] profile, int quarterTurns, byte[] into)
        {
            if (profile == null || profile.Length != ProfileLength) throw new ArgumentException("A profile holds exactly 256 bytes.", nameof(profile));
            if (into == null || into.Length != ProfileLength) throw new ArgumentException("The target holds exactly 256 bytes.", nameof(into));
            if (ReferenceEquals(profile, into)) throw new ArgumentException("The target must not be the source.", nameof(into));
            int k = ((quarterTurns % 4) + 4) % 4;
            for (int j = 0; j < ProfileSide; j++)
                for (int i = 0; i < ProfileSide; i++)
                    into[j * ProfileSide + i] = profile[SourceCell(i, j, k)];
        }

        /// <summary>A new turned profile; null for null.</summary>
        public static byte[] RotateProfile(byte[] profile, int quarterTurns)
        {
            if (profile == null) return null;
            var turned = new byte[ProfileLength];
            RotateProfile(profile, quarterTurns, turned);
            return turned;
        }

        // Index of the old cell that new cell (i, j) takes after k quarter turns (k in 0..3).
        static int SourceCell(int i, int j, int k)
        {
            const int last = ProfileSide - 1;
            int oi, oj;
            switch (k)
            {
                case 1: oi = j; oj = last - i; break;
                case 2: oi = last - i; oj = last - j; break;
                case 3: oi = last - j; oj = i; break;
                default: oi = i; oj = j; break;
            }
            return oj * ProfileSide + oi;
        }
    }
}

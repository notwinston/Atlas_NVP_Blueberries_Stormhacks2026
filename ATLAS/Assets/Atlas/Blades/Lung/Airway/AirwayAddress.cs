using System;
using Atlas.Kernel;

namespace Atlas.Blades.Lung
{
    // Branch address = string over {L, R}; the root is "" (test with Length, never null). Ordinal operations only (MP §10.2.3).
    public static class AirwayAddress
    {
        public static bool IsValid(string a)
        {
            if (a == null || a.Length > RuleS.MaxAddressLength) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != 'L' && a[i] != 'R') return false;
            return true;
        }

        public static string Parent(string a)
        {
            if (string.IsNullOrEmpty(a)) throw new ArgumentException("the root has no parent", nameof(a));
            return a.Substring(0, a.Length - 1);
        }

        public static bool IsPrefixOf(string a, string b) => b.StartsWith(a, StringComparison.Ordinal);
        public static bool IsProperPrefixOf(string a, string b) => b.Length > a.Length && IsPrefixOf(a, b);
    }
}

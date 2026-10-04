namespace Atlas.Blades.FieldFit.BlindSpotFinder
{
    /// <summary>SplitMix64 PRNG (Brief §3.3). Deterministic across platforms.</summary>
    public sealed class SplitMix64
    {
        ulong _s;
        public SplitMix64(ulong seed) { _s = seed; }

        public ulong NextULong()
        {
            unchecked
            {
                _s += 0x9E3779B97F4A7C15UL;
                ulong z = _s;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>Uniform in [0, 1): (NextULong() >> 11) * 2^-53.</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);
    }
}

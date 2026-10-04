// Lane A engine-free core, P2-A24 (breathing chest). Plain C#, System only. Nothing here allocates.
using System;

namespace Atlas.Lung.Core
{
    /// <summary>
    /// Weights of the chest's blend shapes phase_0 .. phase_9 (case contract section 8: base + phase_k is the
    /// organ at phase k) for a continuous phase, wrapping 9 -> 0: weight 1 - t on phase k and t on phase k + 1.
    /// Weights are fractions that sum to 1; Unity's SetBlendShapeWeight takes 100 times these.
    /// </summary>
    public static class BlendShapePlayer
    {
        /// <summary>Continuous phase in [0, phases) at a time, one breath per period, phase 0 at time 0.</summary>
        public static double PhaseAt(double timeSeconds, double periodSeconds, int phases = 10)
        {
            double x = timeSeconds / periodSeconds;
            double p = (x - Math.Floor(x)) * phases;
            return p >= phases ? 0.0 : p;
        }

        /// <summary>Fills <paramref name="weights"/> (one per phase) for a phase given in bins; any real number, wrapped.</summary>
        public static void Weights(double phase, float[] weights)
        {
            int n = weights.Length;
            double p = phase - Math.Floor(phase / n) * n;
            if (p >= n) p = 0.0;
            int k = (int)Math.Floor(p);
            if (k >= n) k = n - 1;
            float t = (float)(p - k);
            for (int i = 0; i < n; i++) weights[i] = 0f;
            weights[(k + 1) % n] = t;
            weights[k] = 1f - t;        // written last so that t = 0 gives exactly 1 when n = 1
        }

        /// <summary>
        /// Largest distance any vertex moves between two weight sets. <paramref name="targets"/> holds one array
        /// per blend shape, 3 floats per vertex (displacements from the base, as in the .glb), in the units of
        /// the mesh (display metres for a case package).
        /// </summary>
        public static double MaxVertexStep(float[][] targets, float[] weightsA, float[] weightsB)
        {
            int n = targets.Length;
            int vertices = targets[0].Length / 3;
            double worst = 0.0;
            for (int v = 0; v < vertices; v++)
            {
                double dx = 0.0, dy = 0.0, dz = 0.0;
                for (int k = 0; k < n; k++)
                {
                    float dw = weightsB[k] - weightsA[k];
                    if (dw == 0f) continue;
                    float[] t = targets[k];
                    dx += dw * t[3 * v]; dy += dw * t[3 * v + 1]; dz += dw * t[3 * v + 2];
                }
                double d2 = dx * dx + dy * dy + dz * dz;
                if (d2 > worst) worst = d2;
            }
            return Math.Sqrt(worst);
        }

        /// <summary>
        /// Largest per-frame vertex step over one whole breath played at a frame rate, and the largest step on
        /// the frames that straddle the wrap from the last phase back to phase 0 (P2-A24: "no visible jump at
        /// the wrap"). <paramref name="scratchA"/> and <paramref name="scratchB"/> are weight buffers of the caller.
        /// </summary>
        public static double MaxStepOverBreath(float[][] targets, double framesPerSecond, double periodSeconds,
                                               float[] scratchA, float[] scratchB, out double maxStepAcrossWrap, out int worstFrame)
        {
            int n = targets.Length;
            int frames = (int)Math.Ceiling(framesPerSecond * periodSeconds);
            double worst = 0.0;
            maxStepAcrossWrap = 0.0;
            worstFrame = 0;
            for (int f = 0; f < frames; f++)
            {
                double p0 = PhaseAt(f / framesPerSecond, periodSeconds, n);
                double p1 = PhaseAt((f + 1) / framesPerSecond, periodSeconds, n);
                Weights(p0, scratchA);
                Weights(p1, scratchB);
                double step = MaxVertexStep(targets, scratchA, scratchB);
                if (step > worst) { worst = step; worstFrame = f; }
                // Frames in the interval phase n - 1 -> phase 0, and the frame that crosses phase 0.
                bool wrap = p0 >= n - 1 || p1 < p0;
                if (wrap && step > maxStepAcrossWrap) maxStepAcrossWrap = step;
            }
            return worst;
        }
    }
}

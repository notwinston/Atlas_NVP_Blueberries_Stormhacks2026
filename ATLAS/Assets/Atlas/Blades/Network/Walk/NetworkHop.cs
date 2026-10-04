namespace Atlas.Blades.Network.Walk
{
    public enum HopPhase : byte { Idle = 0, FadeOut = 1, FadeIn = 2 }

    // V3 hop (design section 4 V3): the world fades to black for 0.25 s, the kernel re-roots at the neighbour node, and the
    // world fades back in for 0.25 s. Engine-free; time is passed in. Level is the fade: 0 clear, 1 black. Tick returns true
    // on the one call that reaches the re-root: the caller re-roots the walker (IFrameLoop.ResetWalker) and logs net.hop then.
    // Allocates nothing.
    public sealed class NetworkHop
    {
        public const double FadeOutSeconds = 0.25, FadeInSeconds = 0.25;

        double _start;

        public HopPhase Phase { get; private set; }
        public bool Active { get { return Phase != HopPhase.Idle; } }
        public int From { get; private set; }
        public int To { get; private set; }
        public double Level { get; private set; }

        // False while a hop is already running.
        public bool Begin(int from, int to, double nowSeconds)
        {
            if (Active) return false;
            From = from; To = to; _start = nowSeconds;
            Phase = HopPhase.FadeOut; Level = 0;
            return true;
        }

        public bool Tick(double nowSeconds)
        {
            if (Phase == HopPhase.Idle) return false;
            double t = nowSeconds - _start;
            if (Phase == HopPhase.FadeOut)
            {
                if (t < FadeOutSeconds) { Level = Clamp01(t / FadeOutSeconds); return false; }
                Phase = HopPhase.FadeIn; Level = 1;
                return true;                                    // black now: re-root
            }
            double u = t - FadeOutSeconds;
            if (u >= FadeInSeconds) { Phase = HopPhase.Idle; Level = 0; return false; }
            Level = 1 - Clamp01(u / FadeInSeconds);
            return false;
        }

        static double Clamp01(double x) { return x < 0 ? 0 : x > 1 ? 1 : x; }
    }
}

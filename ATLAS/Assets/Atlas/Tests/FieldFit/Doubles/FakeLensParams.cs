namespace Atlas.Blades.FieldFit.Tests
{
    // Master Plan §10.4.6; test double for ILensParamSource. Angles in degrees as registered.
    internal sealed class FakeLensParams : ILensParamSource
    {
        public float EDeg = 54f, Rho0Deg = 10f, E1Deg = 4f, T = 1f;
        public int Profile = 1;            // Morph
        public bool On = true;
        public bool FailSetOn;             // SetLensOn(false) does not change the fake (latch test)
        public int SetOnCalls;
        public bool TryRead(out float eDeg, out float rho0Deg, out float e1Deg, out float t, out int profile, out bool on)
        { eDeg = EDeg; rho0Deg = Rho0Deg; e1Deg = E1Deg; t = T; profile = Profile; on = On; return true; }
        public void SetLensOn(bool on) { SetOnCalls++; if (!FailSetOn) On = on; }
    }
}

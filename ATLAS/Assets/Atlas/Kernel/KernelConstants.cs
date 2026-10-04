namespace Atlas.Kernel
{
    /// <summary>Constants of the geometry kernel (Master Plan sections 4.6 and 8.5).</summary>
    public static class KernelConstants
    {
        /// <summary>A floor displacement longer than this in one frame is ignored by the geometry. Metres (D8, section 4.6).</summary>
        public const double MaxStepMetres = 0.2;
        /// <summary>The walker changes tile only once this far past an edge. Kernel units (R3 section A5).</summary>
        public const double CrossHysteresis = 0.02;
        /// <summary>Upper bound on re-roots inside one Step (R3 section A5).</summary>
        public const int MaxCrossingsPerStep = 4;
        /// <summary>Default fog and cull radius rho. Kernel units (D13, section 8.5).</summary>
        public const double DefaultCullRho = 3.0;
        /// <summary>Tile side used before a RoomModel is active. Metres (the lap square of the 3 x 3 m sample room).</summary>
        public const double DefaultFlatTileSideMetres = 2.0;
        /// <summary>Metres per kernel unit at kappa = 0.</summary>
        public const double FlatMetresPerUnit = 1.0;
        /// <summary>Inclusive tolerance on the radius test of VisibleSet. Kernel units.</summary>
        public const double RadiusEpsilon = 1e-9;
    }
}

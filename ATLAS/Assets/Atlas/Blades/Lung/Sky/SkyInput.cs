using Unity.Mathematics;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>Per-step input of the needle sky (MP §6.3 step 5).</summary>
    public struct SkyInput
    {
        public double3 Gaze; public float2 Stick; public bool Confirm;
        public bool ConsolePick; public double ConsoleAzDeg, ConsoleElDeg; public float DeltaTime;
    }
}

using Unity.Mathematics;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>What the presenter draws for one frame.</summary>
    public readonly struct NeedleSkyFrame
    {
        public readonly float3 HeadPosition; public readonly quaternion HeadRotation;
        public readonly int Phase; public readonly bool RobustOn;
        public readonly float3 Cursor; public readonly int PickCount; public readonly float3 Pick0, Pick1, Pick2;
        public readonly bool ArcsOn;                    // B11: true when PickCount == 3
        public readonly NeedleSkyReadout Readout;

        public NeedleSkyFrame(float3 headPosition, quaternion headRotation, int phase, bool robustOn, float3 cursor,
            int pickCount, float3 pick0, float3 pick1, float3 pick2, bool arcsOn, NeedleSkyReadout readout)
        {
            HeadPosition = headPosition; HeadRotation = headRotation; Phase = phase; RobustOn = robustOn; Cursor = cursor;
            PickCount = pickCount; Pick0 = pick0; Pick1 = pick1; Pick2 = pick2; ArcsOn = arcsOn; Readout = readout;
        }
    }

    public interface INeedleSkyPresenter { void SetVisible(bool visible); void Present(in NeedleSkyFrame frame); }
}

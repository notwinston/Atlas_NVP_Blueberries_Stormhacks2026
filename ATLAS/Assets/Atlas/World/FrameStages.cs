// PLACEHOLDER (lane A, P1-A08): contract surface only; replaced by lane A's implementation
namespace Atlas.World
{
    /// <summary>Step 4 of the frame loop (Master Plan §6.3): the kernel step. Optional on a module (deviation PH-3).</summary>
    public interface IKernelStage { void KernelStep(in FrameContext frame); }

    /// <summary>Step 6 of the frame loop (Master Plan §6.3): upload. Optional on a module (deviation PH-3).</summary>
    public interface IUploadStage { void Upload(in FrameContext frame); }
}

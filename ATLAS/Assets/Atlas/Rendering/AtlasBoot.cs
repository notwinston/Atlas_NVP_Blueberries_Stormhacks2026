using UnityEngine;

namespace Atlas.Rendering
{
    /// <summary>Frame pacing for the whole app (Master Plan Appendix C.4). iOS defaults to 30 fps.</summary>
    static class AtlasBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }
    }
}

#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Atlas.Rendering
{
    /// <summary>
    /// NSProcessInfo.thermalState: 0 nominal, 1 fair, 2 serious, 3 critical.
    /// Adaptive Performance does not report it on iOS (Build Pipeline §1.4). Always 0 off the device.
    /// </summary>
    public static class AtlasThermal
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern int AtlasThermalState();

        public static int State => AtlasThermalState();
#else
        public static int State => 0;
#endif
    }
}

using System;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
using System.Text;
#endif
using UnityEngine;

namespace Atlas.Scan.ARKit
{
    /// <summary>
    /// The ARKit side of scan route 4 (Master Plan section 4.2, task P1-C05): a RoomPlan capture on the
    /// running ARSession through Assets/Plugins/iOS/AtlasRoomPlan.swift, read as the walls file of
    /// Atlas.World.WallBridgeJson. Registers itself before the first scene loads; nothing references this
    /// assembly (contract README F1). In the Editor every call does nothing and <see cref="Available"/> is
    /// false (contracts F9).
    /// </summary>
    public sealed class ArKitWallBridge : IWallBridge
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterAtStartUp()
        {
            WallBridge.Register(new ArKitWallBridge());
        }

        public string Name { get { return "RoomPlan"; } }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int AtlasRoomPlanSupported();
        [DllImport("__Internal")] static extern int AtlasRoomPlanStart(IntPtr arSession);
        [DllImport("__Internal")] static extern unsafe int AtlasRoomPlanCopyJson(byte* buffer, int capacity);
        [DllImport("__Internal")] static extern void AtlasRoomPlanStop();

        // What XRSessionSubsystem.nativePtr points to on ARKit (the package's UnityXRNativePtrs.h).
        [StructLayout(LayoutKind.Sequential)]
        struct NativeSession
        {
            public int Version;
            public IntPtr Session;      // ARSession*
        }

        byte[] _buffer = new byte[16 * 1024];

        public bool Available { get { return AtlasRoomPlanSupported() != 0; } }

        public bool Start(IntPtr nativeSession)
        {
            if (nativeSession == IntPtr.Zero) return false;
            IntPtr arSession = Marshal.PtrToStructure<NativeSession>(nativeSession).Session;
            return arSession != IntPtr.Zero && AtlasRoomPlanStart(arSession) != 0;
        }

        public unsafe string TakeJson()
        {
            // The plugin answers with the length of the file; a buffer that was too short is left untouched.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int length;
                fixed (byte* buffer = _buffer) length = AtlasRoomPlanCopyJson(buffer, _buffer.Length);
                if (length <= 0) return "";
                if (length <= _buffer.Length) return Encoding.UTF8.GetString(_buffer, 0, length);
                _buffer = new byte[length + 4096];
            }
            return "";
        }

        public void Stop()
        {
            AtlasRoomPlanStop();
        }
#else
        public bool Available { get { return false; } }

        public bool Start(IntPtr nativeSession)
        {
            return false;
        }

        public string TakeJson()
        {
            return "";
        }

        public void Stop()
        {
        }
#endif
    }
}

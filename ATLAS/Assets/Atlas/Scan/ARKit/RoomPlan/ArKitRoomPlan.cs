using System;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
using System.Text;
#endif
using UnityEngine;

namespace Atlas.Scan.ARKit
{
    /// <summary>
    /// The ARKit side of the scan's one RoomPlan session (standalone-phone plan D2): a RoomCaptureSession on the
    /// running ARSession through Assets/Plugins/iOS/AtlasRoomPlan.swift, read as the atlas.room/1 file of
    /// Atlas.World.RoomPlanJson. Registers itself before the first scene loads; nothing references this
    /// assembly (contract README F1). In the Editor every call does nothing and <see cref="Available"/> is
    /// false (contracts F9).
    /// </summary>
    public sealed class ArKitRoomPlan : IRoomPlanCapture
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterAtStartUp()
        {
            RoomPlanPlugin.Register(new ArKitRoomPlan());
        }

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

        // Objects make the file larger than the walls alone did: 64 KiB holds a furnished room.
        byte[] _buffer = new byte[64 * 1024];

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
                _buffer = new byte[length + 16 * 1024];
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

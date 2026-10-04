using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>One crop of one object. Fields map one to one onto a views[] entry of the Forge's meta (Appendix B.4).</summary>
    public struct ObjectView
    {
        /// <summary>At most 200 kB; the receiver owns the array.</summary>
        public byte[] Jpeg;
        /// <summary>Pixels of the crop, at most 768.</summary>
        public int Width, Height;
        /// <summary>0..1.</summary>
        public float Score;
        /// <summary>About the box, degrees.</summary>
        public float AzimuthDeg;
        /// <summary>fx, fy, cx, cy of the crop, in crop pixels.</summary>
        public float4 Intrinsics;
        /// <summary>Camera frame to the box's local frame (origin at the base centre), metres.</summary>
        public float4x4 CamToBox;
    }

    /// <summary>The photos of one object, ready for the Forge.</summary>
    public sealed class ObjectPhotoSet
    {
        /// <summary>boxId.</summary>
        public int ObjectId;
        public ObjectCategory Category;
        /// <summary>w, h, d, metres.</summary>
        public float3 Size;
        /// <summary>Radians, room frame.</summary>
        public float Yaw;
        /// <summary>3000..5000.</summary>
        public int TriTarget;
        /// <summary>1 to 3; Views[0] feeds generation.</summary>
        public ObjectView[] Views;
    }

    /// <summary>Where object photos go. Every call is on the main thread and returns at once: the receiver
    /// queues its own network work.</summary>
    public interface IObjectPhotoSink
    {
        /// <summary>The RoomModel is final. Called once per scan, before any Submit. roomJson is RoomModelJson.Write(room).</summary>
        void BeginRoom(RoomModel room, string roomJson);
        /// <summary>The crops of one object, largest objects first.</summary>
        void Submit(ObjectPhotoSet photos);
        /// <summary>The object has no usable photo: it takes its built-in mesh at once.</summary>
        void ReportNoPhoto(int objectId);
        /// <summary>Every object with a triangle target has been submitted or reported.</summary>
        void EndRoom();
    }

    /// <summary>For the Editor and tests: counts calls, keeps nothing.</summary>
    public sealed class NullObjectPhotoSink : IObjectPhotoSink
    {
        public int RoomsBegun { get; private set; }
        public int Submitted { get; private set; }
        public int NoPhoto { get; private set; }
        public int RoomsEnded { get; private set; }

        public void BeginRoom(RoomModel room, string roomJson) { RoomsBegun++; }
        public void Submit(ObjectPhotoSet photos) { Submitted++; }
        public void ReportNoPhoto(int objectId) { NoPhoto++; }
        public void EndRoom() { RoomsEnded++; }
    }
}

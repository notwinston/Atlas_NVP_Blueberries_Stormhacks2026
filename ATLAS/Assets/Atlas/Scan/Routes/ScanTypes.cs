using System;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Scan
{
    /// <summary>Where a scan is (contract section 10.2).</summary>
    public enum ScanState : byte
    { Idle = 0, Starting = 1, Sweeping = 2, Processing = 3, AwaitingConfirm = 4, Finalising = 5, Done = 6, Failed = 7 }

    /// <summary>What to tell the walker. The last four are RoomPlan's own instructions.</summary>
    public enum ScanHint : byte
    { None = 0, PointAtFloor = 1, KeepSweeping = 2, TapCorners = 3, SlowDown = 4, TurnOnLight = 5, MoveCloseToWall = 6, MoveAwayFromWall = 7 }

    /// <summary>What a scan may use.</summary>
    public struct ScanOptions
    {
        /// <summary>Route 2: the classified LiDAR mesh.</summary>
        public bool UseMesh;
        /// <summary>Route 3: furniture boxes.</summary>
        public bool UseBoxes;
        /// <summary>Metres, used when no wall plane gives a height.</summary>
        public float WallHeight;
        /// <summary>Null: yyyyMMdd_HHmmss from the clock, UTC.</summary>
        public string RoomId;

        public static ScanOptions Default
        {
            get { return new ScanOptions { UseMesh = true, UseBoxes = true, WallHeight = RoomModel.DefaultWallHeight, RoomId = null }; }
        }
    }

    /// <summary>How far a scan has come; what the sweep view shows.</summary>
    public struct ScanProgress
    {
        /// <summary>Since Begin.</summary>
        public float Seconds;
        /// <summary>Highest route with something to show.</summary>
        public ScanRoute RouteReached;
        /// <summary>The floor height y0 is known.</summary>
        public bool FloorFound;
        /// <summary>Square metres of floor cells found so far.</summary>
        public float FloorArea;
        /// <summary>Inside the preview outline, 0..1.</summary>
        public float UnknownShare;
        public int MeshTriangles;
        public int BoxCount;
        /// <summary>Corners placed by hand.</summary>
        public int CornerCount;
        /// <summary>0..96.</summary>
        public int KeyFrames;
        public TrackingState Tracking;
        public ScanHint Hint;
        /// <summary>Rises when a new preview outline is ready.</summary>
        public int PreviewVersion;
    }

    /// <summary>What the walker is asked to confirm: everything a view needs to draw it on the floor.</summary>
    public sealed class OutlineProposal
    {
        /// <summary>AR session frame, floor points (x, z), counter-clockwise.</summary>
        public float2[] Outline;
        public int Count;
        /// <summary>y0 in the session frame.</summary>
        public float FloorY;
        public ScanRoute Route;
        /// <summary>The RoomModel this outline gives, in its own room frame. Preview.Anchor is the pose of that
        /// frame in the session frame: session = Anchor.Position + RotateY(Anchor.Yaw) * (x, 0, z).</summary>
        public RoomModel Preview;

        /// <summary>A floor point of the preview's room frame, in the session frame.</summary>
        public float2 ToSession(float2 roomXZ)
        {
            float3 p = Preview.Anchor.Position + math.mul(quaternion.RotateY(Preview.Anchor.Yaw), new float3(roomXZ.x, 0f, roomXZ.y));
            return p.xz;
        }

        /// <summary>The four corners of the free rectangle in the session frame, counter-clockwise. into holds at least 4.</summary>
        public void CopyFreeRect(float2[] into)
        {
            CopyRectangle(0.5f * Preview.FreeRect.Wf, 0.5f * Preview.FreeRect.Df, into);
        }

        /// <summary>The four corners of the lap square in the session frame, counter-clockwise. into holds at least 4.</summary>
        public void CopyLapSquare(float2[] into)
        {
            CopyRectangle(0.5f * Preview.LapSide, 0.5f * Preview.LapSide, into);
        }

        void CopyRectangle(float halfX, float halfZ, float2[] into)
        {
            into[0] = ToSession(new float2(-halfX, -halfZ));
            into[1] = ToSession(new float2(halfX, -halfZ));
            into[2] = ToSession(new float2(halfX, halfZ));
            into[3] = ToSession(new float2(-halfX, halfZ));
        }
    }

    /// <summary>The scan as onboarding drives it (contract section 10.3). Phone in the hand.</summary>
    public interface IScanSession
    {
        ScanState State { get; }
        ScanFailure Failure { get; }
        ScanProgress Progress { get; }
        event Action<ScanState> StateChanged;

        /// <summary>LiDAR meshing exists on this device.</summary>
        bool MeshAvailable { get; }
        /// <summary>RoomPlan boxes exist on this device.</summary>
        bool BoxesAvailable { get; }

        /// <summary>Accepted in Idle, Done and Failed.</summary>
        void Begin(in ScanOptions options);
        /// <summary>Per-frame work: called by onboarding's Update while State is not Idle. Reads the head from
        /// IPosePipeline.Raw, never from a transform.</summary>
        void Tick(float deltaTime);

        /// <summary>Ray-casts a screen point (pixels) to the floor and adds a corner. False when nothing is hit.</summary>
        bool TryAddCorner(Vector2 screenPoint);
        /// <summary>Adds a corner at the floor point under the phone.</summary>
        void AddCornerAtFeet();
        void RemoveLastCorner();

        /// <summary>Copies the newest preview outline (session frame). Returns the number of points.</summary>
        int CopyPreviewOutline(float2[] into);

        void EndSweep();

        /// <summary>Valid in AwaitingConfirm.</summary>
        OutlineProposal Proposal { get; }
        /// <summary>Replaces the outline (session frame, at least 3 points, any winding) and recomputes.</summary>
        void SetOutline(float2[] outline, int count);
        void MoveCorner(int index, float2 floorPoint);
        void InsertCorner(int afterIndex, float2 floorPoint);
        void RemoveCorner(int index);
        void Confirm();
        void Cancel();

        /// <summary>Valid in Done.</summary>
        RoomModel Result { get; }
        /// <summary>The ARAnchor's transform; null where there is no anchor subsystem.</summary>
        Transform ResultAnchor { get; }
    }
}

using System;

namespace Atlas.Link
{
    /// <summary><c>TPing</c> (0x03, unreliable, 13 bytes): the first leg of a clock exchange.</summary>
    public struct TPingMessage
    {
        public const int Size = 13;

        public uint Seq;
        /// <summary>Sender's clock when the ping left, microseconds.</summary>
        public long T1Us;

        public readonly int WriteTo(byte[] buffer, int offset)
        {
            if (!Wire.InRange(buffer, offset, Size)) throw new ArgumentException("Buffer too small for TPing");
            int o = offset;
            buffer[o++] = MessageId.TPing;
            Wire.PutU32(buffer, ref o, Seq);
            Wire.PutI64(buffer, ref o, T1Us);
            return o - offset;
        }

        public bool ReadFrom(byte[] buffer, int offset, int length)
        {
            if (length < Size || !Wire.InRange(buffer, offset, length) || buffer[offset] != MessageId.TPing) return false;
            int o = offset + 1;
            Seq = Wire.GetU32(buffer, ref o);
            T1Us = Wire.GetI64(buffer, ref o);
            return true;
        }
    }

    /// <summary><c>TPong</c> (0x04, unreliable, 29 bytes): the answer to a <c>TPing</c>.</summary>
    public struct TPongMessage
    {
        public const int Size = 29;

        public uint Seq;
        /// <summary>Echo of the ping's <c>t1</c>.</summary>
        public long T1Us;
        /// <summary>Answerer's clock when the ping arrived.</summary>
        public long T2Us;
        /// <summary>Answerer's clock when the pong left.</summary>
        public long T3Us;

        public readonly int WriteTo(byte[] buffer, int offset)
        {
            if (!Wire.InRange(buffer, offset, Size)) throw new ArgumentException("Buffer too small for TPong");
            int o = offset;
            buffer[o++] = MessageId.TPong;
            Wire.PutU32(buffer, ref o, Seq);
            Wire.PutI64(buffer, ref o, T1Us);
            Wire.PutI64(buffer, ref o, T2Us);
            Wire.PutI64(buffer, ref o, T3Us);
            return o - offset;
        }

        public bool ReadFrom(byte[] buffer, int offset, int length)
        {
            if (length < Size || !Wire.InRange(buffer, offset, length) || buffer[offset] != MessageId.TPong) return false;
            int o = offset + 1;
            Seq = Wire.GetU32(buffer, ref o);
            T1Us = Wire.GetI64(buffer, ref o);
            T2Us = Wire.GetI64(buffer, ref o);
            T3Us = Wire.GetI64(buffer, ref o);
            return true;
        }
    }

    /// <summary>
    /// <c>ParamSet</c> (0x21, channel 0): <c>count u8</c>, then <c>count x {id u16, value f32}</c>. One instance
    /// is reused for every send and receive, so applying a slider change allocates nothing.
    /// </summary>
    public sealed class ParamSetMessage
    {
        public const int MaxCount = 255;
        public const int HeaderSize = 2;
        public const int EntrySize = 6;
        public const int MaxSize = HeaderSize + MaxCount * EntrySize;

        public readonly ushort[] Ids = new ushort[MaxCount];
        public readonly float[] Values = new float[MaxCount];

        public int Count { get; private set; }

        /// <summary>Bytes this message takes on the wire.</summary>
        public int Size { get { return HeaderSize + Count * EntrySize; } }

        public void Clear() { Count = 0; }

        /// <summary>Appends one assignment. Returns false when the message is full.</summary>
        public bool Add(ushort id, float value)
        {
            if (Count >= MaxCount) return false;
            Ids[Count] = id;
            Values[Count] = value;
            Count++;
            return true;
        }

        public int WriteTo(byte[] buffer, int offset)
        {
            if (!Wire.InRange(buffer, offset, Size)) throw new ArgumentException("Buffer too small for ParamSet");
            int o = offset;
            buffer[o++] = MessageId.ParamSet;
            buffer[o++] = (byte)Count;
            for (int i = 0; i < Count; i++)
            {
                Wire.PutU16(buffer, ref o, Ids[i]);
                Wire.PutF32(buffer, ref o, Values[i]);
            }
            return o - offset;
        }

        public bool ReadFrom(byte[] buffer, int offset, int length)
        {
            if (length < HeaderSize || !Wire.InRange(buffer, offset, length) || buffer[offset] != MessageId.ParamSet) return false;
            int count = buffer[offset + 1];
            if (length < HeaderSize + count * EntrySize) return false;
            int o = offset + HeaderSize;
            for (int i = 0; i < count; i++)
            {
                Ids[i] = Wire.GetU16(buffer, ref o);
                Values[i] = Wire.GetF32(buffer, ref o);
            }
            Count = count;
            return true;
        }
    }

    /// <summary>
    /// <c>Telemetry</c> (0x30, channel 0, 91 bytes): one row per second from the perf probe (Master Plan
    /// Appendix B.2, section 7.3.3). Field order here is wire order and CSV column order.
    /// </summary>
    public struct TelemetryMessage
    {
        public const int Size = 91;

        public long TimeUs;
        public uint Seq;
        public ushort SceneId;
        public byte SpaceId;
        public byte Tier;

        public float Fps, P50, P95, P99, Max;
        /// <summary>Share of frames over 17.2 ms and over 33.8 ms (Master Plan section 11.2).</summary>
        public float Over17_2, Over33_8;
        public float CpuMainP95, CpuRenderP95, GpuP95, RenderScale;

        public ushort DrawCalls, SetPass, TrisK, MemMB, VisibleTiles, Copies, RttMs, LossPermille, CueCount;

        public uint GcAllocB;

        /// <summary>Thermal state 0-3.</summary>
        public byte Thermal;
        public byte Battery, Tracking, Msaa, StateHz, BumpMax, AssetsPlaced, AssetsPending;

        /// <summary>CSV header naming every field in wire order, as the Hub and the phone write it.</summary>
        public const string CsvHeader =
            "t_us,seq,sceneId,spaceId,tier,fps,p50,p95,p99,max,over17_2,over33_8,cpuMainP95,cpuRenderP95,gpuP95," +
            "renderScale,drawCalls,setPass,trisK,memMB,visibleTiles,copies,rttMs,lossPermille,cueCount,gcAllocB," +
            "thermal,battery,tracking,msaa,stateHz,bumpMax,assetsPlaced,assetsPending";

        public readonly int WriteTo(byte[] buffer, int offset)
        {
            if (!Wire.InRange(buffer, offset, Size)) throw new ArgumentException("Buffer too small for Telemetry");
            int o = offset;
            buffer[o++] = MessageId.Telemetry;
            Wire.PutI64(buffer, ref o, TimeUs);
            Wire.PutU32(buffer, ref o, Seq);
            Wire.PutU16(buffer, ref o, SceneId);
            buffer[o++] = SpaceId;
            buffer[o++] = Tier;
            Wire.PutF32(buffer, ref o, Fps);
            Wire.PutF32(buffer, ref o, P50);
            Wire.PutF32(buffer, ref o, P95);
            Wire.PutF32(buffer, ref o, P99);
            Wire.PutF32(buffer, ref o, Max);
            Wire.PutF32(buffer, ref o, Over17_2);
            Wire.PutF32(buffer, ref o, Over33_8);
            Wire.PutF32(buffer, ref o, CpuMainP95);
            Wire.PutF32(buffer, ref o, CpuRenderP95);
            Wire.PutF32(buffer, ref o, GpuP95);
            Wire.PutF32(buffer, ref o, RenderScale);
            Wire.PutU16(buffer, ref o, DrawCalls);
            Wire.PutU16(buffer, ref o, SetPass);
            Wire.PutU16(buffer, ref o, TrisK);
            Wire.PutU16(buffer, ref o, MemMB);
            Wire.PutU16(buffer, ref o, VisibleTiles);
            Wire.PutU16(buffer, ref o, Copies);
            Wire.PutU16(buffer, ref o, RttMs);
            Wire.PutU16(buffer, ref o, LossPermille);
            Wire.PutU16(buffer, ref o, CueCount);
            Wire.PutU32(buffer, ref o, GcAllocB);
            buffer[o++] = Thermal;
            buffer[o++] = Battery;
            buffer[o++] = Tracking;
            buffer[o++] = Msaa;
            buffer[o++] = StateHz;
            buffer[o++] = BumpMax;
            buffer[o++] = AssetsPlaced;
            buffer[o++] = AssetsPending;
            return o - offset;
        }

        public bool ReadFrom(byte[] buffer, int offset, int length)
        {
            if (length < Size || !Wire.InRange(buffer, offset, length) || buffer[offset] != MessageId.Telemetry) return false;
            int o = offset + 1;
            TimeUs = Wire.GetI64(buffer, ref o);
            Seq = Wire.GetU32(buffer, ref o);
            SceneId = Wire.GetU16(buffer, ref o);
            SpaceId = buffer[o++];
            Tier = buffer[o++];
            Fps = Wire.GetF32(buffer, ref o);
            P50 = Wire.GetF32(buffer, ref o);
            P95 = Wire.GetF32(buffer, ref o);
            P99 = Wire.GetF32(buffer, ref o);
            Max = Wire.GetF32(buffer, ref o);
            Over17_2 = Wire.GetF32(buffer, ref o);
            Over33_8 = Wire.GetF32(buffer, ref o);
            CpuMainP95 = Wire.GetF32(buffer, ref o);
            CpuRenderP95 = Wire.GetF32(buffer, ref o);
            GpuP95 = Wire.GetF32(buffer, ref o);
            RenderScale = Wire.GetF32(buffer, ref o);
            DrawCalls = Wire.GetU16(buffer, ref o);
            SetPass = Wire.GetU16(buffer, ref o);
            TrisK = Wire.GetU16(buffer, ref o);
            MemMB = Wire.GetU16(buffer, ref o);
            VisibleTiles = Wire.GetU16(buffer, ref o);
            Copies = Wire.GetU16(buffer, ref o);
            RttMs = Wire.GetU16(buffer, ref o);
            LossPermille = Wire.GetU16(buffer, ref o);
            CueCount = Wire.GetU16(buffer, ref o);
            GcAllocB = Wire.GetU32(buffer, ref o);
            Thermal = buffer[o++];
            Battery = buffer[o++];
            Tracking = buffer[o++];
            Msaa = buffer[o++];
            StateHz = buffer[o++];
            BumpMax = buffer[o++];
            AssetsPlaced = buffer[o++];
            AssetsPending = buffer[o++];
            return true;
        }
    }

    /// <summary>One object box of a <c>RoomOutline</c>: 26 bytes.</summary>
    public struct RoomOutlineBox
    {
        public const int Size = 26;

        public byte Id;
        /// <summary>RoomPlan category index.</summary>
        public byte Category;
        /// <summary>Centre on the floor, width, depth, height in metres; yaw in radians.</summary>
        public float Cx, Cz, W, D, H, Yaw;
    }

    /// <summary>
    /// <c>RoomOutline</c> (0x41, channel 0): the floor polygon, the ceiling height, the free rectangle and the
    /// object boxes, so the Spectator can draw the scanned room (Master Plan Appendix B.2). Arrays are sized for
    /// the largest message, so one instance can be reused.
    /// </summary>
    public sealed class RoomOutlineMessage
    {
        public const int MaxPoints = 255;
        public const int MaxBoxes = 255;
        public const int MaxSize = 2 + MaxPoints * 8 + 4 + 20 + 1 + MaxBoxes * RoomOutlineBox.Size;

        /// <summary>Floor polygon as x0, z0, x1, z1, ... in the room frame.</summary>
        public readonly float[] PointsXZ = new float[MaxPoints * 2];
        public int PointCount;
        public float Height;
        /// <summary>The free rectangle: centre x, centre z, Wf, Df, yaw.</summary>
        public float RectCx, RectCz, RectW, RectD, RectYaw;
        public readonly RoomOutlineBox[] Boxes = new RoomOutlineBox[MaxBoxes];
        public int BoxCount;

        public int Size { get { return 2 + PointCount * 8 + 4 + 20 + 1 + BoxCount * RoomOutlineBox.Size; } }

        public void Clear()
        {
            PointCount = 0;
            BoxCount = 0;
            Height = 0f;
            RectCx = RectCz = RectW = RectD = RectYaw = 0f;
        }

        /// <summary>Appends a floor point. Returns false when the polygon is full.</summary>
        public bool AddPoint(float x, float z)
        {
            if (PointCount >= MaxPoints) return false;
            PointsXZ[PointCount * 2] = x;
            PointsXZ[PointCount * 2 + 1] = z;
            PointCount++;
            return true;
        }

        /// <summary>Appends an object box. Returns false when the list is full.</summary>
        public bool AddBox(in RoomOutlineBox box)
        {
            if (BoxCount >= MaxBoxes) return false;
            Boxes[BoxCount++] = box;
            return true;
        }

        public int WriteTo(byte[] buffer, int offset)
        {
            if (PointCount < 0 || PointCount > MaxPoints || BoxCount < 0 || BoxCount > MaxBoxes)
                throw new InvalidOperationException("RoomOutline counts are out of range");
            if (!Wire.InRange(buffer, offset, Size)) throw new ArgumentException("Buffer too small for RoomOutline");
            int o = offset;
            buffer[o++] = MessageId.RoomOutline;
            buffer[o++] = (byte)PointCount;
            for (int i = 0; i < PointCount * 2; i++) Wire.PutF32(buffer, ref o, PointsXZ[i]);
            Wire.PutF32(buffer, ref o, Height);
            Wire.PutF32(buffer, ref o, RectCx);
            Wire.PutF32(buffer, ref o, RectCz);
            Wire.PutF32(buffer, ref o, RectW);
            Wire.PutF32(buffer, ref o, RectD);
            Wire.PutF32(buffer, ref o, RectYaw);
            buffer[o++] = (byte)BoxCount;
            for (int i = 0; i < BoxCount; i++)
            {
                buffer[o++] = Boxes[i].Id;
                buffer[o++] = Boxes[i].Category;
                Wire.PutF32(buffer, ref o, Boxes[i].Cx);
                Wire.PutF32(buffer, ref o, Boxes[i].Cz);
                Wire.PutF32(buffer, ref o, Boxes[i].W);
                Wire.PutF32(buffer, ref o, Boxes[i].D);
                Wire.PutF32(buffer, ref o, Boxes[i].H);
                Wire.PutF32(buffer, ref o, Boxes[i].Yaw);
            }
            return o - offset;
        }

        public bool ReadFrom(byte[] buffer, int offset, int length)
        {
            if (length < 2 || !Wire.InRange(buffer, offset, length) || buffer[offset] != MessageId.RoomOutline) return false;
            int points = buffer[offset + 1];
            int boxCountAt = 2 + points * 8 + 4 + 20;
            if (length < boxCountAt + 1) return false;
            int boxes = buffer[offset + boxCountAt];
            if (length < boxCountAt + 1 + boxes * RoomOutlineBox.Size) return false;

            int o = offset + 2;
            for (int i = 0; i < points * 2; i++) PointsXZ[i] = Wire.GetF32(buffer, ref o);
            Height = Wire.GetF32(buffer, ref o);
            RectCx = Wire.GetF32(buffer, ref o);
            RectCz = Wire.GetF32(buffer, ref o);
            RectW = Wire.GetF32(buffer, ref o);
            RectD = Wire.GetF32(buffer, ref o);
            RectYaw = Wire.GetF32(buffer, ref o);
            o++; // boxCount
            for (int i = 0; i < boxes; i++)
            {
                Boxes[i].Id = buffer[o++];
                Boxes[i].Category = buffer[o++];
                Boxes[i].Cx = Wire.GetF32(buffer, ref o);
                Boxes[i].Cz = Wire.GetF32(buffer, ref o);
                Boxes[i].W = Wire.GetF32(buffer, ref o);
                Boxes[i].D = Wire.GetF32(buffer, ref o);
                Boxes[i].H = Wire.GetF32(buffer, ref o);
                Boxes[i].Yaw = Wire.GetF32(buffer, ref o);
            }
            PointCount = points;
            BoxCount = boxes;
            return true;
        }
    }
}

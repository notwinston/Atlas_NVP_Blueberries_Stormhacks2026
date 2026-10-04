using System;

namespace Atlas.Link
{
    /// <summary>
    /// <c>State</c> (0x10, channel 1, 202 bytes): everything the Spectator needs to redraw the walker's frame
    /// (Master Plan Appendix B.1). A plain struct of fields with no arrays, so filling, copying and sending it
    /// allocates nothing. The link sets <see cref="Seq"/> and <see cref="TimeUs"/> when it sends.
    /// </summary>
    public struct StateMessage
    {
        /// <summary>Size on the wire, message id included.</summary>
        public const int Size = 202;

        public const int TileLength = 6;
        public const int XformLength = 9;
        public const int ParamLength = 8;

        // Indices into p[] fixed by Appendix B.1.
        public const int ParamL = 0;
        public const int ParamFogStart = 1;
        public const int ParamCullRho = 2;
        public const int ParamSliceW = 3;

        const byte FlagTrackingMask = 0x03;
        const byte FlagPoseJump = 0x04;
        const byte FlagCue = 0x08;
        const byte FlagTierMask = 0x30;
        const byte FlagDemoLock = 0x40;

        /// <summary>Bits 0-1 tracking, bit 2 pose jump rejected, bit 3 warning cue active, bits 4-5 quality tier, bit 6 demo lock.</summary>
        public byte Flags;
        /// <summary>SpaceLab module or use-case scene.</summary>
        public ushort SceneId;
        /// <summary>Counts sends. Written by the link.</summary>
        public uint Seq;
        /// <summary>Phone monotonic clock in microseconds. Written by the link.</summary>
        public long TimeUs;

        /// <summary>Physical head position in the RoomModel anchor frame, metres.</summary>
        public float HeadPosX, HeadPosY, HeadPosZ;
        /// <summary>Physical head rotation, quaternion x, y, z, w.</summary>
        public float HeadRotX, HeadRotY, HeadRotZ, HeadRotW;

        /// <summary>0-6 = S0-S6.</summary>
        public byte SpaceId;
        /// <summary>0 clear, 1 warn, 2 strong, 3 tracking lost.</summary>
        public byte BumpLevel;

        /// <summary>{4,5}: the six Z[phi] integers; S1: cell (i, j, k, 0, 0, 0); S4: face index; S5: room id; else 0.</summary>
        public long Tile0, Tile1, Tile2, Tile3, Tile4, Tile5;

        /// <summary>Mode 2 cell index or portal sheet.</summary>
        public short Sheet;

        /// <summary>Tile-local transform, 3x3 row-major doubles; identity when unused.</summary>
        public double Xform0, Xform1, Xform2, Xform3, Xform4, Xform5, Xform6, Xform7, Xform8;

        /// <summary>[0] L, [1] fog start, [2] cull rho, [3] slice w, [4..7] 4D angles or spare.</summary>
        public float P0, P1, P2, P3, P4, P5, P6, P7;

        /// <summary>Bumps on any regeneration of the world.</summary>
        public ushort WorldVer;

        /// <summary>A state with the identity head rotation and the identity tile-local transform.</summary>
        public static StateMessage Identity
        {
            get
            {
                var s = new StateMessage();
                s.HeadRotW = 1f;
                s.SetXformIdentity();
                return s;
            }
        }

        // ---- flags -------------------------------------------------------------------------------------

        /// <summary>0 normal, 1 limited, 2 lost.</summary>
        public int Tracking
        {
            get { return Flags & FlagTrackingMask; }
            set { Flags = (byte)((Flags & ~FlagTrackingMask) | (value & FlagTrackingMask)); }
        }

        public bool PoseJumpRejected
        {
            get { return (Flags & FlagPoseJump) != 0; }
            set { Flags = (byte)(value ? Flags | FlagPoseJump : Flags & ~FlagPoseJump); }
        }

        public bool CueActive
        {
            get { return (Flags & FlagCue) != 0; }
            set { Flags = (byte)(value ? Flags | FlagCue : Flags & ~FlagCue); }
        }

        /// <summary>Quality tier 0-3 of the thermal ladder.</summary>
        public int QualityTier
        {
            get { return (Flags & FlagTierMask) >> 4; }
            set { Flags = (byte)((Flags & ~FlagTierMask) | ((value & 3) << 4)); }
        }

        public bool DemoLock
        {
            get { return (Flags & FlagDemoLock) != 0; }
            set { Flags = (byte)(value ? Flags | FlagDemoLock : Flags & ~FlagDemoLock); }
        }

        // ---- indexed access to the fixed-length groups -------------------------------------------------

        public long GetTile(int i)
        {
            switch (i)
            {
                case 0: return Tile0; case 1: return Tile1; case 2: return Tile2;
                case 3: return Tile3; case 4: return Tile4; case 5: return Tile5;
                default: throw new ArgumentOutOfRangeException("i");
            }
        }

        public void SetTile(int i, long value)
        {
            switch (i)
            {
                case 0: Tile0 = value; break; case 1: Tile1 = value; break; case 2: Tile2 = value; break;
                case 3: Tile3 = value; break; case 4: Tile4 = value; break; case 5: Tile5 = value; break;
                default: throw new ArgumentOutOfRangeException("i");
            }
        }

        public void SetTile(long t0, long t1, long t2, long t3, long t4, long t5)
        {
            Tile0 = t0; Tile1 = t1; Tile2 = t2; Tile3 = t3; Tile4 = t4; Tile5 = t5;
        }

        /// <summary>Element of the tile-local transform, row-major index 0..8.</summary>
        public double GetXform(int i)
        {
            switch (i)
            {
                case 0: return Xform0; case 1: return Xform1; case 2: return Xform2;
                case 3: return Xform3; case 4: return Xform4; case 5: return Xform5;
                case 6: return Xform6; case 7: return Xform7; case 8: return Xform8;
                default: throw new ArgumentOutOfRangeException("i");
            }
        }

        public void SetXform(int i, double value)
        {
            switch (i)
            {
                case 0: Xform0 = value; break; case 1: Xform1 = value; break; case 2: Xform2 = value; break;
                case 3: Xform3 = value; break; case 4: Xform4 = value; break; case 5: Xform5 = value; break;
                case 6: Xform6 = value; break; case 7: Xform7 = value; break; case 8: Xform8 = value; break;
                default: throw new ArgumentOutOfRangeException("i");
            }
        }

        public void SetXformIdentity()
        {
            Xform0 = 1; Xform1 = 0; Xform2 = 0;
            Xform3 = 0; Xform4 = 1; Xform5 = 0;
            Xform6 = 0; Xform7 = 0; Xform8 = 1;
        }

        public float GetParam(int i)
        {
            switch (i)
            {
                case 0: return P0; case 1: return P1; case 2: return P2; case 3: return P3;
                case 4: return P4; case 5: return P5; case 6: return P6; case 7: return P7;
                default: throw new ArgumentOutOfRangeException("i");
            }
        }

        public void SetParam(int i, float value)
        {
            switch (i)
            {
                case 0: P0 = value; break; case 1: P1 = value; break; case 2: P2 = value; break; case 3: P3 = value; break;
                case 4: P4 = value; break; case 5: P5 = value; break; case 6: P6 = value; break; case 7: P7 = value; break;
                default: throw new ArgumentOutOfRangeException("i");
            }
        }

        // ---- wire --------------------------------------------------------------------------------------

        /// <summary>
        /// Packs the message at <paramref name="offset"/> and returns the bytes written (always
        /// <see cref="Size"/>). The buffer must have room; nothing is allocated.
        /// </summary>
        public readonly int WriteTo(byte[] buffer, int offset)
        {
            if (!Wire.InRange(buffer, offset, Size)) throw new ArgumentException("Buffer too small for State");
            int o = offset;
            buffer[o++] = MessageId.State;
            buffer[o++] = Flags;
            Wire.PutU16(buffer, ref o, SceneId);
            Wire.PutU32(buffer, ref o, Seq);
            Wire.PutI64(buffer, ref o, TimeUs);
            Wire.PutF32(buffer, ref o, HeadPosX);
            Wire.PutF32(buffer, ref o, HeadPosY);
            Wire.PutF32(buffer, ref o, HeadPosZ);
            Wire.PutF32(buffer, ref o, HeadRotX);
            Wire.PutF32(buffer, ref o, HeadRotY);
            Wire.PutF32(buffer, ref o, HeadRotZ);
            Wire.PutF32(buffer, ref o, HeadRotW);
            buffer[o++] = SpaceId;
            buffer[o++] = BumpLevel;
            Wire.PutI64(buffer, ref o, Tile0);
            Wire.PutI64(buffer, ref o, Tile1);
            Wire.PutI64(buffer, ref o, Tile2);
            Wire.PutI64(buffer, ref o, Tile3);
            Wire.PutI64(buffer, ref o, Tile4);
            Wire.PutI64(buffer, ref o, Tile5);
            Wire.PutI16(buffer, ref o, Sheet);
            Wire.PutF64(buffer, ref o, Xform0);
            Wire.PutF64(buffer, ref o, Xform1);
            Wire.PutF64(buffer, ref o, Xform2);
            Wire.PutF64(buffer, ref o, Xform3);
            Wire.PutF64(buffer, ref o, Xform4);
            Wire.PutF64(buffer, ref o, Xform5);
            Wire.PutF64(buffer, ref o, Xform6);
            Wire.PutF64(buffer, ref o, Xform7);
            Wire.PutF64(buffer, ref o, Xform8);
            Wire.PutF32(buffer, ref o, P0);
            Wire.PutF32(buffer, ref o, P1);
            Wire.PutF32(buffer, ref o, P2);
            Wire.PutF32(buffer, ref o, P3);
            Wire.PutF32(buffer, ref o, P4);
            Wire.PutF32(buffer, ref o, P5);
            Wire.PutF32(buffer, ref o, P6);
            Wire.PutF32(buffer, ref o, P7);
            Wire.PutU16(buffer, ref o, WorldVer);
            return o - offset;
        }

        /// <summary>
        /// Reads a packed message of <paramref name="length"/> bytes. Returns false, leaving this value untouched,
        /// when the id is not State or the packet is too short. Bytes after the 202nd are ignored.
        /// </summary>
        public bool ReadFrom(byte[] buffer, int offset, int length)
        {
            if (length < Size || !Wire.InRange(buffer, offset, length) || buffer[offset] != MessageId.State) return false;
            int o = offset + 1;
            Flags = buffer[o++];
            SceneId = Wire.GetU16(buffer, ref o);
            Seq = Wire.GetU32(buffer, ref o);
            TimeUs = Wire.GetI64(buffer, ref o);
            HeadPosX = Wire.GetF32(buffer, ref o);
            HeadPosY = Wire.GetF32(buffer, ref o);
            HeadPosZ = Wire.GetF32(buffer, ref o);
            HeadRotX = Wire.GetF32(buffer, ref o);
            HeadRotY = Wire.GetF32(buffer, ref o);
            HeadRotZ = Wire.GetF32(buffer, ref o);
            HeadRotW = Wire.GetF32(buffer, ref o);
            SpaceId = buffer[o++];
            BumpLevel = buffer[o++];
            Tile0 = Wire.GetI64(buffer, ref o);
            Tile1 = Wire.GetI64(buffer, ref o);
            Tile2 = Wire.GetI64(buffer, ref o);
            Tile3 = Wire.GetI64(buffer, ref o);
            Tile4 = Wire.GetI64(buffer, ref o);
            Tile5 = Wire.GetI64(buffer, ref o);
            Sheet = Wire.GetI16(buffer, ref o);
            Xform0 = Wire.GetF64(buffer, ref o);
            Xform1 = Wire.GetF64(buffer, ref o);
            Xform2 = Wire.GetF64(buffer, ref o);
            Xform3 = Wire.GetF64(buffer, ref o);
            Xform4 = Wire.GetF64(buffer, ref o);
            Xform5 = Wire.GetF64(buffer, ref o);
            Xform6 = Wire.GetF64(buffer, ref o);
            Xform7 = Wire.GetF64(buffer, ref o);
            Xform8 = Wire.GetF64(buffer, ref o);
            P0 = Wire.GetF32(buffer, ref o);
            P1 = Wire.GetF32(buffer, ref o);
            P2 = Wire.GetF32(buffer, ref o);
            P3 = Wire.GetF32(buffer, ref o);
            P4 = Wire.GetF32(buffer, ref o);
            P5 = Wire.GetF32(buffer, ref o);
            P6 = Wire.GetF32(buffer, ref o);
            P7 = Wire.GetF32(buffer, ref o);
            WorldVer = Wire.GetU16(buffer, ref o);
            return true;
        }

        // Offsets of the two fields the link stamps at send time, so a relay or a sender can patch them in place.
        internal const int SeqOffset = 4;
        internal const int TimeOffset = 8;
    }
}

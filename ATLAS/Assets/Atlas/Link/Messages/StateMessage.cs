// PLACEHOLDER (lane D, P1-D01): contract surface only; replaced by lane D's implementation
namespace Atlas.Link
{
    // Master Plan App. B.1 (State 0x10), §6.5. Field names are the B.1 names in PascalCase. Packing is not part of the placeholder.
    public struct StateMessage
    {
        public const byte MsgType = 0x10;
        public const int SizeBytes = 202;

        public byte Flags;
        public ushort SceneId;
        public uint Seq;
        public long TUs;
        public float[] HeadPos;   // 3, m, RoomModel anchor frame
        public float[] HeadRot;   // 4, [x, y, z, w]
        public byte SpaceId;      // u8 0-6
        public byte BumpLevel;
        public long[] TileId;     // 6: Xu, Xv, Yu, Yv, Tu, Tv (kernel.md §5.4)
        public short Sheet;
        public double[] Xform;    // 9, row-major Local.M (kernel.md §4.5)
        public float[] P;         // 8: [0] L, [1] rho-1, [2] rho, [3] slice w, [4..7] spare
        public ushort WorldVer;

        // Allocates the arrays once.
        public static StateMessage Create() => new StateMessage
        {
            HeadPos = new float[3],
            HeadRot = new float[4],
            TileId = new long[6],
            Xform = new double[9],
            P = new float[8],
        };
    }
}

namespace Atlas.Scan
{
    /// <summary>
    /// Raw-pointer kernels for depth images (contract README F2): no engine type, no native container.
    /// </summary>
    public static unsafe class DepthKernel
    {
        /// <summary>
        /// Copies a width x height image of 32-bit floats, whose rows are rowStride bytes apart and whose
        /// pixels are pixelStride bytes apart, into a tightly packed row-major buffer.
        /// </summary>
        public static void CopyRows(byte* source, int rowStride, int pixelStride, int width, int height, float* destination)
        {
            if (pixelStride == sizeof(float))
            {
                for (int y = 0; y < height; y++)
                {
                    float* row = (float*)(source + (long)y * rowStride);
                    float* into = destination + y * width;
                    for (int x = 0; x < width; x++) into[x] = row[x];
                }
                return;
            }
            for (int y = 0; y < height; y++)
            {
                byte* row = source + (long)y * rowStride;
                float* into = destination + y * width;
                for (int x = 0; x < width; x++) into[x] = *(float*)(row + (long)x * pixelStride);
            }
        }

        /// <summary>How many values of a depth buffer are returns (DepthFrame.IsReturn). For the start-up check of gate G1.</summary>
        public static int CountReturns(float* depth, int count)
        {
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                float d = depth[i];
                if (d >= Atlas.World.DepthFrame.MinRange && d <= Atlas.World.DepthFrame.MaxRange) n++;
            }
            return n;
        }
    }
}

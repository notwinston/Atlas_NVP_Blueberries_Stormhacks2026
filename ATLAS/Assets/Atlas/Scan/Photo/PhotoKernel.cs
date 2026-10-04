using System;

namespace Atlas.Scan
{
    /// <summary>
    /// Raw-pointer kernels of the photo capture (contract README F2): no engine type, no native container, no
    /// allocation. The adapter and the tests hand in pinned buffers.
    /// </summary>
    public static unsafe class PhotoKernel
    {
        /// <summary>Width the luma image is reduced to before its sharpness is measured.</summary>
        public const int SharpnessWidth = 160;

        /// <summary>
        /// Copies every step-th pixel of every step-th row of an image plane into a tightly packed buffer of
        /// outWidth x outHeight pixels. Rows of the source are rowStride bytes apart, pixels pixelStride bytes
        /// apart; bytesPerPixel bytes are kept of each.
        /// </summary>
        public static void CopyPlane(byte* source, int rowStride, int pixelStride, int bytesPerPixel, int step,
            int outWidth, int outHeight, byte* destination)
        {
            long rowBytes = (long)outWidth * bytesPerPixel;
            if (step == 1 && pixelStride == bytesPerPixel)
            {
                for (int y = 0; y < outHeight; y++)
                    Buffer.MemoryCopy(source + (long)y * rowStride, destination + y * rowBytes, rowBytes, rowBytes);
                return;
            }
            long hop = (long)step * pixelStride;
            for (int y = 0; y < outHeight; y++)
            {
                byte* from = source + (long)y * step * rowStride;
                byte* into = destination + y * rowBytes;
                if (bytesPerPixel == 1)
                {
                    for (int x = 0; x < outWidth; x++) into[x] = from[x * hop];
                    continue;
                }
                for (int x = 0; x < outWidth; x++)
                {
                    byte* pixel = from + x * hop;
                    for (int b = 0; b < bytesPerPixel; b++) *into++ = pixel[b];
                }
            }
        }

        /// <summary>Depth in metres to 16-bit millimetres; a value that is no return (DepthFrame.IsReturn) becomes 0.</summary>
        public static void DepthToMillimetres(float* metres, int count, ushort* millimetres)
        {
            for (int i = 0; i < count; i++)
            {
                float d = metres[i];
                millimetres[i] = d >= Atlas.World.DepthFrame.MinRange && d <= Atlas.World.DepthFrame.MaxRange
                    ? (ushort)(d * 1000f + 0.5f)
                    : (ushort)0;
            }
        }

        /// <summary>
        /// Variance of the 4-neighbour Laplacian of one channel of an image reduced to 160 pixels wide by
        /// point sampling. Higher is sharper; a flat image gives 0.
        /// </summary>
        public static float Sharpness(byte* plane, int width, int height, int bytesPerPixel, int channel)
        {
            if (width < 3 || height < 3) return 0f;
            int rw = width < SharpnessWidth ? width : SharpnessWidth;
            int rh = (int)((long)height * rw / width);
            if (rh < 3) return 0f;

            double sum = 0.0, sumSq = 0.0;
            int n = 0;
            for (int y = 1; y < rh - 1; y++)
            {
                byte* up = plane + (long)((long)(y - 1) * height / rh) * width * bytesPerPixel + channel;
                byte* mid = plane + (long)((long)y * height / rh) * width * bytesPerPixel + channel;
                byte* down = plane + (long)((long)(y + 1) * height / rh) * width * bytesPerPixel + channel;
                for (int x = 1; x < rw - 1; x++)
                {
                    long c = (long)x * width / rw * bytesPerPixel;
                    long l = (long)(x - 1) * width / rw * bytesPerPixel;
                    long r = (long)(x + 1) * width / rw * bytesPerPixel;
                    int lap = 4 * mid[c] - mid[l] - mid[r] - up[c] - down[c];
                    sum += lap;
                    sumSq += (double)lap * lap;
                    n++;
                }
            }
            if (n == 0) return 0f;
            double mean = sum / n;
            double variance = sumSq / n - mean * mean;
            return variance > 0.0 ? (float)variance : 0f;
        }

        /// <summary>
        /// A crop of a full-range YCbCr 4:2:0 image (a luma plane and a half-size plane of Cb, Cr pairs) as
        /// RGBA, point sampled to outWidth x outHeight. bottomUp writes the last image row first, the row
        /// order of Unity's image encoders.
        /// </summary>
        public static void CropYCbCr(byte* luma, byte* chroma, int width, int height, in CropRect crop,
            int outWidth, int outHeight, byte* rgba, bool bottomUp)
        {
            int chromaWidth = width / 2, chromaHeight = height / 2;
            for (int oy = 0; oy < outHeight; oy++)
            {
                int sy = SourceIndex(crop.Y, crop.Height, oy, outHeight, height);
                int cy = sy / 2 < chromaHeight ? sy / 2 : chromaHeight - 1;
                byte* lumaRow = luma + (long)sy * width;
                byte* chromaRow = chroma + (long)cy * chromaWidth * 2;
                byte* into = rgba + (long)(bottomUp ? outHeight - 1 - oy : oy) * outWidth * 4;
                for (int ox = 0; ox < outWidth; ox++)
                {
                    int sx = SourceIndex(crop.X, crop.Width, ox, outWidth, width);
                    int cx = sx / 2 < chromaWidth ? sx / 2 : chromaWidth - 1;
                    int y = lumaRow[sx];
                    int cb = chromaRow[cx * 2] - 128;
                    int cr = chromaRow[cx * 2 + 1] - 128;
                    // BT.601 full range, 8-bit fixed point.
                    into[0] = Clamp(y + ((359 * cr) >> 8));
                    into[1] = Clamp(y - ((88 * cb + 183 * cr) >> 8));
                    into[2] = Clamp(y + ((454 * cb) >> 8));
                    into[3] = 255;
                    into += 4;
                }
            }
        }

        /// <summary>
        /// A crop of a packed colour image as RGBA. red, green and blue are the byte offsets of the channels
        /// inside a pixel of bytesPerPixel bytes.
        /// </summary>
        public static void CropPacked(byte* pixels, int width, int height, int bytesPerPixel, int red, int green, int blue,
            in CropRect crop, int outWidth, int outHeight, byte* rgba, bool bottomUp)
        {
            for (int oy = 0; oy < outHeight; oy++)
            {
                int sy = SourceIndex(crop.Y, crop.Height, oy, outHeight, height);
                byte* row = pixels + (long)sy * width * bytesPerPixel;
                byte* into = rgba + (long)(bottomUp ? outHeight - 1 - oy : oy) * outWidth * 4;
                for (int ox = 0; ox < outWidth; ox++)
                {
                    byte* pixel = row + (long)SourceIndex(crop.X, crop.Width, ox, outWidth, width) * bytesPerPixel;
                    into[0] = pixel[red];
                    into[1] = pixel[green];
                    into[2] = pixel[blue];
                    into[3] = 255;
                    into += 4;
                }
            }
        }

        // The source pixel under the centre of output pixel o, kept inside the image.
        static int SourceIndex(int start, int length, int o, int outLength, int limit)
        {
            int s = start + (int)(((long)o * 2 + 1) * length / (2L * outLength));
            return s < 0 ? 0 : (s >= limit ? limit - 1 : s);
        }

        static byte Clamp(int v)
        {
            return (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
        }
    }
}

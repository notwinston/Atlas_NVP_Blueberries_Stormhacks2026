using System;
using System.Text;
using Unity.Mathematics;

namespace Atlas.Telemetry.Tests
{
    /// <summary>
    /// A GLB that meets the asset contract, made in memory with an exact triangle count and file size, for the
    /// validator's limits: a top and a bottom plane of fine cells over a box of 1.0 x 0.5 x 1.0 m.
    /// </summary>
    static class TestGlb
    {
        public static readonly float3 Dims = new float3(1.0f, 0.5f, 1.0f);
        const int Cells = 70;                       // per side and plane: up to 19,600 triangles

        /// <param name="triangles">How many triangles the index buffer names, 1 to 19,600.</param>
        /// <param name="padVertices">Extra vertices nobody uses: they only make the file larger.</param>
        public static byte[] Make(int triangles, int padVertices = 0)
        {
            int side = Cells + 1, planeVertices = side * side;
            int vertexCount = 2 * planeVertices + padVertices;
            var position = new float[vertexCount * 3];
            var normal = new float[vertexCount * 3];
            var uv = new float[vertexCount * 2];
            var colour = new byte[vertexCount * 4];
            for (int v = 0; v < vertexCount; v++)
            {
                bool top = v < planeVertices;
                int k = v < 2 * planeVertices ? v % planeVertices : 0;
                float x = v < 2 * planeVertices ? -0.5f + (k % side) / (float)Cells : 0f;
                float z = v < 2 * planeVertices ? -0.5f + (k / side) / (float)Cells : 0f;
                position[3 * v] = x; position[3 * v + 1] = top ? Dims.y : 0f; position[3 * v + 2] = z;
                normal[3 * v + 1] = top ? 1f : -1f;
                uv[2 * v] = 0.5f; uv[2 * v + 1] = 0.5f;
                colour[4 * v] = colour[4 * v + 1] = colour[4 * v + 2] = colour[4 * v + 3] = 255;
            }

            var index = new ushort[triangles * 3];
            int t = 0;
            for (int plane = 0; plane < 2 && t < triangles; plane++)
            {
                int first = plane * planeVertices;
                for (int j = 0; j < Cells && t < triangles; j++)
                {
                    for (int i = 0; i < Cells && t < triangles; i++)
                    {
                        int p00 = first + j * side + i, p10 = p00 + 1, p01 = p00 + side, p11 = p01 + 1;
                        index[3 * t] = (ushort)p00; index[3 * t + 1] = (ushort)p10; index[3 * t + 2] = (ushort)p11;
                        t++;
                        if (t >= triangles) break;
                        index[3 * t] = (ushort)p00; index[3 * t + 1] = (ushort)p11; index[3 * t + 2] = (ushort)p01;
                        t++;
                    }
                }
            }
            if (t != triangles) throw new ArgumentException("too many triangles for the grid");

            var blob = new System.IO.MemoryStream();
            var views = new StringBuilder();
            AppendView(blob, views, ToBytes(index), 34963);
            AppendView(blob, views, ToBytes(position), 34962);
            AppendView(blob, views, ToBytes(normal), 34962);
            AppendView(blob, views, ToBytes(uv), 34962);
            AppendView(blob, views, colour, 34962);
            while (blob.Length % 4 != 0) blob.WriteByte(0);

            string json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}],\"nodes\":[{\"mesh\":0}]," +
                "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":1,\"NORMAL\":2,\"TEXCOORD_0\":3,\"COLOR_0\":4},\"indices\":0,\"mode\":4}]}]," +
                "\"accessors\":[{\"bufferView\":0,\"componentType\":5123,\"count\":" + index.Length + ",\"type\":\"SCALAR\"}," +
                "{\"bufferView\":1,\"componentType\":5126,\"count\":" + vertexCount + ",\"type\":\"VEC3\"}," +
                "{\"bufferView\":2,\"componentType\":5126,\"count\":" + vertexCount + ",\"type\":\"VEC3\"}," +
                "{\"bufferView\":3,\"componentType\":5126,\"count\":" + vertexCount + ",\"type\":\"VEC2\"}," +
                "{\"bufferView\":4,\"componentType\":5121,\"normalized\":true,\"count\":" + vertexCount + ",\"type\":\"VEC4\"}]," +
                "\"bufferViews\":[" + views + "],\"buffers\":[{\"byteLength\":" + blob.Length + "}]}";
            byte[] text = Encoding.UTF8.GetBytes(json);
            int jsonLength = text.Length + (4 - text.Length % 4) % 4;
            int total = 12 + 8 + jsonLength + 8 + (int)blob.Length;
            var file = new byte[total];
            BitConverter.GetBytes(0x46546C67u).CopyTo(file, 0);
            BitConverter.GetBytes(2u).CopyTo(file, 4);
            BitConverter.GetBytes((uint)total).CopyTo(file, 8);
            BitConverter.GetBytes((uint)jsonLength).CopyTo(file, 12);
            BitConverter.GetBytes(0x4E4F534Au).CopyTo(file, 16);
            text.CopyTo(file, 20);
            for (int k = text.Length; k < jsonLength; k++) file[20 + k] = (byte)' ';
            BitConverter.GetBytes((uint)blob.Length).CopyTo(file, 20 + jsonLength);
            BitConverter.GetBytes(0x004E4942u).CopyTo(file, 24 + jsonLength);
            blob.ToArray().CopyTo(file, 28 + jsonLength);
            return file;
        }

        static void AppendView(System.IO.MemoryStream blob, StringBuilder views, byte[] data, int target)
        {
            while (blob.Length % 4 != 0) blob.WriteByte(0);
            if (views.Length > 0) views.Append(',');
            views.Append("{\"buffer\":0,\"byteOffset\":").Append(blob.Length).Append(",\"byteLength\":").Append(data.Length)
                .Append(",\"target\":").Append(target).Append('}');
            blob.Write(data, 0, data.Length);
        }

        static byte[] ToBytes(Array values)
        {
            int size = System.Buffer.ByteLength(values);
            var bytes = new byte[size];
            System.Buffer.BlockCopy(values, 0, bytes, 0, size);
            return bytes;
        }
    }
}

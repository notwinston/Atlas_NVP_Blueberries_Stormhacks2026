using Atlas.Kernel.FourD;
using Atlas.Kernel;
using Atlas.Rendering;
using Unity.Mathematics;

namespace Atlas.Spaces.Slicer
{
    /// <summary>
    /// Turns the slicer's output and the body's shadow into vertices of the look (lane A contract, sections
    /// 10.2 and 13.3). Static, pointer-based and allocation-free (README F2): the Burst jobs and the headless
    /// tests run the same code.
    /// </summary>
    public static unsafe class SlicerMeshKernel
    {
        /// <summary>
        /// Cuts the body and writes the cross-section as a triangle soup in room metres:
        /// position = centre + scale * p.xyz. Returns through the out values what <see cref="Slicer4D.Slice"/>
        /// reports. The scratch buffers and the output hold <paramref name="vertexCapacity"/> entries each;
        /// 6 per tetrahedron always suffice.
        /// </summary>
        public static void SliceToVertices(
            float4* vertices, int* tetrahedra, int tetCount, int* tetCell,
            in float4x4 modelToSlice, in float4 translation, in float4 normal, float offset,
            float2* cellUv, int cellUvCount, float3 centre, float scale, float3 light,
            float3* scratchPositions, float3* scratchNormals, int* scratchCells,
            AtlasVertex* outVertices, int vertexCapacity,
            out int vertexCount, out int triangleCount, out int tetsCut,
            out float3 boundsMin, out float3 boundsMax)
        {
            Slicer4D.Slice(vertices, tetrahedra, tetCount, tetCell, null,
                           in modelToSlice, in translation, in normal, offset,
                           scratchPositions, scratchNormals, scratchCells, null, vertexCapacity,
                           out vertexCount, out triangleCount, out tetsCut);
            PackSlice(scratchPositions, scratchNormals, scratchCells, vertexCount, cellUv, cellUvCount,
                      centre, scale, light, outVertices, out boundsMin, out boundsMax);
        }

        /// <summary>
        /// One <see cref="AtlasVertex"/> per slice vertex: the colour of its cell from the table, the two-sided
        /// baked shade of its face, no lines. Bounds are those of the written positions, or the centre alone
        /// for an empty slice.
        /// </summary>
        public static void PackSlice(
            float3* positions, float3* normals, int* cells, int vertexCount,
            float2* cellUv, int cellUvCount, float3 centre, float scale, float3 light,
            AtlasVertex* outVertices, out float3 boundsMin, out float3 boundsMax)
        {
            float3 lo = centre, hi = centre;
            if (vertexCount > 0)
            {
                lo = new float3(float.MaxValue);
                hi = new float3(float.MinValue);
            }
            for (int i = 0; i < vertexCount; i++)
            {
                float3 p = centre + scale * positions[i];
                float3 n = normals[i];
                int cell = cells[i] % cellUvCount;
                if (cell < 0) cell += cellUvCount;

                AtlasVertex vertex;
                vertex.Position = p;
                vertex.Normal = n;
                vertex.Color = AtlasVertex.PackColor(ShadeTwoSided(n, light), 0);
                vertex.Uv0 = cellUv[cell];
                vertex.Uv1 = float2.zero;
                outVertices[i] = vertex;

                lo = math.min(lo, p);
                hi = math.max(hi, p);
            }
            boundsMin = lo;
            boundsMax = hi;
        }

        /// <summary>
        /// The shadow of the body (Master Plan section 5.1): every boundary triangle projected to three
        /// dimensions and written in room metres, three vertices per triangle, coloured along the w-depth
        /// strip by the w each vertex had before it was flattened. Writes 3 * triangleCount vertices.
        /// </summary>
        public static void ShadowToVertices(
            float4* vertices, int* triangles, int triangleCount,
            in float4x4 modelToSlice, in float4 translation, bool perspective, float focal,
            float3 centre, float scale, float3 light,
            AtlasVertex* outVertices, out float3 boundsMin, out float3 boundsMax)
        {
            float3 lo = centre, hi = centre;
            if (triangleCount > 0)
            {
                lo = new float3(float.MaxValue);
                hi = new float3(float.MinValue);
            }
            float3* corner = stackalloc float3[3];
            float* depth = stackalloc float[3];
            for (int t = 0; t < triangleCount; t++)
            {
                for (int k = 0; k < 3; k++)
                {
                    float4 p = math.mul(modelToSlice, vertices[triangles[t * 3 + k]]) + translation;
                    float3 flat = perspective ? Projector4D.Perspective(p, focal) : p.xyz;
                    corner[k] = centre + scale * flat;
                    depth[k] = p.w;
                }
                float3 normal = math.normalizesafe(math.cross(corner[1] - corner[0], corner[2] - corner[0]),
                                                   new float3(0f, 1f, 0f));
                uint color = AtlasVertex.PackColor(ShadeTwoSided(normal, light), 0);
                for (int k = 0; k < 3; k++)
                {
                    AtlasVertex vertex;
                    vertex.Position = corner[k];
                    vertex.Normal = normal;
                    vertex.Color = color;
                    vertex.Uv0 = DepthUv(depth[k]);
                    vertex.Uv1 = float2.zero;
                    outVertices[t * 3 + k] = vertex;
                    lo = math.min(lo, corner[k]);
                    hi = math.max(hi, corner[k]);
                }
            }
            boundsMin = lo;
            boundsMax = hi;
        }

        /// <summary>Where a w value falls on the w-depth strip: 0 at +CutRange (near the 4D eye), 1 at -CutRange.</summary>
        public static float DepthT(float w)
        {
            return math.saturate(0.5f - 0.5f * w / SlicerObjects.CutRange);
        }

        /// <summary>Palette coordinate on the w-depth strip for a w value. Equals
        /// <c>AtlasPalette.StripUv(AtlasPalette.StripW, DepthT(w))</c>, written out so that Burst needs no table.</summary>
        public static float2 DepthUv(float w)
        {
            float u = (0.5f + DepthT(w) * (AtlasPalette.Size - 1)) / AtlasPalette.Size;
            return new float2(u, (AtlasPalette.StripW + 0.5f) / AtlasPalette.CellsPerSide);
        }

        /// <summary>The byte of <c>AtlasShading.FaceShadeTwoSided</c>, with the light passed in.</summary>
        public static byte ShadeTwoSided(float3 normal, float3 light)
        {
            float shade = AtlasShading.ShadeFloor
                        + (1f - AtlasShading.ShadeFloor) * (0.5f + 0.5f * math.abs(math.dot(normal, light)));
            return (byte)math.round(255f * math.saturate(shade));
        }
    }
}

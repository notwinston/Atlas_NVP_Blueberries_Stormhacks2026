using System;
using Atlas.Kernel;
using Atlas.Rendering;
using Unity.Mathematics;

namespace Atlas.Spaces.SmallPlanet
{
    /// <summary>
    /// The geometry and colours of the Small Planet's daytime sky (SmallPlanetSky draws it), engine-free. The
    /// gradient itself is the look's background (AtlasBackground.shader through AtlasLook): the pale horizon of the
    /// Blender preview (smallplanet_build.py, SKY = #BFE3F5), which is also the fog colour, so the grass fades into
    /// the sky, rising to a deeper blue at the zenith. On it this class builds a sun and a handful of low-poly
    /// cumulus clouds, in Atlas/Surface vertices: palette cells at uv0, the shade in COLOR.r, no lines.
    /// Positions are metres about the head, world axes (y up); the sky is drawn unwarped (mode 0) and unfogged.
    /// </summary>
    public static class SmallPlanetSkyMesh
    {
        /// <summary>The horizon, sRGB 0..1: the Blender preview's sky colour (#BFE3F5). Fog colour and camera clear colour.</summary>
        public static readonly float3 HorizonSrgb = new float3(0xBF, 0xE3, 0xF5) / 255f;
        /// <summary>The zenith: palette cell 199 (0x5AC8FA), the same hue as the horizon, deeper.</summary>
        public const int ZenithCell = AtlasPalette.CueRescan;
        /// <summary>The sun's disc and halo: emissive white (cell 204, 0xFFFFFF) and pale gold (cell 196, 0xFFE66D).</summary>
        public const int SunCell = AtlasPalette.Reticle, SunHaloCell = AtlasPalette.LapMarker;
        /// <summary>The clouds' sunlit body, emissive white (cell 200, 0xFFFFFF), and their flat base, the palette's
        /// lightest neutral (cell 15, a pale blue-grey), shaded.</summary>
        public const int CloudCell = AtlasPalette.CueStandStill, CloudBaseCell = AtlasPalette.Text;

        /// <summary>Distance of the cloud centres and of the sun from the head, metres: beyond everything else of the space
        /// (the planet is fogged out by 0.8 pi R, under 11 m) and inside the eye cameras' far plane (StereoRig.FarPlane, 60 m).</summary>
        public const float CloudDistanceM = 40f, SunDistanceM = 52f;
        /// <summary>Angular radius of the sun's disc and of its halo, degrees (stylised: the real sun is 0.27).</summary>
        public const float SunRadiusDeg = 3.2f, SunHaloDeg = 5f;
        public const int SunSegments = 16;
        /// <summary>The clouds turn slowly about the vertical, degrees a second (a full turn in half an hour); the sun stays.</summary>
        public const float CloudDriftDegPerSecond = 0.2f;

        /// <summary>Towards the sun: the azimuth of the look's fixed light (AtlasShading.LightDirection), 35 degrees up.</summary>
        public static readonly float3 SunDirection = Direction(math.degrees(math.atan2(AtlasShading.LightDirection.x, AtlasShading.LightDirection.z)), 35f);

        /// <summary>One cloud: where it hangs (azimuth from +z towards +x, elevation, degrees), how wide it looks (degrees), how many puffs.</summary>
        public readonly struct Cloud
        {
            public readonly float AzimuthDeg, ElevationDeg, WidthDeg;
            public readonly int Puffs;
            public Cloud(float azimuthDeg, float elevationDeg, float widthDeg, int puffs)
            {
                AzimuthDeg = azimuthDeg; ElevationDeg = elevationDeg; WidthDeg = widthDeg; Puffs = puffs;
            }
        }

        /// <summary>The clouds, spread round the horizon and kept low (the head looks mostly level in a headset).</summary>
        public static readonly Cloud[] Clouds =
        {
            new Cloud(0f, 16f, 18f, 4), new Cloud(70f, 24f, 14f, 3), new Cloud(125f, 12f, 22f, 5), new Cloud(185f, 20f, 16f, 4),
            new Cloud(240f, 14f, 20f, 5), new Cloud(300f, 27f, 12f, 3), new Cloud(20f, 40f, 12f, 3), new Cloud(160f, 44f, 10f, 3),
        };

        const int PuffSegments = 7, PuffRings = 3;
        /// <summary>Triangles of one puff: a low-poly dome on a flat base.</summary>
        public const int PuffTriangles = PuffSegments * (2 * (PuffRings - 1) + 1) + PuffSegments;

        public static float3 HorizonLinear => AtlasPalette.SrgbToLinear(HorizonSrgb);
        public static float3 ZenithLinear => AtlasPalette.Linear(ZenithCell);

        /// <summary>Unit direction at this azimuth (from +z towards +x) and elevation, degrees.</summary>
        public static float3 Direction(float azimuthDeg, float elevationDeg)
        {
            float az = math.radians(azimuthDeg), el = math.radians(elevationDeg);
            return new float3(math.cos(el) * math.sin(az), math.sin(el), math.cos(el) * math.cos(az));
        }

        /// <summary>Appends the sun: a disc facing the head with a halo ring round it. Returns the triangles added.</summary>
        public static int BuildSun(AtlasMeshBuilder into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            int before = into.TriangleCount;
            float3 n = SunDirection, centre = SunDistanceM * n;
            float3 u = math.normalize(math.cross(new float3(0f, 1f, 0f), n)), v = math.cross(n, u);
            float r0 = SunDistanceM * math.tan(math.radians(SunRadiusDeg)), r1 = SunDistanceM * math.tan(math.radians(SunHaloDeg));
            float2 core = AtlasPalette.Uv(SunCell), halo = AtlasPalette.Uv(SunHaloCell);
            int c = into.AddVertex(Vertex(centre, -n, core, 255));
            int coreRing = into.VertexCount;
            for (int i = 0; i < SunSegments; i++) into.AddVertex(Vertex(centre + r0 * Rim(u, v, i), -n, core, 255));
            int inner = into.VertexCount;
            for (int i = 0; i < SunSegments; i++) into.AddVertex(Vertex(centre + r0 * Rim(u, v, i), -n, halo, 255));
            int outer = into.VertexCount;
            for (int i = 0; i < SunSegments; i++) into.AddVertex(Vertex(centre + r1 * Rim(u, v, i), -n, halo, 255));
            for (int i = 0; i < SunSegments; i++)
            {
                int j = (i + 1) % SunSegments;
                into.AddTriangle(c, coreRing + j, coreRing + i);
                into.AddTriangle(inner + i, inner + j, outer + j);
                into.AddTriangle(inner + i, outer + j, outer + i);
            }
            return into.TriangleCount - before;
        }

        static float3 Rim(float3 u, float3 v, int i)
        {
            float a = 2f * math.PI * i / SunSegments;
            return math.cos(a) * u + math.sin(a) * v;
        }

        /// <summary>
        /// Appends every cloud of <see cref="Clouds"/>, flat-shaded (three vertices a triangle): white where a face looks
        /// up or sideways, the blue-grey base where it looks down. The same clouds every time. Returns the triangles added.
        /// </summary>
        public static int BuildClouds(AtlasMeshBuilder into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            int before = into.TriangleCount;
            var rng = new System.Random(20261004);
            var ring = new float3[PuffRings * PuffSegments + 1];
            for (int k = 0; k < Clouds.Length; k++)
            {
                Cloud cloud = Clouds[k];
                float3 forward = Direction(cloud.AzimuthDeg, 0f), right = math.cross(new float3(0f, 1f, 0f), forward);
                float3 centre = CloudDistanceM * Direction(cloud.AzimuthDeg, cloud.ElevationDeg);
                float width = 2f * CloudDistanceM * math.tan(math.radians(0.5f * cloud.WidthDeg));
                for (int p = 0; p < cloud.Puffs; p++)
                {
                    // Bigger puffs in the middle, smaller towards the ends, all on one flat base.
                    float t = cloud.Puffs == 1 ? 0.5f : (float)p / (cloud.Puffs - 1);
                    float middle = 1f - math.abs(2f * t - 1f);
                    float radius = width * (0.16f + 0.10f * middle) * (0.9f + 0.2f * (float)rng.NextDouble());
                    float along = (t - 0.5f) * (width - 2f * radius) * 1.1f;
                    float3 puff = centre + along * right + radius * 0.35f * ((float)rng.NextDouble() - 0.5f) * forward
                                  + new float3(0f, radius * 0.12f * middle, 0f);
                    float3 scale = new float3(radius, radius * (0.7f + 0.25f * middle), radius * 0.85f);
                    AddPuff(into, puff, right, forward, scale, ring, rng);
                }
            }
            return into.TriangleCount - before;
        }

        // A dome of PuffRings rings of PuffSegments points (the first on the base) and a pole, closed by a flat base.
        static void AddPuff(AtlasMeshBuilder into, float3 centre, float3 right, float3 forward, float3 scale, float3[] ring, System.Random rng)
        {
            float3 up = new float3(0f, 1f, 0f);
            float spin = (float)rng.NextDouble() * 2f * math.PI / PuffSegments;
            for (int r = 0; r < PuffRings; r++)
            {
                float phi = 0.5f * math.PI * r / PuffRings;
                for (int s = 0; s < PuffSegments; s++)
                {
                    float th = spin + 2f * math.PI * s / PuffSegments;
                    float jitter = 0.86f + 0.26f * (float)rng.NextDouble();
                    float3 local = new float3(math.cos(phi) * math.cos(th) * jitter, math.sin(phi) * (r == 0 ? 0f : jitter), math.cos(phi) * math.sin(th) * jitter);
                    ring[r * PuffSegments + s] = centre + local.x * scale.x * right + local.y * scale.y * up + local.z * scale.z * forward;
                }
            }
            int pole = PuffRings * PuffSegments;
            ring[pole] = centre + scale.y * (0.92f + 0.16f * (float)rng.NextDouble()) * up;
            for (int r = 0; r + 1 < PuffRings; r++)
                for (int s = 0; s < PuffSegments; s++)
                {
                    int a = r * PuffSegments + s, b = r * PuffSegments + (s + 1) % PuffSegments;
                    Face(into, centre, ring[a], ring[b], ring[b + PuffSegments]);
                    Face(into, centre, ring[a], ring[b + PuffSegments], ring[a + PuffSegments]);
                }
            int top = (PuffRings - 1) * PuffSegments;
            for (int s = 0; s < PuffSegments; s++) Face(into, centre, ring[top + s], ring[top + (s + 1) % PuffSegments], ring[pole]);
            for (int s = 0; s < PuffSegments; s++) Face(into, centre, ring[(s + 1) % PuffSegments], ring[s], centre);
        }

        // One flat triangle, its normal turned away from the puff's centre (the material draws both sides).
        static void Face(AtlasMeshBuilder into, float3 centre, float3 a, float3 b, float3 c)
        {
            float3 n = math.normalizesafe(math.cross(b - a, c - a), new float3(0f, -1f, 0f));
            float3 mid = (a + b + c) / 3f;
            bool flat = math.lengthsq(mid - centre) < 1e-6f || math.abs(mid.y - centre.y) < 1e-4f;
            if (flat ? n.y > 0f : math.dot(n, mid - centre) < 0f) n = -n;
            bool body = n.y > -0.2f;
            float2 uv = AtlasPalette.Uv(body ? CloudCell : CloudBaseCell);
            byte shade = body ? (byte)255 : AtlasShading.ToByte(0.93f + 0.07f * math.saturate(n.y + 1f));
            int i = into.AddVertex(Vertex(a, n, uv, shade));
            into.AddVertex(Vertex(b, n, uv, shade));
            into.AddVertex(Vertex(c, n, uv, shade));
            into.AddTriangle(i, i + 1, i + 2);
        }

        // No lines: line weight 0 and a constant line coordinate.
        static AtlasVertex Vertex(float3 position, float3 normal, float2 uv0, byte shade) => new AtlasVertex
        {
            Position = position,
            Normal = normal,
            Color = AtlasVertex.PackColor(shade, 0),
            Uv0 = uv0,
            Uv1 = new float2(0.5f, 0.5f),
        };
    }
}

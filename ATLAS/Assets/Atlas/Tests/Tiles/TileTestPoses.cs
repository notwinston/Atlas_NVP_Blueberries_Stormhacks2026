// [P1-B06] Shared helpers for the tile-renderer tests: random poses, true eye frusta, point sets (brief section 4 shared settings).
using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Rendering;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Tiles.Tests
{
    public static class TileTestPoses
    {
        public const int Seed = 20261003;
        public const double EyeHeight = 1.6;      // the reference script's H
        public const double EyeHalfIpd = 0.032;   // MP D7, PH-10

        /// <summary>Walker uniform in tile zero's Klein square, half-width tanh(r + extra) (A.3, A.6).</summary>
        public static Iso2 RandomWalker(System.Random rng, int q, double extra = 0)
        {
            double hw = Math.Tanh(Math.Acosh(Math.Cos(Math.PI / q) / Math.Sin(Math.PI / 4)) + extra);
            var k = new double2((rng.NextDouble() * 2 - 1) * hw, (rng.NextDouble() * 2 - 1) * hw);
            double kr = math.length(k);
            if (kr == 0) return Iso2.Boost(double2.zero, -1);
            return Iso2.Boost(k / kr * Math.Atanh(kr), -1);
        }

        public static float3 RandomHead(System.Random rng) =>
            new float3((float)(rng.NextDouble() * 10 - 5), (float)EyeHeight, (float)(rng.NextDouble() * 10 - 5));

        /// <summary>Unity convention: pitchDown positive looks down.</summary>
        public static quaternion Rot(double yawDeg, double pitchDownDeg) =>
            Quaternion.Euler((float)pitchDownDeg, (float)yawDeg, 0f);

        /// <summary>The true (not widened) frustum of both eyes: four side planes as tangents.</summary>
        public readonly struct EyeFrusta
        {
            readonly double3 headPos, fwd, rgt, up;
            readonly double tanS, tanU;
            public EyeFrusta(float3 head, quaternion rot, double halfSideDeg, double halfUpDownDeg)
            {
                headPos = head;
                fwd = math.mul(rot, new float3(0, 0, 1)); rgt = math.mul(rot, new float3(1, 0, 0)); up = math.mul(rot, new float3(0, 1, 0));
                tanS = Math.Tan(halfSideDeg * Math.PI / 180); tanU = Math.Tan(halfUpDownDeg * Math.PI / 180);
            }
            bool InEye(double3 p, double side)
            {
                double3 v = p - (headPos + side * EyeHalfIpd * rgt);
                double z = math.dot(v, fwd);
                return z > 0 && Math.Abs(math.dot(v, rgt)) <= z * tanS && Math.Abs(math.dot(v, up)) <= z * tanU;
            }
            public bool Inside(double3 p) => InEye(p, -1) || InEye(p, 1);
            public double3 Head => headPos;
        }

        /// <summary>All vertices of the B05 Mid mesh (full boundary, grid, post wedges with 2.6 m tops).</summary>
        public static float3[] MidMeshPoints(int q, double L)
        {
            var gen = new TileMeshGenerator(TilePalette.Provisional);
            var mesh = new Mesh();
            gen.Build(TileMeshSpec.Hyperbolic(q, L, TileLod.Mid, 0, 0xF), mesh);
            var v = new List<Vector3>();
            mesh.GetVertices(v);
            var pts = new float3[v.Count];
            for (int i = 0; i < pts.Length; i++) pts[i] = v[i];
            UnityEngine.Object.DestroyImmediate(mesh);
            return pts;
        }

        public const int FloorViolation = 1, ElevatedViolation = 2;

        /// <summary>Flags for a tile with anchor A: a point (vertex warped by the double reference) nearer than rho and inside a true eye frustum.</summary>
        public static int ViolationFlags(float3[] pts, in Iso2 A, double L, double rho, in EyeFrusta fr)
        {
            int flags = 0;
            double3 head = fr.Head;
            double lim = rho * L; lim *= lim;
            for (int i = 0; i < pts.Length; i++)
            {
                int bit = pts[i].y > 0 ? ElevatedViolation : FloorViolation;
                if ((flags & bit) != 0) continue;
                double3 P = AtlasWarpReference.Warp(pts[i], A, -1, L, head);
                double dx = P.x - head.x, dz = P.z - head.z;
                if (dx * dx + dz * dz >= lim) continue;
                if (fr.Inside(P)) flags |= bit;
                if (flags == (FloorViolation | ElevatedViolation)) break;
            }
            return flags;
        }

        /// <summary>A cone that never culls (normals up, eye far below): every tile within reach is drawn.</summary>
        public static TileViewCone NeverCull() =>
            new TileViewCone(new double3(0, -1000, 0), new double3(0, 1, 0), new double3(0, 1, 0), new double3(0, 1, 0), new double3(0, 1, 0));
    }
}

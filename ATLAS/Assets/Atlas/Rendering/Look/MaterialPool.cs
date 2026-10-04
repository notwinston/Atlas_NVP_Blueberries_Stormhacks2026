using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Rendering
{
    /// <summary>
    /// The only source of Atlas/Surface materials: one instance per tile, room or object (Master Plan D14,
    /// section 8.3). Per-instance values live in the material's own constant block, never in a
    /// MaterialPropertyBlock, so the SRP Batcher keeps working. Assign with renderer.sharedMaterial.
    /// </summary>
    public sealed class MaterialPool
    {
        /// <summary>_LineColor default: palette GridLine in linear rgb.</summary>
        static readonly Vector4 s_DefaultLineColor = new Vector4(0.159f, 0.392f, 0.578f, 1f);

        readonly Shader m_Shader;
        readonly PoolLedger m_Ledger;
        readonly Dictionary<Material, int> m_SlotOf;
        Material[] m_Materials;
        bool m_GrowLogged;

        /// <param name="surface">The Atlas/Surface shader.</param>
        /// <param name="capacity">Instances created up front. The composition root passes 256.</param>
        public MaterialPool(Shader surface, int capacity)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            m_Shader = surface;
            m_Ledger = new PoolLedger(capacity);
            m_Materials = new Material[capacity];
            // Room for one doubling without a rehash on the frame path.
            m_SlotOf = new Dictionary<Material, int>(capacity * 2);
            CreateInstances(0, capacity);
        }

        /// <summary>A pool on the shader loaded from Resources. Throws when the shader is not in the build.</summary>
        public static MaterialPool CreateDefault(int capacity = 256)
        {
            Shader surface = AtlasLook.LoadSurfaceShader();
            if (surface == null)
                throw new InvalidOperationException("materials.shader: 'AtlasSurface' is not in Resources.");
            return new MaterialPool(surface, capacity);
        }

        public int Capacity => m_Ledger.Capacity;
        public int Rented => m_Ledger.Rented;
        /// <summary>The shader every instance uses.</summary>
        public Shader Shader => m_Shader;

        /// <summary>A material with every property at its default. Never null.</summary>
        public Material Rent()
        {
            if (m_Ledger.Exhausted)
            {
                // Over budget (at most 256 at once): keep running, and say so once.
                int old = m_Ledger.Grow();
                Array.Resize(ref m_Materials, m_Ledger.Capacity);
                CreateInstances(old, m_Ledger.Capacity);
                if (!m_GrowLogged)
                {
                    m_GrowLogged = true;
                    Debug.LogWarning("materials.grow: the material pool doubled to " + m_Ledger.Capacity + ".");
                }
            }
            return m_Materials[m_Ledger.Rent()];
        }

        /// <summary>Gives a material back. It is reset and reused; never destroy a rented material.</summary>
        public void Return(Material material)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (!m_SlotOf.TryGetValue(material, out int slot))
                throw new ArgumentException("The material was not made by this pool.", nameof(material));
            m_Ledger.Return(slot);
            ResetToDefaults(material);
        }

        /// <summary>True when the pool made the material and it is rented now.</summary>
        public bool Owns(Material material)
        {
            return material != null && m_SlotOf.TryGetValue(material, out int slot) && m_Ledger.IsRented(slot);
        }

        /// <summary>Destroys every instance. For shutdown and tests; the pool is unusable afterwards.</summary>
        public void Dispose()
        {
            for (int i = 0; i < m_Materials.Length; i++)
            {
                Material material = m_Materials[i];
                if (material == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(material);
                else UnityEngine.Object.DestroyImmediate(material);
                m_Materials[i] = null;
            }
            m_SlotOf.Clear();
        }

        void CreateInstances(int from, int to)
        {
            for (int slot = from; slot < to; slot++)
            {
                var material = new Material(m_Shader)
                {
                    name = "AtlasSurface (pooled)",
                    hideFlags = HideFlags.HideAndDontSave,
                    enableInstancing = false,
                };
                ResetToDefaults(material);
                m_Materials[slot] = material;
                m_SlotOf.Add(material, slot);
            }
        }

        /// <summary>Sets every per-material property and render-state value to its default.</summary>
        public static void ResetToDefaults(Material material)
        {
            material.SetMatrix(AtlasShaderIDs.Anchor, Matrix4x4.identity);
            material.SetVector(AtlasShaderIDs.BaseTint, new Vector4(1f, 1f, 1f, 1f));
            material.SetVector(AtlasShaderIDs.LineColor, s_DefaultLineColor);
            material.SetVector(AtlasShaderIDs.LineParams, new Vector4(1f, 2f, 0f, 0f));
            material.SetVector(AtlasShaderIDs.LineMajor, Vector4.zero);
            material.SetVector(AtlasShaderIDs.PortalPlane, new Vector4(0f, 0f, 0f, 1f));
            material.SetVector(AtlasShaderIDs.Gate0, Vector4.zero);
            material.SetVector(AtlasShaderIDs.Gate1, Vector4.zero);
            material.SetVector(AtlasShaderIDs.Gate2, Vector4.zero);
            material.SetVector(AtlasShaderIDs.Gate3, Vector4.zero);
            material.SetVector(AtlasShaderIDs.SurfaceParams, new Vector4(1f, 1f, 0f, 1f));
            material.SetFloat(AtlasShaderIDs.WarpMode, 0f);
            material.SetFloat(AtlasShaderIDs.Cull, (float)CullMode.Back);
            material.SetFloat(AtlasShaderIDs.StencilRef, 0f);
            material.SetFloat(AtlasShaderIDs.StencilComp, (float)CompareFunction.Always);
        }

        // ---- Typed setters: the per-material values, on a rented instance. None allocates. -------------------

        /// <summary>The warp anchor of a tile, room or object (lane B contract, section 5).</summary>
        public static void SetAnchor(Material material, in float4x4 anchor)
        {
            material.SetMatrix(AtlasShaderIDs.Anchor, anchor);
        }

        /// <summary>Warp mode: 0 none, 1 hyperbolic, 2 spherical.</summary>
        public static void SetWarpMode(Material material, int mode)
        {
            material.SetFloat(AtlasShaderIDs.WarpMode, mode);
        }

        /// <summary>Tint (linear rgb, multiplies the palette colour) and opacity 0..1 (dithered).</summary>
        public static void SetTint(Material material, float3 linearRgb, float opacity = 1f)
        {
            material.SetVector(AtlasShaderIDs.BaseTint, new Vector4(linearRgb.x, linearRgb.y, linearRgb.z, opacity));
        }

        /// <summary>Tint by the colour of a palette cell (0..255), and opacity 0..1 (dithered).</summary>
        public static void SetTintIndex(Material material, int paletteIndex, float opacity = 1f)
        {
            SetTint(material, AtlasPalette.Linear(paletteIndex), opacity);
        }

        /// <summary>Line colour, linear rgb.</summary>
        public static void SetLineColor(Material material, float3 linearRgb)
        {
            material.SetVector(AtlasShaderIDs.LineColor, new Vector4(linearRgb.x, linearRgb.y, linearRgb.z, 1f));
        }

        /// <summary>
        /// Lines: strengths 0..1 of the minor lines (every integer of the line coordinate) and of the major lines
        /// (every multiple of majorPeriod; 0 = none), and their widths in multiples of the global line width.
        /// </summary>
        public static void SetLines(Material material, float minorStrength, float majorStrength, float2 majorPeriod,
                                    float minorWidth = 1f, float majorWidth = 2f)
        {
            material.SetVector(AtlasShaderIDs.LineParams, new Vector4(minorWidth, majorWidth, minorStrength, majorStrength));
            material.SetVector(AtlasShaderIDs.LineMajor, new Vector4(majorPeriod.x, majorPeriod.y, 0f, 0f));
        }

        /// <summary>
        /// Emissive gain, fog weight 0..1 (0 for head-locked content), near-fade distance in metres (0 = off) and
        /// fade weight (1 follows the world fade, 0 ignores it).
        /// </summary>
        public static void SetSurface(Material material, float emissiveGain = 1f, float fogWeight = 1f,
                                      float nearFadeMetres = 0f, float fadeWeight = 1f)
        {
            material.SetVector(AtlasShaderIDs.SurfaceParams, new Vector4(emissiveGain, fogWeight, nearFadeMetres, fadeWeight));
        }

        /// <summary>World-space clip plane (n.xyz, d): fragments with dot(n, p) + d below 0 are not drawn.</summary>
        public static void SetPortalPlane(Material material, float4 plane)
        {
            material.SetVector(AtlasShaderIDs.PortalPlane, plane);
        }

        /// <summary>Clip plane through a point, keeping the side the unit normal points to.</summary>
        public static void SetPortalPlane(Material material, float3 normal, float3 pointOnPlane)
        {
            material.SetVector(AtlasShaderIDs.PortalPlane,
                new Vector4(normal.x, normal.y, normal.z, -math.dot(normal, pointOnPlane)));
        }

        /// <summary>Switches the portal clip off: (0, 0, 0, 1).</summary>
        public static void ClearPortalPlane(Material material)
        {
            material.SetVector(AtlasShaderIDs.PortalPlane, new Vector4(0f, 0f, 0f, 1f));
        }

        /// <summary>Sight-line gate 0..3: a corner post on the floor (x, z), world, and the side to keep (0 = off).</summary>
        public static void SetGate(Material material, int gate, float2 post, float sign)
        {
            material.SetVector(GateId(gate), new Vector4(post.x, post.y, sign, 0f));
        }

        /// <summary>Switches all four gates off.</summary>
        public static void ClearGates(Material material)
        {
            material.SetVector(AtlasShaderIDs.Gate0, Vector4.zero);
            material.SetVector(AtlasShaderIDs.Gate1, Vector4.zero);
            material.SetVector(AtlasShaderIDs.Gate2, Vector4.zero);
            material.SetVector(AtlasShaderIDs.Gate3, Vector4.zero);
        }

        /// <summary>Stencil test of the material: reference 0..15 and the comparison (portal rooms use Equal).</summary>
        public static void SetStencil(Material material, int reference, CompareFunction comparison = CompareFunction.Equal)
        {
            material.SetFloat(AtlasShaderIDs.StencilRef, math.clamp(reference, 0, 15));
            material.SetFloat(AtlasShaderIDs.StencilComp, (float)comparison);
        }

        /// <summary>Stencil test off: reference 0, comparison Always.</summary>
        public static void ClearStencil(Material material)
        {
            material.SetFloat(AtlasShaderIDs.StencilRef, 0f);
            material.SetFloat(AtlasShaderIDs.StencilComp, (float)CompareFunction.Always);
        }

        /// <summary>Face culling: Back by default; Off for surfaces seen from both sides (4D slices).</summary>
        public static void SetCull(Material material, CullMode cull)
        {
            material.SetFloat(AtlasShaderIDs.Cull, (float)cull);
        }

        static int GateId(int gate)
        {
            switch (gate)
            {
                case 0: return AtlasShaderIDs.Gate0;
                case 1: return AtlasShaderIDs.Gate1;
                case 2: return AtlasShaderIDs.Gate2;
                case 3: return AtlasShaderIDs.Gate3;
                default: throw new ArgumentOutOfRangeException(nameof(gate));
            }
        }
    }
}

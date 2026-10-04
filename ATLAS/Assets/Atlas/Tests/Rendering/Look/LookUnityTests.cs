#if !ATLAS_HEADLESS
using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Rendering.Tests.Look
{
    /// <summary>Checks of the look that need the engine: the shaders load, the pool rents, a mesh uploads.</summary>
    [Category("Unity")]
    public class LookUnityTests
    {
        [Test]
        public void SurfaceShader_LoadsFromResources()
        {
            Shader shader = AtlasLook.LoadSurfaceShader();
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.name, Is.EqualTo("Atlas/Surface"));
            Assert.That(shader.isSupported, Is.True);
            Assert.That(shader.passCount, Is.EqualTo(1));
        }

        [Test]
        public void BackgroundShader_LoadsFromResources()
        {
            Shader shader = AtlasLook.LoadBackgroundShader();
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.name, Is.EqualTo("Hidden/Atlas/Background"));
            Assert.That(shader.isSupported, Is.True);
        }

        [Test]
        public void PooledMaterial_UsesTheShader_WithContractDefaults()
        {
            MaterialPool pool = MaterialPool.CreateDefault(4);
            try
            {
                Material material = pool.Rent();
                Assert.That(material, Is.Not.Null);
                Assert.That(material.shader.name, Is.EqualTo("Atlas/Surface"));
                Assert.That(material.GetVector(AtlasShaderIDs.BaseTint), Is.EqualTo(new Vector4(1f, 1f, 1f, 1f)));
                Assert.That(material.GetVector(AtlasShaderIDs.LineParams), Is.EqualTo(new Vector4(1f, 2f, 0f, 0f)));
                Assert.That(material.GetVector(AtlasShaderIDs.PortalPlane), Is.EqualTo(new Vector4(0f, 0f, 0f, 1f)));
                Assert.That(material.GetVector(AtlasShaderIDs.SurfaceParams), Is.EqualTo(new Vector4(1f, 1f, 0f, 1f)));
                Assert.That(material.GetMatrix(AtlasShaderIDs.Anchor), Is.EqualTo(Matrix4x4.identity));
                Assert.That(material.GetFloat(AtlasShaderIDs.Cull), Is.EqualTo(2f));
                Assert.That(material.GetFloat(AtlasShaderIDs.StencilComp), Is.EqualTo((float)CompareFunction.Always));
                Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Geometry));
                Assert.That(material.shaderKeywords.Length, Is.EqualTo(0));
            }
            finally
            {
                pool.Dispose();
            }
        }

        [Test]
        public void Pool_RentAndReturn()
        {
            MaterialPool pool = MaterialPool.CreateDefault(2);
            try
            {
                Material a = pool.Rent(), b = pool.Rent();
                Assert.That(a, Is.Not.SameAs(b));
                Assert.That(pool.Rented, Is.EqualTo(2));
                Assert.That(pool.Owns(a), Is.True);

                MaterialPool.SetTintIndex(a, AtlasPalette.CueWarn, 0.5f);
                MaterialPool.SetStencil(a, 3);
                MaterialPool.SetPortalPlane(a, new float3(0f, 0f, 1f), new float3(0f, 0f, 2f));
                MaterialPool.SetGate(a, 2, new float2(1f, -1f), -1f);
                MaterialPool.SetAnchor(a, float4x4.Translate(new float3(1f, 2f, 3f)));
                Assert.That(a.GetVector(AtlasShaderIDs.PortalPlane), Is.EqualTo(new Vector4(0f, 0f, 1f, -2f)));
                Assert.That(a.GetFloat(AtlasShaderIDs.StencilRef), Is.EqualTo(3f));
                Assert.That(a.GetFloat(AtlasShaderIDs.StencilComp), Is.EqualTo((float)CompareFunction.Equal));
                Assert.That(a.GetVector(AtlasShaderIDs.Gate2), Is.EqualTo(new Vector4(1f, -1f, -1f, 0f)));
                Assert.That(a.GetMatrix(AtlasShaderIDs.Anchor).m03, Is.EqualTo(1f));

                pool.Return(a);
                Assert.That(pool.Rented, Is.EqualTo(1));
                Assert.That(pool.Owns(a), Is.False);
                Assert.That(a.GetVector(AtlasShaderIDs.BaseTint), Is.EqualTo(new Vector4(1f, 1f, 1f, 1f)), "a returned material is reset");
                Assert.That(a.GetFloat(AtlasShaderIDs.StencilRef), Is.EqualTo(0f));
                Assert.That(a.GetVector(AtlasShaderIDs.Gate2), Is.EqualTo(Vector4.zero));
                Assert.That(a.GetMatrix(AtlasShaderIDs.Anchor), Is.EqualTo(Matrix4x4.identity));

                Assert.Throws<ArgumentException>(() => pool.Return(a), "returned twice");
                var foreign = new Material(pool.Shader);
                Assert.Throws<ArgumentException>(() => pool.Return(foreign), "not from the pool");
                UnityEngine.Object.DestroyImmediate(foreign);

                Assert.That(pool.Rent(), Is.SameAs(a));
                UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("materials.grow"));
                Material c = pool.Rent();
                Assert.That(c, Is.Not.Null);
                Assert.That(pool.Capacity, Is.EqualTo(4));
                Assert.That(pool.Rented, Is.EqualTo(3));
            }
            finally
            {
                pool.Dispose();
            }
        }

        [Test]
        public void Palette_BindsAPointFilteredSrgbTexture()
        {
            Texture2D texture = AtlasPaletteTexture.Bind();
            Assert.That(texture.width, Is.EqualTo(128));
            Assert.That(texture.height, Is.EqualTo(128));
            Assert.That(texture.mipmapCount, Is.EqualTo(1));
            Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(texture.isDataSRGB, Is.True);
            Assert.That(Shader.GetGlobalTexture(AtlasShaderIDs.Palette), Is.SameAs(texture));
        }

        [Test]
        public void Look_WritesTheGlobals()
        {
            AtlasLook.Initialise();
            AtlasLook.SetFog(2f, 3f);
            AtlasLook.Upload(new float3(1f, 1.6f, 2f), 1.9, 12.5, 1f / 60f);
            Assert.That(Shader.GetGlobalVector(AtlasShaderIDs.HeadPosWS), Is.EqualTo(new Vector4(1f, 1.6f, 2f, 0f)));
            Assert.That(Shader.GetGlobalFloat(AtlasShaderIDs.HypL), Is.EqualTo(1.9f));
            Assert.That(Shader.GetGlobalVector(AtlasShaderIDs.FogParams).x, Is.EqualTo(2f));
            Assert.That(Shader.GetGlobalVector(AtlasShaderIDs.FogParams).y, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(Shader.GetGlobalFloat(AtlasShaderIDs.Time), Is.EqualTo(12.5f));
            Assert.That(Shader.GetGlobalFloat(AtlasShaderIDs.Fade), Is.EqualTo(1f));
            Assert.That(RenderSettings.skybox, Is.Not.Null);
            Assert.That(RenderSettings.skybox.shader.name, Is.EqualTo("Hidden/Atlas/Background"));
            AtlasLook.ResetForSpace();
            Assert.That(Shader.GetGlobalVector(AtlasShaderIDs.FogParams).x, Is.EqualTo(AtlasLook.DefaultFogStart));
        }

        [Test]
        public void Mesh_UploadsInTheContractLayout()
        {
            var builder = new AtlasMeshBuilder();
            builder.AddBox(new float3(1f, 0f, 2f), new float3(1f, 0.5f, 0.75f), 0f, AtlasPalette.Uv(AtlasPalette.Placeholder));
            Mesh mesh = AtlasMesh.Create(builder, "test box");
            try
            {
                Assert.That(mesh.vertexCount, Is.EqualTo(builder.VertexCount));
                Assert.That(mesh.GetIndexCount(0), Is.EqualTo((uint)builder.IndexCount));
                Assert.That(mesh.indexFormat, Is.EqualTo(IndexFormat.UInt16));
                Assert.That(mesh.GetVertexBufferStride(0), Is.EqualTo(AtlasVertex.Stride));
                Assert.That(mesh.GetVertexAttributeOffset(VertexAttribute.Color), Is.EqualTo(24));
                Assert.That(mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord1), Is.EqualTo(36));
                Assert.That(mesh.subMeshCount, Is.EqualTo(1));
                Assert.That(mesh.bounds.center.x, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(mesh.bounds.size.y, Is.EqualTo(0.5f).Within(1e-5f));

                builder.Clear();
                builder.AddQuad(float3.zero, new float3(1f, 0f, 0f), new float3(0f, 0f, 1f), float2.zero);
                AtlasMesh.Apply(builder, mesh);
                Assert.That(mesh.vertexCount, Is.EqualTo(4));
                Assert.That(mesh.GetIndexCount(0), Is.EqualTo(6u));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }
}
#endif

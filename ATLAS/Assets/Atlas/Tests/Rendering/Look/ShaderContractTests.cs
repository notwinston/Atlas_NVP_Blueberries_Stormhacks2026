using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Atlas.Rendering.Tests.Look
{
    /// <summary>
    /// Shader_Contract checks that need no compiler (Master Plan section 11.1): the files are where a player
    /// build finds them, there is one per-material block in the frozen order, and no keyword is declared.
    /// </summary>
    public class ShaderContractTests
    {
        static string ShaderDir
        {
            get
            {
                string project = Environment.GetEnvironmentVariable("ATLAS_PROJECT_DIR");
                if (string.IsNullOrEmpty(project)) project = Directory.GetCurrentDirectory();
                return Path.Combine(project, "Assets", "Atlas", "Rendering", "Shaders", "Resources");
            }
        }

        static string Read(string name)
        {
            return File.ReadAllText(Path.Combine(ShaderDir, name));
        }

        static string StripComments(string source)
        {
            return Regex.Replace(source, @"//[^\n]*", "");
        }

        [Test]
        public void EveryFileOfTheLook_IsUnderResources()
        {
            foreach (string name in new[] { "AtlasSurface.shader", "AtlasBackground.shader", "AtlasCore.hlsl", "AtlasWarp.hlsl", "AtlasPortalClip.hlsl", "AtlasGates.hlsl" })
                Assert.That(File.Exists(Path.Combine(ShaderDir, name)), Is.True, name);
            Assert.That(Read("AtlasSurface.shader"), Does.Contain("Shader \"Atlas/Surface\""));
            Assert.That(Read("AtlasBackground.shader"), Does.Contain("Shader \"Hidden/Atlas/Background\""));
            Assert.That(AtlasLook.SurfaceShaderResource, Is.EqualTo("AtlasSurface"));
            Assert.That(AtlasLook.BackgroundShaderResource, Is.EqualTo("AtlasBackground"));
        }

        [Test]
        public void NoKeywordAnywhere_OneVariantPerPass()
        {
            foreach (string name in Directory.GetFiles(ShaderDir))
            {
                if (name.EndsWith(".meta")) continue;
                string source = StripComments(File.ReadAllText(name));
                Assert.That(source, Does.Not.Contain("multi_compile"), name);
                Assert.That(source, Does.Not.Contain("shader_feature"), name);
                Assert.That(source, Does.Not.Contain("dynamic_branch"), name);
                Assert.That(source, Does.Not.Contain("include_with_pragmas"), name);
            }
            string surface = StripComments(Read("AtlasSurface.shader"));
            Assert.That(Regex.Matches(surface, @"\bPass\s*\{").Count, Is.EqualTo(1), "one pass: no shadow, depth or meta pass");
            Assert.That(surface, Does.Contain("\"LightMode\" = \"UniversalForward\""));
            Assert.That(surface, Does.Contain("\"RenderPipeline\" = \"UniversalPipeline\""));
        }

        [Test]
        public void PerMaterialBlock_IsDeclaredOnce_InTheFrozenOrder()
        {
            string core = StripComments(Read("AtlasCore.hlsl"));
            Assert.That(Regex.Matches(core, @"CBUFFER_START\(UnityPerMaterial\)").Count, Is.EqualTo(1));
            foreach (string other in new[] { "AtlasWarp.hlsl", "AtlasPortalClip.hlsl", "AtlasGates.hlsl", "AtlasSurface.shader", "AtlasBackground.shader" })
                Assert.That(StripComments(Read(other)), Does.Not.Contain("CBUFFER_START"), other);

            int start = core.IndexOf("CBUFFER_START(UnityPerMaterial)", StringComparison.Ordinal);
            int end = core.IndexOf("CBUFFER_END", StringComparison.Ordinal);
            string block = core.Substring(start, end - start);
            MatchCollection fields = Regex.Matches(block, @"(float4x4|float4|float)\s+(_\w+)\s*;");
            string[] expected =
            {
                "float4x4 _Anchor", "float4 _BaseTint", "float4 _LineColor", "float4 _LineParams", "float4 _LineMajor",
                "float4 _PortalPlane", "float4 _Gate0", "float4 _Gate1", "float4 _Gate2", "float4 _Gate3",
                "float4 _SurfaceParams", "float _WarpMode",
            };
            Assert.That(fields.Count, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
                Assert.That(fields[i].Groups[1].Value + " " + fields[i].Groups[2].Value, Is.EqualTo(expected[i]));
        }

        [Test]
        public void Properties_CoverTheBlockAndTheRenderState()
        {
            string surface = StripComments(Read("AtlasSurface.shader"));
            foreach (string property in new[] { "_BaseTint", "_LineColor", "_LineParams", "_LineMajor", "_PortalPlane", "_Gate0", "_Gate1", "_Gate2", "_Gate3", "_SurfaceParams", "_WarpMode", "_Cull", "_StencilRef", "_StencilComp" })
                Assert.That(Regex.IsMatch(surface, property + @"\s*\(""[^""]*"",\s*(Vector|Float)\)"), Is.True, property);
            Assert.That(surface, Does.Contain("Cull [_Cull]"));
            Assert.That(surface, Does.Contain("Ref [_StencilRef]"));
            Assert.That(surface, Does.Contain("Comp [_StencilComp]"));
            Assert.That(surface, Does.Contain("ZWrite On"));
            Assert.That(surface, Does.Contain("\"Queue\" = \"Geometry\""));
        }

        [Test]
        public void Includes_ComeInTheBindingOrder()
        {
            string surface = Read("AtlasSurface.shader");
            string[] order =
            {
                "ShaderLibrary/Core.hlsl\"", "\"AtlasCore.hlsl\"", "\"AtlasWarp.hlsl\"", "\"AtlasPortalClip.hlsl\"", "\"AtlasGates.hlsl\"",
            };
            int last = -1;
            foreach (string include in order)
            {
                int at = surface.IndexOf(include, StringComparison.Ordinal);
                Assert.That(at, Is.GreaterThan(last), include);
                last = at;
            }
        }

        [Test]
        public void EntryPoints_HaveTheContractSignatures()
        {
            Assert.That(Read("AtlasWarp.hlsl"), Does.Contain("float3 AtlasWarp(float3 positionOS)"));
            Assert.That(Read("AtlasWarp.hlsl"), Does.Contain("return TransformObjectToWorld(positionOS);"));
            Assert.That(Read("AtlasGates.hlsl"), Does.Contain("void AtlasGatesClip(float3 positionWS)"));
            Assert.That(Read("AtlasPortalClip.hlsl"), Does.Contain("void AtlasPortalClip(float3 positionWS)"));
            string core = Read("AtlasCore.hlsl");
            foreach (string signature in new[]
            {
                "half3 AtlasPaletteColor(float2 uv0)", "bool AtlasIsEmissive(float2 uv0)",
                "half AtlasLineMask(float2 lineCoord, half lineWeight)", "half AtlasFogFactor(float3 positionWS)",
                "void AtlasDitherClip(float4 positionCS, half opacity)", "half3 AtlasBackground(float3 viewDirWS)",
                "half4 AtlasShade(float3 positionWS, float2 uv0, float2 lineCoord, half4 vertexColor, float4 positionCS)",
            })
                Assert.That(core, Does.Contain(signature));
            string surface = Read("AtlasSurface.shader");
            Assert.That(surface, Does.Contain("AtlasWarp(input.positionOS)"));
            Assert.That(surface.IndexOf("AtlasGatesClip(input.positionWS)", StringComparison.Ordinal),
                        Is.LessThan(surface.IndexOf("AtlasPortalClip(input.positionWS)", StringComparison.Ordinal)));
        }
    }
}

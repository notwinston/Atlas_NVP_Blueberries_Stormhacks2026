using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Rendering.Tests.Look
{
    /// <summary>The values behind the shader globals, and the fog cross-fade (lane A contract, 10.4 and 10.8).</summary>
    public class LookStateTests
    {
        [Test]
        public void Default_MatchesTheContractDefaults()
        {
            AtlasLookState state = AtlasLookState.Default();
            Assert.That(state.HeadPosition.y, Is.EqualTo(1.6f));
            Assert.That(state.MetresPerUnit, Is.EqualTo(1f));
            Assert.That(state.FogParams.x, Is.EqualTo(8f));
            Assert.That(state.FogParams.y, Is.EqualTo(1f / 6f).Within(1e-6f));
            Assert.That(state.Shade.x, Is.EqualTo(0.25f));
            Assert.That(state.Shade.y, Is.EqualTo(1f));
            Assert.That(state.LineWidth, Is.EqualTo(1.5f));
            Assert.That(state.Fade, Is.EqualTo(1f));
            Assert.That(math.all(state.FogColor == AtlasPalette.Linear(AtlasPalette.FogBase)), Is.True);
            Assert.That(math.all(state.SkyTop == AtlasPalette.Linear(AtlasPalette.SkyTop)), Is.True);
            Assert.That(state.FogFading, Is.False);
        }

        [Test]
        public void FogColour_CrossFadesOverOneSecond()
        {
            AtlasLookState state = AtlasLookState.Default();
            float3 from = state.FogColor;
            float3 to = new float3(0.2f, 0.4f, 0.6f);
            state.SetFogColor(to, false);
            Assert.That(math.all(state.FogColor == from), Is.True, "nothing changes before the next frame");
            Assert.That(state.FogFading, Is.True);

            state.Step(float3.zero, 1.0, 0.0, 0.25f);
            Assert.That(math.cmax(math.abs(state.FogColor - math.lerp(from, to, 0.25f))), Is.LessThan(1e-6f));
            state.Step(float3.zero, 1.0, 0.25, 0.25f);
            Assert.That(math.cmax(math.abs(state.FogColor - math.lerp(from, to, 0.5f))), Is.LessThan(1e-6f));
            state.Step(float3.zero, 1.0, 0.5, 5f);
            Assert.That(math.all(state.FogColor == to), Is.True);
            Assert.That(state.FogFading, Is.False);
        }

        [Test]
        public void FogColour_ImmediateSkipsTheFade()
        {
            AtlasLookState state = AtlasLookState.Default();
            float3 to = new float3(0.1f, 0.1f, 0.3f);
            state.SetFogColor(to, true);
            Assert.That(math.all(state.FogColor == to), Is.True);
            Assert.That(state.FogFading, Is.False);
        }

        [Test]
        public void FogColour_RetargetStartsFromTheColourDrawnNow()
        {
            AtlasLookState state = AtlasLookState.Default();
            state.SetFogColor(new float3(1f, 1f, 1f), false);
            state.Step(float3.zero, 1.0, 0.0, 0.5f);
            float3 midway = state.FogColor;
            state.SetFogColor(new float3(0f, 0f, 0f), false);
            Assert.That(math.all(state.FogColor == midway), Is.True, "no jump on a retarget");
            state.Step(float3.zero, 1.0, 0.5, 0.5f);
            Assert.That(math.cmax(math.abs(state.FogColor - midway * 0.5f)), Is.LessThan(1e-6f));
        }

        [Test]
        public void SetFog_KeepsTheRampValid()
        {
            AtlasLookState state = AtlasLookState.Default();
            state.SetFog(2f, 3f);
            Assert.That(state.FogParams.x, Is.EqualTo(2f));
            Assert.That(state.FogParams.y, Is.EqualTo(1f).Within(1e-6f));
            state.SetFog(5f, 5f);
            Assert.That(float.IsInfinity(state.FogParams.y) || float.IsNaN(state.FogParams.y), Is.False);
            Assert.That(state.FogEnd, Is.GreaterThan(state.FogStart));
        }

        [Test]
        public void Step_WrapsTimeAndGuardsTheUnit()
        {
            AtlasLookState state = AtlasLookState.Default();
            state.Step(new float3(1f, 1.5f, -2f), 1.9, 3601.5, 1f / 60f);
            Assert.That(state.Time, Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(state.MetresPerUnit, Is.EqualTo(1.9f));
            Assert.That(state.HeadPosition.z, Is.EqualTo(-2f));
            state.Step(float3.zero, 0.0, 10.0, 1f / 60f);
            Assert.That(state.MetresPerUnit, Is.EqualTo(1f), "a unit of 0 would divide by zero in the warp");
        }

        [Test]
        public void ResetForSpace_RestoresDefaultsAtOnce()
        {
            AtlasLookState state = AtlasLookState.Default();
            state.SetFog(1f, 2f);
            state.SetFogColor(new float3(1f, 0f, 0f), false);
            state.SkyTop = new float3(0f, 1f, 0f);
            state.ResetForSpace();
            Assert.That(state.FogStart, Is.EqualTo(8f));
            Assert.That(state.FogEnd, Is.EqualTo(14f));
            Assert.That(state.FogFading, Is.False);
            Assert.That(math.all(state.FogColor == AtlasPalette.Linear(AtlasPalette.FogBase)), Is.True);
            Assert.That(math.all(state.SkyTop == AtlasPalette.Linear(AtlasPalette.SkyTop)), Is.True);
        }
    }
}

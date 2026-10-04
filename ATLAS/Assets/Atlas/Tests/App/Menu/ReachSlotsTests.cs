using System;
using System.IO;
using System.Text.RegularExpressions;
using Atlas.App.Menu;
using Atlas.Rendering;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.App.Tests
{
    /// <summary>
    /// The table of places within reach of the hands (documents/ATLAS_Hands_Design.md section 6.1), what a view
    /// asks of it, and the shader the near text is drawn with. Engine-free: the table is arithmetic and the
    /// shader is read as text.
    /// </summary>
    public class ReachSlotsTests
    {
        /// <summary>The scale Atlas.Hands gives at the usual reach (HandsConstants.ReachPanelScale).</summary>
        const float UsualScale = 0.75f;

        /// <summary>The near layout as Atlas.Hands publishes it, with every number in the test's hands.</summary>
        sealed class Reach : IReachLayout
        {
            public bool Near { get; set; }
            public float3 Position { get; set; }
            public quaternion Rotation { get; set; }
            public float Scale { get; set; }
        }

        Reach _reach;
        IReachLayout _before;

        [SetUp]
        public void StartWithNoLayout()
        {
            if (AtlasServices.TryGet(out _before)) AtlasServices.Unregister(_before);
            _reach = null;
        }

        [TearDown]
        public void PutTheLayoutBack()
        {
            if (_reach != null) AtlasServices.Unregister<IReachLayout>(_reach);
            if (_before != null) AtlasServices.Register(_before);
        }

        Reach Publish(bool near, float3 position, quaternion rotation, float scale)
        {
            if (_reach != null) AtlasServices.Unregister<IReachLayout>(_reach);
            _reach = new Reach { Near = near, Position = position, Rotation = rotation, Scale = scale };
            AtlasServices.Register<IReachLayout>(_reach);
            return _reach;
        }

        // ---- the table -------------------------------------------------------------------------------------

        [Test]
        public void TheTable_HasAPlaceForEverySlot_OnAGridAboutTheAnchor()
        {
            Assert.That(Enum.GetValues(typeof(ReachSlot)).Length, Is.EqualTo(ReachSlots.Count));
            float p = ReachSlots.Pitch;
            Assert.That(ReachSlots.Centre(ReachSlot.MenuPanel), Is.EqualTo(float3.zero), "the anchor's origin is the panel's centre");
            Assert.That(ReachSlots.Centre(ReachSlot.MenuChip), Is.EqualTo(new float3(-p, p, 0f)));
            Assert.That(ReachSlots.Centre(ReachSlot.Mode), Is.EqualTo(new float3(p, p, 0f)));
            Assert.That(ReachSlots.Centre(ReachSlot.DialLeft), Is.EqualTo(new float3(-p, 0f, 0f)));
            Assert.That(ReachSlots.Centre(ReachSlot.SignHome), Is.EqualTo(new float3(0f, 0f, 0f)));
            Assert.That(ReachSlots.Centre(ReachSlot.DialRight), Is.EqualTo(new float3(p, 0f, 0f)));
            Assert.That(ReachSlots.Centre(ReachSlot.SignPrevious), Is.EqualTo(new float3(-p, -p, 0f)));
            Assert.That(ReachSlots.Centre(ReachSlot.SignMenu), Is.EqualTo(new float3(0f, -p, 0f)));
            Assert.That(ReachSlots.Centre(ReachSlot.SignNext), Is.EqualTo(new float3(p, -p, 0f)));

            // Every slot but the panel's has a place of its own.
            for (int a = 1; a < ReachSlots.Count; a++)
                for (int b = a + 1; b < ReachSlots.Count; b++)
                    Assert.That(math.distance(ReachSlots.Centre((ReachSlot)a), ReachSlots.Centre((ReachSlot)b)),
                        Is.GreaterThanOrEqualTo(p - 1e-6f), (ReachSlot)a + " and " + (ReachSlot)b);
        }

        [Test]
        public void OnlyTheMenuPanel_StaysWhileTheMenuIsOpen()
        {
            Assert.That(ReachSlots.HidesWhileMenuOpen(ReachSlot.MenuPanel), Is.False);
            for (int i = 1; i < ReachSlots.Count; i++)
                Assert.That(ReachSlots.HidesWhileMenuOpen((ReachSlot)i), Is.True, ((ReachSlot)i) + " stands where the open panel stands");
            Assert.That(MenuLayout.PanelSlot, Is.EqualTo(ReachSlot.MenuPanel));
            Assert.That(MenuLayout.ChipSlot, Is.EqualTo(ReachSlot.MenuChip));
        }

        [Test]
        public void APlace_IsItsCentreInTheAnchorsFrame_TimesTheScale()
        {
            // The table says x right, y up along the panel, z towards the walker; the anchor's +Z points away.
            Assert.That(ReachSlots.Local(new float3(0.1f, 0.2f, 0.3f), 0.5f), Is.EqualTo(new float3(0.05f, 0.1f, -0.15f)));
            Assert.That(ReachSlots.Local(ReachSlot.MenuChip, 0.5f), Is.EqualTo(new float3(-0.5f * ReachSlots.Pitch, 0.5f * ReachSlots.Pitch, 0f)));

            var position = new float3(1f, 1.2f, -2f);
            // An anchor turned a quarter to the right: its +X is the room's -Z, its +Z the room's +X.
            quaternion rotation = quaternion.RotateY(math.radians(90f));
            float3 chip = ReachSlots.World(position, rotation, 0.5f, ReachSlot.MenuChip);
            float h = 0.5f * ReachSlots.Pitch;
            Assert.That(math.distance(chip, position + new float3(0f, h, h)), Is.LessThan(1e-6f), chip.ToString());
            Assert.That(ReachSlots.World(position, rotation, 0.5f, ReachSlot.MenuPanel), Is.EqualTo(position));
        }

        [Test]
        public void ATarget_IsItsSizeTimesTheScale_AndNeverUnderThreeCentimetres()
        {
            Assert.That(ReachSlots.MinTargetMetres, Is.EqualTo(0.03f));
            Assert.That(ReachSlots.TargetHalf(0.06f, 0.75f), Is.EqualTo(0.06f * 0.75f));
            Assert.That(ReachSlots.TargetHalf(0.018f, 0.75f), Is.EqualTo(0.015f), "1.35 cm becomes 1.5 cm");
            Assert.That(ReachSlots.TargetHalf(0.06f, 0.1f), Is.EqualTo(0.015f));
            Assert.That(2f * ReachSlots.TargetHalf(0.02f, 0.75f), Is.GreaterThanOrEqualTo(ReachSlots.MinTargetMetres), "a menu row is 3 cm tall");
        }

        [Test]
        public void AtTheUsualScale_EveryTargetOfTheOpenMenu_IsThreeCentimetresAcross_AndOnThePanel()
        {
            for (int target = 0; target < MenuLayout.TargetCount; target++)
            {
                float2 half = MenuLayout.NearTargetHalfSize(target, UsualScale);
                Assert.That(2f * math.cmin(half), Is.GreaterThanOrEqualTo(ReachSlots.MinTargetMetres), "target " + target);
                float2 usual = MenuLayout.TargetHalfSize(target);
                Assert.That(half.x, Is.EqualTo(math.max(UsualScale * usual.x, 0.015f)).Within(1e-7f));
                Assert.That(half.y, Is.EqualTo(math.max(UsualScale * usual.y, 0.015f)).Within(1e-7f));
                float3 centre = UsualScale * MenuLayout.TargetCentre(target, 7);
                Assert.That(math.abs(centre.x) + half.x, Is.LessThanOrEqualTo(UsualScale * MenuLayout.Width * 0.5f + 1e-5f), "target " + target);
                Assert.That(math.abs(centre.y) + half.y, Is.LessThanOrEqualTo(UsualScale * MenuLayout.Height * 0.5f + 1e-5f), "target " + target);
            }
            // Rows 3 cm tall on a 3.15 cm pitch: no row on the next.
            float pitch = UsualScale * MenuLayout.RowPitch;
            Assert.That(pitch, Is.GreaterThanOrEqualTo(2f * MenuLayout.NearTargetHalfSize(0, UsualScale).y));
            // The step targets stand on the focused row and not on its neighbours.
            Assert.That(MenuLayout.NearTargetHalfSize(MenuLayout.Plus, UsualScale).y, Is.LessThanOrEqualTo(0.5f * pitch));
            // The panel is 0.525 by 0.41 m since the menu grew to nine rows: within 21 cm of the anchor up and down,
            // wider than that to each side.
            Assert.That(UsualScale * MenuLayout.Height * 0.5f, Is.LessThanOrEqualTo(0.21f));
            Assert.That(UsualScale * MenuLayout.Width * 0.5f, Is.EqualTo(0.2625f).Within(1e-6f));
        }

        // ---- what a view asks ------------------------------------------------------------------------------

        [Test]
        public void TryGetNear_IsFalse_WithNoLayout_AndWithNearFalse()
        {
            float3 position;
            quaternion rotation;
            float scale;
            Assert.That(ReachSlots.TryGetNear(out position, out rotation, out scale), Is.False, "no service");
            Assert.That(scale, Is.EqualTo(1f));
            Assert.That(rotation, Is.EqualTo(quaternion.identity));

            Publish(false, new float3(1f, 2f, 3f), quaternion.RotateY(1f), 0.75f);
            Assert.That(ReachSlots.TryGetNear(out position, out rotation, out scale), Is.False, "Near false");
            Assert.That(position, Is.EqualTo(float3.zero), "nothing of a far layout is handed on");
            Assert.That(scale, Is.EqualTo(1f));
        }

        [Test]
        public void TryGetNear_HandsOnTheAnchorOfThisFrame()
        {
            quaternion turn = math.mul(quaternion.RotateY(0.4f), quaternion.RotateX(0.3f));
            Reach reach = Publish(true, new float3(1f, 2f, 3f), turn, 0.75f);
            float3 position;
            quaternion rotation;
            float scale;
            Assert.That(ReachSlots.TryGetNear(out position, out rotation, out scale), Is.True);
            Assert.That(position, Is.EqualTo(reach.Position));
            Assert.That(rotation, Is.EqualTo(turn), "a rotation of unit length is handed on as it is");
            Assert.That(scale, Is.EqualTo(0.75f));

            // A rotation that is not of unit length is brought to it: a view multiplies vectors by it.
            reach.Rotation = new quaternion(turn.value * 3f);
            Assert.That(ReachSlots.TryGetNear(out position, out rotation, out scale), Is.True);
            Assert.That(math.length(rotation.value), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(math.abs(math.dot(rotation.value, turn.value)), Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void TryGetNear_IsFalse_ForNumbersThatAreNotAPose()
        {
            float3 position;
            quaternion rotation;
            float scale;
            var at = new float3(1f, 2f, 3f);
            foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                Publish(true, at, quaternion.identity, bad);
                Assert.That(ReachSlots.TryGetNear(out position, out rotation, out scale), Is.False, "scale " + bad);
            }
            Publish(true, new float3(1f, float.NaN, 3f), quaternion.identity, 0.75f);
            Assert.That(ReachSlots.TryGetNear(out position, out rotation, out scale), Is.False, "a position that is not a number");
            Publish(true, new float3(float.PositiveInfinity, 0f, 0f), quaternion.identity, 0.75f);
            Assert.That(ReachSlots.TryGetNear(out position, out rotation, out scale), Is.False, "a position at infinity");
            Publish(true, at, new quaternion(0f, 0f, 0f, 0f), 0.75f);
            Assert.That(ReachSlots.TryGetNear(out position, out rotation, out scale), Is.False, "a rotation of no length");
            Publish(true, at, new quaternion(float.NaN, 0f, 0f, 1f), 0.75f);
            Assert.That(ReachSlots.TryGetNear(out position, out rotation, out scale), Is.False, "a rotation that is not a number");
            Assert.That(scale, Is.EqualTo(1f));
            Assert.That(position, Is.EqualTo(float3.zero));
        }

        [Test]
        public void TargetsAreMadeAgain_OnlyWhenTheScaleOrTheRotationReallyMoved()
        {
            Assert.That(ReachSlots.ScaleMoved(0.75f, 0.75f), Is.False);
            Assert.That(ReachSlots.ScaleMoved(0.75f, 0.75f + 1e-6f), Is.False, "a tremble in the last bits");
            Assert.That(ReachSlots.ScaleMoved(0.75f, 0.76f), Is.True);
            Assert.That(ReachSlots.ScaleMoved(1f, 0.75f), Is.True, "coming within reach");
            Assert.That(ReachSlots.ScaleMoved(0.75f, float.NaN), Is.True, "never mistaken for the same");

            quaternion q = math.mul(quaternion.RotateY(0.4f), quaternion.RotateX(0.3f));
            Assert.That(ReachSlots.RotationMoved(q, q), Is.False);
            Assert.That(ReachSlots.RotationMoved(q, new quaternion(-q.value)), Is.False, "the same rotation, written the other way");
            Assert.That(ReachSlots.RotationMoved(q, math.mul(quaternion.RotateY(math.radians(0.05f)), q)), Is.False, "under a millimetre at the panel's edge");
            Assert.That(ReachSlots.RotationMoved(q, math.mul(quaternion.RotateY(math.radians(0.5f)), q)), Is.True);
            Assert.That(ReachSlots.RotationMoved(quaternion.identity, quaternion.RotateY(math.radians(90f))), Is.True);
        }

        // ---- the near text's shader (decision R7; Master Plan section 11.1, Shader_Contract) -------------------

        static string ShaderPath
        {
            get
            {
                string project = Environment.GetEnvironmentVariable("ATLAS_PROJECT_DIR");
                if (string.IsNullOrEmpty(project)) project = Directory.GetCurrentDirectory();
                return Path.Combine(project, "Assets", "Atlas", "Rendering", "Shaders", "Resources", NearText.ShaderResource + ".shader");
            }
        }

        [Test]
        public void TheNearTextShader_IsTheFontShader_WithTheDepthTestOn_AndNoKeyword()
        {
            Assert.That(NearText.ShaderResource, Is.EqualTo("AtlasNearText"));
            Assert.That(NearText.ShaderName, Is.EqualTo("Atlas/NearText"));
            Assert.That(File.Exists(ShaderPath), Is.True, "under Resources, where a player build finds it by name: " + ShaderPath);
            string source = Regex.Replace(File.ReadAllText(ShaderPath), @"//[^\n]*", "");

            Assert.That(source, Does.Contain("Shader \"" + NearText.ShaderName + "\""));
            // What the built-in font shader does, with one word changed: the depth test.
            Assert.That(source, Does.Contain("ZTest LEqual"));
            Assert.That(source, Does.Not.Contain("ZTest Always"));
            Assert.That(source, Does.Contain("ZWrite Off"));
            Assert.That(source, Does.Contain("Cull Off"));
            Assert.That(source, Does.Contain("Blend SrcAlpha OneMinusSrcAlpha"));
            Assert.That(source, Does.Contain("\"Queue\" = \"Transparent\""));
            Assert.That(source, Does.Contain("_MainTex"), "the font's texture");
            Assert.That(source, Does.Contain("_Color"));
            Assert.That(Regex.Matches(source, @"\bPass\s*\{").Count, Is.EqualTo(1), "one pass");
            Assert.That(source, Does.Not.Contain("LightMode"), "no tag: the pipeline draws it as unlit, in the transparent queue");
            Assert.That(source, Does.Contain("\"RenderPipeline\" = \"UniversalPipeline\""));
            // Contract README F7, as ShaderContractTests asks of every file in that folder: one variant per pass.
            foreach (string keyword in new[] { "multi_compile", "shader_feature", "dynamic_branch", "include_with_pragmas" })
                Assert.That(source, Does.Not.Contain(keyword), keyword);
            // And the per-material block is its own: it shares nothing with Atlas/Surface.
            Assert.That(Regex.Matches(source, @"CBUFFER_START\(UnityPerMaterial\)").Count, Is.EqualTo(1));
            Assert.That(source, Does.Not.Contain("AtlasCore.hlsl"));
        }
    }
}

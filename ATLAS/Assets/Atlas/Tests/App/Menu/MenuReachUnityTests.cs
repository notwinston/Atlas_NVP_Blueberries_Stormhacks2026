#if !ATLAS_HEADLESS
using Atlas.App.Menu;
using Atlas.Rendering;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.App.Tests
{
    /// <summary>
    /// What the menu and the signs draw when they come within reach of the hands (documents/ATLAS_Hands_Design.md
    /// section 6.1), in the engine: the world layer on every object (a child does not take its parent's layer),
    /// the size times the scale, text that goes behind a finger, and everything back as it was when the layout
    /// says far. Needs the Test Runner; the places and the gaze targets are tested headless.
    /// </summary>
    [Category("Unity")]
    public class MenuReachUnityTests
    {
        const float Scale = 0.75f;

        GameObject _hud;
        MaterialPool _pool;
        Font _font;

        [SetUp]
        public void BuildTheHudParent()
        {
            _hud = new GameObject("Hud") { layer = PortalStencilTable.HudLayer };
            _pool = MaterialPool.CreateDefault(8);
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        [TearDown]
        public void Destroy()
        {
            Object.DestroyImmediate(_hud);
            _pool.Dispose();
        }

        static void AllOnLayer(Transform root, int layer, string what)
        {
            Assert.That(root.gameObject.layer, Is.EqualTo(layer), what);
            for (int i = 0; i < root.childCount; i++)
                Assert.That(root.GetChild(i).gameObject.layer, Is.EqualTo(layer), what + ": " + root.GetChild(i).name);
        }

        void TextIsDepthTested(Transform root, string what)
        {
            TextMesh[] texts = root.GetComponentsInChildren<TextMesh>(true);
            Assert.That(texts.Length, Is.GreaterThan(0), what);
            for (int i = 0; i < texts.Length; i++)
            {
                Material material = texts[i].GetComponent<MeshRenderer>().sharedMaterial;
                Assert.That(material.shader.name, Is.EqualTo(NearText.ShaderName), what + ": " + texts[i].name);
                Assert.That(material.mainTexture, Is.EqualTo(_font.material.mainTexture), "the font's own texture");
            }
        }

        void TextIsTheFonts(Transform root, string what)
        {
            TextMesh[] texts = root.GetComponentsInChildren<TextMesh>(true);
            for (int i = 0; i < texts.Length; i++)
                Assert.That(texts[i].GetComponent<MeshRenderer>().sharedMaterial, Is.EqualTo(_font.material), what + ": " + texts[i].name);
        }

        [Test]
        public void TheNearTextShader_IsInResources_AndItsMaterialCarriesTheFontsTexture()
        {
            Assume.That(_font, Is.Not.Null, "the built-in font");
            Shader shader = Resources.Load<Shader>(NearText.ShaderResource);
            Assert.That(shader, Is.Not.Null, "loaded by name, as the app's other shaders are");
            Assert.That(shader.name, Is.EqualTo(NearText.ShaderName));

            Material material = NearText.For(_font);
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader, Is.EqualTo(shader));
            Assert.That(material.mainTexture, Is.EqualTo(_font.material.mainTexture));
            Assert.That(material.renderQueue, Is.EqualTo(3000), "the transparent queue, as the font's own material");
            Assert.That(material.passCount, Is.EqualTo(1));
            Assert.That(NearText.For(_font), Is.EqualTo(material), "one material per font, made once");
            Assert.That(NearText.For(null), Is.Null);
        }

        [Test]
        public void MenuView_WithinReach_IsOnTheWorldLayer_TimesTheScale_WithDepthTestedText_AndGoesBack()
        {
            Assume.That(_font, Is.Not.Null, "the built-in font");
            MenuView view = MenuView.Create(_hud.transform, _pool);
            Transform panel = _hud.transform.Find("AtlasMenu"), chip = _hud.transform.Find("AtlasMenuChip");
            Assert.That(panel, Is.Not.Null);
            Assert.That(chip, Is.Not.Null);
            Material surface = panel.GetComponent<MeshRenderer>().sharedMaterial;
            var corner = new Vector3(MenuLayout.OpenChip.x, MenuLayout.OpenChip.y, MenuLayout.OpenChip.z);
            var frame = (Quaternion)quaternion.RotateY(0.3f);
            var at = new float3(0.4f, 1.1f, 0.7f);

            // As built: the head-locked layer, the size the layout gives, the font's own material.
            AllOnLayer(panel, PortalStencilTable.HudLayer, "the panel, as built");
            AllOnLayer(chip, PortalStencilTable.HudLayer, "the chip, as built");
            TextIsTheFonts(panel, "the panel, as built");
            TextIsTheFonts(chip, "the chip, as built");
            Vector3[] textPlaces = new Vector3[panel.childCount];
            for (int i = 0; i < panel.childCount; i++) textPlaces[i] = panel.GetChild(i).localPosition;
            view.PlaceChip(at, frame);
            Assert.That(Quaternion.Angle(chip.rotation, frame * Quaternion.LookRotation(corner)), Is.LessThan(0.01f), "turned to face the head from its corner");

            view.SetReach(true, Scale);
            AllOnLayer(panel, PortalStencilTable.WorldLayer, "the panel within reach");
            AllOnLayer(chip, PortalStencilTable.WorldLayer, "the chip within reach");
            Assert.That(panel.localScale, Is.EqualTo(new Vector3(Scale, Scale, Scale)), "0.70 by 0.50 m becomes 0.525 by 0.375 m");
            Assert.That(chip.localScale, Is.EqualTo(new Vector3(Scale, Scale, Scale)));
            Bounds mesh = panel.GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.That(mesh.size.x * panel.lossyScale.x, Is.EqualTo(MenuLayout.Width * Scale).Within(1e-5f));
            Assert.That(mesh.size.y * panel.lossyScale.y, Is.EqualTo(MenuLayout.Height * Scale).Within(1e-5f));
            TextIsDepthTested(panel, "the panel within reach");
            TextIsDepthTested(chip, "the chip within reach");
            Assert.That(panel.GetComponent<MeshRenderer>().sharedMaterial, Is.EqualTo(surface), "the panel keeps the look's shader: opaque, depth-tested");
            Assert.That(surface.shader.name, Is.EqualTo("Atlas/Surface"));

            view.PlacePanel(at, (quaternion)frame);
            Assert.That(Vector3.Distance(panel.position, (Vector3)at), Is.LessThan(1e-6f));
            Assert.That(Quaternion.Angle(panel.rotation, frame), Is.LessThan(0.01f));
            view.PlaceChip(at, (quaternion)frame);
            Assert.That(Vector3.Distance(chip.position, (Vector3)at), Is.LessThan(1e-6f));
            Assert.That(Quaternion.Angle(chip.rotation, frame), Is.LessThan(0.01f), "within reach the chip lies in the anchor's plane");

            // Far again: every layer, size and material as built.
            view.SetReach(false, 1f);
            AllOnLayer(panel, PortalStencilTable.HudLayer, "the panel, back");
            AllOnLayer(chip, PortalStencilTable.HudLayer, "the chip, back");
            Assert.That(panel.localScale, Is.EqualTo(Vector3.one));
            Assert.That(chip.localScale, Is.EqualTo(Vector3.one));
            TextIsTheFonts(panel, "the panel, back");
            TextIsTheFonts(chip, "the chip, back");
            for (int i = 0; i < panel.childCount; i++)
                Assert.That(panel.GetChild(i).localPosition, Is.EqualTo(textPlaces[i]), "text " + panel.GetChild(i).name + " did not move on the panel");
            view.PlaceChip(at, (quaternion)frame);
            Assert.That(Quaternion.Angle(chip.rotation, frame * Quaternion.LookRotation(corner)), Is.LessThan(0.01f));

            view.Dispose();
            Assert.That(_pool.Rented, Is.EqualTo(0));
        }

        [Test]
        public void SpaceSignsView_WithinReach_StandsOnItsSlots_OnTheWorldLayer_WithDepthTestedText_AndGoesBack()
        {
            Assume.That(_font, Is.Not.Null, "the built-in font");
            SpaceSignsView view = SpaceSignsView.Create(_hud.transform, _pool);
            Transform root = _hud.transform.Find("AtlasSpaceSigns");
            Assert.That(root, Is.Not.Null);
            Mesh mesh = root.GetComponent<MeshFilter>().sharedMesh;
            Material surface = root.GetComponent<MeshRenderer>().sharedMaterial;

            // As built: the head-locked layer, the boards a metre ahead in the root's frame.
            AllOnLayer(root, PortalStencilTable.HudLayer, "the signs, as built");
            TextIsTheFonts(root, "the signs, as built");
            Bounds far = mesh.bounds;
            Assert.That(far.center.z, Is.EqualTo(1.0f).Within(1e-6f));
            Vector3[] textPlaces = new Vector3[root.childCount];
            for (int i = 0; i < root.childCount; i++) textPlaces[i] = root.GetChild(i).localPosition;

            view.SetReach(true, Scale);
            AllOnLayer(root, PortalStencilTable.WorldLayer, "the signs within reach");
            TextIsDepthTested(root, "the signs within reach");
            Assert.That(root.GetComponent<MeshRenderer>().sharedMaterial, Is.EqualTo(surface));
            // The four boards on their slots in the root's frame, which is the anchor's: in its plane.
            float pitch = Scale * ReachSlots.Pitch;
            Bounds near = mesh.bounds;
            Assert.That(near.center.z, Is.EqualTo(0f).Within(1e-6f), "in the anchor's plane");
            Assert.That(near.size.x, Is.EqualTo(2f * (pitch + Scale * SpaceSignLayout.BoardHalfWidth)).Within(1e-5f));
            Assert.That(near.size.y, Is.EqualTo(pitch + 2f * Scale * SpaceSignLayout.BoardHalfHeight).Within(1e-5f));
            Assert.That(near.max.y, Is.EqualTo(Scale * SpaceSignLayout.BoardHalfHeight).Within(1e-5f), "the beacon's board is the top one");
            for (int i = 0; i < SpaceSignLayout.Count; i++)
            {
                Transform text = root.GetChild(i);
                float3 slot = ReachSlots.Local(SpaceSignLayout.SlotOf(i), Scale);
                Assert.That(text.localPosition.x, Is.EqualTo(slot.x).Within(1e-6f), "text " + i);
                Assert.That(text.localPosition.y, Is.EqualTo(slot.y).Within(1e-6f), "text " + i);
                Assert.That(text.localPosition.z, Is.LessThan(slot.z), "a hair in front of its board, towards the walker");
                Assert.That(text.localScale, Is.EqualTo(new Vector3(Scale, Scale, Scale)), "text " + i);
            }

            var at = new float3(0.4f, 1.1f, 0.7f);
            var frame = (Quaternion)quaternion.RotateY(0.3f);
            view.Place(at, (quaternion)frame);
            Assert.That(Vector3.Distance(root.position, (Vector3)at), Is.LessThan(1e-6f));
            Assert.That(Quaternion.Angle(root.rotation, frame), Is.LessThan(0.01f));

            // Far again: every layer, place and material as built.
            view.SetReach(false, 1f);
            AllOnLayer(root, PortalStencilTable.HudLayer, "the signs, back");
            TextIsTheFonts(root, "the signs, back");
            Assert.That(mesh.bounds, Is.EqualTo(far), "the boards a metre ahead again");
            for (int i = 0; i < root.childCount; i++)
            {
                Assert.That(root.GetChild(i).localPosition, Is.EqualTo(textPlaces[i]), "text " + i);
                Assert.That(root.GetChild(i).localScale, Is.EqualTo(Vector3.one), "text " + i);
            }

            view.Dispose();
            Assert.That(_pool.Rented, Is.EqualTo(0));
        }
    }
}
#endif

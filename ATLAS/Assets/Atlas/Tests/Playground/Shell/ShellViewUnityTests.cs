#if !ATLAS_HEADLESS
using Atlas.World;
using NUnit.Framework;
using UnityEngine;

namespace Atlas.Blades.Playground.Tests
{
    /// <summary>The views of P2-C06 and P2-C08 in the Editor: they build under a parent, follow their cores and
    /// clean up after themselves. They need the engine (a font, game objects), so they run in Unity only.</summary>
    [Category("Unity")]
    public class ShellViewUnityTests
    {
        GameObject _hud;

        [SetUp]
        public void SetUp()
        {
            _hud = new GameObject("TestHud");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_hud);
        }

        static string TextOf(Transform root, string child)
        {
            Transform t = root.Find(child);
            Assert.That(t, Is.Not.Null, child);
            return t.Find("Text").GetComponent<TextMesh>().text;
        }

        [Test]
        public void TheChipViewShowsTheRuleAtEveryEntranceAndFadesAway()
        {
            var host = new FakeHost();
            var chip = new CaptionChip();
            chip.Attach(host);
            CaptionChipView view = CaptionChipView.Create(chip, _hud.transform, null);
            try
            {
                int shown = 0;
                foreach (SpaceId id in new[] { SpaceId.Glued, SpaceId.CurvedWalk, SpaceId.AlignedCells, SpaceId.SmallPlanet, SpaceId.Portal, SpaceId.Slicer })
                {
                    host.Enter(id);
                    view.Update(CaptionChip.FadeSeconds);
                    Transform panel = _hud.transform.Find("PlaygroundCaptionChip");
                    if (panel != null && panel.gameObject.activeSelf && TextOf(_hud.transform, "PlaygroundCaptionChip") == CaptionTexts.For(id)) shown++;
                    host.Exit(id);
                    view.Update(CaptionChip.FadeSeconds);
                }
                Assert.That(shown, Is.EqualTo(6), "entrances with the chip drawn");
                Assert.That(_hud.transform.Find("PlaygroundCaptionChip").gameObject.activeSelf, Is.False);
            }
            finally
            {
                view.Dispose();
                chip.Dispose();
            }
        }

        [Test]
        public void TheCardViewDrawsTheCardWhileBoundAndHidesItWhenNot()
        {
            var card = new EquationCard();
            EquationCardView view = EquationCardView.Create(card, _hud.transform, null);
            try
            {
                view.Update(0.1f);
                Assert.That(_hud.transform.Find("PlaygroundEquationCard").gameObject.activeSelf, Is.False);
                card.Bind(new HomeEquationSource(new FakeWalk { Q = 4 }));
                view.Update(0.1f);
                Assert.That(_hud.transform.Find("PlaygroundEquationCard").gameObject.activeSelf, Is.True);
                Assert.That(TextOf(_hud.transform, "PlaygroundEquationCard"), Does.StartWith("4 x 90 deg = 360 deg"));
                card.Unbind();
                view.Update(0.1f);
                Assert.That(_hud.transform.Find("PlaygroundEquationCard").gameObject.activeSelf, Is.False);
            }
            finally
            {
                view.Dispose();
            }
        }
    }
}
#endif

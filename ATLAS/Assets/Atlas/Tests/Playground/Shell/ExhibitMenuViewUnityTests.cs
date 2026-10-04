#if !ATLAS_HEADLESS
using Atlas.World;
using NUnit.Framework;
using UnityEngine;

namespace Atlas.Blades.Playground.Tests
{
    /// <summary>The sign view of P2-C08 in the Editor: it builds under the room's root, follows the menu and
    /// cleans up after itself. They need the engine (a font, game objects), so they run in Unity only.</summary>
    [Category("Unity")]
    public class ExhibitMenuViewUnityTests
    {
        GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("TestRoomRoot");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        static string TextOf(Transform root, string child)
        {
            Transform t = root.Find(child);
            Assert.That(t, Is.Not.Null, child);
            return t.Find("Text").GetComponent<TextMesh>().text;
        }

        [Test]
        public void TheMenuViewShowsOneBoardClosedAndFourSignsOpen()
        {
            var host = new FakeHost();
            var menu = new ExhibitMenu(host, null, null);
            RoomModel room = Rooms.Empty();
            ExhibitMenuView view = ExhibitMenuView.Create(menu, room, _root.transform, null);
            try
            {
                view.Update();
                Assert.That(_root.transform.Find("PlaygroundExhibitBoard").gameObject.activeSelf, Is.True);
                Assert.That(_root.transform.Find("PlaygroundExhibitSign0").gameObject.activeSelf, Is.False);
                menu.Open();
                view.Update();
                Assert.That(_root.transform.Find("PlaygroundExhibitBoard").gameObject.activeSelf, Is.False);
                for (int k = 0; k < ExhibitSignLayout.Count; k++)
                    Assert.That(_root.transform.Find("PlaygroundExhibitSign" + k).gameObject.activeSelf, Is.True, "sign " + k);
                Assert.That(TextOf(_root.transform, "PlaygroundExhibitSign2"), Is.EqualTo("Slicer"));
            }
            finally
            {
                view.Dispose();
                menu.Dispose();
            }
        }
    }
}
#endif

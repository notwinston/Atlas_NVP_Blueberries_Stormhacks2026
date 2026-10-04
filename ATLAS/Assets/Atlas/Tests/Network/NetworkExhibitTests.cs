using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
#if !ATLAS_HEADLESS
using Atlas.Rendering;
using UnityEngine;
#endif

namespace Atlas.Blades.Network.Tests
{
    // NET-08: the network as an exhibit in Home. Headset run deferred: phone.
    public class NetworkExhibitTests
    {
        static RoomModel Room()
        {
            const float w = 4f, d = 5f;
            var room = new RoomModel { Id = "net08" };
            room.FreeRect = new FreeRect(w - 0.6f, d - 0.6f);
            room.Outline = new[]
            {
                new float2(-w / 2, -d / 2), new float2(w / 2, -d / 2), new float2(w / 2, d / 2), new float2(-w / 2, d / 2)
            };
            room.Walls = new RoomWall[4];
            for (int i = 0; i < 4; i++)
                room.Walls[i] = new RoomWall { A = room.Outline[i], B = room.Outline[(i + 1) % 4], Height = 2.4f };
            room.ApplyLoopFit();
            return room;
        }

        [Test]
        public void ReturnHome_RoomUnchanged_WallsWithin5cm()
        {
            RoomModel room = Room();
            var before = new float2[room.Walls.Length * 2];
            for (int i = 0; i < room.Walls.Length; i++) { before[2 * i] = room.Walls[i].A; before[2 * i + 1] = room.Walls[i].B; }
            float2 freeMin = room.FreeRect.Min, freeMax = room.FreeRect.Max;

#if !ATLAS_HEADLESS
            MaterialPool pool = MaterialPool.CreateDefault(8);
            NetworkFloorView view = NetworkFloorView.Create(NetworkTestEnv.Model(7), pool);
            try
            {
                view.Place(room);
                var exhibit = new NetworkExhibit(() => view);
                Assert.That(exhibit.Label, Is.EqualTo("Network"));
                Assert.That(exhibit.Ready, Is.True);
                Assert.That(exhibit.Active, Is.False);
                Assert.That(exhibit.Begin(), Is.True);
                Assert.That(exhibit.Active, Is.True);
                Assert.That(view.Visible, Is.True);
                exhibit.End();
                Assert.That(exhibit.Active, Is.False);
                Assert.That(view.Visible, Is.False);
            }
            finally { Object.DestroyImmediate(view.gameObject); }
#else
            var none = new NetworkExhibit(() => null);
            Assert.That(none.Ready, Is.False);
            Assert.That(none.Begin(), Is.False);
            none.End();
#endif
            for (int i = 0; i < room.Walls.Length; i++)
            {
                Assert.That(math.distance(room.Walls[i].A, before[2 * i]), Is.LessThanOrEqualTo(0.05f), "wall " + i + " A");
                Assert.That(math.distance(room.Walls[i].B, before[2 * i + 1]), Is.LessThanOrEqualTo(0.05f), "wall " + i + " B");
            }
            Assert.That(math.distance(room.FreeRect.Min, freeMin), Is.LessThanOrEqualTo(0.05f));
            Assert.That(math.distance(room.FreeRect.Max, freeMax), Is.LessThanOrEqualTo(0.05f));
        }
    }
}

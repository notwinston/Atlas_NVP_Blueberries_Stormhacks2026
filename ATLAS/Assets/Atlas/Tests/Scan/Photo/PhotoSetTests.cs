using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Photo
{
    /// <summary>
    /// What a photo set carries to the Forge besides its crops (mesh design sections 5.3 and 5.4): the object's
    /// profile, by which the Forge orients the generated mesh, and the room's light target.
    /// </summary>
    public class PhotoSetTests
    {
        const int ChairId = 1;

        // A chair in front of the cameras, further objects that receive meshes where no camera looks, and one
        // object that receives none.
        static RoomModel Room(int meshed, byte[] chairProfile)
        {
            var objects = new RoomObject[meshed + 1];
            objects[0] = new RoomObject
            {
                Id = ChairId, Category = ObjectCategory.Chair, Base = float3.zero, Size = new float3(0.8f, 0.8f, 0.8f), Yaw = 0f,
                TriTarget = 3000, Profile = chairProfile, Facing = chairProfile != null ? ObjectFacingRule.Profile : ObjectFacingRule.Box
            };
            for (int k = 1; k < meshed; k++)
                objects[k] = new RoomObject { Id = 1 + k, Category = ObjectCategory.Storage, Base = new float3(0f, 0f, 40f + k), Size = new float3(0.5f, 0.5f, 0.5f), TriTarget = 3000 };
            objects[meshed] = new RoomObject { Id = 99, Category = ObjectCategory.Plant, Base = new float3(0f, 0f, -40f), Size = new float3(0.3f, 0.3f, 0.3f), TriTarget = 0 };
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            room.Objects = objects;
            return room;
        }

        static ObjectPhotoSet CaptureTheChair(RoomModel room)
        {
            var sink = new PhotoPreferenceTests.Sink();
            PhotoCaptureCore core = PhotoPreferenceTests.Core(sink);
            PhotoBox chair = PhotoScore.ToCaptureFrame(room.Objects[0], float3.zero, 0f);
            PhotoPreferenceTests.AddFrame(core, in chair, 0f, 0f, 2f);
            core.BeginCapture(room, float3.zero, 0f);
            for (int i = 0; i < 200 && core.Busy; i++) core.Tick();
            Assert.That(core.Busy, Is.False);
            Assert.That(sink.Sets.Count, Is.EqualTo(1));
            // Every other object that receives a mesh is reported as unseen; the one that receives none is not mentioned.
            Assert.That(sink.NoPhoto.Count, Is.EqualTo(room.MeshedObjectCount - 1));
            Assert.That(sink.NoPhoto, Has.No.Member(99));
            Assert.That(sink.Sets[0].ObjectId, Is.EqualTo(ChairId));
            return sink.Sets[0];
        }

        [Test]
        public void TheSet_CarriesTheObjectsProfile()
        {
            var profile = new byte[ObjectProfile.Length];
            for (int c = 0; c < profile.Length; c++) profile[c] = c < 48 ? (byte)255 : (byte)115;
            ObjectPhotoSet set = CaptureTheChair(Room(3, profile));
            Assert.That(set.Profile, Is.EqualTo(profile));
            Assert.That(set.Profile.Length, Is.EqualTo(256));
            Assert.That(set.TriTarget, Is.EqualTo(3000));

            // An object the scan gave no profile has none in its set either.
            Assert.That(CaptureTheChair(Room(3, null)).Profile, Is.Null);
        }

        // The light target of a room is 60000 / n in whole numbers, kept between 1500 and 5000, with n the
        // objects that receive meshes: 60000 and 5000 are cut to 5000; 60000 / 13 = 4615; 3000; 1875.
        [TestCase(1, 5000)]
        [TestCase(12, 5000)]
        [TestCase(13, 4615)]
        [TestCase(20, 3000)]
        [TestCase(32, 1875)]
        public void TheSet_CarriesTheRoomsLightTarget(int meshed, int light)
        {
            RoomModel room = Room(meshed, null);
            Assert.That(room.MeshedObjectCount, Is.EqualTo(meshed));
            Assert.That(CaptureTheChair(room).TriTargetLight, Is.EqualTo(light));
        }
    }
}

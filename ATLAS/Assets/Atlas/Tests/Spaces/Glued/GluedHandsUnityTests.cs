#if !ATLAS_HEADLESS
using System;
using System.Text.RegularExpressions;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Spaces.Glued;
using Atlas.Telemetry;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Atlas.Spaces.Tests
{
    /// <summary>
    /// The copies' hands where they need the engine (documents/ATLAS_Hands_Design.md section 5.6): the module as
    /// SpaceLab drives it, with the services AtlasApp publishes, and the one avatar mesh every copy draws. The module
    /// builds its avatar from the kit pieces in Resources when they are there, so what the mesh must hold is worked out
    /// by an engine-free rig given the same piece (decision R15: the kit avatar carries the reduced hands, the
    /// built-in body the full ones).
    /// </summary>
    [Category("Unity")]
    public class GluedHandsUnityTests
    {
        static readonly float3 Head = new float3(0.35f, 1.6f, 0.2f);
        const float Heading = 0.7f;

        /// <summary>A Glued space entered in a 2.4 x 3.4 m room with a material pool and, until a test publishes one, no hands service.</summary>
        sealed class Stage : IDisposable
        {
            public readonly MaterialPool Pool = MaterialPool.CreateDefault(8);
            public readonly GluedSpace Module;
            public FrameContext Frame;
            readonly MaterialPool _oldPool;
            readonly IHands _oldHands;
            IHands _hands;

            public Stage()
            {
                IHands existing;
                AtlasServices.TryGet(out existing);
                _oldHands = existing;
                if (_oldHands != null) AtlasServices.Unregister(_oldHands);
                _oldPool = AtlasServices.Replace(Pool);
                Module = (GluedSpace)GluedSpace.Create();
                Module.Configure(new RoomModel { FreeRect = new FreeRect(2.4f, 3.4f) }, new ParamRegistry());
                Module.Enter(default(WalkerPose));
                Frame = new FrameContext { DeltaTime = 1f / 60f, HeadPosition = Head, HeadRotation = quaternion.RotateY(Heading) };
            }

            public void Publish(IHands hands)
            {
                Withdraw();
                _hands = hands;
                AtlasServices.Register(hands);
            }

            public void Withdraw()
            {
                if (_hands != null) AtlasServices.Unregister(_hands);
                _hands = null;
            }

            /// <summary>One frame of the loop's steps 5 and 6 for the module.</summary>
            public void Run()
            {
                Module.Tick(in Frame);
                Module.Upload(in Frame);
            }

            public void Dispose()
            {
                Withdraw();
                Module.Exit();                                          // safe twice: gives the meshes and the materials back
                UnityEngine.Object.DestroyImmediate(Module.gameObject);
                if (_oldPool != null) AtlasServices.Register(_oldPool); else AtlasServices.Unregister(Pool);
                if (_oldHands != null) AtlasServices.Register(_oldHands);
                Pool.Dispose();
                AtlasLook.ResetForSpace();
            }
        }

        // Both hands tracked, held as GluedHandPoses holds them, for a walker at Head turned by Heading.
        static GluedFakeHands BothHands()
        {
            var hands = new GluedFakeHands { Active = true };
            foreach (HandSide side in new[] { HandSide.Left, HandSide.Right })
            {
                float3[] avatar = GluedHandPoses.InAvatarFrame(side, GluedHandPoses.Wrist(side), 0.3f);
                hands.Track(side, GluedHandPoses.InRoom(avatar, Head, Heading));
            }
            return hands;
        }

        // The mesh the active copies draw, and how many copies draw it. Every one must draw the same mesh.
        static Mesh AvatarMesh(GluedSpace module, out int copies)
        {
            Mesh shared = null;
            copies = 0;
            foreach (MeshFilter filter in module.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.name != "Avatar") continue;
                copies++;
                if (shared == null) shared = filter.sharedMesh;
                Assert.That(filter.sharedMesh, Is.SameAs(shared), "every copy draws the one shared mesh");
            }
            return shared;
        }

        // The avatar piece the module reads from Resources, read the way GluedSpace reads it: null when the build ships
        // none or refuses it, and the module then draws the built-in body.
        static MeshData ShippedAvatar()
        {
            KitSet kit = KitSet.Load(name =>
            {
                var asset = Resources.Load<TextAsset>(KitSet.ResourceFolder + "/" + name);
                if (asset == null) return null;
                byte[] bytes = asset.bytes;
                Resources.UnloadAsset(asset);
                return bytes;
            });
            var mesh = new MeshData(512, 2048);
            return kit.TryGet("avatar", mesh, out string _) ? mesh : null;
        }

        // The rig the module's own is expected to match: the same body, no hand yet.
        static GluedAvatarRig ExpectedRig()
        {
            return new GluedAvatarRig(ShippedAvatar());
        }

        // The mesh holds exactly what the engine-free rig holds: the counts and the bounds of the vertices and indices.
        static void AssertMeshIs(Mesh mesh, GluedAvatarRig expected)
        {
            Assert.That(mesh.vertexCount, Is.EqualTo(expected.Data.VertexCount));
            Assert.That(mesh.GetIndexCount(0), Is.EqualTo((uint)expected.Data.IndexCount));
            Assert.That(mesh.bounds.min.x, Is.EqualTo(expected.Data.BoundsMin.x).Within(1e-4f));
            Assert.That(mesh.bounds.min.y, Is.EqualTo(expected.Data.BoundsMin.y).Within(1e-4f));
            Assert.That(mesh.bounds.min.z, Is.EqualTo(expected.Data.BoundsMin.z).Within(1e-4f));
            Assert.That(mesh.bounds.max.x, Is.EqualTo(expected.Data.BoundsMax.x).Within(1e-4f));
            Assert.That(mesh.bounds.max.y, Is.EqualTo(expected.Data.BoundsMax.y).Within(1e-4f));
            Assert.That(mesh.bounds.max.z, Is.EqualTo(expected.Data.BoundsMax.z).Within(1e-4f));
        }

        [Test]
        public void WithNoHandsService_TheAvatarMeshIsTheBodyAndNothingIsUploaded()
        {
            using (var stage = new Stage())
            {
                GluedAvatarRig body = ExpectedRig();
                for (int i = 0; i < 30; i++) stage.Run();
                Mesh mesh = AvatarMesh(stage.Module, out int copies);
                Assert.That(copies, Is.GreaterThan(0), "the copies are drawn");
                AssertMeshIs(mesh, body);
                Assert.That(stage.Module.AvatarUploads, Is.EqualTo(0), "nothing is uploaded while no hand is tracked");

                // A service whose hands are not tracked changes nothing either.
                stage.Publish(new GluedFakeHands { Active = true });
                for (int i = 0; i < 30; i++) stage.Run();
                AssertMeshIs(mesh, body);
                Assert.That(stage.Module.AvatarUploads, Is.EqualTo(0));
            }
        }

        [Test]
        public void TrackedHands_RideInTheSharedAvatarMesh_AndNoCopyGainsADrawCall()
        {
            using (var stage = new Stage())
            {
                stage.Run();
                GluedAvatarRig expected = ExpectedRig();
                Mesh mesh = AvatarMesh(stage.Module, out int copies);
                int renderers = stage.Module.GetComponentsInChildren<MeshRenderer>().Length;
                Assert.That(copies, Is.GreaterThan(0));
                AssertMeshIs(mesh, expected);

                // Whatever body the build ships, both hands appear: at the level it leaves room for.
                GluedFakeHands hands = BothHands();
                Assert.That(expected.CanShowHands, Is.True, "the body leaves room for the hands at some level");
                Assert.That(expected.Update(hands, Head, Heading), Is.True);
                Assert.That(expected.HandCount, Is.EqualTo(2));
                stage.Publish(hands);
                stage.Run();
                Assert.That(AvatarMesh(stage.Module, out int copiesWithHands), Is.SameAs(mesh), "the same mesh object: nothing was swapped");
                Assert.That(copiesWithHands, Is.EqualTo(copies));
                Assert.That(stage.Module.GetComponentsInChildren<MeshRenderer>().Length, Is.EqualTo(renderers), "no renderer was added");
                AssertMeshIs(mesh, expected);
                Assert.That(mesh.GetIndexCount(0), Is.EqualTo((uint)(3 * GluedBudget.AvatarTriangles(ShippedAvatar()))), "the accounting counts what is drawn");
                Assert.That(mesh.GetIndexCount(0) / 3, Is.LessThanOrEqualTo((uint)AtlasMeshBudget.SelfAvatar));
                Assert.That(mesh.bounds.max.z, Is.GreaterThan(0.4f), "the bounds hold hands held out in front");
                Assert.That(stage.Module.AvatarUploads, Is.EqualTo(1));

                // A hand held still is uploaded once, not every frame.
                for (int i = 0; i < 20; i++) stage.Run();
                Assert.That(stage.Module.AvatarUploads, Is.EqualTo(1));

                // One hand lost: the other stays. Both lost: the body alone goes back, once.
                hands.Lose(HandSide.Right);
                stage.Run();
                expected.Update(hands, Head, Heading);
                Assert.That(expected.HandCount, Is.EqualTo(1));
                AssertMeshIs(mesh, expected);
                hands.Lose(HandSide.Left);
                stage.Run();
                AssertMeshIs(mesh, ExpectedRig());
                int uploads = stage.Module.AvatarUploads;
                for (int i = 0; i < 20; i++) stage.Run();
                Assert.That(stage.Module.AvatarUploads, Is.EqualTo(uploads), "the plain body is not uploaded again");
            }
        }

        [Test]
        public void TheRebuildEveryFrame_AllocatesNothing_AsFarAsTheEngineCanTell()
        {
            using (var stage = new Stage())
            {
                GluedFakeHands hands = BothHands();
                stage.Publish(hands);
                // Once to build the first upload, then measured: every frame a fingertip moves, so every frame rebuilds and uploads.
                for (int i = 0; i < 5; i++)
                {
                    hands.Joints[0][(int)HandJoint.IndexTip] += new float3(0.001f, 0f, 0f);
                    stage.Run();
                }
                FrameContext frame = stage.Frame;
                GluedSpace module = stage.Module;
                int before = module.AvatarUploads;
                Assert.That(() =>
                {
                    for (int i = 0; i < 20; i++)
                    {
                        hands.Joints[0][(int)HandJoint.IndexTip] += new float3(0.001f, 0f, 0f);
                        module.Upload(in frame);
                    }
                }, Is.Not.AllocatingGCMemory());
                Assert.That(module.AvatarUploads - before, Is.EqualTo(20));
            }
        }

        // A hands service that fails: the first frame it answers, then the next time a joint is asked for.
        sealed class FailingHands : IHands
        {
            public readonly GluedFakeHands Inner = BothHands();
            public bool Fail;
            public int JointCalls;

            public bool Active { get { return Inner.Active; } }
            public ref readonly HandPose Pose(HandSide side) { return ref Inner.Pose(side); }
            public ref readonly TwoHandPinch TwoHand { get { return ref Inner.TwoHand; } }
            public void HideThisFrame() { }

            public float3 Joint(HandSide side, HandJoint joint)
            {
                JointCalls++;
                if (Fail) throw new InvalidOperationException("the hands service failed");
                return Inner.Joint(side, joint);
            }
        }

        [Test]
        public void AFaultInTheHandsService_TurnsTheHandsOffForTheVisit_AndKeepsTheSpace()
        {
            using (var stage = new Stage())
            {
                var hands = new FailingHands();
                stage.Publish(hands);
                stage.Run();
                Mesh mesh = AvatarMesh(stage.Module, out _);
                GluedAvatarRig expected = ExpectedRig();
                expected.Update(hands.Inner, Head, Heading);
                Assert.That(expected.HandCount, Is.EqualTo(2));
                AssertMeshIs(mesh, expected);

                // A hands fault must not take the walker out of the room: the space catches it, says so once and shows
                // the plain body for the rest of the visit.
                LogAssert.Expect(LogType.Warning, new Regex("glued\\.hands"));
                hands.Fail = true;
                hands.Inner.Joints[0][(int)HandJoint.IndexTip] += new float3(0.01f, 0f, 0f);       // so that a rebuild is needed
                Assert.DoesNotThrow(() => stage.Run());
                AssertMeshIs(mesh, ExpectedRig());

                int calls = hands.JointCalls;
                for (int i = 0; i < 10; i++) Assert.DoesNotThrow(() => stage.Run());
                Assert.That(hands.JointCalls, Is.EqualTo(calls), "the hands stay off: the service is not asked again");
                AssertMeshIs(mesh, ExpectedRig());
            }
        }

        [Test]
        public void TheModule_HandsBackItsMaterials_AndLeavesNothingBehind_WithHandsToo()
        {
            using (var stage = new Stage())
            {
                stage.Publish(BothHands());
                stage.Run();
                Assert.That(stage.Pool.Rented, Is.EqualTo(2), "one for every cell, one for everything without lines: the hands add none");
                stage.Module.Exit();
                Assert.That(stage.Pool.Rented, Is.EqualTo(0));
                Assert.That(() => stage.Module.Upload(in stage.Frame), Throws.Nothing, "a module that has left ignores the frame");
            }
        }
    }
}
#endif

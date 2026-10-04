#if !ATLAS_HEADLESS
using System;
using System.Text.RegularExpressions;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Spaces.Glued;
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
    /// SpaceLab drives it, with the services AtlasApp publishes, and the one avatar mesh every copy draws.
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

        static void AssertIsTodaysBody(Mesh mesh)
        {
            Assert.That(mesh.vertexCount, Is.EqualTo(241));
            Assert.That(mesh.GetIndexCount(0), Is.EqualTo(546u));
            Assert.That(mesh.bounds.min.x, Is.EqualTo(-0.29f).Within(1e-5f));
            Assert.That(mesh.bounds.min.y, Is.EqualTo(-0.78f).Within(1e-5f));
            Assert.That(mesh.bounds.min.z, Is.EqualTo(-0.185f).Within(1e-5f));
            Assert.That(mesh.bounds.max.x, Is.EqualTo(0.29f).Within(1e-5f));
            Assert.That(mesh.bounds.max.y, Is.EqualTo(0.105f).Within(1e-5f));
            Assert.That(mesh.bounds.max.z, Is.EqualTo(0.055f).Within(1e-5f));
        }

        [Test]
        public void WithNoHandsService_TheAvatarMeshIsTodaysAndNothingIsUploaded()
        {
            using (var stage = new Stage())
            {
                for (int i = 0; i < 30; i++) stage.Run();
                Mesh mesh = AvatarMesh(stage.Module, out int copies);
                Assert.That(copies, Is.GreaterThan(0), "the copies are drawn");
                AssertIsTodaysBody(mesh);
                Assert.That(stage.Module.AvatarUploads, Is.EqualTo(0), "nothing is uploaded while no hand is tracked");

                // A service whose hands are not tracked changes nothing either.
                stage.Publish(new GluedFakeHands { Active = true });
                for (int i = 0; i < 30; i++) stage.Run();
                AssertIsTodaysBody(mesh);
                Assert.That(stage.Module.AvatarUploads, Is.EqualTo(0));
            }
        }

        [Test]
        public void TrackedHands_RideInTheSharedAvatarMesh_AndNoCopyGainsADrawCall()
        {
            using (var stage = new Stage())
            {
                stage.Run();
                Mesh mesh = AvatarMesh(stage.Module, out int copies);
                int renderers = stage.Module.GetComponentsInChildren<MeshRenderer>().Length;
                Assert.That(copies, Is.GreaterThan(0));
                AssertIsTodaysBody(mesh);

                GluedFakeHands hands = BothHands();
                stage.Publish(hands);
                stage.Run();
                Assert.That(AvatarMesh(stage.Module, out int copiesWithHands), Is.SameAs(mesh), "the same mesh object: nothing was swapped");
                Assert.That(copiesWithHands, Is.EqualTo(copies));
                Assert.That(stage.Module.GetComponentsInChildren<MeshRenderer>().Length, Is.EqualTo(renderers), "no renderer was added");
                Assert.That(mesh.vertexCount, Is.EqualTo(241 + 2 * GluedAvatarHands.VerticesPerHand));
                Assert.That(mesh.GetIndexCount(0), Is.EqualTo((uint)(3 * GluedBudget.AvatarTriangles())));
                Assert.That(mesh.GetIndexCount(0) / 3, Is.LessThanOrEqualTo((uint)AtlasMeshBudget.SelfAvatar));
                Assert.That(mesh.bounds.max.z, Is.GreaterThan(0.4f), "the bounds hold hands held out in front");
                Assert.That(stage.Module.AvatarUploads, Is.EqualTo(1));

                // A hand held still is uploaded once, not every frame.
                for (int i = 0; i < 20; i++) stage.Run();
                Assert.That(stage.Module.AvatarUploads, Is.EqualTo(1));

                // One hand lost: the other stays. Both lost: the body alone goes back, once.
                hands.Lose(HandSide.Right);
                stage.Run();
                Assert.That(mesh.vertexCount, Is.EqualTo(241 + GluedAvatarHands.VerticesPerHand));
                hands.Lose(HandSide.Left);
                stage.Run();
                AssertIsTodaysBody(mesh);
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
                Assert.That(mesh.vertexCount, Is.EqualTo(241 + 2 * GluedAvatarHands.VerticesPerHand));

                // A hands fault must not take the walker out of the room: the space catches it, says so once and shows
                // the plain body for the rest of the visit.
                LogAssert.Expect(LogType.Warning, new Regex("glued\\.hands"));
                hands.Fail = true;
                hands.Inner.Joints[0][(int)HandJoint.IndexTip] += new float3(0.01f, 0f, 0f);       // so that a rebuild is needed
                Assert.DoesNotThrow(() => stage.Run());
                AssertIsTodaysBody(mesh);

                int calls = hands.JointCalls;
                for (int i = 0; i < 10; i++) Assert.DoesNotThrow(() => stage.Run());
                Assert.That(hands.JointCalls, Is.EqualTo(calls), "the hands stay off: the service is not asked again");
                AssertIsTodaysBody(mesh);
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

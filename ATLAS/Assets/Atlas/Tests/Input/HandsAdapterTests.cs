#if !ATLAS_HEADLESS
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Input.Tests
{
    /// <summary>Hands at the service itself, and the reticle as an object in the scene: both need the engine.</summary>
    [Category("Unity")]
    public class HandsAdapterTests
    {
        private static readonly Vector3 Head = new Vector3(0f, 1.6f, 0f);
        private const float Dt = 1f / 60f;

        [Test]
        public void Input_Hands_NoHandsNoChange_TheServiceForgetsHandsThatAreNotSetAgain()
        {
            IAtlasInput input = AtlasInput.Install(null, null);
            try
            {
                var hands = input as IHandsInput;
                Assert.That(hands, Is.Not.Null, "the composition root reaches hands with \"input as IHandsInput\"");
                input.PushContext(InputContext.Space);

                GazeTarget row = GazeTarget.WorldRect(AtlasAction.Primary, new Vector3(0f, 1.5f, 0.45f), Quaternion.identity,
                                                      new Vector2(0.25f, 0.015f), 4);
                row.Label = "row";
                GazeTargetId id = input.RegisterGazeTarget(in row);
                int fired = 0;
                input.ActionFired += (action, source, tag) => { if (action == AtlasAction.Primary && source == InputSource.Gaze && tag == 4) fired++; };
                Quaternion up = Quaternion.Euler(-60f, 0f, 0f);

                // A fingertip 4 cm in front of the row, set before the tick: it hovers the row.
                var hovering = new HandsInput
                {
                    Active = true,
                    Right = new HandsFingertip { Valid = true, World = new float3(0f, 1.5f, 0.41f), Confidence = 1f }
                };
                hands.SetHands(in hovering);
                input.Tick(Head, up, Dt);
                Assert.That(hands.HandsTouch.Right.Target, Is.EqualTo(id));
                Assert.That(hands.HandsTouch.Right.Hover, Is.EqualTo(0.5f).Within(1e-3f));
                Assert.That(hands.HandsTouch.AnyNear, Is.True);
                Assert.That(hands.LabelOf(id), Is.EqualTo("row"));
                Assert.That(hands.TagOf(id), Is.EqualTo(4));

                // No SetHands this frame: the fingertip is not replayed, and hands do nothing.
                input.Tick(Head, up, Dt);
                Assert.That(hands.HandsTouch.Right.Target.IsValid, Is.False);
                Assert.That(hands.HandsTouch.AnyNear, Is.False);

                // Set again each frame, the fingertip goes through the row and fires it as the gaze would.
                for (int i = 0; i <= 12; i++)
                {
                    var pressing = hovering;
                    pressing.Right.World = new float3(0f, 1.5f, 0.41f + 0.06f * i / 12f);
                    hands.SetHands(in pressing);
                    input.Tick(Head, up, Dt);
                }
                Assert.That(fired, Is.EqualTo(1));
                Assert.That(hands.HandsTouch.Right.Pressed, Is.True);
                input.Tick(Head, up, Dt);
                Assert.That(hands.HandsTouch.Right.Pressed, Is.False, "a frame with no SetHands lets go");
                Assert.That(hands.LabelOf(default), Is.Null);
            }
            finally
            {
                ((System.IDisposable)input).Dispose();
            }
        }

        [Test]
        public void Input_Reticle_DistanceRule_TheReticleMovesAndKeepsItsSize()
        {
            var hud = new GameObject("Hud (test)");
            var reticle = new GazeReticle(hud.transform, null);
            try
            {
                Transform t = hud.transform.GetChild(0);
                reticle.Set(true, 0f);
                Assert.That(t.localPosition.z, Is.EqualTo(GazeReticle.DistanceMetres).Within(1e-5f), "far unless told");
                Assert.That(t.localScale.x, Is.EqualTo(1f).Within(1e-5f));

                reticle.Set(true, 0.5f, 0.45f);
                Assert.That(t.localPosition, Is.EqualTo(new Vector3(0f, 0f, 0.45f)));
                Assert.That(t.localScale.x, Is.EqualTo(0.225f).Within(1e-5f), "the same size in the view");
                Assert.That(t.localScale.y, Is.EqualTo(0.225f).Within(1e-5f));

                reticle.Set(true, 0.5f, GazeReticle.DistanceMetres);
                Assert.That(t.localPosition.z, Is.EqualTo(GazeReticle.DistanceMetres).Within(1e-5f));
                Assert.That(t.localScale.x, Is.EqualTo(1f).Within(1e-5f));
            }
            finally
            {
                reticle.Dispose();
                Object.DestroyImmediate(hud);
            }
        }
    }
}
#endif

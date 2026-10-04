using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Rendering.Tests.Portals
{
    /// <summary>The engine-free cores of the stencil portal engine (Master Plan task P1-A10).</summary>
    public class PortalEngineTests
    {
        // ---- the stencil allocation table -------------------------------------------------------------------

        [Test]
        public void StencilTable_OneRowPerReference_AsTheContractLaysItOut()
        {
            Assert.That(PortalStencilTable.MaxSlots, Is.EqualTo(15));
            Assert.That(PortalRenderer.MaxSlots, Is.EqualTo(15));
            Assert.That(PortalStencilTable.MaskLayer, Is.EqualTo(6));
            Assert.That(PortalStencilTable.HudLayer, Is.EqualTo(7));
            Assert.That(PortalStencilTable.MaskEvent, Is.EqualTo(300));
            for (int reference = 1; reference <= 15; reference++)
            {
                Assert.That(PortalStencilTable.IsReference(reference), Is.True);
                Assert.That(PortalStencilTable.Layer(reference), Is.EqualTo(7 + reference));
                Assert.That(PortalStencilTable.PassEvent(reference), Is.EqualTo(300 + reference));
                Assert.That(PortalStencilTable.ReferenceOfLayer(PortalStencilTable.Layer(reference)), Is.EqualTo(reference));
                Assert.That(PortalStencilTable.LayerName(reference), Is.EqualTo("AtlasRoom" + reference.ToString("00")));
                Assert.That(PortalStencilTable.FeatureName(reference), Is.EqualTo("Atlas Room " + reference.ToString("00")));
            }
            Assert.That(PortalStencilTable.LayerName(1), Is.EqualTo("AtlasRoom01"));
            Assert.That(PortalStencilTable.FeatureName(15), Is.EqualTo("Atlas Room 15"));
            Assert.That(PortalStencilTable.Layer(15), Is.EqualTo(22));
        }

        [Test]
        public void StencilTable_RejectsEverythingOutsideOneToFifteen()
        {
            Assert.That(PortalStencilTable.IsReference(0), Is.False);
            Assert.That(PortalStencilTable.IsReference(16), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => PortalStencilTable.Layer(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => PortalStencilTable.Layer(16));
            Assert.Throws<ArgumentOutOfRangeException>(() => PortalStencilTable.FeatureName(-1));
            Assert.That(PortalStencilTable.ReferenceOfLayer(0), Is.EqualTo(0), "Default is the walker's room");
            Assert.That(PortalStencilTable.ReferenceOfLayer(6), Is.EqualTo(0));
            Assert.That(PortalStencilTable.ReferenceOfLayer(7), Is.EqualTo(0));
            Assert.That(PortalStencilTable.ReferenceOfLayer(23), Is.EqualTo(0), "the Spectator's layer");
        }

        [Test]
        public void StencilTable_FeatureLayersAreTheMaskTheHudAndTheFifteenRooms()
        {
            int mask = PortalStencilTable.FeatureLayerMask;
            for (int layer = 0; layer < 32; layer++)
            {
                bool expected = layer >= 6 && layer <= 22;
                Assert.That((mask & (1 << layer)) != 0, Is.EqualTo(expected), "layer " + layer);
            }
            Assert.That(PortalStencilTable.MarkQueue, Is.LessThan(PortalStencilTable.ResetQueue), "every mark before any reset");
            Assert.That(PortalStencilTable.ResetQueue, Is.LessThanOrEqualTo(2500), "both in the opaque range");
        }

        [Test]
        public void SlotAllocator_GivesReferencesInOrder_AndNeverMoreThanFifteen()
        {
            var slots = new PortalSlotAllocator();
            slots.Begin();
            Assert.That(slots.Count, Is.EqualTo(0));
            // Called largest doorway first, so the largest gets reference 1.
            Assert.That(slots.Assign(7), Is.EqualTo(1));
            Assert.That(slots.Assign(3), Is.EqualTo(2));
            Assert.That(slots.Assign(7), Is.EqualTo(1), "two doorways into one room share its reference");
            Assert.That(slots.Count, Is.EqualTo(2));
            Assert.That(slots.ReferenceOf(3), Is.EqualTo(2));
            Assert.That(slots.ReferenceOf(9), Is.EqualTo(0));
            Assert.That(slots.RoomOf(1), Is.EqualTo(7));
            Assert.That(slots.RoomOf(3), Is.EqualTo(-1));

            slots.Begin();
            for (int room = 0; room < 40; room++)
            {
                int reference = slots.Assign(100 + room);
                if (room < 15) Assert.That(reference, Is.EqualTo(room + 1));
                else Assert.That(reference, Is.EqualTo(0), "the sixteenth room gets none");
            }
            Assert.That(slots.Count, Is.EqualTo(15));
            Assert.That(slots.Assign(100), Is.EqualTo(1), "a room that has one keeps it when the table is full");

            slots.Begin();
            Assert.That(slots.Count, Is.EqualTo(0), "a new frame starts empty");
            Assert.That(slots.ReferenceOf(100), Is.EqualTo(0));
        }

        // ---- the swap rule ------------------------------------------------------------------------------------

        [Test]
        public void SwapRule_NoSwapWhileEitherEyeSeesTheOldRoom()
        {
            var rule = new PortalSwapRule(4);
            for (int frame = 0; frame < 20; frame++)
                Assert.That(rule.Step(1, true, false, false), Is.False, "left eye, frame " + frame);
            for (int frame = 0; frame < 20; frame++)
                Assert.That(rule.Step(1, false, true, false), Is.False, "right eye, frame " + frame);
            for (int frame = 0; frame < 20; frame++)
                Assert.That(rule.Step(1, true, true, false), Is.False, "both eyes, frame " + frame);
            Assert.That(rule.HeldFrames(1), Is.EqualTo(0));
        }

        [Test]
        public void SwapRule_NoSwapWhileTheHeadIsInTheOverlap()
        {
            var rule = new PortalSwapRule(4);
            for (int frame = 0; frame < 20; frame++)
                Assert.That(rule.Step(2, false, false, true), Is.False, "frame " + frame);
            Assert.That(rule.HeldFrames(2), Is.EqualTo(0));
        }

        [Test]
        public void SwapRule_SwapsAfterThreeConsecutiveFrames()
        {
            Assert.That(PortalSwapRule.HoldFrames, Is.EqualTo(3));
            var rule = new PortalSwapRule(4);
            Assert.That(rule.Step(1, false, false, false), Is.False, "frame 1");
            Assert.That(rule.Step(1, false, false, false), Is.False, "frame 2");
            Assert.That(rule.Step(1, false, false, false), Is.True, "frame 3");
            Assert.That(rule.Step(1, false, false, false), Is.True, "and it stays allowed");
        }

        [Test]
        public void SwapRule_AnInterruptionStartsTheCountAgain()
        {
            var rule = new PortalSwapRule(4);
            rule.Step(1, false, false, false);
            rule.Step(1, false, false, false);
            Assert.That(rule.Step(1, false, true, false), Is.False, "the right eye sees it again");
            Assert.That(rule.Step(1, false, false, false), Is.False);
            Assert.That(rule.Step(1, false, false, false), Is.False);
            Assert.That(rule.Step(1, false, false, true), Is.False, "the head walked into the overlap");
            Assert.That(rule.Step(1, false, false, false), Is.False);
            Assert.That(rule.Step(1, false, false, false), Is.False);
            Assert.That(rule.Step(1, false, false, false), Is.True);

            rule.Reset(1);
            Assert.That(rule.Step(1, false, false, false), Is.False, "a reset room counts from zero");
        }

        [Test]
        public void SwapRule_RoomsAreCountedApart_AndTheTableGrows()
        {
            var rule = new PortalSwapRule(2);
            rule.EnsureCapacity(9);
            rule.Step(8, false, false, false);
            rule.Step(8, false, false, false);
            Assert.That(rule.Step(3, false, false, false), Is.False, "room 3 has its own count");
            Assert.That(rule.Step(8, false, false, false), Is.True);
            Assert.That(rule.Step(99, false, false, false), Is.False, "an unknown room is never switched off");
            rule.Reset();
            Assert.That(rule.HeldFrames(8), Is.EqualTo(0));
        }

        // ---- geometry -----------------------------------------------------------------------------------------

        // A frustum as GeometryUtility gives it: left, right, down, up, near, far, normals inwards.
        static float4[] Frustum(float3 eye, float halfAngleTan, float near, float far)
        {
            // Looking along +Z.
            float3 l = math.normalize(new float3(1f, 0f, halfAngleTan));
            float3 r = math.normalize(new float3(-1f, 0f, halfAngleTan));
            float3 d = math.normalize(new float3(0f, 1f, halfAngleTan));
            float3 u = math.normalize(new float3(0f, -1f, halfAngleTan));
            var f = new float3(0f, 0f, 1f);
            return new[]
            {
                new float4(l, -math.dot(l, eye)), new float4(r, -math.dot(r, eye)),
                new float4(d, -math.dot(d, eye)), new float4(u, -math.dot(u, eye)),
                new float4(f, -math.dot(f, eye + f * near)), new float4(-f, math.dot(f, eye + f * far)),
            };
        }

        [Test]
        public void DoorPlane_FacesTheWalkerWhoSeesAOnTheLeft()
        {
            // Looking along +Z, the left is -X.
            float4 plane = PortalMath.DoorPlane(new float2(-0.45f, 2f), new float2(0.45f, 2f));
            Assert.That(plane.xyz.z, Is.EqualTo(-1f).Within(1e-6f));
            Assert.That(PortalMath.Side(plane, new float3(0f, 1.6f, 0f)), Is.EqualTo(2f).Within(1e-5f));
            Assert.That(PortalMath.Side(plane, new float3(0f, 1.6f, 3f)), Is.EqualTo(-1f).Within(1e-5f));
            Assert.That(PortalMath.Side(PortalMath.Flip(plane), new float3(0f, 1.6f, 3f)), Is.EqualTo(1f).Within(1e-5f));

            // Looking along +X, the left is +Z.
            plane = PortalMath.DoorPlane(new float2(1f, 0.5f), new float2(1f, -0.5f));
            Assert.That(PortalMath.Side(plane, new float3(0f, 0f, 0f)), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Crossing_NeedsTheHeadToGoThroughTheDoorway_FromTheFront()
        {
            var a = new float2(-0.45f, 2f);
            var b = new float2(0.45f, 2f);
            float4 plane = PortalMath.DoorPlane(a, b);
            var before = new float3(0.1f, 1.6f, 1.95f);
            var after = new float3(0.1f, 1.6f, 2.03f);
            Assert.That(PortalMath.Crossed(plane, a, b, 2.1f, before, after), Is.True);
            Assert.That(PortalMath.Crossed(plane, a, b, 2.1f, after, before), Is.False, "from behind is no crossing");
            Assert.That(PortalMath.Crossed(plane, a, b, 2.1f, before, before), Is.False, "standing in front");
            Assert.That(PortalMath.Crossed(plane, a, b, 2.1f, after, after), Is.False, "standing behind");

            var asideBefore = new float3(1.2f, 1.6f, 1.95f);
            var asideAfter = new float3(1.2f, 1.6f, 2.03f);
            Assert.That(PortalMath.Crossed(plane, a, b, 2.1f, asideBefore, asideAfter), Is.False, "past the jamb");
            var highBefore = new float3(0f, 2.6f, 1.95f);
            var highAfter = new float3(0f, 2.6f, 2.03f);
            Assert.That(PortalMath.Crossed(plane, a, b, 2.1f, highBefore, highAfter), Is.False, "above the lintel");
        }

        [Test]
        public void InsideDoor_IsTheRectangleWidenedByTheMargin()
        {
            var a = new float2(0f, 0f);
            var b = new float2(0.9f, 0f);
            Assert.That(PortalMath.InsideDoor(a, b, 2.1f, new float3(0.45f, 1.6f, 0.05f), 0f), Is.True);
            Assert.That(PortalMath.InsideDoor(a, b, 2.1f, new float3(0.98f, 1.6f, 0f), 0f), Is.False);
            Assert.That(PortalMath.InsideDoor(a, b, 2.1f, new float3(0.98f, 1.6f, 0f), 0.1f), Is.True);
            Assert.That(PortalMath.InsideDoor(a, b, 2.1f, new float3(0.45f, 2.3f, 0f), 0.1f), Is.False);
        }

        [Test]
        public void BoundsInFrustum_MatchesThePlaneTest()
        {
            float4[] planes = Frustum(new float3(0f, 1.6f, 0f), 1f, 0.05f, 60f);
            Assert.That(PortalMath.BoundsInFrustum(planes, new float3(-1f, 0f, 2f), new float3(1f, 2.4f, 4f)), Is.True, "ahead");
            Assert.That(PortalMath.BoundsInFrustum(planes, new float3(-1f, 0f, -4f), new float3(1f, 2.4f, -2f)), Is.False, "behind");
            Assert.That(PortalMath.BoundsInFrustum(planes, new float3(20f, 0f, 2f), new float3(22f, 2.4f, 4f)), Is.False, "far to the side");
            Assert.That(PortalMath.BoundsInFrustum(planes, new float3(-1f, 0f, 70f), new float3(1f, 2.4f, 72f)), Is.False, "past the far plane");
            Assert.That(PortalMath.BoundsInFrustum(planes, new float3(-5f, 0f, -5f), new float3(5f, 3f, 5f)), Is.True, "the eye is inside");
        }

        [Test]
        public void InOverlap_IsTheVolumeBothRoomsShare()
        {
            var minA = new float3(-1f, 0f, -1f);
            var maxA = new float3(1f, 2.4f, 1f);
            var minB = new float3(0f, 0f, 0f);
            var maxB = new float3(3f, 2.4f, 3f);
            Assert.That(PortalMath.InOverlap(new float3(0.5f, 1.6f, 0.5f), minA, maxA, minB, maxB), Is.True);
            Assert.That(PortalMath.InOverlap(new float3(-0.5f, 1.6f, 0.5f), minA, maxA, minB, maxB), Is.False, "in A only");
            Assert.That(PortalMath.InOverlap(new float3(2f, 1.6f, 2f), minA, maxA, minB, maxB), Is.False, "in B only");
        }

        [Test]
        public void DoorViewArea_IsTheDoorwayOnThePlaneOneMetreAhead()
        {
            var eye = new float3(0f, 1f, 0f);
            float4[] planes = Frustum(eye, 2f, 0.05f, 60f);
            // 0.9 m by 2.0 m, 3 m ahead, centred: its picture one metre ahead is a third of the size.
            var a = new float2(-0.45f, 3f);
            var b = new float2(0.45f, 3f);
            float area = PortalMath.DoorViewArea(a, b, 2f, eye, planes);
            Assert.That(area, Is.EqualTo(0.9f * 2f / 9f).Within(1e-4f));

            // Twice as far, a quarter of the view: the nearer doorway takes the lower reference.
            float farArea = PortalMath.DoorViewArea(new float2(-0.45f, 6f), new float2(0.45f, 6f), 2f, eye, planes);
            Assert.That(farArea, Is.EqualTo(area / 4f).Within(1e-4f));

            Assert.That(PortalMath.DoorViewArea(new float2(-0.45f, -3f), new float2(0.45f, -3f), 2f, eye, planes), Is.EqualTo(0f), "behind the eye");
            Assert.That(PortalMath.DoorViewArea(new float2(30f, 3f), new float2(31f, 3f), 2f, eye, planes), Is.EqualTo(0f), "out of view");

            // Half in view: the frustum's left plane cuts it.
            float4[] narrow = Frustum(eye, 0.5f, 0.05f, 60f);
            float whole = PortalMath.DoorViewArea(new float2(-0.45f, 3f), new float2(0.45f, 3f), 0.9f, new float3(0f, 0.45f, 0f), Frustum(new float3(0f, 0.45f, 0f), 0.5f, 0.05f, 60f));
            float cut = PortalMath.DoorViewArea(new float2(-2f, 3f), new float2(0f, 3f), 2f, eye, narrow);
            Assert.That(whole, Is.EqualTo(0.9f * 0.9f / 9f).Within(1e-4f));
            Assert.That(cut, Is.GreaterThan(0f).And.LessThan(2f * 2f / 9f));
        }

        [Test]
        public void PortalBox_ConstantsHoldTheContract()
        {
            Assert.That(PortalMath.BoxDistance, Is.EqualTo(0.10f), "an eye within 0.10 m of the plane gets the box");
            Assert.That(PortalMath.BoxDepth, Is.GreaterThan(0.25f), "deeper than the corner of a 0.05 m near plane at any field of view in use");
            Assert.That(PortalMath.FullViewArea, Is.GreaterThan(1000f), "the straddled doorway is always the largest");
        }

        // ---- the mask shader ----------------------------------------------------------------------------------

        static string MaskShader
        {
            get
            {
                string project = Environment.GetEnvironmentVariable("ATLAS_PROJECT_DIR");
                if (string.IsNullOrEmpty(project)) project = Directory.GetCurrentDirectory();
                string path = Path.Combine(project, "Assets", "Atlas", "Rendering", "Portals", "Resources",
                                           PortalRenderer.MaskShaderResource + ".shader");
                return Regex.Replace(File.ReadAllText(path), @"//[^\n]*", "");
            }
        }

        [Test]
        public void PortalMaskShader_IsUnderResources_WritesNoColour_AndHasOneVariant()
        {
            string source = MaskShader;
            Assert.That(source, Does.Contain("Shader \"Hidden/Atlas/PortalMask\""));
            Assert.That(source, Does.Contain("ColorMask 0"));
            Assert.That(source, Does.Contain("\"LightMode\" = \"UniversalForward\""), "a tag the Render Objects pass draws");
            Assert.That(source, Does.Contain("Ref [_StencilRef]"));
            Assert.That(source, Does.Contain("Comp [_StencilComp]"));
            Assert.That(source, Does.Contain("Pass [_StencilPass]"));
            Assert.That(source, Does.Contain("UNITY_RAW_FAR_CLIP_VALUE"), "the reset lands on the far plane");
            Assert.That(source, Does.Contain("CBUFFER_START(UnityPerMaterial)"));
            Assert.That(source, Does.Not.Contain("multi_compile"));
            Assert.That(source, Does.Not.Contain("shader_feature"));
            Assert.That(source, Does.Not.Contain("dynamic_branch"));
            Assert.That(Regex.Matches(source, @"\bPass\s*\{").Count, Is.EqualTo(1), "one pass: no extra draw per doorway");
        }
    }
}

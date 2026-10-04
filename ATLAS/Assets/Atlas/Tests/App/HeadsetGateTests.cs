using System.Collections.Generic;
using Atlas.Rendering;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.App.Tests
{
    /// <summary>
    /// The headset refusal (lane A contract, section 7.2, rule 2): a device build with no Bump Guard shows
    /// "Bump Guard missing" and never reaches the headset view, whoever asks.
    /// </summary>
    public class HeadsetGateTests
    {
        sealed class FakeRig : IStereoRig
        {
            public int SetModeCalls;
            public DisplayMode Mode { get; set; }
            public void SetMode(DisplayMode mode) { SetModeCalls++; Mode = mode; }
            public float Ipd { get; set; }
            public float3 HeadOffset { get; set; }
            public quaternion PredictionOffset { get; set; }
            public Transform Head { get { return null; } }
            public Transform Hud { get { return null; } }
            public Camera GetEyeCamera(Eye eye) { return null; }
            public float3 GetEyePosition(Eye eye) { return eye == Eye.Left ? new float3(-1f, 0f, 0f) : new float3(1f, 0f, 0f); }
            public void GetEyeFrustumPlanes(Eye eye, Plane[] planes) { }
        }

        [Test]
        public void Unlocked_PassesEverythingThrough()
        {
            var inner = new FakeRig();
            var rig = new GuardedStereoRig(inner);
            rig.SetMode(DisplayMode.Headset);
            Assert.That(inner.Mode, Is.EqualTo(DisplayMode.Headset));
            Assert.That(rig.Mode, Is.EqualTo(DisplayMode.Headset));
            rig.Ipd = 0.06f;
            Assert.That(inner.Ipd, Is.EqualTo(0.06f));
            Assert.That(rig.GetEyePosition(Eye.Right).x, Is.EqualTo(1f));
        }

        [Test]
        public void Locked_RefusesTheHeadset_ReportsIt_AndStillAllowsTheHand()
        {
            var inner = new FakeRig();
            var refused = new List<string>();
            var rig = new GuardedStereoRig(inner) { Refused = refused.Add, HeadsetLocked = true };

            rig.SetMode(DisplayMode.Headset);
            Assert.That(inner.SetModeCalls, Is.EqualTo(0));
            Assert.That(rig.Mode, Is.EqualTo(DisplayMode.Hand));
            Assert.That(refused, Is.EqualTo(new[] { "Bump Guard missing" }));

            rig.SetMode(DisplayMode.Hand);
            Assert.That(inner.SetModeCalls, Is.EqualTo(1));
            Assert.That(refused.Count, Is.EqualTo(1));
        }

        [Test]
        public void Locking_BringsAHeadsetRigBackToTheHand()
        {
            var inner = new FakeRig { Mode = DisplayMode.Headset };
            var rig = new GuardedStereoRig(inner);
            rig.HeadsetLocked = true;
            Assert.That(inner.Mode, Is.EqualTo(DisplayMode.Hand));
        }

        [Test]
        public void Warning_IsOwedOnlyOnADeviceWithNoBumpGuard()
        {
            Assert.That(HeadsetGate.MustWarn(true, false), Is.True);
            Assert.That(HeadsetGate.MustWarn(true, true), Is.False);
            Assert.That(HeadsetGate.MustWarn(false, false), Is.False);
        }

        [Test]
        public void Overlay_IsDrawnForTheWarning_WhateverDevOverlayIs()
        {
            // A release build on a phone: dev.overlay is off, the warning still shows.
            Assert.That(HeadsetGate.OverlayDrawn(false, true, false, false), Is.True);
            Assert.That(HeadsetGate.OverlayDrawn(false, true, false, true), Is.False);
            // The usual rule: shown when asked for, never over the lens view on a phone.
            Assert.That(HeadsetGate.OverlayDrawn(true, true, false, true), Is.True);
            Assert.That(HeadsetGate.OverlayDrawn(true, true, true, true), Is.False);
            Assert.That(HeadsetGate.OverlayDrawn(true, false, true, false), Is.True);
            Assert.That(HeadsetGate.OverlayDrawn(false, false, false, false), Is.False);
        }
    }
}

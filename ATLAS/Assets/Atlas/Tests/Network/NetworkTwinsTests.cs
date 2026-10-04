using System;
using System.Collections.Generic;
using Atlas.Blades.Network.Core;
using Atlas.Input;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Telemetry;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Blades.Network.Tests
{
    // NET-07: every action by gamepad alone and by gaze alone, on the real input core. Device checks: phone.
    public class NetworkTwinsTests
    {
        const float Dt = 1f / 60f;
        // A visitor standing at the disc centre: the chips must be dwelt on from here.
        static readonly float3 Head = new float3(0f, 1.6f, 0f);

        /// <summary>The engine-free input core behind the service's interface: no devices, gaze and contexts only.</summary>
        sealed class CoreInput : IAtlasInput
        {
            public readonly AtlasInputCore Core = new AtlasInputCore();
            public RawInput Raw;
            public float3 HeadPosition = Head;
            public quaternion HeadRotation = quaternion.identity;
            public void Tick(Vector3 headPosition, Quaternion headRotation, float deltaTime)
            {
                Core.Tick(Raw, HeadPosition, HeadRotation, deltaTime);
            }
            public InputContext Context { get { return Core.Context; } }
            public void PushContext(InputContext context) { Core.PushContext(context); }
            public void PopContext(InputContext context) { Core.PopContext(context); }
            public bool Pressed(AtlasAction action) { return Core.Pressed(action); }
            public bool Held(AtlasAction action) { return Core.Held(action); }
            public float Axis(AtlasAxis axis) { return Core.Axis(axis); }
            public InputSource LastSource(AtlasAction action) { return Core.LastSource(action); }
            public int LastTag(AtlasAction action) { return Core.LastTag(action); }
            public event Action<AtlasAction, InputSource, int> ActionFired
            {
                add { Core.ActionFired += value; }
                remove { Core.ActionFired -= value; }
            }
            public float DwellSeconds { get { return Core.DwellSeconds; } set { Core.DwellSeconds = value; } }
            public GazeTargetId RegisterGazeTarget(in GazeTarget target)
            {
                GazeTargetData data = target.ToData();
                return Core.Targets.Add(in data);
            }
            public void MoveGazeTarget(GazeTargetId id, Vector3 centre) { Core.Targets.Move(id, new float3(centre.x, centre.y, centre.z)); }
            public void SetGazeTargetEnabled(GazeTargetId id, bool enabled) { Core.Targets.SetEnabled(id, enabled); }
            public void UnregisterGazeTarget(GazeTargetId id) { Core.Targets.Remove(id); }
            public GazeState Gaze { get { return new GazeState { Focused = Core.GazeFocused, Progress = Core.GazeProgress }; } }
            public void CopyActionsWithoutGazeTwin(List<AtlasAction> into) { Core.CopyActionsWithoutGazeTwin(into); }
            public bool TryGetTap(out Vector2 screenPosition) { screenPosition = default(Vector2); return false; }
            public bool GamepadPresent { get { return false; } }
            public void SetRumble(float lowFrequency, float highFrequency) { }
            public WalkerInput Walker { get { return default(WalkerInput); } }
        }

        sealed class Rig
        {
            public readonly CoreInput Input = new CoreInput();
            public readonly NetworkModel Model;
            public readonly NetworkFocus Focus;
            public readonly NetworkFloorMesher Mesher;
            public readonly NetworkTwins Twins;

            public Rig(int maxGazeNodes = 64)
            {
                Model = NetworkTestEnv.Model(7);
                Mesher = new NetworkFloorMesher(Model);
                Focus = new NetworkFocus(Model);
                Input.PushContext(InputContext.Space);
                Twins = new NetworkTwins(Input, Model, Focus, Mesher) { MaxGazeNodes = maxGazeNodes };
                Frame(1);
            }

            public void Frame(int frames)
            {
                for (int i = 0; i < frames; i++)
                {
                    Input.Tick(Vector3.zero, Quaternion.identity, Dt);
                    Twins.Update(Dt);
                    Mesher.Step(Focus, Dt);
                }
            }

            public void Pad(AtlasAction action)
            {
                Input.Raw.PadDown = InputTwins.Bit(action);
                Frame(1);
                Input.Raw.PadDown = 0;
                Frame(1);
            }

            public void Stick(float x, float y)
            {
                Input.Raw.LeftStick = new float2(x, y);
                Frame(1);
                Input.Raw.LeftStick = float2.zero;
                Frame(1);
            }

            public void LookAt(float3 target)
            {
                Input.HeadRotation = quaternion.LookRotation(math.normalize(target - Input.HeadPosition), new float3(0f, 1f, 0f));
            }

            public void Dwell(float3 target)
            {
                LookAt(target);
                Frame(120);                                 // 2 s at 60 Hz, past the 0.7 s dwell
                Input.HeadRotation = quaternion.LookRotation(new float3(0f, 0f, -1f), new float3(0f, 1f, 0f));
                Frame(2);
            }

            public float3 TargetCentre(int slot)
            {
                GazeRegistry reg = Input.Core.Targets;
                int seen = 0;
                for (int s = 0; s < reg.SlotLimit; s++)
                {
                    if (!reg.TryGetSlot(s, out GazeTargetId id) || !reg.TryGet(id, out GazeTargetData d)) continue;
                    if (d.Action != AtlasAction.Confirm) continue;
                    if (d.Tag == slot) return d.Centre;
                    seen++;
                }
                Assert.Fail("no node target in slot " + slot + " (" + seen + " found)");
                return default(float3);
            }
        }

        // Event capture for the net scene.
        sealed class Capture : IDisposable
        {
            public readonly List<EventMessage> Events = new List<EventMessage>();
            readonly EventLog m_Log;
            readonly IEventLog m_Previous;
            public Capture()
            {
                m_Log = new EventLog(new ManualClock(), null);
                m_Previous = AtlasServices.Replace<IEventLog>(m_Log);
                m_Log.Logged += Events.Add;
            }
            public void Dispose()
            {
                m_Log.Logged -= Events.Add;
                if (m_Previous != null) AtlasServices.Replace(m_Previous); else AtlasServices.Unregister<IEventLog>(m_Log);
            }
        }

        static float3 Chip(GazeTargetId id, Rig rig)
        {
            Assert.IsTrue(rig.Input.Core.Targets.TryGet(id, out GazeTargetData d));
            return d.Centre;
        }

        // ---- gamepad alone ----

        [Test]
        public void Recentre_ByGamepadAlone()
        {
            var rig = new Rig();
            using (var cap = new Capture())
            {
                int start = rig.Twins.Cursor;
                // Push the stick east until the cursor has moved; A (Confirm) re-centres on it.
                int moved = start;
                float3[] dirs = { new float3(1, 0, 0), new float3(0, 1, 0), new float3(-1, 0, 0), new float3(0, -1, 0) };
                for (int d = 0; d < dirs.Length && moved == start; d++)
                {
                    rig.Stick(dirs[d].x, dirs[d].y);
                    moved = rig.Twins.Cursor;
                }
                Assert.AreNotEqual(start, moved, "the stick moved the cursor to another node");
                rig.Pad(AtlasAction.Confirm);
                Assert.AreEqual(moved, rig.Focus.FocusNode);
                Assert.AreEqual(1, rig.Twins.Recentres);
                Assert.AreEqual(1, cap.Events.Count);
                Assert.AreEqual("net.recentre", cap.Events[0].Ev);
                Assert.AreEqual(moved, (int)cap.Events[0].Data["node"].AsDouble());
                Assert.AreEqual(rig.Model.Degree(moved), (int)cap.Events[0].Data["degree"].AsDouble());
                Assert.AreEqual(rig.Model.Radius(moved), cap.Events[0].Data["r"].AsDouble(), 1e-9);
            }
        }

        [Test]
        public void SendPacket_ByGamepadAlone()
        {
            var rig = new Rig();
            using (var cap = new Capture())
            {
                rig.Stick(1f, 0f);
                rig.Pad(AtlasAction.Primary);
                Assert.AreEqual(1, rig.Twins.Packet.Runs);
                Assert.AreEqual(1, cap.Events.Count);
                Assert.AreEqual("net.packet", cap.Events[0].Ev);
                Assert.AreEqual(rig.Twins.Packet.Hops, (int)cap.Events[0].Data["hops"].AsDouble());
                StringAssert.Contains("hyperbolic", rig.Twins.Caption.Text);
            }
        }

        [Test]
        public void SwitchRule_ByGamepadAlone()
        {
            var rig = new Rig();
            Assert.AreEqual(RoutingRule.Hyperbolic, rig.Twins.Rule);
            rig.Pad(AtlasAction.Secondary);
            Assert.AreEqual(RoutingRule.Flat, rig.Twins.Rule);
            rig.Pad(AtlasAction.Secondary);
            Assert.AreEqual(RoutingRule.Hyperbolic, rig.Twins.Rule);
        }

        // ---- gaze alone ----

        [Test]
        public void Recentre_ByGazeAlone()
        {
            var rig = new Rig();
            using (var cap = new Capture())
            {
                // Look straight down at a node away from the chips, so that no other target is on the ray.
                int slot = 0;
                while (math.abs(rig.TargetCentre(slot).z) < 0.2f) slot++;
                int node = rig.Twins.TargetNode(slot);
                float3 c = rig.TargetCentre(slot);
                rig.Input.HeadPosition = new float3(c.x, 1.6f, c.z);
                rig.Input.HeadRotation = quaternion.LookRotation(new float3(0f, -1f, 0f), new float3(0f, 0f, 1f));
                rig.Frame(120);
                Assert.AreEqual(node, rig.Focus.FocusNode, "a dwell on the node re-centres on it");
                Assert.AreEqual(1, rig.Twins.Recentres);
                Assert.AreEqual("net.recentre", cap.Events[0].Ev);
            }
        }

        [Test]
        public void SendPacket_ByGazeAlone()
        {
            var rig = new Rig();
            using (var cap = new Capture())
            {
                rig.Dwell(Chip(rig.Twins.PacketChip, rig));
                Assert.AreEqual(1, rig.Twins.Packet.Runs, "a dwell on the packet chip sends a packet");
                Assert.AreEqual(1, cap.Events.Count);
                Assert.AreEqual("net.packet", cap.Events[0].Ev);
            }
        }

        [Test]
        public void SwitchRule_ByGazeAlone()
        {
            var rig = new Rig();
            rig.Dwell(Chip(rig.Twins.RuleChip, rig));
            Assert.AreEqual(RoutingRule.Flat, rig.Twins.Rule, "a dwell on the rule chip switches the rule");
        }

        // ---- the registry ----

        [Test]
        public void GazeNodeTargets_AtMostMaxGazeNodes()
        {
            foreach (int max in new[] { 16, 40, 64 })
            {
                var rig = new Rig(max);
                // min(net.maxGazeNodes, free slots - 2): the two chips hold slots of the 64.
                Assert.AreEqual(Math.Min(max, GazeRegistry.Capacity - NetworkTwins.ChipSlots), rig.Twins.NodeTargetCount);
                Assert.LessOrEqual(rig.Twins.NodeTargetCount, max);
                Assert.LessOrEqual(rig.Input.Core.Targets.Count, max + NetworkTwins.ChipSlots, "nodes plus the two chips");
                Assert.LessOrEqual(rig.Input.Core.Targets.Count, GazeRegistry.Capacity);
                // The cap follows the parameter while running.
                rig.Twins.MaxGazeNodes = 20;
                rig.Frame(1);
                Assert.AreEqual(20, rig.Twins.NodeTargetCount);
                Assert.AreEqual(20 + NetworkTwins.ChipSlots, rig.Input.Core.Targets.Count);
            }
            // The chips register first, so a registry that fills up afterwards never takes their slots.
            var crowded = new Rig(0);
            var filler = new GazeTarget { Action = AtlasAction.NextSpace, Space = GazeSpace.World, Radius = 0.1f, Context = InputContext.Space, Rotation = Quaternion.identity };
            while (crowded.Input.Core.Targets.Count < GazeRegistry.Capacity - 10) crowded.Input.RegisterGazeTarget(filler);
            crowded.Twins.MaxGazeNodes = 64;
            crowded.Frame(1);
            Assert.AreEqual(10, crowded.Twins.NodeTargetCount, "every free slot; the chips already hold theirs");
            Assert.IsTrue(crowded.Twins.PacketChip.IsValid && crowded.Twins.RuleChip.IsValid);
        }

        // ---- the registry follows the view ----

        [Test]
        public void Hidden_ReturnsEveryGazeRegistrySlot_ShownTakesThemBack()
        {
            var rig = new Rig();
            int shown = rig.Input.Core.Targets.Count;
            Assert.AreEqual(rig.Twins.NodeTargetCount + NetworkTwins.ChipSlots, shown);
            rig.Twins.Visible = false;
            rig.Frame(2);
            Assert.AreEqual(0, rig.Input.Core.Targets.Count, "hiding unregisters the nodes and both chips, not just disables them");
            Assert.AreEqual(0, rig.Twins.NodeTargetCount);
            // The rest of the app can fill the whole registry while the map is hidden.
            var filler = new GazeTarget { Action = AtlasAction.NextSpace, Space = GazeSpace.World, Radius = 0.1f, Context = InputContext.Space, Rotation = Quaternion.identity };
            while (rig.Input.Core.Targets.Count < GazeRegistry.Capacity) Assert.IsTrue(rig.Input.RegisterGazeTarget(filler).IsValid);
            Assert.AreEqual(GazeRegistry.Capacity, rig.Input.Core.Targets.Count);
            // A hidden twins never takes a slot, however many frames pass.
            var hiddenFromStart = new Rig();
            hiddenFromStart.Twins.Visible = false;
            hiddenFromStart.Frame(5);
            Assert.AreEqual(0, hiddenFromStart.Input.Core.Targets.Count);
            hiddenFromStart.Twins.Visible = true;
            hiddenFromStart.Frame(1);
            Assert.AreEqual(shown, hiddenFromStart.Input.Core.Targets.Count);
            Assert.IsTrue(hiddenFromStart.Twins.PacketChip.IsValid && hiddenFromStart.Twins.RuleChip.IsValid);
        }

        [Test]
        public void Dispose_ReturnsEveryGazeRegistrySlot()
        {
            var rig = new Rig();
            rig.Twins.Dispose();
            Assert.AreEqual(0, rig.Input.Core.Targets.Count);
        }

        // ---- the chips face the visitor at the disc centre ----

        [Test]
        public void Chips_FaceTheDiscCentre_NotAPlaneThroughTheHead()
        {
            foreach (int chip in new[] { NetworkTwins.PacketChipIndex, NetworkTwins.RuleChipIndex, NetworkTwins.CaptionChipIndex })
            {
                float3 c = NetworkTwins.ChipCentre(chip, 1f);
                quaternion rot = NetworkTwins.ChipRotation(chip);
                float3 f = math.mul(rot, new float3(0f, 0f, -1f));
                Vector3 front = new Vector3(f.x, f.y, f.z);      // the front is -Z of the chip's frame
                Vector3 toHead = new Vector3(Head.x - c.x, Head.y - c.y, Head.z - c.z).normalized;
                Vector3 flat = new Vector3(toHead.x, 0f, toHead.z).normalized;
                Assert.Greater(Vector3.Dot(front, flat), 0.999f, "chip " + chip + " faces the head");
                Assert.Greater(Vector3.Dot(front, toHead), 0.9f, "the gaze ray meets chip " + chip + " head on, not edge on");
            }
            var rig = new Rig();
            Assert.IsTrue(rig.Input.Core.Targets.TryGet(rig.Twins.PacketChip, out GazeTargetData packet));
            Assert.IsTrue(rig.Input.Core.Targets.TryGet(rig.Twins.RuleChip, out GazeTargetData rule));
            Assert.Less(packet.Centre.x, 0f);
            Assert.Greater(rule.Centre.x, 0f);
            Assert.Greater(math.abs(packet.Centre.z), 0.4f, "in front of the centre, not in the plane through it");
        }

        [Test]
        public void Chips_ArcRadius_FollowsTheDiscScale_WithinTheMenuSignBounds()
        {
            Assert.AreEqual(NetworkTwins.ChipArcRadius(0.5f), math.length(new float2(NetworkTwins.ChipCentre(0, 0.5f).x, NetworkTwins.ChipCentre(0, 0.5f).z)), 1e-5f);
            Assert.GreaterOrEqual(NetworkTwins.ChipArcRadius(0.01f), NetworkTwins.ChipRadiusMin);
            Assert.LessOrEqual(NetworkTwins.ChipArcRadius(10f), NetworkTwins.ChipRadiusMax);
        }

        // ---- the drawing: caption, packet and rule chips and the cursor ----

#if !ATLAS_HEADLESS
        [Test, Category("Unity")]
        public void ChipView_DrawsCaptionPacketRuleAndCursor()
        {
            var rig = new Rig();
            MaterialPool pool = MaterialPool.CreateDefault(8);
            var view = new NetworkChipView(rig.Twins, rig.Mesher, pool);
            try
            {
                view.Update();
                Assert.IsTrue(view.Shown);
                Assert.IsTrue(view.PacketChip.gameObject.activeSelf && view.RuleChip.gameObject.activeSelf);
                Assert.IsFalse(view.CaptionChip.gameObject.activeSelf, "no caption before the first packet");
                StringAssert.Contains("hyperbolic", view.RuleText);

                // The chips are drawn where the gaze targets are, with the same orientation.
                Assert.IsTrue(rig.Input.Core.Targets.TryGet(rig.Twins.PacketChip, out GazeTargetData packet));
                Assert.AreEqual(packet.Centre.x, view.PacketChip.localPosition.x, 1e-5f);
                Assert.AreEqual(packet.Centre.z, view.PacketChip.localPosition.z, 1e-5f);
                Assert.Greater(Quaternion.Dot(NetworkTwins.ChipRotation(NetworkTwins.PacketChipIndex), view.PacketChip.localRotation), 0.9999f);

                rig.Stick(1f, 0f);
                rig.Pad(AtlasAction.Primary);
                view.Update();
                Assert.IsTrue(view.CaptionChip.gameObject.activeSelf);
                Assert.AreEqual(rig.Twins.Caption.Text, view.CaptionText, "hop count and stretch shown");
                StringAssert.Contains("hop", view.CaptionText);

                rig.Pad(AtlasAction.Secondary);
                view.Update();
                StringAssert.Contains("flat", view.RuleText);

                float3 p = rig.Mesher.NodePosition(rig.Twins.Cursor);
                Assert.AreEqual(p.x, view.CursorPuck.localPosition.x, 1e-5f);
                Assert.AreEqual(p.z, view.CursorPuck.localPosition.z, 1e-5f);

                rig.Twins.Visible = false;
                rig.Frame(1);
                view.Update();
                Assert.IsFalse(view.Shown);
                Assert.IsFalse(view.PacketChip.gameObject.activeSelf || view.CursorPuck.gameObject.activeSelf);
            }
            finally { view.Dispose(); }
        }
#endif

        [Test]
        public void ChipView_UsesOnlyAtlasLook()
        {
            string dir = System.IO.Path.Combine(NetworkTestEnv.ProjectDir, "Assets", "Atlas", "Blades", "Network");
            string text = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "NetworkChipView.cs"));
            Assert.IsFalse(text.Contains("MaterialPropertyBlock") || text.Contains("new Material(") || text.Contains("Shader.Find"));
            Assert.IsTrue(text.Contains("AtlasMeshBuilder") && text.Contains("AtlasPalette") && text.Contains("MaterialPool"));
        }
    }
}

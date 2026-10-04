// PLACEHOLDER (lane D, P1-D01/P1-D03/P1-D04/P2-D01/P2-D07/P2-D08/P3-D01): contract surface only; replaced by lane D's implementation
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Atlas.Blades.Lung;
using Atlas.Link;
using Atlas.Telemetry;
using NUnit.Framework;
using UnityEngine;

namespace Atlas.PlaceholdersD.Tests
{
    public class PlaceholdersDTests
    {
        // ---- ParamRegistry -------------------------------------------------------------------------------------

        [Test] // Master Plan §7.3.2: "clamped; ignored under lock"
        public void Placeholder_Registry_ClampAndLock()
        {
            var r = new ParamRegistry();
            var id = r.Register("curved.cullRho", "curved", 2f, 3.5f, 3f, false);
            Assert.That(r.Get(id), Is.EqualTo(3f));
            Assert.That(r.Set(id, 9f), Is.EqualTo(3.5f));
            Assert.That(r.Set(id, 0f), Is.EqualTo(2f));
            var seen = new List<float>();
            r.Changed += (i, v) => seen.Add(v);
            r.DemoLock = true;
            Assert.That(r.ApplyRemote(id, 3f), Is.False);
            Assert.That(r.Get(id), Is.EqualTo(2f));
            r.DemoLock = false;
            Assert.That(r.ApplyRemote(id, 3f), Is.True);
            Assert.That(r.Get(id), Is.EqualTo(3f));
            Assert.That(seen, Is.EqualTo(new[] { 3f }));
        }

        [Test] // Master Plan App. B.2 (u16 ids in registration order)
        public void Placeholder_Registry_IdsInRegistrationOrder()
        {
            var r = new ParamRegistry();
            var a = r.Register("a", "g", 0, 1, 0, false);
            var b = r.Register("b", "g", 0, 1, 0, true);
            Assert.That(b, Is.EqualTo(a + 1));
            Assert.That(r.Register("a", "other", 5, 6, 5, true), Is.EqualTo(a));
            Assert.That(r.TryGetId("b", out var got) && got == b);
            Assert.That(r.TryGetId("zz", out _), Is.False);
        }

        // ---- AtlasParam ----------------------------------------------------------------------------------------

        enum Profile { Uniform, Power, Tanh }

        class Owner
        {
            [AtlasParam("lens.E", "lens", 0f, 90f, 54f)] public float E;
            [AtlasParam("lens.on", "lens", 0f, 1f, 0f)] public bool On;
            [AtlasParam("lens.profile", "lens", 0f, 2f, 1f)] public Profile Prof;
            [AtlasParam("curved.mode", "curved", 0f, 3f, 2f, OnRelease = true)] public int Mode;
        }

        [Test] // Master Plan §6.5 AtlasParam: registered, written into the field, idempotent Bind
        public void Placeholder_Scanner_BindsFieldsAndIsIdempotent()
        {
            var r = new ParamRegistry();
            var o = new Owner();
            AtlasParamScanner.Bind(o, r);
            AtlasParamScanner.Bind(o, r);
            Assert.That(o.E, Is.EqualTo(54f));
            Assert.That(o.Prof, Is.EqualTo(Profile.Power));
            Assert.That(o.Mode, Is.EqualTo(2));
            Assert.That(r.TryGetId("lens.E", out var e));
            r.Set(e, 30f);
            r.TryGetId("lens.on", out var on); r.Set(on, 1f);
            r.TryGetId("lens.profile", out var pf); r.Set(pf, 2f);
            Assert.That(o.E, Is.EqualTo(30f));
            Assert.That(o.On, Is.True);
            Assert.That(o.Prof, Is.EqualTo(Profile.Tanh));
            Assert.That(r.Register("curved.mode", "x", 0, 9, 9, false), Is.EqualTo((ushort)3));
        }

        [Test] // Master Plan §6.5: AttributeUsage Field and the five fields
        public void Placeholder_AtlasParamAttribute_Shape()
        {
            var a = new AtlasParamAttribute("x", "g", 1f, 2f, 1.5f) { OnRelease = true };
            Assert.That((a.Id, a.Group, a.Min, a.Max, a.Default, a.OnRelease), Is.EqualTo(("x", "g", 1f, 2f, 1.5f, true)));
            var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(typeof(AtlasParamAttribute), typeof(AttributeUsageAttribute));
            Assert.That(usage.ValidOn, Is.EqualTo(AttributeTargets.Field));
        }

        // ---- State / Event -------------------------------------------------------------------------------------

        [Test] // Master Plan App. B.1: State 0x10, 202 bytes, array lengths 3/4/6/9/8
        public void Placeholder_State_Layout()
        {
            var s = StateMessage.Create();
            Assert.That(StateMessage.MsgType, Is.EqualTo((byte)0x10));
            Assert.That(StateMessage.SizeBytes, Is.EqualTo(202));
            Assert.That((s.HeadPos.Length, s.HeadRot.Length, s.TileId.Length, s.Xform.Length, s.P.Length), Is.EqualTo((3, 4, 6, 9, 8)));
        }

        [Test] // Master Plan App. B.2: Event 0x40 with scene, ev, data
        public void Placeholder_Event_Emit()
        {
            Assert.That(EventMessage.MsgType, Is.EqualTo((byte)0x40));
            var got = new List<EventMessage>();
            Action<EventMessage> h = got.Add;
            EventLog.Emitted += h;
            try
            {
                EventLog.Emit("lung", "compass_choice", "{\"x\":1}");
                EventLog.Emit("lung", "fork", "{}");
            }
            finally { EventLog.Emitted -= h; }
            Assert.That(got.Count, Is.EqualTo(2));
            Assert.That((got[0].Scene, got[0].Ev, got[0].Data), Is.EqualTo(("lung", "compass_choice", "{\"x\":1}")));
            Assert.That(got[1].TUs, Is.GreaterThanOrEqualTo(got[0].TUs));
        }

        // ---- Case package fixtures -----------------------------------------------------------------------------

        static string Case => CaseManifest.Root("illustrative");

        [Test] // Master Plan §10.2.4 manifest.json: atlas.case/1, M = 15, Lambda = 1.5 m, hashes; §10.2.1 label
        public void Placeholder_Manifest_LoadsAndHashesMatch()
        {
            var m = CaseManifest.Load(Case);
            Assert.That(m.schema, Is.EqualTo("atlas.case/1"));
            Assert.That(m.kind, Is.EqualTo("illustrative"));
            Assert.That(m.M, Is.EqualTo(15f));
            Assert.That(m.Lambda_m, Is.EqualTo(1.5f));
            Assert.That(m.labels.Select(l => l.label), Contains.Item("Model-generated airway"));
            Assert.That(m.files.Select(f => f.path), Contains.Item("airway/tree.json"));
            Assert.That(m.files.Length, Is.EqualTo(13));
            foreach (var f in m.files)
            {
                using var sha = SHA256.Create();
                var hex = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(Case, f.path)))).Replace("-", "").ToLowerInvariant();
                Assert.That(hex, Is.EqualTo(f.sha256), f.path);
            }
        }

        [Serializable] class TreeNode { public string address; public int gen; public float len_mm, radius_mm; public float[] pos_mm, dir; public string source; }
        [Serializable] class AddressRef { public string address; }
        [Serializable] class TreeFile { public string schema; public TreeNode[] nodes; public AddressRef target, compass_fork; }

        static float Dot(float[] a, double[] b) => (float)(a[0] * b[0] + a[1] * b[1] + a[2] * b[2]);

        // P2-D07 checks: 16 generations addressable; ratio 0.794 +- 0.08; exactly 1 wrong Euclidean-compass fork on the route.
        // NOT checked, and not met (PH-39): P2-D07 says "on the 4-fork route"; this route has target.address.Length = 6 forks.
        [Test]
        public void Placeholder_Tree_AddressableRatioOneWrongFork()
        {
            var t = JsonUtility.FromJson<TreeFile>(File.ReadAllText(Path.Combine(Case, "airway", "tree.json")));
            Assert.That(t.schema, Is.EqualTo("atlas.tree/2"));
            var by = t.nodes.ToDictionary(n => n.address);
            Assert.That(t.nodes.Max(n => n.gen), Is.EqualTo(16));
            Assert.That(t.nodes.Length, Is.EqualTo((1 << 17) - 1));
            Assert.That(t.nodes.All(n => n.source == "model"));
            foreach (var n in t.nodes.Where(n => n.gen > 0).Take(5000))
                Assert.That(n.radius_mm / by[n.address.Substring(0, n.address.Length - 1)].radius_mm, Is.EqualTo(0.794f).Within(0.08f));
            // Euclidean compass: the child whose dir . (target point - child.pos_mm) is larger
            var tgt = by[t.target.address];
            var tp = new double[3];
            for (var i = 0; i < 3; i++) tp[i] = tgt.pos_mm[i] + tgt.dir[i] * tgt.len_mm;
            var wrong = new List<string>();
            for (var k = 0; k < t.target.address.Length; k++)
            {
                var a = t.target.address.Substring(0, k);
                double Score(string c)
                {
                    var n = by[a + c]; double s = 0;
                    for (var i = 0; i < 3; i++) s += n.dir[i] * (tp[i] - n.pos_mm[i]);
                    return s;
                }
                var pick = Score("L") > Score("R") ? 'L' : 'R';
                if (pick != t.target.address[k]) wrong.Add(a);
            }
            Assert.That(wrong, Is.EqualTo(new[] { t.compass_fork.address }));
        }

        [Serializable] class SkyCheck { public float az_deg, el_deg, clear_mm; public float[] clear_mm_phase; }
        [Serializable] class SkyFile { public string schema; public int width, height, phases; public float threshold_mm, mm_per_unit; public SkyCheck[] checks; }

        [Test] // P2-D08: robust.r8 equals the pixel-wise AND of the 10 phase maps; checks match decoded bytes within 0.1 mm
        public void Placeholder_Sky_P2D08()
        {
            var dir = Path.Combine(Case, "sky");
            var sky = JsonUtility.FromJson<SkyFile>(File.ReadAllText(Path.Combine(dir, "sky.json")));
            Assert.That((sky.schema, sky.width, sky.height, sky.phases), Is.EqualTo(("atlas.sky/1", 512, 256, 10)));
            var maps = Enumerable.Range(0, 10).Select(k => File.ReadAllBytes(Path.Combine(dir, $"risk_p{k}.r8"))).ToArray();
            var robust = File.ReadAllBytes(Path.Combine(dir, "robust.r8"));
            Assert.That(robust.Length, Is.EqualTo(512 * 256));
            var differing = 0;
            for (var p = 0; p < robust.Length; p++)
            {
                var all = maps.All(m => m[p] * sky.mm_per_unit >= sky.threshold_mm - 1e-4f);
                if ((robust[p] == 255) != all) differing++;
            }
            Assert.That(differing, Is.EqualTo(0));
            Assert.That(sky.checks.Length, Is.EqualTo(20));
            foreach (var c in sky.checks)
            {
                var i = (int)((c.az_deg + 180f) / 360f * 512f);
                var j = (int)((90f - c.el_deg) / 180f * 256f);
                var min = float.MaxValue;
                for (var k = 0; k < 10; k++)
                {
                    var mm = maps[k][j * 512 + i] * sky.mm_per_unit;
                    Assert.That(c.clear_mm_phase[k], Is.EqualTo(mm).Within(0.1f));
                    min = Mathf.Min(min, mm);
                }
                Assert.That(c.clear_mm, Is.EqualTo(min).Within(0.1f));
            }
        }

        // ---- Field-loss mask -----------------------------------------------------------------------------------

        [Test] // P3-D01: the tunnel mask's seen area equals a 10 degree disc (pi * 40^2 px at 4 px/deg) within 1 %
        public void Placeholder_TunnelArea()
        {
            var path = Path.Combine(Application.streamingAssetsPath, "fieldloss", "tunnel10.png");
            var tex = new Texture2D(2, 2, TextureFormat.R8, false);
            try
            {
                Assert.That(ImageConversion.LoadImage(tex, File.ReadAllBytes(path)), Is.True);
                Assert.That((tex.width, tex.height), Is.EqualTo((512, 512)));
                var px = tex.GetPixels32();
                var seen = px.Count(p => p.r >= 128);
                Assert.That(seen, Is.EqualTo(Math.PI * 40 * 40).Within(0.01 * Math.PI * 40 * 40));
                Assert.That(px[256 * 512 + 256].r, Is.EqualTo(255));
                Assert.That(px[0].r, Is.EqualTo(0));
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }
    }
}

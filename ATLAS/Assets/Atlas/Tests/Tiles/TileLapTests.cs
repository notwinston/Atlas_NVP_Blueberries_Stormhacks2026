// [P1-B06] T3 (row acceptance: "over a recorded lap with head turns and looks down at the floor, no undrawn tile has a point
// nearer than rho inside a camera frustum"). Runs on the recorded lap of the sample room (level gaze) with synthetic head turns
// and looks down. Engine-free.
using System;
using System.IO;
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Tiles.Tests
{
    public class TileLapTests
    {
        [Test, Timeout(600000)]
        public void RecordedLap_NoUndrawnTileInView()
        {
            const int q = 5; const double rho = 3.0;
            string project = Environment.GetEnvironmentVariable(RoomFixtures.ProjectDirVariable);
            if (string.IsNullOrEmpty(project)) project = Directory.GetCurrentDirectory();
            HeadPose[] samples;
            using (StreamReader reader = File.OpenText(Path.GetFullPath(Path.Combine(project, "..", "recordings", "walk_lap5_3x3.poselog"))))
                samples = PoseLog.Read(reader).Samples;
            double L = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3).Curvature.L5;
            Assert.Greater(samples.Length, 100);
            double rc = TileDrawRule.CircumRadius(q);
            // the eye cameras: 48 deg up and down, atan(tan 48 * 1118/1032) = 50.27 deg to each side (LensModel's default limits)
            double halfUD = 48.0, halfSide = Math.Atan(Math.Tan(48.0 * Math.PI / 180) * 1118.0 / 1032.0) * 180 / Math.PI;
            var pts = TileTestPoses.MidMeshPoints(q, L);
            var graph = new TileGraph(q);
            var sel = new TileSelection(graph, L) { Rho = rho };
            var h0 = samples[0].Position;
            var pose = new WalkerPose { Tile = TileId.Zero, Local = Iso2.Boost(new double2(h0.x, h0.z) / L, -1) };
            graph.Reroot(ref pose);   // test-harness copy of kernel.md 4.7
            double2 prev = new double2(h0.x, h0.z);
            int crossings = 0, viol = 0, evals = 0, maxAny = 0, maxGaze = 0;
            double[] yawOffs = { -90, -45, 0, 45, 90 }, downs = { -20, 0, 30, 60, 90 };
            for (int i = 0; i < samples.Length; i++)
            {
                var s = samples[i];
                if (s.Tracking != TrackingState.Normal) continue;
                var cur = new double2(s.Position.x, s.Position.z);
                var step = (cur - prev) / L; prev = cur;
                pose.Local = (pose.Local * Iso2.Boost(step, -1)).Orthonormalised();
                if (graph.Reroot(ref pose)) crossings++;
                if (i % 6 != 0) continue;
                var uInv = pose.Local.Inverse();
                foreach (double yo in yawOffs)
                    foreach (double dn in downs)
                    {
                        quaternion rot = math.mul(math.mul(quaternion.AxisAngle(math.up(), math.radians((float)yo)), s.Rotation), quaternion.AxisAngle(math.right(), math.radians((float)dn)));
                        float3 head = s.Position;
                        var cone = TileViewCone.FromHead(head, rot, halfSide, halfUD);
                        int n = sel.Update(pose, cone);
                        evals++;
                        maxAny = Math.Max(maxAny, n);
                        if (dn == 0) maxGaze = Math.Max(maxGaze, n);
                        Assert.LessOrEqual(n, 71, "drawn per evaluation");
                        var fr = new TileTestPoses.EyeFrusta(head, rot, halfSide, halfUD);
                        int j = 0;
                        for (int c = 0; c < sel.Candidates.Count; c++)
                        {
                            var t = sel.Candidates[c];
                            bool drawn = j < sel.DrawnCount && sel.Drawn[j].Id.Equals(t.Id);
                            if (drawn) { j++; continue; }
                            var A = uInv * t.F;
                            if (Math.Acosh(Math.Max(1, A.M.c2.z)) > rho + rc) continue;
                            if (TileTestPoses.ViolationFlags(pts, A, L, rho, fr) != 0) viol++;
                        }
                    }
            }
            string msg = $"[P1-B06 T3] L={L} samples={samples.Length} evals={evals} crossings={crossings} max drawn gaze={maxGaze} any={maxAny} violations={viol} outOfTile={sel.OutOfTileFrames}";
            TestContext.Out.WriteLine(msg);
            Assert.AreEqual(0, viol);
            Assert.GreaterOrEqual(crossings, 1);
            Assert.AreEqual(0, sel.OutOfTileFrames);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Atlas.Blades.FieldFit.BlindSpotFinder;
using NUnit.Framework;

namespace Atlas.Blades.FieldFit.BlindSpotTests
{
    public class BlindSpotProcedureTests
    {
        // Simulated observer: a point is seen iff hypot(T - 15.5, El + 1.5) > 3.0 - 0.215.
        static bool Sees(GridPoint p) => Math.Sqrt((p.TemporalDeg - 15.5) * (p.TemporalDeg - 15.5) + (p.ElDeg + 1.5) * (p.ElDeg + 1.5)) > 3.0 - 0.215;
        const double Rt = 0.45;

        // Runs the procedure to completion. tapCatches: tap even on catch trials. sees: observer.
        static BlindSpotProcedure Run(EyeSide eye, uint seed, Func<GridPoint, bool> sees, bool tapCatches = false)
        {
            var p = new BlindSpotProcedure(eye, seed);
            p.Begin(0);
            double t = 0;
            int guard = 0;
            while (p.Phase == BlindSpotPhase.Running && guard++ < 200000)
            {
                t += 0.05;
                p.Advance(t);
                if (p.Phase == BlindSpotPhase.Running && p.InTrial)
                {
                    var pt = p.CurrentPoint;
                    bool seen = sees(pt);
                    if (seen) p.Tap(p.CurrentOnsetS + Rt);
                }
            }
            return p;
        }

        // Catch trials are tapped only when tapCatches; needs the kind, so use a stepwise driver instead.
        static BlindSpotProcedure RunCatch(EyeSide eye, uint seed, bool tapCatches)
        {
            var p = new BlindSpotProcedure(eye, seed);
            p.Begin(0);
            double t = 0; int guard = 0;
            while (p.Phase == BlindSpotPhase.Running && guard++ < 200000)
            {
                t += 0.05;
                p.Advance(t);
                if (p.Phase == BlindSpotPhase.Running && p.InTrial)
                {
                    // the kind of the running trial is known only after it ends; infer: catch at found point once found
                    bool found = p.ConfirmedMisses.Count > 0;
                    bool isCatchNow = found && NextIsCatch(p);
                    bool seen = isCatchNow ? tapCatches : Sees(p.CurrentPoint);
                    if (seen) p.Tap(p.CurrentOnsetS + Rt);
                }
            }
            return p;
        }

        // Count non-catch trials since the last catch, from the completed probes (confirming trial excluded).
        static bool NextIsCatch(BlindSpotProcedure p)
        {
            int since = 0; bool found = false;
            foreach (var r in p.Probes)
            {
                if (!found) { if (r.Kind == ProbeKind.Reprobe && !r.Seen) { found = true; since = 0; } continue; }
                if (r.Kind == ProbeKind.Catch) since = 0; else since++;
            }
            return found && since >= 4;
        }

        [Test]
        public void Proc_Timing()
        {
            // checks: stimulus timing (D2): lead-in 1.0 s, dot 0.2 s, ISI 0.5-1.0 s
            var p = new BlindSpotProcedure(EyeSide.Right, 5);
            p.Begin(10.0);
            p.Advance(10.9); Assert.IsFalse(p.InTrial);
            p.Advance(11.0 + 0.1);
            Assert.IsTrue(p.InTrial);
            Assert.AreEqual(1.0, p.CurrentOnsetS, 1e-9);
            Assert.IsTrue(p.DotVisible);
            p.Advance(11.0 + 0.2 + 1e-6); Assert.IsFalse(p.DotVisible);

            var q = Run(EyeSide.Right, 5, _ => true);
            Assert.AreEqual(BlindSpotPhase.Done, q.Phase);
            Assert.AreEqual(72, q.Probes.Count);
            Assert.IsTrue(q.Probes.All(r => r.Kind == ProbeKind.Grid && r.Seen));
            Assert.AreEqual(1.0, q.Probes[0].OnsetS, 1e-9);
            for (int i = 0; i < 72; i++)
            {
                Assert.AreEqual(q.Probes[i].OnsetS + 0.45, q.Probes[i].EndS, 1e-9);
                if (i > 0)
                {
                    double isi = q.Probes[i].OnsetS - q.Probes[i - 1].EndS;
                    Assert.GreaterOrEqual(isi, 0.5 - 1e-9); Assert.Less(isi, 1.0);
                }
            }
            var r0 = q.Result;
            Assert.IsFalse(r0.Found); Assert.AreEqual(-1.0, r0.TimeToFindS); Assert.AreEqual(0, r0.CatchTrials);
        }

        [Test]
        public void Proc_ResponseWindow()
        {
            // checks: response window 0.1-1.5 s after onset (D2)
            var p = new BlindSpotProcedure(EyeSide.Right, 5); p.Begin(0);
            p.Advance(1.0); double on = p.CurrentOnsetS;
            p.Tap(on + 0.05); Assert.IsTrue(p.InTrial);               // anticipation ignored
            p.Advance(on + 1.6);
            Assert.IsFalse(p.Probes[0].Seen); Assert.AreEqual(on + 1.5, p.Probes[0].EndS, 1e-9);

            var q = new BlindSpotProcedure(EyeSide.Right, 5); q.Begin(0);
            q.Advance(1.0); on = q.CurrentOnsetS;
            q.Tap(on + 1.49);
            Assert.IsTrue(q.Probes[0].Seen); Assert.AreEqual(1.49, q.Probes[0].ResponseS, 1e-9);

            var s = new BlindSpotProcedure(EyeSide.Right, 5); s.Begin(0);
            s.Advance(1.0); on = s.CurrentOnsetS;
            s.Tap(on + 1.51);
            Assert.IsFalse(s.Probes[0].Seen); Assert.AreEqual(on + 1.5, s.Probes[0].EndS, 1e-9);
        }

        [Test]
        public void Proc_ReprobeLapse()
        {
            // checks: a single miss is re-probed; a lapse (seen on re-probe) is no blind spot (D3)
            var first = BlindSpotGrid.PresentationOrder(EyeSide.Right, 9)[0];
            bool found = false;
            var p = new BlindSpotProcedure(EyeSide.Right, 9);
            p.SpotFound += _ => found = true;
            p.Begin(0);
            double t = 0;
            while (p.Probes.Count < 3) { t += 0.05; p.Advance(t); if (p.InTrial) { bool firstPresentation = p.Probes.Count == 0; if (!firstPresentation) p.Tap(p.CurrentOnsetS + Rt); } }
            Assert.AreEqual(ProbeKind.Grid, p.Probes[0].Kind); Assert.IsFalse(p.Probes[0].Seen);
            Assert.AreEqual(ProbeKind.Reprobe, p.Probes[1].Kind);
            Assert.AreEqual(first.TemporalDeg, p.Probes[1].Point.TemporalDeg);
            Assert.IsTrue(p.Probes[1].Seen);
            Assert.AreEqual(0, p.ConfirmedMisses.Count); Assert.IsFalse(found);
        }

        [Test]
        public void Proc_Found()
        {
            // checks: found spot is a miss then a confirming re-probe miss; SpotFound once (D3)
            var p = new BlindSpotProcedure(EyeSide.Right, 3);
            int foundCount = 0; p.SpotFound += _ => foundCount++;
            p.Begin(0);
            var target = BlindSpotGrid.PresentationOrder(EyeSide.Right, 3)[0];
            double t = 0;
            while (p.Phase == BlindSpotPhase.Running && p.Probes.Count < 4)
            {
                t += 0.05; p.Advance(t);
                if (p.InTrial && !(p.CurrentPoint.TemporalDeg == target.TemporalDeg && p.CurrentPoint.ElDeg == target.ElDeg)) p.Tap(p.CurrentOnsetS + Rt);
            }
            Assert.AreEqual(1, foundCount);
            Assert.AreEqual(ProbeKind.Grid, p.Probes[0].Kind); Assert.IsFalse(p.Probes[0].Seen);
            Assert.AreEqual(ProbeKind.Reprobe, p.Probes[1].Kind); Assert.IsFalse(p.Probes[1].Seen);
            var res = p.Result;
            Assert.IsTrue(res.Found);
            Assert.AreEqual(p.Probes[1].EndS, res.TimeToFindS, 1e-9);
            Assert.AreEqual(target.TemporalDeg, res.FoundPoint.TemporalDeg); Assert.AreEqual(target.ElDeg, res.FoundPoint.ElDeg);
        }

        [Test]
        public void Proc_CatchTrials()
        {
            // checks: catch trials re-probe the found spot every 5th trial after found; false positives counted (D3)
            foreach (bool tap in new[] { false, true })
            {
                var p = RunCatch(EyeSide.Right, 3, tap);
                var res = p.Result;
                Assert.IsTrue(res.Found);
                int foundIdx = -1;
                for (int i = 0; i < p.Probes.Count; i++) if (p.Probes[i].Kind == ProbeKind.Reprobe && !p.Probes[i].Seen) { foundIdx = i; break; }
                int since = 0, nonCatch = 0, catches = 0;
                for (int i = foundIdx + 1; i < p.Probes.Count; i++)
                {
                    var r = p.Probes[i];
                    if (r.Kind == ProbeKind.Catch)
                    {
                        catches++;
                        Assert.AreEqual(4, since);
                        Assert.AreEqual(res.FoundPoint.TemporalDeg, r.Point.TemporalDeg); Assert.AreEqual(res.FoundPoint.ElDeg, r.Point.ElDeg);
                        since = 0;
                    }
                    else { since++; nonCatch++; }
                }
                Assert.AreEqual(nonCatch / 4, res.CatchTrials);
                Assert.AreEqual(catches, res.CatchTrials);
                Assert.AreEqual(tap ? res.CatchTrials : 0, res.CatchFalsePositives);
            }
        }

        [Test]
        public void Proc_Golden_Seed3()
        {
            // checks: §10.4.7 measures reproduce the reference simulation (Brief §3.4, D4)
            var p = Run(EyeSide.Right, 3, Sees);   // catches are not tapped when the observer is blind there
            var r = p.Result;
            Assert.AreEqual(8.6527, r.TimeToFindS, 0.001);
            Assert.AreEqual(16, r.FoundPoint.TemporalDeg); Assert.AreEqual(-4, r.FoundPoint.ElDeg);
            Assert.AreEqual(6, r.MissedCount); Assert.AreEqual(18, r.CatchTrials); Assert.AreEqual(96, r.Trials);
            Assert.AreEqual(15.6667 * Math.PI / 180, r.CentreAzRad, 1e-6);
            Assert.AreEqual(15.0 + 2.0 / 3.0, r.CentreAzRad * 180 / Math.PI, 1e-9);
            Assert.AreEqual(-5.0 / 3.0, r.CentreElRad * 180 / Math.PI, 1e-9);
            Assert.AreEqual(2.763953, r.RadiusRad * 180 / Math.PI, 1e-6);
            var l = Run(EyeSide.Left, 3, Sees).Result;   // observer in temporal coordinates: same
            Assert.AreEqual(8.6527, l.TimeToFindS, 0.001);
            Assert.AreEqual(-r.CentreAzRad, l.CentreAzRad, 1e-9);
            Assert.AreEqual(r.CentreElRad, l.CentreElRad, 1e-9);
        }

        [Test]
        public void Proc_FindsWithin30s()
        {
            // checks: P3-B06 acceptance proxy: blind spot found in <= 30 s (Spec §11.5)
            for (uint seed = 1; seed <= 20; seed++)
            {
                var r = Run(EyeSide.Right, seed, Sees).Result;
                Assert.IsTrue(r.Found, "seed " + seed);
                Assert.LessOrEqual(r.TimeToFindS, 30.0, "seed " + seed);
            }
        }

        [Test]
        public void Proc_Abort()
        {
            // checks: abort ends the run once
            var p = new BlindSpotProcedure(EyeSide.Right, 1);
            int fin = 0; BlindSpotResult res = default;
            p.Finished += r => { fin++; res = r; };
            p.Begin(0); p.Advance(2.0);
            p.Abort(2.5); p.Abort(3.0);
            Assert.AreEqual(BlindSpotPhase.Aborted, p.Phase);
            Assert.AreEqual(1, fin); Assert.IsTrue(res.Aborted);
        }
    }
}

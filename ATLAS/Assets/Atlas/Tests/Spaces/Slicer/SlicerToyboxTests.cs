using System.Collections.Generic;
using System.Text;
using Atlas.Spaces.Slicer;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.Tests
{
    /// <summary>
    /// The Slicer toybox's rules (Master Plan section 10.1.3, tasks P2-A01 and P2-A02): the look-down shadow, the guided
    /// steps, the panel's text and the slicer.* events. Engine-free.
    /// </summary>
    public class SlicerToyboxTests
    {
        const float Dt = 1f / 60f;

        sealed class RecordingLog : IEventLog
        {
            public readonly List<string> Events = new List<string>(), Data = new List<string>();
            public void Log(string ev) { Events.Add(ev); Data.Add(""); }
            public void Log(string ev, string key, double value) { Events.Add(ev); Data.Add(key); }
            public void Log(string ev, string key, string value) { Events.Add(ev); Data.Add(key); }
            public void Log(string ev, string key1, double value1, string key2, double value2) { Events.Add(ev); Data.Add(key1); }
            public void LogJson(string ev, string dataJson) { Events.Add(ev); Data.Add(dataJson); }
            public int Count(string ev) { int n = 0; foreach (string e in Events) if (e == ev) n++; return n; }
            public string Last(string ev) { for (int i = Events.Count - 1; i >= 0; i--) if (Events[i] == ev) return Data[i]; return null; }
        }

        static quaternion Pitch(float degrees) => quaternion.Euler(math.radians(-degrees), 0f, 0f);

        // ---- look down for the shadow (P2-A02) --------------------------------------------------------------------------

        [Test]
        public void LookDown_PitchIsMeasuredFromTheHorizon()
        {
            Assert.AreEqual(0f, LookDownShadow.PitchDegrees(quaternion.identity), 1e-3f);
            Assert.AreEqual(-40f, LookDownShadow.PitchDegrees(Pitch(-40f)), 1e-2f);
            Assert.AreEqual(25f, LookDownShadow.PitchDegrees(Pitch(25f)), 1e-2f);
        }

        // Acceptance P2-A02: the shadow toggles at -35 degrees (2 degrees either side), standing still
        [Test]
        public void LookDown_StandingStill_ShowsAndHidesWithHysteresis()
        {
            var look = new LookDownShadow();
            float3 head = new float3(0f, 1.6f, 0f);
            for (int i = 0; i < 60; i++) look.Step(head, Pitch(-36f), Dt);
            Assert.IsFalse(look.Peeking, "-36: inside the hysteresis band, not yet");
            for (int i = 0; i < 20; i++) look.Step(head, Pitch(-38f), Dt);
            Assert.IsTrue(look.Peeking, "-38 while still for over 0.5 s");
            look.Step(head, Pitch(-34f), Dt);
            Assert.IsTrue(look.Peeking, "-34: still inside the band, stays on");
            look.Step(head, Pitch(-32f), Dt);
            Assert.IsFalse(look.Peeking, "-32: off");
        }

        // Walking the lane while looking at the floor never peeks
        [Test]
        public void LookDown_WalkingNeverPeeks()
        {
            var look = new LookDownShadow();
            for (int i = 0; i < 300; i++) look.Step(new float3(0f, 1.6f, i * 0.6f * Dt), Pitch(-60f), Dt);   // 0.6 m/s
            Assert.IsFalse(look.Peeking);
            // Stop: half a second later the peek starts.
            float3 stop = new float3(0f, 1.6f, 3f);
            int frames = 0;
            while (!look.Peeking && frames < 200) { look.Step(stop, Pitch(-60f), Dt); frames++; }
            Assert.IsTrue(look.Peeking);
            Assert.That(frames * Dt, Is.GreaterThanOrEqualTo(LookDownShadow.StillSeconds).And.LessThan(1.5f));
            // Walking off ends it.
            for (int i = 1; i <= 30 && look.Peeking; i++) look.Step(stop + new float3(0f, 0f, i * 1.0f * Dt), Pitch(-60f), Dt);
            Assert.IsFalse(look.Peeking);
        }

        [Test]
        public void LookDown_KillSwitchEndsThePeek()
        {
            var look = new LookDownShadow();
            for (int i = 0; i < 60; i++) look.Step(float3.zero, Pitch(-50f), Dt);
            Assert.IsTrue(look.Peeking);
            Assert.IsTrue(look.Step(float3.zero, Pitch(-50f), Dt, enabled: false));
            Assert.IsFalse(look.Peeking);
        }

        // ---- the guided steps (P2-A01) -------------------------------------------------------------------------------------

        static SlicerTourInput At(SlicerObject body, SectionShape section, float w = 0f, float xw = 0f, bool shadow = false)
            => new SlicerTourInput { Body = body, Section = section, W = w, Xw = xw, ShadowShown = shadow, DeltaTime = Dt };

        // Master Plan section 10.1.3: the whole toybox, in order
        [Test]
        public void Tour_RunsTheStepsInOrder()
        {
            var tour = new SlicerTour();
            Assert.AreEqual(SlicerTask.PushThrough, tour.Task);
            tour.Step(At(SlicerObject.Tesseract, SectionShape.Tetrahedron, -0.8f));
            tour.Step(At(SlicerObject.Tesseract, SectionShape.TruncatedTetrahedron, -0.3f));
            Assert.AreEqual(SlicerTask.PushThrough, tour.Task, "the octahedron not yet seen");
            Assert.IsTrue(tour.Step(At(SlicerObject.Tesseract, SectionShape.Octahedron, 0f)));
            Assert.AreEqual(SlicerTask.Shadow, tour.Task);

            for (int i = 0; i < 30; i++) tour.Step(At(SlicerObject.Tesseract, SectionShape.Octahedron, shadow: true));
            Assert.AreEqual(SlicerTask.Shadow, tour.Task, "half a second of shadow is not enough");
            for (int i = 0; i < 40; i++) tour.Step(At(SlicerObject.Tesseract, SectionShape.Octahedron, shadow: true));
            Assert.AreEqual(SlicerTask.Rotate, tour.Task);

            // The step began at xw = 0 (the frame the shadow step ended).
            tour.Step(At(SlicerObject.Tesseract, SectionShape.Octahedron, xw: 0.2f));
            tour.Step(At(SlicerObject.Tesseract, SectionShape.Polyhedron, xw: 0.45f));
            Assert.AreEqual(SlicerTask.Rotate, tour.Task, "26 degrees from where the step began: less than 30");
            tour.Step(At(SlicerObject.Tesseract, SectionShape.Polyhedron, xw: 0.6f));
            Assert.AreEqual(SlicerTask.Cell5, tour.Task);

            tour.Step(At(SlicerObject.Tesseract, SectionShape.Octahedron, -0.9f));
            tour.Step(At(SlicerObject.Tesseract, SectionShape.Octahedron, 0.9f));
            Assert.AreEqual(SlicerTask.Cell5, tour.Task, "sweeping another body does not count");
            tour.Step(At(SlicerObject.Cell5, SectionShape.Tetrahedron, -0.7f));
            tour.Step(At(SlicerObject.Cell5, SectionShape.TriangularPrism, 0.6f));
            Assert.AreEqual(SlicerTask.Cell16, tour.Task);

            tour.Step(At(SlicerObject.Cell16, SectionShape.Octahedron, 0.5f));
            tour.Step(At(SlicerObject.Cell16, SectionShape.Octahedron, -0.8f));
            Assert.IsTrue(tour.Completed);
            StringAssert.Contains("Three 4D shapes", tour.Prompt);
        }

        [Test]
        public void Tour_RestartGoesBackToTheFirstStep()
        {
            var tour = new SlicerTour();
            tour.Step(At(SlicerObject.Tesseract, SectionShape.Tetrahedron));
            tour.Step(At(SlicerObject.Tesseract, SectionShape.TruncatedTetrahedron));
            tour.Step(At(SlicerObject.Tesseract, SectionShape.Octahedron));
            tour.Restart();
            Assert.AreEqual(SlicerTask.PushThrough, tour.Task);
            tour.Step(At(SlicerObject.Tesseract, SectionShape.Octahedron));
            Assert.AreEqual(SlicerTask.PushThrough, tour.Task, "what was seen before the restart is forgotten");
        }

        [Test]
        public void Tour_EveryStepNamesItsControls()
        {
            for (SlicerTask t = SlicerTask.PushThrough; t < SlicerTask.Done; t++)
            {
                string p = SlicerTour.PromptOf(t);
                StringAssert.Contains("(", p, t + ": a control twin in brackets, or a sign to look at");
                Assert.IsNotEmpty(SlicerTour.TaskName(t));
            }
        }

        // ---- the panel (P2-A01) -----------------------------------------------------------------------------------------------

        [Test]
        public void Readout_ShowsBodySectionCutTurnsAndStep()
        {
            var sb = new StringBuilder();
            var s = new SlicerReadoutState
            {
                Body = SlicerObject.Tesseract,
                Section = new SectionInfo { Shape = SectionShape.Octahedron, Faces = 8, Vertices = 6, Edges = 12, Triangles = 8 },
                W = 0f, Xw = math.radians(15f), View = SlicerView.Slice, Task = SlicerTask.PushThrough, ShowTour = true,
            };
            SlicerReadout.Build(sb, in s);
            string text = sb.ToString();
            StringAssert.StartsWith("TESSERACT  (1 of 6)", text);
            StringAssert.Contains("slice: octahedron, 8 faces", text);
            StringAssert.Contains("w +0.00  [----------|----------]", text);
            StringAssert.Contains("XW 15°   YW 0°   ZW 0°", text);
            StringAssert.Contains("Step 1 of 5: Walk along the line", text);
        }

        [Test]
        public void Readout_KeyChangesOnlyWithAShownDigit()
        {
            var a = new SlicerReadoutState { Body = SlicerObject.Cell5, W = 0.301f, Xw = math.radians(10.2f) };
            var b = a; b.W = 0.302f; b.Xw = math.radians(10.3f);
            Assert.AreEqual(SlicerReadout.Key(in a), SlicerReadout.Key(in b), "same digits on screen");
            b.W = 0.32f;
            Assert.AreNotEqual(SlicerReadout.Key(in a), SlicerReadout.Key(in b));
            b = a; b.Peeking = true;
            Assert.AreNotEqual(SlicerReadout.Key(in a), SlicerReadout.Key(in b));
        }

        [Test]
        public void Readout_NamesEverySectionKind()
        {
            var sb = new StringBuilder();
            foreach (SectionShape shape in new[] { SectionShape.None, SectionShape.Point, SectionShape.Flat, SectionShape.Sphere, SectionShape.Polyhedron })
            {
                var s = new SlicerReadoutState { Body = SlicerObject.Ball, Section = new SectionInfo { Shape = shape, Faces = 11 } };
                SlicerReadout.Build(sb, in s);
                StringAssert.Contains("slice: ", sb.ToString());
            }
            Assert.AreEqual("4D ball", SlicerReadout.BodyName(SlicerObject.Ball));
        }

        // ---- events (Master Plan section 10.1.3: slicer.w, slicer.rot, slicer.shadow, slicer.poly) ---------------------------

        [Test]
        public void Events_CutIsLoggedWhenItSettles_NotEveryFrame()
        {
            var log = new RecordingLog();
            var ev = new SlicerEvents(log);
            var section = new SectionInfo { Shape = SectionShape.Octahedron };
            double t = 0;
            ev.Step(t, 0f, 0f, 0f, 0f, in section, SlicerObject.Tesseract);
            // Walk the cut from 0 to 0.6 over two seconds: logged a few times, not 120.
            for (int i = 1; i <= 120; i++) { t += Dt; ev.Step(t, 0.005f * i, 0f, 0f, 0f, in section, SlicerObject.Tesseract); }
            int moving = log.Count(SlicerEvents.EventW);
            Assert.That(moving, Is.InRange(2, 5));
            // Stand still: one more, at the settled value.
            for (int i = 0; i < 90; i++) { t += Dt; ev.Step(t, 0.6f, 0f, 0f, 0f, in section, SlicerObject.Tesseract); }
            Assert.AreEqual("{\"w\":0.600}", log.Last(SlicerEvents.EventW));
            int settled = log.Count(SlicerEvents.EventW);
            for (int i = 0; i < 120; i++) { t += Dt; ev.Step(t, 0.6f, 0f, 0f, 0f, in section, SlicerObject.Tesseract); }
            Assert.AreEqual(settled, log.Count(SlicerEvents.EventW), "nothing more while nothing moves");
        }

        [Test]
        public void Events_TurnsAndSectionsAndShadow()
        {
            var log = new RecordingLog();
            var ev = new SlicerEvents(log);
            var octa = new SectionInfo { Shape = SectionShape.Octahedron, Faces = 8, Vertices = 6 };
            var tetra = new SectionInfo { Shape = SectionShape.Tetrahedron, Faces = 4, Vertices = 4 };
            double t = 0;
            ev.Step(t, 0f, 0f, 0f, 0f, in octa, SlicerObject.Tesseract);
            for (int i = 1; i <= 90; i++) { t += Dt; ev.Step(t, 0f, 0f, math.radians(0.5f * i), 0f, in octa, SlicerObject.Tesseract); }
            StringAssert.StartsWith("{\"plane\":\"yw\",\"deg\":", log.Last(SlicerEvents.EventRot));
            // A section seen for one frame is not logged; one that holds is.
            t += Dt; ev.Step(t, 0.7f, 0f, 0f, 0f, in tetra, SlicerObject.Tesseract);
            t += Dt; ev.Step(t, 0f, 0f, 0f, 0f, in octa, SlicerObject.Tesseract);
            Assert.AreEqual(0, log.Count(SlicerEvents.EventPoly));
            for (int i = 0; i < 30; i++) { t += Dt; ev.Step(t, 0.7f, 0f, 0f, 0f, in tetra, SlicerObject.Tesseract); }
            Assert.AreEqual("{\"object\":\"tesseract\",\"section\":\"tetrahedron\",\"faces\":4,\"vertices\":4,\"w\":0.700}", log.Last(SlicerEvents.EventPoly));
            ev.Shadow(true, byLook: true);
            Assert.AreEqual("{\"on\":1,\"by\":\"look\"}", log.Last(SlicerEvents.EventShadow));
            ev.Task(SlicerTask.Rotate);
            Assert.AreEqual("{\"task\":\"rotate\",\"step\":3}", log.Last(SlicerEvents.EventTask));
        }

        [Test]
        public void Events_NullLogIsSilent()
        {
            var ev = new SlicerEvents(null);
            var s = new SectionInfo();
            ev.Step(0, 0f, 0f, 0f, 0f, in s, SlicerObject.Tesseract);
            ev.Shadow(true, false);
            ev.Task(SlicerTask.Done);
            Assert.Pass();
        }
    }
}

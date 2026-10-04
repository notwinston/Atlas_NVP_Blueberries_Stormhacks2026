using System.Collections.Generic;
using Atlas.Blades.Playground.Exhibits;
using Atlas.Spaces.Slicer;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Blades.Playground.Tests
{
    /// <summary>The Slicer toybox as a Playground exhibit (tasks P2-A01, P2-A03): its task line follows the space's steps,
    /// and the S6 equation card reads the cut, the turns and the slice's name.</summary>
    public class ToyboxExhibitTests
    {
        sealed class FakeSlicer : ISlicerReadings
        {
            public SlicerObject Body { get; set; }
            public SectionInfo Section { get; set; }
            public float W { get; set; }
            public float Xw { get; set; }
            public float Yw { get; set; }
            public float Zw { get; set; }
            public SlicerTour Tour { get; } = new SlicerTour();
        }

        static SlicerTourInput At(SectionShape shape) =>
            new SlicerTourInput { Body = SlicerObject.Tesseract, Section = shape, DeltaTime = 1f / 60f };

        [Test]
        public void Exhibit_FollowsTheSpacesSteps()
        {
            var exhibit = new ToyboxExhibit();
            var link = new ToyboxExhibitLink(exhibit);
            Assert.AreEqual(string.Empty, link.Prompt, "outside S6 there is no task line");
            Assert.IsFalse(link.Completed);

            var space = new FakeSlicer();
            exhibit.Bind(space);
            Assert.AreEqual(SlicerTour.PromptOf(SlicerTask.PushThrough), link.Prompt);
            var lines = new List<string>();
            link.PromptChanged += lines.Add;
            space.Tour.Step(At(SectionShape.Tetrahedron));
            space.Tour.Step(At(SectionShape.TruncatedTetrahedron));
            space.Tour.Step(At(SectionShape.Octahedron));
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual(SlicerTour.PromptOf(SlicerTask.Shadow), lines[0]);

            link.Restart();
            Assert.AreEqual(SlicerTask.PushThrough, space.Tour.Task);
            Assert.AreEqual(2, lines.Count, "a restart sends the first line again");

            exhibit.Unbind();
            space.Tour.Step(At(SectionShape.Octahedron));
            Assert.AreEqual(2, lines.Count, "unbound: the space's steps no longer reach the chip");
            Assert.AreEqual(string.Empty, link.Prompt);
        }

        [Test]
        public void EquationCard_ReadsTheCutTheTurnsAndTheSlice()
        {
            var exhibit = new ToyboxExhibit();
            var card = new SlicerEquationSource(exhibit);
            Assert.AreEqual(SpaceId.Slicer, card.Space);
            var rows = new EquationRow[card.MaxRows];
            Assert.AreEqual(6, card.Read(rows));
            foreach (EquationRow r in rows) Assert.IsFalse(r.Available, r.Label + ": a dash outside S6");

            exhibit.Bind(new FakeSlicer
            {
                Body = SlicerObject.Tesseract, W = 0.25f, Xw = math.radians(30f),
                Section = new SectionInfo { Shape = SectionShape.TruncatedTetrahedron, Faces = 8 },
            });
            card.Read(rows);
            Assert.AreEqual(0.25, rows[0].Value, 1e-6);
            Assert.AreEqual(30.0, rows[1].Value, 1e-4);
            Assert.AreEqual("deg", rows[1].Unit);
            Assert.AreEqual("truncated tetrahedron", rows[4].Text);
            Assert.AreEqual(8.0, rows[5].Value);
            Assert.IsTrue(rows[5].Available);
            StringAssert.Contains("w₀", card.Template);
        }
    }
}

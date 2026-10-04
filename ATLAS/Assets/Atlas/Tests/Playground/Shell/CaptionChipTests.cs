using Atlas.World;
using NUnit.Framework;

namespace Atlas.Blades.Playground.Tests
{
    /// <summary>P2-C06: the caption chip at every entrance, in the words of Master Plan section 10.1.3.</summary>
    public class CaptionChipTests
    {
        static readonly (SpaceId id, string rule)[] Entrances =
        {
            (SpaceId.Glued, "Every wall is a door to its opposite wall. Flat, but it wraps."),
            (SpaceId.CurvedWalk, "Five rooms meet at every corner. Constant negative curvature."),
            (SpaceId.AlignedCells, "Each cell is flat. All the curvature sits at the corners."),
            (SpaceId.SmallPlanet, "Three rooms meet at every corner. Six tiles, then it repeats."),
            (SpaceId.Portal, "Flat rooms joined so that they overlap."),
            (SpaceId.Slicer, "A 3D slice of a flat 4D shape."),
        };

        [Test]
        public void TheChipIsShownAtSixOfSixEntrances()
        {
            var host = new FakeHost();
            var chip = new CaptionChip();
            chip.Attach(host);
            int shown = 0;
            foreach (var (id, rule) in Entrances)
            {
                host.Enter(id);
                if (chip.Visible && chip.Text == rule && chip.Space == id && chip.ShownCount(id) == 1) shown++;
                host.Exit(id);
                Assert.That(chip.Visible, Is.False, "hidden at the exit of " + id);
            }
            Assert.That(shown, Is.EqualTo(6), "entrances with the chip shown");
            Assert.That(chip.EntrancesShown, Is.EqualTo(6));
        }

        [Test]
        public void HomeHasItsOwnChipAndEveryReturnShowsItAgain()
        {
            var host = new FakeHost();
            var chip = new CaptionChip();
            chip.Attach(host);
            host.Enter(SpaceId.Home);
            Assert.That(chip.Text, Is.EqualTo(CaptionTexts.Home));
            host.Exit(SpaceId.Home);
            host.Enter(SpaceId.Home);
            Assert.That(chip.ShownCount(SpaceId.Home), Is.EqualTo(2));
        }

        [Test]
        public void ARepeatedEntranceShowsTheChipAgain()
        {
            var host = new FakeHost();
            var chip = new CaptionChip();
            chip.Attach(host);
            for (int i = 0; i < 3; i++)
            {
                host.Enter(SpaceId.Portal);
                host.Exit(SpaceId.Portal);
            }
            Assert.That(chip.ShownCount(SpaceId.Portal), Is.EqualTo(3));
        }

        [Test]
        public void AFlatSpaceIsNeverCalledCurved()
        {
            Assert.That(CaptionTexts.CallsFlatSpaceCurved(CaptionTexts.Home), Is.False);
            Assert.That(CaptionTexts.CallsFlatSpaceCurved(CaptionTexts.Glued), Is.False);
            Assert.That(CaptionTexts.CallsFlatSpaceCurved(CaptionTexts.Portal), Is.False);
            Assert.That(CaptionTexts.CallsFlatSpaceCurved(CaptionTexts.Slicer), Is.False);
            Assert.That(CaptionTexts.CallsFlatSpaceCurved(CaptionTexts.CurvedWalkForDial(4)), Is.False, "S2 on the flat detent");
            Assert.That(CaptionTexts.CallsFlatSpaceCurved(CaptionTexts.CurvedWalkForDial(5)), Is.True, "the lint does catch a curved text");
        }

        [Test]
        public void TheDialChangesTheCaptionOfTheCurvedWalk()
        {
            var host = new FakeHost();
            var chip = new CaptionChip();
            chip.Attach(host);
            host.Enter(SpaceId.CurvedWalk);
            Assert.That(chip.Text, Does.StartWith("Five rooms"));
            chip.SetDial(4);
            Assert.That(chip.Text, Does.Contain("Flat"));
            Assert.That(CaptionTexts.CallsFlatSpaceCurved(chip.Text), Is.False);
            chip.SetDial(6);
            Assert.That(chip.Text, Does.StartWith("Six rooms"));
            Assert.That(chip.EntrancesShown, Is.EqualTo(1), "a dial change is not an entrance");
        }

        [Test]
        public void TheModeCaptionsOfBeatEightAreTheTwoPhrasesOfThePlan()
        {
            Assert.That(CaptionTexts.ForMode(1), Is.EqualTo("smooth curvature"));
            Assert.That(CaptionTexts.ForMode(2), Is.EqualTo("curvature at the corners"));
            Assert.That(CaptionTexts.ForMode(3), Is.Empty);
            var chip = new CaptionChip();
            chip.ShowText(CaptionTexts.ForMode(2));
            Assert.That(chip.Visible, Is.True);
            Assert.That(chip.Text, Is.EqualTo("curvature at the corners"));
        }

        [Test]
        public void TheChipFadesInStaysThenFadesOut()
        {
            var chip = new CaptionChip();
            chip.OnSpaceEntered(SpaceId.Glued);
            Assert.That(chip.Alpha, Is.EqualTo(0f));
            chip.Tick(CaptionChip.FadeSeconds * 0.5f);
            Assert.That(chip.Alpha, Is.EqualTo(0.5f).Within(1e-4));
            chip.Tick(CaptionChip.FadeSeconds);
            Assert.That(chip.Alpha, Is.EqualTo(1f));
            chip.Tick(CaptionChip.ShowSeconds);
            Assert.That(chip.Visible, Is.False);
            chip.Tick(CaptionChip.FadeSeconds);
            Assert.That(chip.Alpha, Is.EqualTo(0f));
        }

        [Test]
        public void ADismissHidesIt()
        {
            var chip = new CaptionChip();
            chip.OnSpaceEntered(SpaceId.Slicer);
            int rev = chip.Revision;
            chip.Dismiss();
            Assert.That(chip.Visible, Is.False);
            Assert.That(chip.Revision, Is.GreaterThan(rev));
            chip.Dismiss();
        }

        [Test]
        public void DetachStopsTheListening()
        {
            var host = new FakeHost();
            var chip = new CaptionChip();
            chip.Attach(host);
            chip.Detach();
            host.Enter(SpaceId.Glued);
            Assert.That(chip.EntrancesShown, Is.EqualTo(0));
        }
    }
}

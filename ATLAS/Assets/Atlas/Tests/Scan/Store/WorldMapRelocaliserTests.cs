using Atlas.World;
using NUnit.Framework;

namespace Atlas.Scan.Tests.Store
{
    /// <summary>The decisions of the pre-scan path (lane C section 12; Master Plan section 4.2, task P1-C13).</summary>
    public class WorldMapRelocaliserTests
    {
        const float Dt = 1f / 60f;

        static Relocaliser Applied()
        {
            var r = new Relocaliser();
            r.Begin(true, true);
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Loading));
            r.MapLoaded(WorldMapLoad.Applied);
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Relocalising));
            return r;
        }

        // Ticks until the attempt ends or the time is up. Returns the last step that was not None.
        static RelocaliseStep Run(Relocaliser r, float seconds, TrackingState tracking, bool anchor)
        {
            RelocaliseStep last = RelocaliseStep.None;
            for (float t = 0f; t < seconds && r.Busy; t += Dt)
            {
                RelocaliseStep step = r.Tick(Dt, tracking, anchor);
                if (step != RelocaliseStep.None) last = step;
            }
            return last;
        }

        [Test]
        public void Success_OnTheRestoredAnchor()
        {
            Relocaliser r = Applied();
            Assert.That(Run(r, 3f, TrackingState.Limited, false), Is.EqualTo(RelocaliseStep.None));
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Relocalising));
            Assert.That(r.Progress, Is.EqualTo(3f / Relocaliser.Timeout).Within(0.01f));

            // Tracking is back but the anchor arrives a few frames later.
            Assert.That(Run(r, 0.2f, TrackingState.Normal, false), Is.EqualTo(RelocaliseStep.None));
            Assert.That(r.Tick(Dt, TrackingState.Normal, true), Is.EqualTo(RelocaliseStep.UseRestoredAnchor));
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Aligned));
            Assert.That(r.AnchorRestored, Is.True);
            Assert.That(r.Reason, Is.EqualTo(RelocaliseReason.None));
            Assert.That(r.Seconds, Is.EqualTo(3.2f).Within(0.1f));
            Assert.That(r.Busy, Is.False);
            Assert.That(r.Progress, Is.EqualTo(1f));

            // Finished: further ticks change nothing.
            float seconds = r.Seconds;
            Assert.That(r.Tick(Dt, TrackingState.NotTracking, false), Is.EqualTo(RelocaliseStep.None));
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Aligned));
            Assert.That(r.Seconds, Is.EqualTo(seconds));
        }

        [Test]
        public void Success_WithoutTheSavedAnchor_AsksForANewOneAfterOneSecond()
        {
            Relocaliser r = Applied();
            Run(r, 2f, TrackingState.Limited, false);
            Assert.That(Run(r, 0.9f, TrackingState.Normal, false), Is.EqualTo(RelocaliseStep.None));
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Relocalising));
            Assert.That(Run(r, 0.3f, TrackingState.Normal, false), Is.EqualTo(RelocaliseStep.CreateAnchor));
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Aligned));
            Assert.That(r.AnchorRestored, Is.False);
            Assert.That(r.SecondsNormal, Is.GreaterThanOrEqualTo(Relocaliser.AnchorWait));
        }

        [Test]
        public void ADropInTracking_RestartsTheAnchorWait()
        {
            Relocaliser r = Applied();
            Run(r, 1f, TrackingState.Limited, false);
            Run(r, 0.8f, TrackingState.Normal, false);
            Run(r, 0.1f, TrackingState.Limited, false);
            Assert.That(r.SecondsNormal, Is.EqualTo(0f));
            Assert.That(Run(r, 0.8f, TrackingState.Normal, false), Is.EqualTo(RelocaliseStep.None));
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Relocalising));
        }

        [Test]
        public void NormalTrackingBeforeTheSearch_ProvesNothing()
        {
            // The session was already tracking when the map was applied and has not restarted yet.
            Relocaliser r = Applied();
            Assert.That(Run(r, 2f, TrackingState.Normal, true), Is.EqualTo(RelocaliseStep.None));
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Relocalising));
            Assert.That(r.SawSearch, Is.False);
            Run(r, 0.5f, TrackingState.Limited, false);
            Assert.That(r.SawSearch, Is.True);
            Assert.That(r.Tick(Dt, TrackingState.Normal, true), Is.EqualTo(RelocaliseStep.UseRestoredAnchor));
        }

        [Test]
        public void TimeOut_WhenTheRoomIsNotRecognised()
        {
            Relocaliser r = Applied();
            Run(r, 5f, TrackingState.Limited, false);
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Relocalising));
            Assert.That(Run(r, 30f, TrackingState.Limited, false), Is.EqualTo(RelocaliseStep.None));
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Failed));
            Assert.That(r.Reason, Is.EqualTo(RelocaliseReason.TimedOut));
            Assert.That(r.Seconds, Is.EqualTo(Relocaliser.Timeout).Within(2f * Dt));
            Assert.That(r.Progress, Is.EqualTo(1f));
        }

        [Test]
        public void TrackingNeverNormal_Fails()
        {
            Relocaliser r = Applied();
            Assert.That(Run(r, 60f, TrackingState.NotTracking, true), Is.EqualTo(RelocaliseStep.None));
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Failed));
            Assert.That(r.Reason, Is.EqualTo(RelocaliseReason.TimedOut));
            Assert.That(r.AnchorRestored, Is.False);
        }

        [Test]
        public void TimeOut_CoversAMapThatNeverLoads()
        {
            var r = new Relocaliser();
            r.Begin(true, true);
            Run(r, 60f, TrackingState.Normal, true);
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Failed));
            Assert.That(r.Reason, Is.EqualTo(RelocaliseReason.TimedOut));
            r.MapLoaded(WorldMapLoad.Applied);
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Failed), "a late file changes nothing");
        }

        [Test]
        public void NoSavedRoom_IsUnsupportedAtOnce()
        {
            var r = new Relocaliser();
            r.Begin(false, true);
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Unsupported));
            Assert.That(r.Reason, Is.EqualTo(RelocaliseReason.NoSavedRoom));
            Assert.That(r.Busy, Is.False);
            Assert.That(r.Tick(Dt, TrackingState.Normal, true), Is.EqualTo(RelocaliseStep.None));
            Assert.That(r.Seconds, Is.EqualTo(0f));
        }

        [Test]
        public void NoWorldMapSupport_IsUnsupportedAtOnce()
        {
            var r = new Relocaliser();
            r.Begin(true, false);
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Unsupported));
            Assert.That(r.Reason, Is.EqualTo(RelocaliseReason.NotSupported));
        }

        [Test]
        public void MissingAndBadMapFiles()
        {
            var r = new Relocaliser();
            r.Begin(true, true);
            r.MapLoaded(WorldMapLoad.Missing);
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Unsupported));
            Assert.That(r.Reason, Is.EqualTo(RelocaliseReason.NoWorldMap));

            r.Begin(true, true);
            Assert.That(r.Reason, Is.EqualTo(RelocaliseReason.None));
            r.MapLoaded(WorldMapLoad.Bad);
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Failed));
            Assert.That(r.Reason, Is.EqualTo(RelocaliseReason.BadWorldMap));
        }

        [Test]
        public void Cancel_ReturnsToIdle_AndASecondAttemptStartsClean()
        {
            Relocaliser r = Applied();
            Run(r, 4f, TrackingState.Limited, false);
            r.Cancel();
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Idle));
            Assert.That(r.Reason, Is.EqualTo(RelocaliseReason.Cancelled));
            Assert.That(r.Progress, Is.EqualTo(0f));
            Assert.That(r.Tick(Dt, TrackingState.Normal, true), Is.EqualTo(RelocaliseStep.None));

            r.Begin(true, true);
            Assert.That(r.Seconds, Is.EqualTo(0f));
            Assert.That(r.SawSearch, Is.False);
            r.MapLoaded(WorldMapLoad.Applied);
            Run(r, 0.5f, TrackingState.Limited, false);
            Assert.That(r.Tick(Dt, TrackingState.Normal, true), Is.EqualTo(RelocaliseStep.UseRestoredAnchor));
        }

        [Test]
        public void TimeOut_AfterTheMapWasApplied_AsksToDropIt_Once()
        {
            Relocaliser r = Applied();
            Assert.That(r.MapToDrop, Is.False);
            Run(r, Relocaliser.Timeout + 1f, TrackingState.Limited, false);
            Assert.That(r.Reason, Is.EqualTo(RelocaliseReason.TimedOut));
            Assert.That(r.MapToDrop, Is.True);
            Assert.That(r.TakeMapToDrop(), Is.True);
            Assert.That(r.TakeMapToDrop(), Is.False);
        }

        [Test]
        public void Cancel_WhileRelocalising_AsksToDropTheMap_AndBeginKeepsTheDuty()
        {
            Relocaliser r = Applied();
            Run(r, 2f, TrackingState.Limited, false);
            r.Cancel();
            r.Begin(true, true);
            Assert.That(r.TakeMapToDrop(), Is.True);
            Assert.That(r.TakeMapToDrop(), Is.False);
        }

        [Test]
        public void NoMapToDrop_WhenNoneWasApplied_OrTheRoomWasRecognised()
        {
            var r = new Relocaliser();
            r.Begin(true, true);
            Run(r, Relocaliser.Timeout + 1f, TrackingState.Normal, false);     // the map never loads
            Assert.That(r.Reason, Is.EqualTo(RelocaliseReason.TimedOut));
            Assert.That(r.MapToDrop, Is.False);

            r.Begin(true, true);
            r.Cancel();                                                         // cancelled while Loading
            Assert.That(r.MapToDrop, Is.False);

            r.Begin(true, true);
            r.MapLoaded(WorldMapLoad.Bad);
            r.Cancel();
            Assert.That(r.MapToDrop, Is.False);

            r = Applied();
            Run(r, 0.5f, TrackingState.Limited, false);
            Run(r, 3f, TrackingState.Normal, true);
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Aligned));
            r.Cancel();                                                         // clearing a finished attempt
            Assert.That(r.MapToDrop, Is.False);

            // A second attempt that never applies its map owes nothing for the first, aligned one.
            r.Begin(true, true);
            Run(r, Relocaliser.Timeout + 1f, TrackingState.Limited, false);
            Assert.That(r.MapToDrop, Is.False);
        }

        [Test]
        public void BadTimeSteps_AddNothing()
        {
            Relocaliser r = Applied();
            r.Tick(float.NaN, TrackingState.Limited, false);
            r.Tick(-1f, TrackingState.Limited, false);
            Assert.That(r.Seconds, Is.EqualTo(0f));
            Assert.That(r.State, Is.EqualTo(RelocaliseState.Relocalising));
        }

        [Test]
        public void SaveRule_WaitsForMappedThenAsksAnyway()
        {
            Assert.That(WorldMapSaveRule.AskNow(WorldMappingQuality.Mapped, 0f), Is.True);
            Assert.That(WorldMapSaveRule.AskNow(WorldMappingQuality.Extending, 1f), Is.False);
            Assert.That(WorldMapSaveRule.AskNow(WorldMappingQuality.NotAvailable, 4.9f), Is.False);
            Assert.That(WorldMapSaveRule.AskNow(WorldMappingQuality.Limited, WorldMapSaveRule.MappedWait), Is.True);
        }

        [Test]
        public void NeutralPlatform_HasNoWorldMap()
        {
            IArWorldMapPlatform platform = ArWorldMapPlatform.Current;
            Assert.That(platform, Is.Not.Null);
            Assert.That(platform.WorldMapSupported, Is.False);
            Assert.That(platform.Name, Is.EqualTo("none"));
        }
    }
}

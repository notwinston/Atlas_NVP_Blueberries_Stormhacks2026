using System;
using System.Collections.Generic;
using Atlas.App;
using Atlas.Safety;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.App.Tests
{
    /// <summary>
    /// The guard step and the depth mask (hands design section 3.7, E5): with a mask source, a frame that holds a
    /// new depth image is copied and the copy carries the source's mask for that image; a source that throws
    /// never stops Bump Guard. Written and run by the hands/safety package; it belongs in Tests/App, which that
    /// package does not own.
    /// </summary>
    public class BumpGuardStepMaskTests
    {
        sealed class Pipeline : IPosePipeline
        {
            public SensorFrame Frame;
            public IPoseSource Source { get; set; }
            public IDepthSource Depth { get; set; }
            public RoomModel Room { get; set; }
            public ref readonly SensorFrame Sensors { get { return ref Frame; } }
            public HeadPose Raw { get { return Frame.Head; } }
            public HeadPose Geometry { get { return Frame.Head; } }
            public float2 Step { get { return float2.zero; } }
            public float2 PathFloor { get { return float2.zero; } }
            public bool JumpRejected { get { return false; } }
            public int JumpCount { get { return 0; } }
            public bool TrackingLost { get { return false; } }
            public void ResetGeometry() { }
            public float GyroPredictMs { get; set; }
            public quaternion PredictionRotation { get { return quaternion.identity; } }
#pragma warning disable 67
            public event Action<TrackingState> TrackingChanged;
            public event Action TrackingLostRaised;
            public event Action TrackingRegained;
            public event Action<float> JumpRejectedRaised;
#pragma warning restore 67
        }

        sealed class Guard : IBumpGuard
        {
            public readonly List<SensorFrame> Seen = new List<SensorFrame>();
            public BumpLevel Level { get { return BumpLevel.Clear; } }
            public float2 Direction { get { return float2.zero; } }
            public bool RescanAdvised { get { return false; } }
            public void Tick(in SensorFrame sensors) { Seen.Add(sensors); }
        }

        sealed class Source : IDepthMaskSource
        {
            public int Asked = -1, Calls;
            public bool Have = true, Throw;
            public DepthMask Mask;
            public bool TryGetMask(int depthSequence, out DepthMask mask)
            {
                Calls++;
                Asked = depthSequence;
                if (Throw) throw new InvalidOperationException("the hands are broken");
                mask = Mask;
                return Have;
            }
        }

        static DepthMask SomeMask(int sequence)
        {
            return new DepthMask { Count = 1, DepthSequence = sequence, Region0 = new DepthMaskRegion { MinU = 1, MaxU = 2, MinV = 3, MaxV = 4, MaxDepth = 0.6f } };
        }

        [Test]
        public void GuardStep_PassesTheMaskOfTheDepthImage_AndASourceThatThrowsDoesNotStopBumpGuard()
        {
            var pipeline = new Pipeline();
            var guard = new Guard();
            var source = new Source();
            var frame = default(FrameContext);

            // No source: today's call, the pipeline's own frame.
            var plain = new BumpGuardStep(guard, pipeline);
            pipeline.Frame.HasDepth = true;
            pipeline.Frame.Depth.Sequence = 7;
            pipeline.Frame.Time = 1.5;
            plain.Guard(in frame);
            Assert.That(guard.Seen.Count, Is.EqualTo(1));
            Assert.That(guard.Seen[0].Mask.IsEmpty, Is.True);
            Assert.That(guard.Seen[0].Time, Is.EqualTo(1.5));

            var step = new BumpGuardStep(guard, pipeline, source);
            // No new depth image: the source is not asked.
            pipeline.Frame.HasDepth = false;
            step.Guard(in frame);
            Assert.That(source.Calls, Is.EqualTo(0));
            Assert.That(guard.Seen[1].Mask.IsEmpty, Is.True);

            // A new depth image: the source is asked for that image's mask and the copy carries it.
            pipeline.Frame.HasDepth = true;
            pipeline.Frame.Depth.Sequence = 8;
            source.Mask = SomeMask(8);
            step.Guard(in frame);
            Assert.That(source.Asked, Is.EqualTo(8));
            Assert.That(guard.Seen[2].Mask.Count, Is.EqualTo(1));
            Assert.That(guard.Seen[2].Mask.DepthSequence, Is.EqualTo(8));
            Assert.That(guard.Seen[2].Mask.Region0.MaxDepth, Is.EqualTo(0.6f));
            Assert.That(guard.Seen[2].Depth.Sequence, Is.EqualTo(8));
            Assert.That(guard.Seen[2].Time, Is.EqualTo(1.5));
            Assert.That(pipeline.Frame.Mask.IsEmpty, Is.True, "the pipeline's own frame is untouched");

            // The source has none: an empty mask, whatever it left in the out value.
            source.Have = false;
            step.Guard(in frame);
            Assert.That(guard.Seen[3].Mask.IsEmpty, Is.True);

            // The source throws: Bump Guard still runs, on the unmasked frame.
            source.Throw = true;
            step.Guard(in frame);
            Assert.That(guard.Seen.Count, Is.EqualTo(5));
            Assert.That(guard.Seen[4].Mask.IsEmpty, Is.True);
            Assert.That(guard.Seen[4].Depth.Sequence, Is.EqualTo(8));
            Assert.That(step.MaskFaults, Is.EqualTo(1));
            step.Guard(in frame);
            Assert.That(step.MaskFaults, Is.EqualTo(2));
            Assert.That(guard.Seen.Count, Is.EqualTo(6));

#if ATLAS_HEADLESS
            // No allocation on the frame path.
            source.Throw = false;
            source.Have = true;
            guard.Seen.Clear();
            guard.Seen.Capacity = 256;
            step.Guard(in frame);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 100; k++) step.Guard(in frame);
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.EqualTo(0L));
#endif
        }
    }
}

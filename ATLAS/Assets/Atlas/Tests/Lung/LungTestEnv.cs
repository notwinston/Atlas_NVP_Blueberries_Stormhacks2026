using System;
using System.Collections.Generic;
using System.IO;
using Atlas.Link;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Tests
{
    // Where the tests find repository files, and the real event log they capture "lung" events from.
    static class LungTestEnv
    {
        public static string ProjectDir => Environment.GetEnvironmentVariable("ATLAS_PROJECT_DIR") ?? Directory.GetCurrentDirectory();

        // StreamingAssets/cases/<id>: the case package fixtures (lane D's P2-D01 is a placeholder; the files are in the project).
        public static string CaseRoot(string id) => Path.Combine(ProjectDir, "Assets", "StreamingAssets", "cases", id);

        // A normal-tracking frame: the geometry head pose equals the raw one.
        public static FrameContext Frame(long index, double time, float dt, float3 head, quaternion rotation)
        {
            return new FrameContext
            {
                FrameIndex = (int)index, Time = time, DeltaTime = dt, RawHeadPosition = head, RawHeadRotation = rotation,
                Tracking = TrackingState.Normal, HeadPosition = head, HeadRotation = rotation,
                LeftEyePosition = head, RightEyePosition = head,
            };
        }

        // The task's evidence files under docs/laneB/evidence are written only when ATLAS_WRITE_EVIDENCE=1, so a test run
        // does not rewrite tracked documents.
        public static void WriteEvidence(string task, string file, string text)
        {
            if (Environment.GetEnvironmentVariable("ATLAS_WRITE_EVIDENCE") != "1") return;
            string dir = Path.GetFullPath(Path.Combine(ProjectDir, "..", "docs", "laneB", "evidence", task));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, file), text);
        }
    }

    // A real EventLog (manual clock, no files) published as the session's IEventLog, with every event it writes collected.
    // Dispose puts back what was published before.
    sealed class EventCapture : IDisposable
    {
        public readonly ManualClock Clock = new ManualClock();
        public readonly EventLog Log;
        public readonly List<EventMessage> Events = new List<EventMessage>();
        readonly IEventLog _previous;

        public EventCapture()
        {
            Log = new EventLog(Clock, null);
            _previous = AtlasServices.Replace<IEventLog>(Log);
            Log.Logged += Events.Add;
        }

        public void Dispose()
        {
            Log.Logged -= Events.Add;
            if (_previous != null) AtlasServices.Replace(_previous); else AtlasServices.Unregister<IEventLog>(Log);
        }
    }
}

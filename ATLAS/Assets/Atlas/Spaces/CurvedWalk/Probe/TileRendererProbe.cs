// P1-B06 phone probe: test-scene driver for the deferred acceptance runs (docs/laneB/checklists/P1-B06_phone.md). Not product code.
// [P1-B06 D12] Runs in LateUpdate outside the frame loop and carries its own minimal U update. The product path is the
// P1-B02 HolonomyRig through AtlasFrameLoop (MP 6.3); this probe must not be copied into it.
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.World;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace Atlas.Spaces.CurvedWalk
{
    [DefaultExecutionOrder(101), RequireComponent(typeof(TileRenderer))]
    public sealed class TileRendererProbe : MonoBehaviour
    {
        public Shader surfaceShader;         // the AtlasSurface.shader asset, referenced so the build includes it
        public float lengthScale = 1.885f;   // room_3x3 L5 (MP 4.4)
        public float cullRho = 3f;           // curved.cullRho (MP 12.4)

        TileRenderer tiles; TileGraph graph; Material shared;
        WalkerPose pose; bool started; float3 prevHead; long frame;
        ProfilerRecorder rDraw, rSetPass, rTris, rGc;
        int maxShellDraws; long maxDraws, maxTris, maxGcAfterCrossing; int crossings, slowFrames;
        bool crossedPrev; int skipGc;

        void OnEnable()
        {
            tiles = GetComponent<TileRenderer>();
            graph = new TileGraph(5);
            shared = new Material(surfaceShader) { hideFlags = HideFlags.DontSave };
            tiles.Configure(graph, lengthScale, shared);
            tiles.CullRho = cullRho;
            // If a recorder is not Valid in a Release build, repeat the run in a Development build and note it (checklist item 2).
            rDraw = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            rSetPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            rTris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            rGc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            started = false;
        }

        void OnDisable()
        {
            rDraw.Dispose(); rSetPass.Dispose(); rTris.Dispose(); rGc.Dispose();
            if (shared != null) Destroy(shared);
        }

        void LateUpdate()
        {
            var headT = StereoRig.Current != null && StereoRig.Current.Head != null ? StereoRig.Current.Head : (Camera.main != null ? Camera.main.transform : null);
            if (headT == null) return;
            float3 head = headT.position;
            double L = lengthScale;
            if (!started)
            {
                pose = new WalkerPose { Tile = TileId.Zero, Local = Iso2.Boost(new double2(head.x, head.z) / L, -1) };
                graph.Reroot(ref pose);
                prevHead = head; started = true;
            }
            float2 d = head.xz - prevHead.xz; prevHead = head;
            if (math.length(d) > 0.2f) d = float2.zero;                                   // MP 6.3 step 3
            pose.Local = (pose.Local * Iso2.Boost((double2)d / L, -1)).Orthonormalised();
            bool crossed = graph.Reroot(ref pose);
            if (crossed) crossings++;
            tiles.CullRho = cullRho;   // a change forces one candidate rebuild; the same value does nothing
            tiles.SetLengthScale(lengthScale);

            // GC allocated in the previous frame (the recorder's last value) belongs to the frame after a crossing
            if (crossedPrev && skipGc == 0 && rGc.Valid) maxGcAfterCrossing = System.Math.Max(maxGcAfterCrossing, rGc.LastValue);
            if (skipGc > 0) skipGc--;
            crossedPrev = crossed;

            var fc = new FrameContext(frame++, Time.unscaledTimeAsDouble, Time.unscaledDeltaTime, head, headT.rotation, true, false);
            tiles.KernelStep(pose, fc);
            tiles.Upload(fc);

            maxShellDraws = System.Math.Max(maxShellDraws, 2 * tiles.DrawnCount);
            if (rDraw.Valid) maxDraws = System.Math.Max(maxDraws, rDraw.LastValue);
            if (rTris.Valid) maxTris = System.Math.Max(maxTris, rTris.LastValue);
            if (Time.unscaledDeltaTime > 0.0172f) slowFrames++;

            if (frame % 600 == 0)
            {
                Debug.Log("[P1-B06 probe] max 2xdrawn " + maxShellDraws + " draw calls " + maxDraws + " setpass " + (rSetPass.Valid ? rSetPass.LastValue : -1)
                    + " triangles " + maxTris + " crossings " + crossings + " max GC after crossing " + maxGcAfterCrossing + " B frames>17.2ms " + slowFrames);
                skipGc = 2;   // the logging frame and the frame after it are excluded from the GC maximum
            }
        }
    }
}

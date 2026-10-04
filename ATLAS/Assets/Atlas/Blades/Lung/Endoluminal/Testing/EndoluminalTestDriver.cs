using System.Diagnostics;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;

namespace Atlas.Blades.Lung.Endoluminal.Testing
{
    // Phone test driver for P2-B07 (docs/laneB/TESTING.md): 120 baseline frames, then Show for SyntheticForks.Route(4), one fork every 300 frames.
    // Logs per frame: the window code time and RenderCount, to persistentDataPath/endoluminal_timing.csv, and a summary to the console.
    public sealed class EndoluminalTestDriver : MonoBehaviour
    {
        public const int BaselineFrames = 120, FramesPerFork = 300, ForkCount = 4;

        [SerializeField] Shader surfaceShader;      // Atlas/AtlasSurface
        [SerializeField] Shader displayShader;      // Universal Render Pipeline/Unlit
        [SerializeField] TMP_FontAsset labelFont;   // LiberationSans SDF

        EndoluminalWindow window;
        ForkView[] forks;
        readonly StringBuilder csv = new StringBuilder(64 * 1024);
        readonly Stopwatch watch = new Stopwatch();
        int frame, lastCount;
        double renderMsTotal;
        int rendersSeen, quietFramesWithWork;
        bool written;

        void Start()
        {
            var go = new GameObject("EndoluminalWindow");
            window = go.AddComponent<EndoluminalWindow>();
            window.Initialise(surfaceShader, displayShader, labelFont);
            var cam = Camera.main;
            if (cam != null) { go.transform.SetParent(cam.transform, false); go.transform.localPosition = new Vector3(0, 0, 0.6f); }
            forks = SyntheticForks.Route(ForkCount);
            csv.AppendLine("frame,fork,window_ms,render_count,frame_ms");
        }

        void Update()
        {
            int f = frame - BaselineFrames;
            int forkIndex = f < 0 ? -1 : f / FramesPerFork;
            double windowMs = 0;
            if (forkIndex >= 0 && forkIndex < ForkCount && f % FramesPerFork == 0)
            {
                watch.Restart();
                window.Show(forks[forkIndex]);
                watch.Stop();
                windowMs = watch.Elapsed.TotalMilliseconds;   // the one frame that does the work
            }
            int count = window.RenderCount;
            if (count != lastCount) { rendersSeen++; renderMsTotal += windowMs; }
            else if (windowMs > 0) quietFramesWithWork++;
            lastCount = count;
            csv.Append(frame).Append(',').Append(forkIndex).Append(',').Append(windowMs.ToString("F3")).Append(',').Append(count).Append(',')
               .Append((Time.unscaledDeltaTime * 1000f).ToString("F2")).Append('\n');
            frame++;

            if (!written && frame >= BaselineFrames + FramesPerFork * ForkCount)
            {
                written = true;
                string path = Path.Combine(Application.persistentDataPath, "endoluminal_timing.csv");
                File.WriteAllText(path, csv.ToString());
                UnityEngine.Debug.Log($"[P2-B07] done: renders {rendersSeen} (expected {ForkCount}), mean render-frame window time {(rendersSeen > 0 ? renderMsTotal / rendersSeen : 0):F3} ms, " +
                                      $"frames with window work and no render {quietFramesWithWork}, csv {path}");
            }
        }
    }
}

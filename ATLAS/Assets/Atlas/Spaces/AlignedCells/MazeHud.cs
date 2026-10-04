using System;
using System.Text;
using TMPro;
using UnityEngine;

namespace Atlas.Spaces.AlignedCells
{
    // S3 duck maze design (2026-10-04) §5: the head-locked read-out of the maze, one TextMeshPro object under the HUD root as
    // CurvedWalkHud's (same font, distance and tilt). While searching "Find the duck  m:ss  walls N"; once found "Found the duck!
    // N steps, M walls crossed, m:ss". The text is rebuilt into one cached builder only when the shown second, the walls, the
    // steps of a find or the found state change, and handed over without a string. A build without TextMeshPro fonts shows
    // nothing and runs on.
    public sealed class MazeHud : IDisposable
    {
        /// <summary>Distance of the read-out from the eyes, metres, how far above the view axis it sits, degrees, its line height.</summary>
        public const float Distance = 1.0f, AboveDegrees = 9f, LineHeight = 0.02f;
        // One point of a TextMeshPro world text is a tenth of a unit, and a line of the default font is about 1.15 times its
        // point size (as CurvedWalkHud and the telemetry HUD).
        const float PointsPerLineMetre = 10f / 1.15f;
        static readonly Color s_text = new Color(0.92f, 0.95f, 0.92f, 1f);

        readonly StringBuilder text = new StringBuilder(64);
        readonly GameObject readoutObject;
        readonly TextMeshPro readout;
        int shownSecond = -1, shownWalls = -1, shownSteps = -1;
        bool shown, shownFound, disposed;

        /// <summary>hud: the head-locked root (IStereoRig.Hud), or null (no read-out).</summary>
        public MazeHud(Transform hud)
        {
            if (hud == null) return;
            // The default font asset of the project's TextMeshPro resources. A build without them has no text and runs on.
            TMP_FontAsset font = TMP_Settings.instance != null ? TMP_Settings.defaultFontAsset : null;
            if (font == null) return;
            readoutObject = new GameObject("MazeReadout") { hideFlags = HideFlags.DontSave };
            readoutObject.layer = hud.gameObject.layer;
            readoutObject.transform.SetParent(hud, false);
            readout = readoutObject.AddComponent<TextMeshPro>();
            readout.font = font;
            readout.fontSize = LineHeight * PointsPerLineMetre;
            readout.enableAutoSizing = false;
            readout.alignment = TextAlignmentOptions.Center;
            readout.textWrappingMode = TextWrappingModes.NoWrap;
            readout.overflowMode = TextOverflowModes.Overflow;
            readout.richText = false;
            readout.raycastTarget = false;
            readout.color = s_text;
            readout.rectTransform.sizeDelta = new Vector2(0.40f, LineHeight * 2f);
            float a = AboveDegrees * Mathf.Deg2Rad;
            readout.rectTransform.localPosition = new Vector3(0f, Mathf.Sin(a) * Distance, Mathf.Cos(a) * Distance);
            readout.rectTransform.localRotation = Quaternion.Euler(-AboveDegrees, 0f, 0f);   // tilted to face the eyes
            Renderer r = readout.renderer;
            if (r != null)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            readoutObject.SetActive(false);
        }

        /// <summary>The text last built (also without a read-out).</summary>
        public StringBuilder Text => text;
        public bool Visible => shown;

        /// <summary>Per frame in maze mode: the run's time, steps and walls crossed, and whether the duck is found.</summary>
        public void Show(double elapsedSeconds, int steps, int walls, bool found)
        {
            if (disposed) return;
            int second = WholeSeconds(elapsedSeconds);
            int foundSteps = found ? steps : -1;
            if (shown && second == shownSecond && walls == shownWalls && found == shownFound && foundSteps == shownSteps) return;
            shownSecond = second; shownWalls = walls; shownFound = found; shownSteps = foundSteps;
            Format(text, elapsedSeconds, steps, walls, found);
            shown = true;
            if (readout == null) return;
            if (!readoutObject.activeSelf) readoutObject.SetActive(true);
            readout.SetText(text);
        }

        /// <summary>Out of maze mode and on Exit.</summary>
        public void Hide()
        {
            if (disposed) return;
            shown = false;
            if (readoutObject != null && readoutObject.activeSelf) readoutObject.SetActive(false);
        }

        /// <summary>Destroys the read-out; Show and Hide do nothing afterwards.</summary>
        public void Dispose()
        {
            if (disposed) return;
            Hide();
            disposed = true;
            if (readoutObject != null) Kill(readoutObject);
        }

        static void Kill(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        /// <summary>The read-out's text into sb (cleared first). Engine-free; allocates nothing once sb has grown.</summary>
        public static void Format(StringBuilder sb, double elapsedSeconds, int steps, int walls, bool found)
        {
            sb.Length = 0;
            if (found)
            {
                sb.Append("Found the duck!  ");
                AppendInt(sb, steps); sb.Append(" steps, ");
                AppendInt(sb, walls); sb.Append(" walls crossed, ");
                AppendTime(sb, elapsedSeconds);
            }
            else
            {
                sb.Append("Find the duck  ");
                AppendTime(sb, elapsedSeconds);
                sb.Append("  walls ");
                AppendInt(sb, walls);
            }
        }

        static int WholeSeconds(double seconds) => seconds > 0 && !double.IsNaN(seconds) ? (int)Math.Min(Math.Floor(seconds), int.MaxValue) : 0;

        // m:ss
        static void AppendTime(StringBuilder sb, double seconds)
        {
            int s = WholeSeconds(seconds);
            AppendInt(sb, s / 60);
            sb.Append(':');
            sb.Append((char)('0' + s % 60 / 10));
            sb.Append((char)('0' + s % 10));
        }

        // Digits one by one: StringBuilder.Append(int) makes a string on Mono.
        static void AppendInt(StringBuilder sb, int value)
        {
            if (value < 0) { sb.Append('-'); value = value == int.MinValue ? int.MaxValue : -value; }
            int div = 1;
            while (value / div >= 10) div *= 10;
            for (; div > 0; div /= 10) sb.Append((char)('0' + value / div % 10));
        }
    }
}

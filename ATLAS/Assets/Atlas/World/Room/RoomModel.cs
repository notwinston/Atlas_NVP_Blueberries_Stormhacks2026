// PLACEHOLDER (lane C, P1-C06): contract surface only; replaced by lane C's implementation
using System;
using UnityEngine;

namespace Atlas.World
{
    // Frame and units (placeholder choice PH-15): everything is in the anchor frame (Master Plan §4.2, "everything is
    // stored relative to it"). Origin at the free rectangle's centre on the floor, +x along the Wf side, +z along the
    // Df side, +y up, Unity left-handed axes, metres. Angles are radians, Unity yaw sense (+z turns towards +x).
    // The JSON is read with JsonUtility (Master Plan §7.2): no dictionaries, no nested arrays, enums as strings.

    /// <summary>The RoomModel of Master Plan §4.3.</summary>
    [Serializable]
    public sealed class RoomModel
    {
        public string schema = "atlas.room/1";   // PH-16
        public string roomId;                    // sample id, or the Forge roomId (App. B.4)
        public RoomAnchor anchor;                // Master Plan §4.3
        public DistanceField sdf;                // Master Plan §4.3
        public FloorPolygon walkable;            // Master Plan §4.3
        public FreeRect freeRect;                // Master Plan §4.3
        public LapSquare lapSquare;              // Master Plan §4.3, §4.4
        public Curvature curvature;              // Master Plan §4.3, §4.4
        public Cells cells;                      // Master Plan §4.3, §4.7
        public bool standing;                    // min(Wf, Df) < 1.5 m (Master Plan §4.4)
        public Wall[] walls;                     // Master Plan §4.3
        public RoomObject[] objects;             // Master Plan §4.3
        public ScanQuality quality;              // Master Plan §4.3

        public static RoomModel FromJson(string json) => JsonUtility.FromJson<RoomModel>(json);
        public static RoomModel Load(string path) => FromJson(System.IO.File.ReadAllText(path));
        public string ToJson() => JsonUtility.ToJson(this, true);
    }

    [Serializable] public struct FloorPoint { public float x, z; }   // m; RoomOutline uses {x, z} (App. B.2)

    // ARAnchor pose in AR session space; rotation's +x is the Wf axis ("axis direction", Master Plan §4.3).
    [Serializable] public struct RoomAnchor { public Vector3 position; public Quaternion rotation; }

    /// <summary>Distance field on the 5 cm grid, positive inside free floor (Master Plan §4.3).</summary>
    [Serializable]
    public sealed class DistanceField
    {
        public float originX, originZ;   // m, corner of cell (0,0)
        public int cols, rows;           // at most 160 x 160 (Master Plan §7.4 budget)
        public float cell = 0.05f;       // m
        public string data;              // base64, float32 little-endian, row-major (row = z index), metres (PH-17)

        [NonSerialized] float[] _values;

        // Nearest cell; outside the grid returns -cell, i.e. not free (PH-17).
        public float Sample(float x, float z)
        {
            int i = Mathf.FloorToInt((x - originX) / cell);
            int j = Mathf.FloorToInt((z - originZ) / cell);
            if (i < 0 || j < 0 || i >= cols || j >= rows) return -cell;
            if (_values == null) Decode();
            return _values[j * cols + i];
        }

        void Decode()
        {
            var bytes = Convert.FromBase64String(data ?? "");
            var v = new float[cols * rows];
            for (int k = 0; k < v.Length && (k + 1) * 4 <= bytes.Length; k++)
                v[k] = BitConverter.ToSingle(bytes, k * 4);   // little-endian hosts only (Unity targets)
            _values = v;
        }
    }

    [Serializable] public sealed class FloorPolygon { public FloorPoint[] outer; public Ring[] holes; }
    [Serializable] public sealed class Ring { public FloorPoint[] points; }

    // m; centred on the anchor (centre 0,0 and yaw 0 in the anchor frame).
    [Serializable] public struct FreeRect { public float Wf, Df; }

    // m; centred on the anchor, sides along x and z (Master Plan §4.4).
    [Serializable] public struct LapSquare { public float s; }

    // m: L for q = 5 and 6, R for q = 3 (Master Plan §4.3, §4.4).
    [Serializable] public struct Curvature { public float L5, L6, R3; }

    [Serializable] public struct Cells { public GluedCells glued; public AlignedGrid aligned; }
    [Serializable] public struct GluedCells { public int nW, nD; public float cellW, cellD; }   // Master Plan §4.3 rule
    [Serializable] public struct AlignedGrid { public int n, m; public float g; }               // Master Plan §4.7, n along the short side

    [Serializable] public sealed class Wall { public FloorPoint a, b; public float height; public Opening[] openings; }

    // kind: "door" | "window" | "opening"; metres.
    [Serializable] public struct Opening { public string kind; public float from, to, bottom, top; }

    [Serializable]
    public sealed class RoomObject
    {
        public int id;           // 0-255 (RoomOutline boxId is u8, App. B.2)
        public string category;  // RoomPlan category, e.g. "table" (PH-18)
        public Vector3 position; // centre of the box's base, y = 0 (matches the GLB origin, Master Plan §6.4)
        public Vector3 size;     // w, h, d along the box's local x, y, z
        public float yaw;        // rad, Unity sense
        public string mesh;      // "placeholder" | "requested" | "ready" | "built-in" (Master Plan §4.3)
        public string hash;      // sha256 hex or "" (Master Plan §7.4)
    }

    // unknownShare 0..1; route 1..4 (Master Plan §4.2).
    [Serializable] public struct ScanQuality { public float unknownShare; public int route; }
}

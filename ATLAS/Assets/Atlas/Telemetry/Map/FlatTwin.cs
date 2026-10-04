using System;
using Atlas.Kernel;
using Atlas.Link;

namespace Atlas.Telemetry
{
    /// <summary>The colours of a flat twin. The first eight are the map's own; the last four colour the Station's indicators.</summary>
    public enum FlatTwinInk : byte
    {
        Outline = 0, Grid = 1, Cell = 2, Highlight = 3, Object = 4, Walker = 5, Trail = 6, Portal = 7,
        Good = 8, Warn = 9, Alert = 10, Muted = 11,
    }

    /// <summary>What one entry of a <see cref="FlatTwin"/> is.</summary>
    public enum FlatTwinShape : byte { Line = 0, Dot = 1, Arrow = 2, Bar = 3 }

    /// <summary>
    /// One entry of the draw list in map units. Line and Bar: (A, B) to (C, D). Dot: centre (A, B), radius C.
    /// Arrow: tip-centre (A, B), heading C in radians from +Z towards +X.
    /// </summary>
    public struct FlatTwinItem
    {
        public FlatTwinShape Shape;
        public FlatTwinInk Ink;
        public float A, B, C, D;
    }

    /// <summary>
    /// A retained list of lines, polygons, dots and labels in map units (Master Plan rule 6, task P1-D07). The
    /// map plane is the floor seen from above: +X to the right, +Z up the page. Engine-free. Once the list has
    /// grown to its working size, clearing and refilling it allocates nothing.
    /// </summary>
    public sealed class FlatTwin
    {
        public const int MaxLabels = 12;

        FlatTwinItem[] _items = new FlatTwinItem[512];
        int _count;
        readonly string[] _labels = new string[MaxLabels];
        readonly float[] _labelXZ = new float[MaxLabels * 2];
        int _labelCount;
        float _minX, _minZ, _maxX, _maxZ;
        bool _boundsEmpty = true;

        public FlatTwin()
        {
            HalfExtent = 1f;
        }

        /// <summary>Entries drawn, in order: later ones lie over earlier ones.</summary>
        public int Count { get { return _count; } }
        public int LabelCount { get { return _labelCount; } }

        /// <summary>The view: the square of this half side round the centre fills the panel.</summary>
        public float CentreX { get; private set; }
        public float CentreZ { get; private set; }
        public float HalfExtent { get; private set; }
        /// <summary>True for a disc view (the Poincaré disc of lane B): everything outside the unit circle is cut away.</summary>
        public bool ClipToDisc { get; private set; }

        /// <summary>+1 on every Clear, so a panel can tell that the list was rebuilt.</summary>
        public int Revision { get; private set; }

        public void Clear()
        {
            _count = 0;
            _boundsEmpty = true;
            for (int i = 0; i < _labelCount; i++) _labels[i] = null;
            _labelCount = 0;
            CentreX = 0f;
            CentreZ = 0f;
            HalfExtent = 1f;
            ClipToDisc = false;
            Revision++;
        }

        public void SetView(float centreX, float centreZ, float halfExtent, bool clipToDisc)
        {
            CentreX = IsFinite(centreX) ? centreX : 0f;
            CentreZ = IsFinite(centreZ) ? centreZ : 0f;
            HalfExtent = IsFinite(halfExtent) && halfExtent > 1e-4f ? halfExtent : 1f;
            ClipToDisc = clipToDisc;
        }

        /// <summary>
        /// Sets the view to the bounding square of everything drawn so far, grown by <paramref name="margin"/>
        /// (a share of the half side). An empty list gives a 1 m half side round the origin.
        /// </summary>
        public void FitView(float margin = 0.08f, bool clipToDisc = false)
        {
            if (_boundsEmpty)
            {
                SetView(0f, 0f, 1f, clipToDisc);
                return;
            }
            float half = 0.5f * Math.Max(_maxX - _minX, _maxZ - _minZ);
            SetView(0.5f * (_minX + _maxX), 0.5f * (_minZ + _maxZ), Math.Max(half, 0.05f) * (1f + margin), clipToDisc);
        }

        public void Line(float ax, float az, float bx, float bz, FlatTwinInk ink)
        {
            if (!(IsFinite(ax) && IsFinite(az) && IsFinite(bx) && IsFinite(bz))) return;
            Add(FlatTwinShape.Line, ink, ax, az, bx, bz);
            Grow(ax, az);
            Grow(bx, bz);
        }

        /// <summary>A closed outline: x0, z0, x1, z1, …</summary>
        public void Polygon(ReadOnlySpan<float> xz, FlatTwinInk ink)
        {
            int n = xz.Length / 2;
            if (n < 2) return;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                if (n == 2 && i == 1) break;
                Line(xz[2 * i], xz[2 * i + 1], xz[2 * j], xz[2 * j + 1], ink);
            }
        }

        /// <summary>An axis-aligned rectangle as four lines.</summary>
        public void Rect(float minX, float minZ, float maxX, float maxZ, FlatTwinInk ink)
        {
            Line(minX, minZ, maxX, minZ, ink);
            Line(maxX, minZ, maxX, maxZ, ink);
            Line(maxX, maxZ, minX, maxZ, ink);
            Line(minX, maxZ, minX, minZ, ink);
        }

        /// <summary>A rectangle of the given size round a centre, turned by yaw about +Y (Unity's sense), as four lines.</summary>
        public void Box(float cx, float cz, float width, float depth, float yaw, FlatTwinInk ink)
        {
            float c = (float)Math.Cos(yaw), s = (float)Math.Sin(yaw);
            // Unity's yaw carries local +X to (cos, -sin) and local +Z to (sin, cos) on the floor.
            float ux = 0.5f * width * c, uz = -0.5f * width * s;
            float vx = 0.5f * depth * s, vz = 0.5f * depth * c;
            Line(cx - ux - vx, cz - uz - vz, cx + ux - vx, cz + uz - vz, ink);
            Line(cx + ux - vx, cz + uz - vz, cx + ux + vx, cz + uz + vz, ink);
            Line(cx + ux + vx, cz + uz + vz, cx - ux + vx, cz - uz + vz, ink);
            Line(cx - ux + vx, cz - uz + vz, cx - ux - vx, cz - uz - vz, ink);
        }

        public void Dot(float x, float z, float radius, FlatTwinInk ink)
        {
            if (!(IsFinite(x) && IsFinite(z) && IsFinite(radius))) return;
            Add(FlatTwinShape.Dot, ink, x, z, Math.Abs(radius), 0f);
            Grow(x, z);
        }

        /// <summary>The walker: an arrow head at (x, z) pointing along the heading, measured from +Z towards +X.</summary>
        public void Arrow(float x, float z, float headingRadians, FlatTwinInk ink)
        {
            if (!(IsFinite(x) && IsFinite(z))) return;
            Add(FlatTwinShape.Arrow, ink, x, z, IsFinite(headingRadians) ? headingRadians : 0f, 0f);
            Grow(x, z);
        }

        /// <summary>A filled axis-aligned rectangle: the Station's level bars.</summary>
        public void Bar(float minX, float minZ, float maxX, float maxZ, FlatTwinInk ink)
        {
            if (!(IsFinite(minX) && IsFinite(minZ) && IsFinite(maxX) && IsFinite(maxZ))) return;
            Add(FlatTwinShape.Bar, ink, minX, minZ, maxX, maxZ);
            Grow(minX, minZ);
            Grow(maxX, maxZ);
        }

        /// <summary>A short text centred at (x, z). At most <see cref="MaxLabels"/>; more are dropped.</summary>
        public void Label(float x, float z, string text)
        {
            if (_labelCount >= MaxLabels || string.IsNullOrEmpty(text) || !(IsFinite(x) && IsFinite(z))) return;
            _labels[_labelCount] = text;
            _labelXZ[2 * _labelCount] = x;
            _labelXZ[2 * _labelCount + 1] = z;
            _labelCount++;
        }

        // ---- reading the list ----------------------------------------------------------------------------

        public ref readonly FlatTwinItem this[int index] { get { return ref _items[index]; } }

        public string GetLabel(int index, out float x, out float z)
        {
            x = _labelXZ[2 * index];
            z = _labelXZ[2 * index + 1];
            return _labels[index];
        }

        /// <summary>How many entries have this shape and ink.</summary>
        public int CountOf(FlatTwinShape shape, FlatTwinInk ink)
        {
            int n = 0;
            for (int i = 0; i < _count; i++)
                if (_items[i].Shape == shape && _items[i].Ink == ink) n++;
            return n;
        }

        /// <summary>The first entry of this shape and ink. False when there is none.</summary>
        public bool TryFind(FlatTwinShape shape, FlatTwinInk ink, out FlatTwinItem item)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_items[i].Shape != shape || _items[i].Ink != ink) continue;
                item = _items[i];
                return true;
            }
            item = default(FlatTwinItem);
            return false;
        }

        /// <summary>The box round everything drawn. False while the list is empty.</summary>
        public bool TryGetBounds(out float minX, out float minZ, out float maxX, out float maxZ)
        {
            minX = _minX; minZ = _minZ; maxX = _maxX; maxZ = _maxZ;
            return !_boundsEmpty;
        }

        /// <summary>True when the point lies inside the view square (or disc).</summary>
        public bool InView(float x, float z)
        {
            float px = (x - CentreX) / HalfExtent, pz = (z - CentreZ) / HalfExtent;
            return ClipToDisc ? px * px + pz * pz <= 1f : Math.Abs(px) <= 1f && Math.Abs(pz) <= 1f;
        }

        void Add(FlatTwinShape shape, FlatTwinInk ink, float a, float b, float c, float d)
        {
            if (_count == _items.Length) Array.Resize(ref _items, _items.Length * 2);
            _items[_count].Shape = shape;
            _items[_count].Ink = ink;
            _items[_count].A = a;
            _items[_count].B = b;
            _items[_count].C = c;
            _items[_count].D = d;
            _count++;
        }

        void Grow(float x, float z)
        {
            if (_boundsEmpty)
            {
                _minX = _maxX = x;
                _minZ = _maxZ = z;
                _boundsEmpty = false;
                return;
            }
            if (x < _minX) _minX = x;
            if (x > _maxX) _maxX = x;
            if (z < _minZ) _minZ = z;
            if (z > _maxZ) _maxZ = z;
        }

        static bool IsFinite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
    }

    /// <summary>Optional, on a space module: the space draws its own flat twin. Called at most 10 times per second.</summary>
    public interface IFlatTwinSource
    {
        void WriteFlatTwin(FlatTwin twin);
    }

    /// <summary>
    /// The last 600 walker positions at 10 Hz, and the last 64 tile ids, without repeats. Fixed rings: adding
    /// allocates nothing. A change of space starts both again.
    /// </summary>
    public sealed class FlatTwinTrail
    {
        public const int MaxPositions = 600;
        public const int MaxTiles = 64;
        /// <summary>Positions are kept this far apart in time (10 Hz).</summary>
        public const long SampleUs = 100000;
        /// <summary>A walker who stands still adds no position: the next one is kept after this many metres.</summary>
        public const float MinStepMetres = 0.03f;

        readonly float[] _xz = new float[MaxPositions * 2];
        int _posHead, _posCount;
        readonly TileId[] _tiles = new TileId[MaxTiles];
        int _tileHead, _tileCount;
        long _lastSampleUs;
        bool _hasSample;
        int _space = -1;

        public int PositionCount { get { return _posCount; } }
        public int TileCount { get { return _tileCount; } }
        /// <summary>Tile ids ever added since the last Clear, repeats left out; more than <see cref="MaxTiles"/> when old ones have dropped off.</summary>
        public long TilesSeen { get; private set; }

        /// <summary>Position 0 is the oldest kept.</summary>
        public void GetPosition(int index, out float x, out float z)
        {
            int i = (_posHead + index) % MaxPositions;
            x = _xz[2 * i];
            z = _xz[2 * i + 1];
        }

        /// <summary>Tile 0 is the oldest kept; the last is the tile the walker is on.</summary>
        public TileId GetTile(int index)
        {
            return _tiles[(_tileHead + index) % MaxTiles];
        }

        /// <summary>
        /// Takes one State. Its tile id is appended when it differs from the newest one, so every State may be
        /// handed in and no tile is missed; its head position is kept at most ten times per second of the
        /// State's own clock.
        /// </summary>
        public void Add(in StateMessage state)
        {
            if (state.SpaceId != _space)
            {
                Clear();
                _space = state.SpaceId;
            }

            var tile = new TileId(state.Tile0, state.Tile1, state.Tile2, state.Tile3, state.Tile4, state.Tile5);
            if (_tileCount == 0 || GetTile(_tileCount - 1) != tile)
            {
                if (_tileCount < MaxTiles) _tiles[(_tileHead + _tileCount++) % MaxTiles] = tile;
                else
                {
                    _tiles[_tileHead] = tile;
                    _tileHead = (_tileHead + 1) % MaxTiles;
                }
                TilesSeen++;
            }

            float x = state.HeadPosX, z = state.HeadPosZ;
            if (float.IsNaN(x) || float.IsNaN(z) || float.IsInfinity(x) || float.IsInfinity(z)) return;
            long t = state.TimeUs;
            // A State with no clock (the phone's own panel) or a clock that started again is always taken.
            bool due = !_hasSample || t == 0 || t < _lastSampleUs || t - _lastSampleUs >= SampleUs;
            if (!due) return;
            if (_posCount > 0)
            {
                float lx, lz;
                GetPosition(_posCount - 1, out lx, out lz);
                float dx = x - lx, dz = z - lz;
                if (dx * dx + dz * dz < MinStepMetres * MinStepMetres) return;
            }
            _lastSampleUs = t;
            _hasSample = true;
            int slot;
            if (_posCount < MaxPositions) slot = (_posHead + _posCount++) % MaxPositions;
            else
            {
                slot = _posHead;
                _posHead = (_posHead + 1) % MaxPositions;
            }
            _xz[2 * slot] = x;
            _xz[2 * slot + 1] = z;
        }

        public void Clear()
        {
            _posHead = 0;
            _posCount = 0;
            _tileHead = 0;
            _tileCount = 0;
            TilesSeen = 0;
            _hasSample = false;
            _lastSampleUs = 0;
            _space = -1;
        }
    }
}

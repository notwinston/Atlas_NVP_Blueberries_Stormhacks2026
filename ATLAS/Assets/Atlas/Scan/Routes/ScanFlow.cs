using System;
using System.Globalization;
using System.Threading.Tasks;
using Atlas.Scan.Grid;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Scan
{
    /// <summary>
    /// The scan's state machine (contract section 10.2; Master Plan section 4.2, tasks P1-C02 to P1-C06):
    /// Idle, Starting, Sweeping, Processing, AwaitingConfirm, Finalising, Done, Failed. It owns the corners
    /// tapped by hand, the walker's floor path, the preview during the sweep, the outline the walker
    /// corrects, and the proposal: always the room of the highest route that succeeded.
    /// <para>Engine-free. What the sensors gathered arrives through one delegate that returns a snapshot;
    /// the grid work runs on a worker thread and its result is taken in Tick. Time comes in as arguments.</para>
    /// </summary>
    public sealed class ScanFlow
    {
        /// <summary>Tracking not Normal for this long during the sweep fails the scan, seconds.</summary>
        public const float TrackingLostSeconds = 10f;
        /// <summary>With no floor found after this long the walker is asked to tap the corners, seconds.</summary>
        public const float NoFloorHintSeconds = 20f;
        /// <summary>The preview outline is recomputed this often during the sweep, seconds.</summary>
        public const float PreviewInterval = 0.5f;
        /// <summary>A path point is kept when the walker has moved this far, metres.</summary>
        public const float PathStep = 0.05f;
        /// <summary>The path holds this many points: 400 m of walking.</summary>
        public const int MaxPathPoints = 8192;

        sealed class Job
        {
            public Task<ScanSolution> Task;
            public ScanSolution Ready;
            public bool Preview;
            public ScanSnapshot Snapshot;

            public bool IsDone { get { return Ready != null || (Task != null && Task.IsCompleted); } }

            public ScanSolution Take(out string error)
            {
                error = null;
                if (Ready != null) return Ready;
                if (Task.IsFaulted)
                {
                    error = Task.Exception != null ? Task.Exception.GetBaseException().Message : "scan worker failed";
                    return new ScanSolution { Failure = ScanFailure.NoFloor };
                }
                return Task.Result;
            }
        }

        readonly Func<ScanSnapshot> _capture;
        readonly float2[] _corners = new float2[OutlineBuilder.MaxPoints];
        readonly float2[] _path = new float2[MaxPathPoints];
        int _cornerCount, _pathCount;
        float _cornerFloorY = float.NaN;

        ScanState _state;
        ScanOptions _options = ScanOptions.Default;
        ScanProgress _progress;
        string _roomId = "";
        Job _job;
        ScanSnapshot _final;              // the sweep as it ended; corrections reuse it with another outline
        float2[] _edit;                   // the outline being corrected, session frame
        bool _rerun;
        float2[] _preview = Array.Empty<float2>();
        float2 _walker;
        float _lostSeconds, _previewTimer;
        bool _floorFound;

        /// <param name="capture">Returns what the sensors hold now (mesh, planes, boxes, floor height), in
        /// the session frame; called on the caller's thread. The flow adds the path, the walker and the
        /// outline. Null, or a null return, means nothing was sensed.</param>
        /// <param name="background">False runs the grid work inside the call that starts it (tests).</param>
        public ScanFlow(Func<ScanSnapshot> capture, bool background = true)
        {
            _capture = capture;
            Background = background;
        }

        public bool Background { get; private set; }
        public ScanState State { get { return _state; } }
        public ScanFailure Failure { get; private set; }
        public ScanProgress Progress { get { return _progress; } }
        public ScanOptions Options { get { return _options; } }
        /// <summary>The id the room of this scan gets.</summary>
        public string RoomId { get { return _roomId; } }
        /// <summary>Valid in AwaitingConfirm (and while a correction is processed).</summary>
        public OutlineProposal Proposal { get; private set; }
        /// <summary>Valid in Done.</summary>
        public RoomModel Result { get; private set; }
        /// <summary>The grid of the newest preview or proposal, for a view of the floor found so far; null before the first.</summary>
        public FloorGridResult Grid { get; private set; }
        /// <summary>The text of the last worker failure, or null. It is reported, never thrown.</summary>
        public string LastError { get; private set; }
        /// <summary>RoomPlan's own instruction, set by the session; shown instead of the flow's hint during the sweep.</summary>
        public ScanHint ExternalHint { get; set; }
        /// <summary>Key frames held by the photo capture, set by its owner; reported in Progress.</summary>
        public int KeyFrames { get; set; }
        /// <summary>False when the device has no LiDAR mesh to wait for: the walker is asked for corners at once.</summary>
        public bool MeshExpected { get; set; } = true;
        public int CornerCount { get { return _cornerCount; } }

        public event Action<ScanState> StateChanged;

        // ---- driving ------------------------------------------------------------------------------------

        /// <summary>Starts a scan. Accepted in Idle, Done and Failed; ignored otherwise.</summary>
        public void Begin(in ScanOptions options, DateTime utcNow)
        {
            if (_state != ScanState.Idle && _state != ScanState.Done && _state != ScanState.Failed) return;
            _options = options;
            if (!(_options.WallHeight > 0f)) _options.WallHeight = RoomModel.DefaultWallHeight;
            _roomId = CleanId(options.RoomId, utcNow);
            _cornerCount = 0;
            _pathCount = 0;
            _cornerFloorY = float.NaN;
            _job = null;
            _final = null;
            _edit = null;
            _rerun = false;
            _preview = Array.Empty<float2>();
            _lostSeconds = 0f;
            _previewTimer = 0f;
            _floorFound = false;
            Proposal = null;
            Grid = null;
            LastError = null;
            Failure = ScanFailure.None;
            _progress = default(ScanProgress);
            Set(ScanState.Starting);
        }

        /// <summary>Per-frame work. headSession is the head in the AR session frame.</summary>
        public void Tick(float deltaTime, TrackingState tracking, float3 headSession)
        {
            if (_state == ScanState.Idle || _state == ScanState.Done || _state == ScanState.Failed) return;
            _progress.Seconds += deltaTime;
            _progress.Tracking = tracking;
            _progress.KeyFrames = KeyFrames;
            _progress.CornerCount = _cornerCount;

            switch (_state)
            {
                case ScanState.Starting:
                    if (tracking == TrackingState.Normal)
                    {
                        _walker = headSession.xz;
                        Set(ScanState.Sweeping);
                    }
                    break;

                case ScanState.Sweeping:
                    if (tracking == TrackingState.Normal)
                    {
                        _lostSeconds = 0f;
                        _walker = headSession.xz;
                        RecordPath(_walker);
                    }
                    else
                    {
                        _lostSeconds += deltaTime;
                        if (_lostSeconds >= TrackingLostSeconds)
                        {
                            Fail(ScanFailure.TrackingLost);
                            return;
                        }
                    }
                    if (_job != null && _job.IsDone) TakePreview();
                    _previewTimer += deltaTime;
                    if (_job == null && _previewTimer >= PreviewInterval)
                    {
                        _previewTimer = 0f;
                        Start(Capture(TappedOutline()), true);
                    }
                    break;

                case ScanState.Processing:
                    if (_job != null && _job.IsDone) TakeProposal();
                    else if (_job == null && _rerun) StartCorrection();
                    break;
            }
            _progress.Hint = Hint();
        }

        /// <summary>The sweep is over: the last snapshot is taken and the room is worked out. Accepted in Sweeping.</summary>
        public void EndSweep()
        {
            if (_state != ScanState.Sweeping) return;
            _final = Capture(TappedOutline());
            Set(ScanState.Processing);
            // A preview still running is left to finish and is dropped: its job is replaced here.
            Start(_final, false);
        }

        /// <summary>The walker accepts the proposal. Accepted in AwaitingConfirm; the owner then creates the anchor and calls CompleteFinalise.</summary>
        public void Confirm()
        {
            if (_state != ScanState.AwaitingConfirm || Proposal == null) return;
            Set(ScanState.Finalising);
        }

        /// <summary>The room is complete: the anchor's id (or "") and the time of confirmation are written. Accepted in Finalising.</summary>
        public void CompleteFinalise(string anchorId, DateTime utcNow)
        {
            if (_state != ScanState.Finalising) return;
            RoomModel room = Proposal.Preview;
            room.Anchor.Id = anchorId ?? "";
            room.CreatedUtc = utcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            Result = room;
            Set(ScanState.Done);
        }

        /// <summary>Any state to Idle. Failure reads Cancelled.</summary>
        public void Cancel()
        {
            if (_state == ScanState.Idle) return;
            _job = null;
            _rerun = false;
            Proposal = null;
            Failure = ScanFailure.Cancelled;
            Set(ScanState.Idle);
        }

        /// <summary>
        /// A room that needs no scan (a fixture, a saved room, a recorded room): it becomes the result and the
        /// state goes to Done. A scan in progress is dropped.
        /// </summary>
        public void UseRoom(RoomModel room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            _job = null;
            _rerun = false;
            Proposal = null;
            Failure = ScanFailure.None;
            Result = room;
            Set(ScanState.Done);
        }

        /// <summary>Ends the scan with a reason (the owner's own failures, such as a lost session).</summary>
        public void Fail(ScanFailure reason)
        {
            _job = null;
            _rerun = false;
            Failure = reason;
            Set(ScanState.Failed);
        }

        // ---- route 1: corners -----------------------------------------------------------------------------

        /// <summary>Adds a tapped corner (session frame). floorY, when known, is the height the tap hit. Accepted while Starting or Sweeping.</summary>
        public bool AddCorner(float2 floorPoint, float floorY = float.NaN)
        {
            if (_state != ScanState.Sweeping && _state != ScanState.Starting) return false;
            if (_cornerCount >= _corners.Length || !math.all(math.isfinite(floorPoint))) return false;
            if (_cornerCount > 0 && math.distance(_corners[_cornerCount - 1], floorPoint) < OutlineBuilder.MinEdge) return false;
            _corners[_cornerCount++] = floorPoint;
            if (!float.IsNaN(floorY)) _cornerFloorY = float.IsNaN(_cornerFloorY) ? floorY : math.min(_cornerFloorY, floorY);
            _progress.CornerCount = _cornerCount;
            _previewTimer = PreviewInterval;      // show it at once
            return true;
        }

        public void RemoveLastCorner()
        {
            if ((_state != ScanState.Sweeping && _state != ScanState.Starting) || _cornerCount == 0) return;
            _cornerCount--;
            _progress.CornerCount = _cornerCount;
            _previewTimer = PreviewInterval;
        }

        /// <summary>The corners tapped so far, in the order tapped (session frame). Returns how many were copied.</summary>
        public int CopyCorners(float2[] into)
        {
            int n = Math.Min(_cornerCount, into != null ? into.Length : 0);
            Array.Copy(_corners, into, n);
            return n;
        }

        /// <summary>Copies the newest preview outline (session frame). Returns the number of points copied.</summary>
        public int CopyPreviewOutline(float2[] into)
        {
            float2[] preview = _preview;
            int n = Math.Min(preview.Length, into != null ? into.Length : 0);
            Array.Copy(preview, into, n);
            return n;
        }

        /// <summary>The walker's floor path so far (session frame). Returns how many points were copied.</summary>
        public int CopyPath(float2[] into)
        {
            int n = Math.Min(_pathCount, into != null ? into.Length : 0);
            Array.Copy(_path, into, n);
            return n;
        }

        // ---- confirmation and correction ------------------------------------------------------------------

        /// <summary>Replaces the outline (session frame, at least 3 points, any winding) and recomputes.</summary>
        public void SetOutline(float2[] outline, int count)
        {
            if (!CanCorrect()) return;
            if (outline == null || count < 3 || count > outline.Length) { Failure = ScanFailure.OutlineInvalid; return; }
            var copy = new float2[count];
            Array.Copy(outline, copy, count);
            Correct(copy);
        }

        public void MoveCorner(int index, float2 floorPoint)
        {
            if (!CanCorrect()) return;
            if (index < 0 || index >= _edit.Length) { Failure = ScanFailure.OutlineInvalid; return; }
            var copy = (float2[])_edit.Clone();
            copy[index] = floorPoint;
            Correct(copy);
        }

        public void InsertCorner(int afterIndex, float2 floorPoint)
        {
            if (!CanCorrect()) return;
            if (afterIndex < 0 || afterIndex >= _edit.Length) { Failure = ScanFailure.OutlineInvalid; return; }
            var copy = new float2[_edit.Length + 1];
            Array.Copy(_edit, 0, copy, 0, afterIndex + 1);
            copy[afterIndex + 1] = floorPoint;
            Array.Copy(_edit, afterIndex + 1, copy, afterIndex + 2, _edit.Length - afterIndex - 1);
            Correct(copy);
        }

        public void RemoveCorner(int index)
        {
            if (!CanCorrect()) return;
            if (index < 0 || index >= _edit.Length || _edit.Length <= 3) { Failure = ScanFailure.OutlineInvalid; return; }
            var copy = new float2[_edit.Length - 1];
            Array.Copy(_edit, 0, copy, 0, index);
            Array.Copy(_edit, index + 1, copy, index, _edit.Length - index - 1);
            Correct(copy);
        }

        bool CanCorrect()
        {
            return Proposal != null && _edit != null && _final != null &&
                   (_state == ScanState.AwaitingConfirm || _state == ScanState.Processing);
        }

        // An invalid correction is refused: the proposal stays and Failure reads OutlineInvalid until the
        // next valid one (contract section 10.3).
        void Correct(float2[] outline)
        {
            if (!OutlineBuilder.IsValid(outline, outline.Length))
            {
                Failure = ScanFailure.OutlineInvalid;
                return;
            }
            Failure = ScanFailure.None;
            _edit = outline;
            _rerun = true;
            if (_state != ScanState.Processing) Set(ScanState.Processing);
            if (_job == null) StartCorrection();
        }

        void StartCorrection()
        {
            _rerun = false;
            ScanSnapshot last = _final;
            var corrected = new ScanSnapshot
            {
                Vertices = last.Vertices, Indices = last.Indices, Classes = last.Classes, MeshFromPlanes = last.MeshFromPlanes,
                Planes = last.Planes, Walls = last.Walls, WallTop = last.WallTop, Boxes = last.Boxes, Path = last.Path,
                Walker = last.Walker, FloorHeight = last.FloorHeight, Outline = _edit,
            };
            Start(corrected, false);
        }

        // ---- the worker -----------------------------------------------------------------------------------

        ScanSnapshot Capture(float2[] outline)
        {
            ScanSnapshot snapshot = (_capture != null ? _capture() : null) ?? new ScanSnapshot();
            if (!_options.UseMesh && !snapshot.MeshFromPlanes)
            {
                snapshot.Vertices = null;
                snapshot.Indices = null;
                snapshot.Classes = null;
            }
            if (!_options.UseBoxes) snapshot.Boxes = null;
            var path = new float2[_pathCount];
            Array.Copy(_path, path, _pathCount);
            snapshot.Path = path;
            snapshot.Walker = _walker;
            snapshot.Outline = outline;
            if (float.IsNaN(snapshot.FloorHeight)) snapshot.FloorHeight = _cornerFloorY;

            _floorFound = !float.IsNaN(snapshot.FloorHeight);
            bool scannedMesh = snapshot.TriangleCount > 0 && !snapshot.MeshFromPlanes;
            int boxes = snapshot.Boxes != null ? snapshot.Boxes.Length : 0;
            _progress.MeshTriangles = scannedMesh ? snapshot.TriangleCount : 0;
            _progress.BoxCount = boxes;
            _progress.FloorFound = _floorFound;
            _progress.RouteReached = boxes > 0 ? ScanRoute.ObjectBoxes : scannedMesh ? ScanRoute.FloorScan : ScanRoute.ManualOutline;
            return snapshot;
        }

        float2[] TappedOutline()
        {
            return _cornerCount >= 3 ? OutlineBuilder.FromCorners(_corners, _cornerCount) : null;
        }

        void Start(ScanSnapshot snapshot, bool preview)
        {
            string id = _roomId;
            float wallHeight = _options.WallHeight;
            bool decideFacing = _options.DecideFacing;
            var job = new Job { Preview = preview, Snapshot = snapshot };
            if (Background) job.Task = Task.Run(() => ScanSolver.Solve(snapshot, id, wallHeight, !preview, decideFacing));
            else job.Ready = ScanSolver.Solve(snapshot, id, wallHeight, !preview, decideFacing);
            _job = job;
        }

        void TakePreview()
        {
            Job job = _job;
            _job = null;
            if (!job.Preview) return;
            string error;
            ScanSolution solution = job.Take(out error);
            if (error != null) LastError = error;
            if (solution.Grid != null) Grid = solution.Grid;
            _preview = solution.Outline ?? Array.Empty<float2>();
            _progress.FloorArea = solution.FloorArea;
            _progress.UnknownShare = solution.UnknownShare;
            if (solution.FloorArea > 0f) _progress.FloorFound = _floorFound = true;
            _progress.PreviewVersion++;
        }

        void TakeProposal()
        {
            Job job = _job;
            _job = null;
            if (job.Preview)
            {
                // Cannot happen (EndSweep replaces the job), but a stale preview must never become a proposal.
                Start(_final, false);
                return;
            }
            if (_rerun)
            {
                // The outline moved again while this one was worked out: only the newest counts.
                StartCorrection();
                return;
            }
            string error;
            ScanSolution solution = job.Take(out error);
            if (error != null) LastError = error;
            if (solution.Failure != ScanFailure.None)
            {
                if (Proposal == null)
                {
                    Fail(solution.Failure);
                    return;
                }
                // A correction that gives no room is refused like an invalid one: the last proposal stays.
                Failure = solution.Failure;
                _edit = (float2[])Proposal.Outline.Clone();
                Set(ScanState.AwaitingConfirm);
                return;
            }
            Grid = solution.Grid;
            Proposal = new OutlineProposal
            {
                Outline = solution.Outline, Count = solution.Outline.Length, FloorY = solution.FloorY,
                Route = solution.Route, Preview = solution.Room,
            };
            _edit = (float2[])solution.Outline.Clone();
            _preview = solution.Outline;
            _progress.FloorArea = solution.FloorArea;
            _progress.UnknownShare = solution.UnknownShare;
            _progress.RouteReached = solution.Route;
            _progress.PreviewVersion++;
            Failure = ScanFailure.None;
            Set(ScanState.AwaitingConfirm);
        }

        // ---- small things -----------------------------------------------------------------------------------

        void RecordPath(float2 floorPoint)
        {
            if (_pathCount >= _path.Length || !math.all(math.isfinite(floorPoint))) return;
            if (_pathCount > 0 && math.distancesq(_path[_pathCount - 1], floorPoint) < PathStep * PathStep) return;
            _path[_pathCount++] = floorPoint;
        }

        ScanHint Hint()
        {
            if (_state != ScanState.Sweeping && _state != ScanState.Starting) return ScanHint.None;
            if (ExternalHint != ScanHint.None) return ExternalHint;
            if (!MeshExpected && _cornerCount < 3) return ScanHint.TapCorners;
            if (_floorFound || _cornerCount >= 3) return ScanHint.KeepSweeping;
            return _progress.Seconds >= NoFloorHintSeconds ? ScanHint.TapCorners : ScanHint.PointAtFloor;
        }

        void Set(ScanState state)
        {
            if (_state == state) return;
            _state = state;
            if (state != ScanState.Sweeping && state != ScanState.Starting) _progress.Hint = ScanHint.None;
            Action<ScanState> changed = StateChanged;
            if (changed != null) changed(state);
        }

        /// <summary>A room id of 1 to 40 letters, digits and underscores: the given one cleaned, or yyyyMMdd_HHmmss.</summary>
        public static string CleanId(string wanted, DateTime utcNow)
        {
            if (string.IsNullOrEmpty(wanted)) return utcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var id = new char[Math.Min(wanted.Length, 40)];
            for (int k = 0; k < id.Length; k++)
            {
                char c = wanted[k];
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                id[k] = ok ? c : '_';
            }
            return new string(id);
        }
    }
}

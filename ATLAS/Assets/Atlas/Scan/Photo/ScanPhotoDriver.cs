using Atlas.World;

namespace Atlas.Scan
{
    /// <summary>
    /// Object photo capture as the scan drives it (contract lane C section 11.4): ObjectPhotoCapture in the
    /// app, a recording stand-in in tests.
    /// </summary>
    public interface IObjectPhotoCapture
    {
        int KeyFrameCount { get; }
        /// <summary>Selection, cropping or delivery is under way.</summary>
        bool Busy { get; }
        /// <summary>The sweep starts: the ring is emptied and key frames are collected from now on.</summary>
        void BeginSweep();
        /// <summary>Once per frame during the sweep, with the raw pose and this frame's depth (may be invalid).</summary>
        void OnFrame(in HeadPose raw, in DepthFrame depth);
        /// <summary>Starts selection and cropping for a final room; the sink is called as crops finish.</summary>
        void BeginCapture(RoomModel room);
        /// <summary>Delivers finished crops, one object per call.</summary>
        void Tick();
        /// <summary>Clears the ring and drops pending crops.</summary>
        void Cancel();
    }

    /// <summary>
    /// Connects object photo capture to the states of the scan (contract lane C sections 10.2 and 11; Master
    /// Plan section 4.5, task P1-C08). Starting empties the key-frame ring; key frames are taken only while
    /// Sweeping; a room that was confirmed starts selection and cropping the moment it is final; a scan that
    /// ends any other way drops its key frames. The crops are handed to the photo sink at step 8b of the
    /// frame, one object per frame. Outside a sweep and a hand-over this costs one comparison per frame.
    /// <para>Engine-free: the scan session passes the states and the frame's sensors in.</para>
    /// </summary>
    public sealed class ScanPhotoDriver : IAfterFrameStep
    {
        readonly IObjectPhotoCapture _photos;
        bool _confirmed;        // Finalising was seen: the Done that follows carries the scanned room
        bool _delivering;       // BeginCapture ran and the sink has not had its EndRoom yet

        /// <summary>photos may be null (a session with no photo capture): every call then returns at once.</summary>
        public ScanPhotoDriver(IObjectPhotoCapture photos)
        {
            _photos = photos;
        }

        /// <summary>Key frames held now, for the scan's progress.</summary>
        public int KeyFrameCount { get { return _photos != null ? _photos.KeyFrameCount : 0; } }

        /// <summary>The crops of a confirmed room are being made or handed over.</summary>
        public bool Delivering { get { return _delivering; } }

        /// <summary>The scan changed state. result is the flow's Result: the final room once the state is Done.</summary>
        public void OnState(ScanState state, RoomModel result)
        {
            if (_photos == null) return;
            switch (state)
            {
                case ScanState.Starting:
                    // A new sweep: what an earlier scan left, crops not yet handed over included, is dropped.
                    _confirmed = false;
                    _delivering = false;
                    _photos.BeginSweep();
                    break;

                case ScanState.Finalising:
                    _confirmed = true;
                    break;

                case ScanState.Done:
                    if (_confirmed && result != null)
                    {
                        // The room is final (anchor, id, time): the sink gets BeginRoom now, the crops as they finish.
                        _confirmed = false;
                        _photos.BeginCapture(result);
                        _delivering = _photos.Busy;
                    }
                    else
                    {
                        // A room that came from a file in the middle of a scan: there is nothing to photograph.
                        Drop();
                    }
                    break;

                case ScanState.Idle:
                case ScanState.Failed:
                    // The scan was given up. A room confirmed before keeps its hand-over.
                    if (!_delivering) Drop();
                    break;
            }
        }

        /// <summary>
        /// Once per frame of the scan, from the session's Tick. A key frame is considered only while Sweeping,
        /// with this frame's depth image or none.
        /// </summary>
        public void OnFrame(ScanState state, in SensorFrame sensors)
        {
            if (state != ScanState.Sweeping || _photos == null) return;
            if (sensors.HasDepth)
            {
                _photos.OnFrame(in sensors.Head, in sensors.Depth);
                return;
            }
            // The depth image kept from an earlier frame does not belong to this frame's camera image.
            DepthFrame none = default(DepthFrame);
            _photos.OnFrame(in sensors.Head, in none);
        }

        /// <summary>Another room replaces the scanned one, or the scan is given up: key frames and pending crops go.</summary>
        public void Drop()
        {
            _confirmed = false;
            _delivering = false;
            if (_photos != null) _photos.Cancel();
        }

        /// <summary>Hands at most one object's crops to the sink; nothing while no hand-over is under way.</summary>
        public void Deliver()
        {
            if (!_delivering) return;
            _photos.Tick();
            _delivering = _photos.Busy;
        }

        /// <summary>Step 8b of the frame.</summary>
        public void AfterFrame(in FrameContext frame)
        {
            Deliver();
        }
    }
}

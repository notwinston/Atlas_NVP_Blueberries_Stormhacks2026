using System;
using System.Threading;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Scan
{
    /// <summary>Turns RGBA pixels into a JPEG. Called on a worker thread.</summary>
    public interface IPhotoEncoder
    {
        /// <summary>True when the encoder expects the last image row first.</summary>
        bool RowsBottomUp { get; }
        /// <summary>rgba holds exactly width * height * 4 bytes. Quality 1..100. Null when encoding failed.</summary>
        byte[] EncodeJpeg(byte[] rgba, int width, int height, int quality);
    }

    /// <summary>
    /// Object photo capture without the engine (Master Plan sections 4.5 and 7.4, task P1-C08; contract lane C
    /// section 11): the key-frame ring of a sweep, then for each object of the final room the choice of one to
    /// three key frames, their crops and JPEG encoding on a worker thread, and the hand-over to the photo sink
    /// on the caller's thread, one object per Tick. The key frames are dropped as soon as the crops exist;
    /// nothing is written to disk (rule 10 of section 0.5).
    /// </summary>
    public sealed class PhotoCaptureCore
    {
        /// <summary>First JPEG quality tried.</summary>
        public const int JpegQuality = 85;
        /// <summary>Quality is lowered by this much while a crop is too large.</summary>
        public const int JpegQualityStep = 10;
        public const int MinJpegQuality = 15;
        /// <summary>Largest crop handed to the sink, bytes.</summary>
        public const int MaxJpegBytes = 200 * 1000;

        static readonly WaitCallback RunJob = state => ((Job)state).Run();

        readonly KeyFrameRing _ring;
        readonly IPhotoEncoder _encoder;
        readonly NullObjectPhotoSink _nullSink = new NullObjectPhotoSink();

        Job _job;
        IObjectPhotoSink _activeSink;
        int _delivered;
        bool _sweeping, _swept;

        public PhotoCaptureCore(IPhotoEncoder encoder, int capacity = KeyFrameRing.DefaultCapacity)
        {
            if (encoder == null) throw new ArgumentNullException(nameof(encoder));
            _encoder = encoder;
            _ring = new KeyFrameRing(capacity);
        }

        /// <summary>False runs selection and cropping inside BeginCapture instead of on a worker thread (tests).</summary>
        internal bool Threaded = true;

        public KeyFrameRing Ring { get { return _ring; } }
        /// <summary>Where the photos go. Null gives a sink that only counts.</summary>
        public IObjectPhotoSink Sink { get; set; }
        public int KeyFrameCount { get { return _ring.Count; } }
        /// <summary>Between BeginSweep and BeginCapture or Cancel: key frames are being collected.</summary>
        public bool Sweeping { get { return _sweeping; } }
        /// <summary>Crops are being made or delivered.</summary>
        public bool Busy { get { return _job != null; } }

        /// <summary>Objects handed to the sink with photos in the last capture.</summary>
        public int Submitted { get; private set; }
        /// <summary>Objects reported as having no usable photo in the last capture.</summary>
        public int NoPhoto { get; private set; }
        /// <summary>Key frames the last capture chose from.</summary>
        public int FramesUsed { get; private set; }
        /// <summary>Objects of the last capture whose crops failed with an error; they are among NoPhoto.</summary>
        public int Errors { get; private set; }
        /// <summary>The first such error, or null.</summary>
        public string FirstError { get; private set; }

        /// <summary>A sweep starts: any capture in progress is dropped and the ring is emptied.</summary>
        public void BeginSweep()
        {
            Cancel();
            _sweeping = true;
            _swept = true;
        }

        /// <summary>
        /// The room is final. Its frame sits at roomOrigin, turned by roomYaw, in the frame of the key frames
        /// (RoomAnchor.Position and RoomAnchor.Yaw). Calls BeginRoom on the sink, then chooses and crops on a
        /// worker thread; Tick delivers. Does nothing when no sweep came before: a room loaded from a file
        /// has no photos and the sink is not called.
        /// </summary>
        public void BeginCapture(RoomModel room, float3 roomOrigin, float roomYaw)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (_job != null) AbandonJob();
            bool swept = _swept;
            _sweeping = false;
            _swept = false;
            if (!swept)
            {
                _ring.Clear();
                return;
            }

            RoomObject[] objects = OrderLargestFirst(room.Objects);
            var boxes = new PhotoBox[objects.Length];
            for (int k = 0; k < objects.Length; k++) boxes[k] = PhotoScore.ToCaptureFrame(objects[k], roomOrigin, roomYaw);

            Submitted = 0;
            NoPhoto = 0;
            Errors = 0;
            FirstError = null;
            FramesUsed = _ring.Count;
            _delivered = 0;
            _activeSink = Sink ?? _nullSink;
            // Every object of the room has the same light target: the room's (mesh design section 5.4).
            _job = new Job(_ring.TakeAll(), objects, boxes, _encoder, ObjectMeshRules.LightTarget(objects.Length));

            _activeSink.BeginRoom(room, RoomModelJson.Write(room));
            // The sink may have cancelled from inside its call.
            Job job = _job;
            if (job == null) return;
            if (Threaded && job.Total > 0) ThreadPool.QueueUserWorkItem(RunJob, job);
            else job.Run();
        }

        /// <summary>Once per frame: hands at most one finished object to the sink, then EndRoom after the last.</summary>
        public void Tick()
        {
            Job job = _job;
            if (job == null) return;
            if (_delivered < job.Total)
            {
                if (_delivered >= job.Completed) return;        // the worker is still on this object
                int k = _delivered++;
                ObjectPhotoSet photos = job.Results[k];
                job.Results[k] = null;
                if (photos != null)
                {
                    Submitted++;
                    _activeSink.Submit(photos);
                }
                else
                {
                    NoPhoto++;
                    _activeSink.ReportNoPhoto(job.Objects[k].Id);
                }
                if (_job != job || _delivered < job.Total) return;
            }
            Errors = job.Errors;
            FirstError = job.FirstError;
            IObjectPhotoSink sink = _activeSink;
            _job = null;
            _activeSink = null;
            sink.EndRoom();
        }

        /// <summary>Stops everything: the ring is emptied, crops not yet delivered are dropped, the sink is not called again.</summary>
        public void Cancel()
        {
            if (_job != null) AbandonJob();
            _ring.Clear();
            _sweeping = false;
            _swept = false;
        }

        void AbandonJob()
        {
            _job.Cancelled = true;
            _job = null;
            _activeSink = null;
            _delivered = 0;
        }

        // Objects that get a mesh, largest volume first; equal volumes keep the room's order.
        static RoomObject[] OrderLargestFirst(RoomObject[] all)
        {
            int n = 0;
            for (int k = 0; k < all.Length; k++) if (all[k] != null && all[k].TriTarget > 0) n++;
            var ordered = new RoomObject[n];
            n = 0;
            for (int k = 0; k < all.Length; k++)
            {
                RoomObject obj = all[k];
                if (obj == null || obj.TriTarget <= 0) continue;
                int at = n++;
                while (at > 0 && ordered[at - 1].Volume < obj.Volume)
                {
                    ordered[at] = ordered[at - 1];
                    at--;
                }
                ordered[at] = obj;
            }
            return ordered;
        }

        /// <summary>The work of one capture. Run is called once, on a worker thread; the owner reads Results[k] only for k below Completed.</summary>
        sealed class Job
        {
            public readonly RoomObject[] Objects;
            public readonly ObjectPhotoSet[] Results;
            public volatile bool Cancelled;
            public volatile int Errors;
            public volatile string FirstError;

            readonly PhotoBox[] _boxes;
            readonly IPhotoEncoder _encoder;
            readonly int _lightTarget;
            KeyFrame[] _frames;
            volatile int _completed;

            float[] _sharpness, _scores, _azimuths;
            BoxProjection[] _projections;
            readonly int[] _picked = new int[PhotoScore.MaxViews];
            readonly ObjectView[] _views = new ObjectView[PhotoScore.MaxViews];

            public Job(KeyFrame[] frames, RoomObject[] objects, PhotoBox[] boxes, IPhotoEncoder encoder, int lightTarget)
            {
                _frames = frames;
                Objects = objects;
                _boxes = boxes;
                _encoder = encoder;
                _lightTarget = lightTarget;
                Results = new ObjectPhotoSet[objects.Length];
            }

            public int Total { get { return Objects.Length; } }
            public int Completed { get { return _completed; } }

            public void Run()
            {
                try
                {
                    KeyFrame[] frames = _frames;
                    int n = frames.Length;
                    _sharpness = new float[n];
                    _scores = new float[n];
                    _azimuths = new float[n];
                    _projections = new BoxProjection[n];
                    for (int i = 0; i < n; i++)
                    {
                        // Normally measured during the sweep; a frame taken in its last moments may still be waiting.
                        if (!frames[i].SharpnessReady) frames[i].MeasureSharpness();
                        _sharpness[i] = frames[i].Sharpness;
                    }
                    float median = PhotoScore.Median(_sharpness, n, _scores);

                    for (int k = 0; k < Objects.Length && !Cancelled; k++)
                    {
                        ObjectPhotoSet photos = null;
                        try
                        {
                            photos = Shoot(k, frames, median);
                        }
                        catch (Exception e)
                        {
                            // One bad crop must not cost the others: this object takes its built-in mesh.
                            NoteError(e);
                        }
                        Results[k] = photos;
                        _completed = k + 1;
                    }
                }
                catch (Exception e)
                {
                    NoteError(e);
                }
                finally
                {
                    // The camera images are not needed any more, whatever happened.
                    _frames = null;
                    _completed = Objects.Length;
                }
            }

            void NoteError(Exception e)
            {
                if (FirstError == null) FirstError = e.GetType().Name + ": " + e.Message;
                Errors = Errors + 1;
            }

            ObjectPhotoSet Shoot(int k, KeyFrame[] frames, float median)
            {
                PhotoBox box = _boxes[k];
                int n = frames.Length;
                for (int i = 0; i < n; i++)
                {
                    KeyFrame frame = frames[i];
                    PhotoCamera camera = frame.Camera;
                    float score = PhotoScore.Evaluate(in camera, in box, _sharpness[i], median, out _projections[i]);
                    if (score > 0f && PhotoScore.IsOccluded(frame.DepthAt(_projections[i].Centre), _projections[i].FaceDepth))
                        score = 0f;
                    _scores[i] = score;
                    _azimuths[i] = score > 0f ? PhotoScore.AzimuthDeg(frame.Position, in box) : 0f;
                }

                int chosen = PhotoScore.Select(_scores, _azimuths, n, _picked);
                int made = 0;
                for (int v = 0; v < chosen && !Cancelled; v++)
                {
                    int i = _picked[v];
                    ObjectView view;
                    if (!TryMakeView(frames[i], in box, in _projections[i], _scores[i], _azimuths[i], out view)) continue;
                    _views[made++] = view;
                }
                if (made == 0 || Cancelled) return null;

                RoomObject obj = Objects[k];
                var result = new ObjectView[made];
                Array.Copy(_views, result, made);
                Array.Clear(_views, 0, _views.Length);
                return new ObjectPhotoSet
                {
                    ObjectId = obj.Id,
                    Category = obj.Category,
                    Size = obj.Size,
                    Yaw = obj.Yaw,
                    TriTarget = obj.TriTarget,
                    TriTargetLight = _lightTarget,
                    Profile = obj.Profile,
                    Views = result
                };
            }

            unsafe bool TryMakeView(KeyFrame frame, in PhotoBox box, in BoxProjection projection, float score, float azimuth,
                out ObjectView view)
            {
                view = default(ObjectView);
                CropRect crop = PhotoScore.Crop(projection.Min, projection.Max, frame.Width, frame.Height);
                int width, height;
                PhotoScore.OutputSize(in crop, out width, out height);

                var rgba = new byte[width * height * 4];
                bool bottomUp = _encoder.RowsBottomUp;
                fixed (byte* into = rgba)
                fixed (byte* plane0 = frame.Plane0)
                {
                    if (frame.Format == KeyFrameFormat.YCbCr420)
                    {
                        fixed (byte* plane1 = frame.Plane1)
                            PhotoKernel.CropYCbCr(plane0, plane1, frame.Width, frame.Height, in crop, width, height, into, bottomUp);
                    }
                    else
                    {
                        int red, green, blue;
                        KeyFrame.ChannelOffsets(frame.Format, out red, out green, out blue);
                        PhotoKernel.CropPacked(plane0, frame.Width, frame.Height, frame.BytesPerPixel, red, green, blue,
                            in crop, width, height, into, bottomUp);
                    }
                }

                byte[] jpeg = null;
                for (int quality = JpegQuality; quality >= MinJpegQuality; quality -= JpegQualityStep)
                {
                    jpeg = _encoder.EncodeJpeg(rgba, width, height, quality);
                    if (jpeg == null || jpeg.Length == 0) return false;
                    if (jpeg.Length <= MaxJpegBytes) break;
                    jpeg = null;
                }
                if (jpeg == null) return false;

                view.Jpeg = jpeg;
                view.Width = width;
                view.Height = height;
                view.Score = score;
                view.AzimuthDeg = azimuth;
                view.Intrinsics = PhotoScore.CropIntrinsics(frame.Intrinsics, in crop, width, height);
                view.CamToBox = PhotoScore.CamToBox(frame.Position, frame.Rotation, in box);
                return true;
            }
        }
    }
}

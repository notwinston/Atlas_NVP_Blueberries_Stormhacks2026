using System;
using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>
    /// A recorded hand log played back as the detector (documents/ATLAS_Hands_Design.md section 4.5): the headless
    /// tests of the lift, the filter, touch and the mask run on it, and so can the Editor, from a file of /recordings.
    /// Each record is handed out once, when the time passed to <see cref="TryRead"/> reaches the record's time,
    /// counted from the first read. When the frame is slower than the log, the records that fell between two reads
    /// are skipped and the newest is given, as the phone's detector keeps only its newest result; a clock that goes
    /// back gives nothing until it passes what was given. The record comes as it was written, the detector's own
    /// clock and sequence included; in a looping replay those two are carried on from lap to lap so that they keep
    /// rising. Engine-free and allocation-free.
    /// </summary>
    public sealed class ReplayHandsSource : IHandsSource
    {
        // Seconds. The clock is a sum of frame times and the log's times are k / 30: a record must not be missed by a rounding error.
        const double ClockSlack = 1e-6;
        /// <summary>The shortest lap of a loop, seconds: a log of one record, or of records all at one time, plays this often.</summary>
        const double ShortestLapSeconds = 1.0 / 30.0;

        readonly HandsLogReader _log;
        readonly bool _loop;
        readonly double _first;         // the time of record 0
        readonly double _lapSeconds;    // from a record to the same record of the next lap
        readonly int _lapSequences;     // how far the sequence rises in a lap

        double _start;
        bool _started;
        int _deliveredLap;
        int _deliveredIndex = -1;
        bool _idle;
        bool _stopped;

        /// <summary>loop plays the log again when it has been played to its end.</summary>
        public ReplayHandsSource(HandsLogReader log, bool loop = false)
        {
            if (log == null) throw new ArgumentNullException(nameof(log));
            _log = log;
            _loop = loop;
            _lapSeconds = ShortestLapSeconds;
            _lapSequences = 1;
            if (log.Count == 0) return;

            _first = log.Time(0);
            if (log.Count > 1)
            {
                // A lap is the log and one more gap, so that the cadence has no hiccup where the log starts again.
                double duration = log.Time(log.Count - 1) - _first;
                _lapSeconds = Math.Max(duration + duration / (log.Count - 1), ShortestLapSeconds);
            }
            HandsDetection scratch = default(HandsDetection);
            log.Read(0, ref scratch, out float3 _, out quaternion _);
            int firstSequence = scratch.Sequence;
            log.Read(log.Count - 1, ref scratch, out float3 _, out quaternion _);
            _lapSequences = Math.Max(scratch.Sequence - firstSequence + 1, 1);
        }

        /// <summary>Reads a hand-log file. Throws what HandsLogReader.FromFile throws.</summary>
        public static ReplayHandsSource FromFile(string path, bool loop = false)
        {
            return new ReplayHandsSource(HandsLogReader.FromFile(path), loop);
        }

        /// <summary>Always: there is a log to play.</summary>
        public bool Available { get { return true; } }
        /// <summary>"replay".</summary>
        public string Name { get { return "replay"; } }
        /// <summary>A replay does not fail: always 0.</summary>
        public int Fault { get { return 0; } }

        /// <summary>The log being played.</summary>
        public HandsLogReader Log { get { return _log; } }
        /// <summary>The log starts again when it has been played to its end.</summary>
        public bool Loop { get { return _loop; } }
        /// <summary>The log has been played to its last record: TryRead gives nothing until <see cref="Restart"/>. Never true for a loop.</summary>
        public bool Ended { get; private set; }
        /// <summary>Index of the record last handed out, within its lap; -1 before the first.</summary>
        public int Index { get; private set; } = -1;
        /// <summary>The lap of the record last handed out: how many times a looping replay has started again.</summary>
        public int LoopCount { get; private set; }
        /// <summary>The head pose recorded with the record last handed out: a replay in the Editor may drive the head with it.</summary>
        public float3 HeadPosition { get; private set; }
        /// <summary>The head rotation recorded with the record last handed out.</summary>
        public quaternion HeadRotation { get; private set; } = quaternion.identity;

        /// <summary>The next <see cref="TryRead"/> starts the log again from its first record.</summary>
        public void Restart()
        {
            _started = false;
            _deliveredLap = 0;
            _deliveredIndex = -1;
            Ended = false;
            Index = -1;
            LoopCount = 0;
        }

        /// <summary>
        /// A replay plays the log at the pace it was recorded, whatever the rate asked for. 0 idles it: nothing
        /// is handed out, and the clock goes on, so that it carries on with the newest record that is due.
        /// Any other rate lets it run, and starts it from the first record when it was stopped.
        /// </summary>
        public void SetRate(int hz)
        {
            if (hz <= 0)
            {
                _idle = true;
                return;
            }
            if (_stopped)
            {
                _stopped = false;
                Restart();
            }
            _idle = false;
        }

        /// <summary>Stops the replay and rewinds it: nothing is handed out until a rate is asked for.</summary>
        public void Stop()
        {
            _stopped = true;
            Restart();
        }

        public bool TryRead(double time, ref HandsDetection detection)
        {
            if (_stopped || _idle || Ended) return false;
            int count = _log.Count;
            if (count == 0)
            {
                Ended = true;
                return false;
            }
            if (!_started)
            {
                _started = true;
                _start = time;
            }

            double local = time - _start;
            if (local < 0.0) local = 0.0;
            int lap = 0;
            if (_loop)
            {
                lap = (int)Math.Floor((local + ClockSlack) / _lapSeconds);
                local -= lap * _lapSeconds;
                if (local < 0.0) local = 0.0;
            }
            int index = Newest(_first + local + ClockSlack, count);
            if (lap < _deliveredLap || (lap == _deliveredLap && index <= _deliveredIndex)) return false;

            Deliver(lap, index, ref detection);
            if (!_loop && index == count - 1) Ended = true;
            return true;
        }

        /// <summary>The newest record at or before t. Record 0 when t is before the log starts.</summary>
        int Newest(double t, int count)
        {
            int low = 0, high = count - 1;
            while (low < high)
            {
                int middle = (low + high + 1) >> 1;
                if (_log.Time(middle) <= t) low = middle;
                else high = middle - 1;
            }
            return low;
        }

        void Deliver(int lap, int index, ref HandsDetection detection)
        {
            _log.Read(index, ref detection, out float3 position, out quaternion rotation);
            if (lap != 0)
            {
                double shift = lap * _lapSeconds;
                detection.Sequence += lap * _lapSequences;
                detection.CaptureTime += shift;
                detection.PollTime += shift;
            }
            HeadPosition = position;
            HeadRotation = rotation;
            Index = index;
            LoopCount = lap;
            _deliveredLap = lap;
            _deliveredIndex = index;
        }
    }
}

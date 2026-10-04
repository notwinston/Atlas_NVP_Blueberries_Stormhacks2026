using System;
using System.Collections.Generic;

namespace Atlas.Blades.FieldFit.BlindSpotFinder
{
    public enum BlindSpotPhase { Idle, Running, Done, Aborted }
    public enum ProbeKind { Grid, Reprobe, Catch }

    /// <summary>Stimulus timing, seconds (deviation D2, D3).</summary>
    public static class BlindSpotTiming
    {
        public const double LeadInS = 1.0, DotDurationS = 0.2, MinResponseS = 0.1, ResponseWindowS = 1.5;
        public const double IsiMinS = 0.5, IsiSpanS = 0.5;
        public const int CatchEvery = 5;   // every 5th trial after found is a catch trial (D3)
    }

    public readonly struct ProbeRecord
    {
        public int Index { get; }
        public ProbeKind Kind { get; }
        public GridPoint Point { get; }
        public double OnsetS { get; }
        public double EndS { get; }
        public bool Seen { get; }
        public double ResponseS { get; }   // tap - onset; -1 if not seen

        public ProbeRecord(int index, ProbeKind kind, GridPoint point, double onsetS, double endS, bool seen, double responseS)
        { Index = index; Kind = kind; Point = point; OnsetS = onsetS; EndS = endS; Seen = seen; ResponseS = responseS; }
    }

    /// <summary>Master Plan §10.4.7 measures: blind-spot centre, time to find, catch-trial false positives.</summary>
    public readonly struct BlindSpotResult
    {
        public EyeSide Eye { get; }
        public uint Seed { get; }
        public bool Found { get; }
        public bool Aborted { get; }
        public double CentreAzRad { get; }
        public double CentreElRad { get; }
        public double RadiusRad { get; }     // 0 when !Found
        public double TimeToFindS { get; }   // -1 when !Found
        public GridPoint FoundPoint { get; }
        public int MissedCount { get; }
        public int CatchTrials { get; }
        public int CatchFalsePositives { get; }
        public int Trials { get; }
        public double DurationS { get; }

        public BlindSpotResult(EyeSide eye, uint seed, bool found, bool aborted, double centreAzRad, double centreElRad,
            double radiusRad, double timeToFindS, GridPoint foundPoint, int missedCount, int catchTrials,
            int catchFalsePositives, int trials, double durationS)
        {
            Eye = eye; Seed = seed; Found = found; Aborted = aborted; CentreAzRad = centreAzRad; CentreElRad = centreElRad;
            RadiusRad = radiusRad; TimeToFindS = timeToFindS; FoundPoint = foundPoint; MissedCount = missedCount;
            CatchTrials = catchTrials; CatchFalsePositives = catchFalsePositives; Trials = trials; DurationS = durationS;
        }
    }

    /// <summary>Pure C# blind-spot search with catch trials (Brief §3.4). Times are seconds relative to Begin.</summary>
    public sealed class BlindSpotProcedure
    {
        public EyeSide Eye { get; }
        public uint Seed { get; }
        public BlindSpotPhase Phase { get; private set; } = BlindSpotPhase.Idle;
        public bool InTrial { get; private set; }
        public GridPoint CurrentPoint { get; private set; }
        public double CurrentOnsetS { get; private set; }
        public IReadOnlyList<ProbeRecord> Probes => _probes;
        public IReadOnlyList<GridPoint> ConfirmedMisses => _misses;

        public bool DotVisible => Phase == BlindSpotPhase.Running && InTrial && _lastRel < CurrentOnsetS + BlindSpotTiming.DotDurationS;

        public event Action<ProbeRecord> ProbeCompleted;
        public event Action<BlindSpotResult> SpotFound, Finished;

        readonly List<ProbeRecord> _probes = new List<ProbeRecord>(128);
        readonly List<GridPoint> _misses = new List<GridPoint>(16);
        readonly List<GridPoint> _queue;
        readonly SplitMix64 _isi;
        int _qi;
        double _t0, _lastRel, _nextOnset, _duration;
        ProbeKind _curKind, _nextKind;
        GridPoint _nextPoint;
        bool _hasPending; GridPoint _pending;
        bool _found; GridPoint _foundPoint; double _tFind;
        int _sinceCatch, _catchTrials, _catchFp;

        public BlindSpotProcedure(EyeSide eye, uint seed)
        {
            Eye = eye; Seed = seed;
            _queue = BlindSpotGrid.PresentationOrder(eye, seed);
            _isi = new SplitMix64((ulong)seed ^ 0x9E3779B97F4A7C15UL);
        }

        public BlindSpotResult Result
        {
            get
            {
                int n = _misses.Count;
                double az = 0, el = 0, rad = 0;
                if (n > 0)
                {
                    foreach (var p in _misses) { az += p.AzRad; el += p.ElRad; }
                    az /= n; el /= n;
                    double step = BlindSpotGrid.StepDeg * Math.PI / 180.0;
                    rad = Math.Sqrt(n * step * step / Math.PI);   // equivalent-area radius (D4)
                }
                bool found = n > 0;
                return new BlindSpotResult(Eye, Seed, found, Phase == BlindSpotPhase.Aborted, az, el, rad,
                    found ? _tFind : -1.0, _foundPoint, n, _catchTrials, _catchFp, _probes.Count, _duration);
            }
        }

        public void Begin(double nowS)
        {
            _t0 = nowS; _lastRel = 0;
            Phase = BlindSpotPhase.Running;
            _nextOnset = BlindSpotTiming.LeadInS;
            _nextKind = ProbeKind.Grid; _nextPoint = _queue[0]; _qi = 1;
            InTrial = false;
        }

        public void Advance(double nowS)
        {
            if (Phase != BlindSpotPhase.Running) return;
            double rel = nowS - _t0;
            if (rel > _lastRel) _lastRel = rel; else rel = _lastRel;
            while (Phase == BlindSpotPhase.Running)
            {
                if (!InTrial && rel >= _nextOnset)
                {
                    InTrial = true; _curKind = _nextKind; CurrentPoint = _nextPoint; CurrentOnsetS = _nextOnset;
                }
                else if (InTrial && rel >= CurrentOnsetS + BlindSpotTiming.ResponseWindowS)
                    EndTrial(false, CurrentOnsetS + BlindSpotTiming.ResponseWindowS, -1);
                else break;
            }
        }

        public void Tap(double nowS)
        {
            if (Phase != BlindSpotPhase.Running) return;
            Advance(nowS);
            if (Phase != BlindSpotPhase.Running || !InTrial) return;
            double rel = nowS - _t0;
            double rt = rel - CurrentOnsetS;
            if (rt >= BlindSpotTiming.MinResponseS && rt <= BlindSpotTiming.ResponseWindowS)
                EndTrial(true, rel, rt);
            // otherwise: anticipation or ISI, ignored
        }

        public void Abort(double nowS)
        {
            if (Phase != BlindSpotPhase.Running) return;
            Phase = BlindSpotPhase.Aborted;
            InTrial = false;
            _duration = nowS - _t0;
            Finished?.Invoke(Result);
        }

        void EndTrial(bool seen, double end, double rt)
        {
            var rec = new ProbeRecord(_probes.Count, _curKind, CurrentPoint, CurrentOnsetS, end, seen, seen ? rt : -1.0);
            _probes.Add(rec);
            InTrial = false;
            ProbeCompleted?.Invoke(rec);

            bool confirming = false;
            if (_curKind == ProbeKind.Grid && !seen) { _hasPending = true; _pending = CurrentPoint; }
            else if (_curKind == ProbeKind.Reprobe && !seen)
            {
                _misses.Add(CurrentPoint);
                if (!_found)
                {
                    _found = true; _foundPoint = CurrentPoint; _tFind = end; _sinceCatch = 0; confirming = true;
                    SpotFound?.Invoke(Result);
                }
            }
            else if (_curKind == ProbeKind.Catch)
            {
                _catchTrials++;
                if (seen) _catchFp++;
                _sinceCatch = 0;
            }
            if (_curKind != ProbeKind.Catch && _found && !confirming) _sinceCatch++;

            bool more = true;
            if (_hasPending) { _nextKind = ProbeKind.Reprobe; _nextPoint = _pending; _hasPending = false; }
            else if (_found && _sinceCatch >= BlindSpotTiming.CatchEvery - 1) { _nextKind = ProbeKind.Catch; _nextPoint = _foundPoint; }
            else if (_qi < _queue.Count) { _nextKind = ProbeKind.Grid; _nextPoint = _queue[_qi++]; }
            else more = false;

            if (more) _nextOnset = end + BlindSpotTiming.IsiMinS + BlindSpotTiming.IsiSpanS * _isi.NextDouble();
            else { Phase = BlindSpotPhase.Done; _duration = end; Finished?.Invoke(Result); }
        }
    }
}

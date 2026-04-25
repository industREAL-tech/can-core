using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace industREAL.CAN.CORE
{
    public sealed class CanDispatcher : IAsyncDisposable, IDisposable
    {
        // ----- incoming routing -----
        private readonly Dictionary<uint, Action<CanFrame>> _exact = new();
        private readonly List<(uint Match, uint Mask, Action<CanFrame> Handler)> _masked = new();

        public void RegisterExact(uint id, Action<CanFrame> handler) => _exact[id] = handler;
        public void RegisterMasked(uint match, uint mask, Action<CanFrame> handler) => _masked.Add((match, mask, handler));

        public void Route(CanFrame f)
        {
            try
            {
                if (_exact.TryGetValue(f._canID, out var h)) { h(f); return; }
                var id = f._canID;
                foreach (var e in _masked)
                    if ((id & e.Mask) == e.Match) { e.Handler(f); return; }
            }
            catch (Exception ex)
            {
                Log.Debug("CAN Route EXCEPTION", ex.Message);
            }
        }

        // ----- outgoing periodic scheduler -----
        private readonly Action<CanFrame> _enqueue;   // e.g., canGateway.addToMultipleFrame
        private readonly Action _flush;               // e.g., canGateway.FlushMultipleFrames

        private sealed class Job
        {
            public Func<CanFrame?> Producer = null!;
            public long PeriodTicks;      // stopwatch ticks
            public long NextDueTicks;     // next run time in ticks
            public bool Enabled = true;
        }

        private readonly List<Job> _jobs = new();
        private readonly object _jobsLock = new();

        private Thread? _thread;
        private CancellationTokenSource? _cts;
        private readonly ManualResetEventSlim _wake = new(false); // wake loop when jobs/cancel arrive
        private volatile bool _running;
        private long _nowTicks;

        public CanDispatcher(Action<CanFrame> enqueue, Action flush)
        {
            _enqueue = enqueue;
            _flush = flush;
        }

        /// Register a periodic producer. periodMs >= 1. Returns IDisposable to stop it.
        public IDisposable RegisterPeriodicOutput(int periodMs, Func<CanFrame?> producer)
        {
            if (periodMs < 1) throw new ArgumentOutOfRangeException(nameof(periodMs));

            var periodTicks = (long)Math.Max(1, periodMs / 1000.0 * Stopwatch.Frequency);
            var job = new Job { Producer = producer, PeriodTicks = periodTicks };

            lock (_jobsLock)
            {
                EnsureThread_NoLock();
                job.NextDueTicks = _nowTicks + job.PeriodTicks;
                _jobs.Add(job);
            }

            _wake.Set(); // new job may change next wake-up
            return new JobHandle(this, job);
        }

        /// Convenience: producer that always emits the same frame instance (copied by CAN layer).
        public IDisposable RegisterPeriodicOutput(int periodMs, CanFrame constantFrame)
            => RegisterPeriodicOutput(periodMs, () => constantFrame);

        private void EnsureThread_NoLock()
        {
            if (_thread != null) return;
            _cts = new CancellationTokenSource();
            _running = true;

            _thread = new Thread(() => Loop(_cts.Token))
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal, // tune for your needs
                Name = "CanDispatcher"
            };
            _thread.Start();
        }

        private void Loop(CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            _nowTicks = sw.ElapsedTicks;

            while (_running && !token.IsCancellationRequested)
            {
                _wake.Reset();
                _nowTicks = sw.ElapsedTicks;

                // 1) collect due jobs and compute nextDue
                List<Job>? due = null;
                long nextDue = long.MaxValue;

                lock (_jobsLock)
                {
                    for (int i = 0; i < _jobs.Count; i++)
                    {
                        var j = _jobs[i];
                        if (!j.Enabled) continue;

                        if (_nowTicks >= j.NextDueTicks)
                        {
                            // schedule next run: catch up if we fell behind
                            var missed = (_nowTicks - j.NextDueTicks) / j.PeriodTicks + 1;
                            j.NextDueTicks += missed * j.PeriodTicks;

                            (due ??= new List<Job>(4)).Add(j);
                        }

                        if (j.NextDueTicks < nextDue) nextDue = j.NextDueTicks;
                    }
                }

                // 2) run due producers outside lock; flush once
                bool any = false;
                if (due != null)
                {
                    foreach (var j in due)
                    {
                        try
                        {
                            var frame = j.Producer();
                            if (frame is not null)
                            {
                                _enqueue(frame);
                                any = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Debug("CAN Producer exception", ex.Message);
                        }
                    }
                    if (any)
                    {
                        try { _flush(); } catch (Exception ex) { Log.Debug("CAN Flush exception", ex.Message); }
                    }
                }

                // 3) wait until nextDue or wake early if jobs changed/cancelled
                _nowTicks = sw.ElapsedTicks;

                if (nextDue == long.MaxValue)
                {
                    // no jobs -> sleep until something arrives
                    _wake.Wait(TimeSpan.FromMilliseconds(50)); // idle backoff
                    continue;
                }

                var waitTicks = nextDue - _nowTicks;
                if (waitTicks <= 0)
                {
                    // immediately loop (we’re due again / very close)
                    Thread.Yield();
                    continue;
                }

                // convert to ms, cap small waits to short timed waits for responsiveness
                var waitMs = (int)Math.Min(5, Math.Max(0, waitTicks * 1000.0 / Stopwatch.Frequency));
                if (waitMs > 0)
                    _wake.Wait(waitMs);
                else
                    Thread.Yield(); // sub-ms granularity—yield is good enough
            }
        }

        private sealed class JobHandle : IDisposable
        {
            private readonly CanDispatcher _owner;
            private Job? _job;
            public JobHandle(CanDispatcher owner, Job job) { _owner = owner; _job = job; }
            public void Dispose()
            {
                var j = Interlocked.Exchange(ref _job, null);
                if (j is null) return;

                lock (_owner._jobsLock)
                {
                    j.Enabled = false;
                    _owner._jobs.Remove(j);
                    if (_owner._jobs.Count == 0)
                        _owner.Stop_NoLock();
                }

                _owner._wake.Set();
            }
        }

        private void Stop_NoLock()
        {
            _running = false;
            try { _cts?.Cancel(); } catch { }
            _wake.Set();

            try { _thread?.Join(); } catch { }
            _cts?.Dispose();
            _thread = null;
            _cts = null;
        }

        public void Dispose() => Stop_NoLock();
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

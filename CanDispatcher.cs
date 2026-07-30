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
        //
        // Guarded by _routesLock. Route() used to read the dictionary and iterate
        // the list with no synchronisation at all while Register* mutated them
        // from arbitrary threads, so a registration concurrent with a frame could
        // throw InvalidOperationException or corrupt the dictionary. The tables
        // are swapped copy-on-write so Route() never holds a lock while calling
        // into subscriber code.
        private readonly object _routesLock = new();
        private Dictionary<uint, List<Action<CanFrame>>> _exact = new();
        private List<(uint Match, uint Mask, Action<CanFrame> Handler)> _masked = new();

        /// <summary>
        /// Registers a handler for an exact identifier. Multiple handlers may be
        /// registered for the same id and all of them are invoked.
        /// </summary>
        /// <remarks>
        /// This used to be <c>_exact[id] = handler</c>, which silently replaced
        /// any previously registered handler for that id - so two devices sharing
        /// an id, or a second subscriber added later, quietly unsubscribed the
        /// first with no diagnostic.
        /// </remarks>
        public void RegisterExact(uint id, Action<CanFrame> handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            lock (_routesLock)
            {
                var next = new Dictionary<uint, List<Action<CanFrame>>>(_exact);
                next[id] = next.TryGetValue(id, out var existing)
                    ? new List<Action<CanFrame>>(existing) { handler }
                    : new List<Action<CanFrame>> { handler };
                _exact = next;
            }
        }

        public void RegisterMasked(uint match, uint mask, Action<CanFrame> handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            lock (_routesLock)
            {
                _masked = new List<(uint, uint, Action<CanFrame>)>(_masked) { (match, mask, handler) };
            }
        }

        /// <summary>
        /// Delivers a frame to every matching handler.
        /// </summary>
        /// <remarks>
        /// Both the exact and the masked lookup used to <c>return</c> after the
        /// first match, so fan-out was impossible by construction: a frame could
        /// never reach both an exact subscriber and a masked one, nor two masked
        /// ones. All matches are now invoked, and one throwing handler no longer
        /// prevents the rest from seeing the frame.
        /// </remarks>
        public void Route(CanFrame f)
        {
            if (f is null) return;

            Dictionary<uint, List<Action<CanFrame>>> exact;
            List<(uint Match, uint Mask, Action<CanFrame> Handler)> masked;
            lock (_routesLock) { exact = _exact; masked = _masked; }

            if (exact.TryGetValue(f._canID, out var handlers))
            {
                for (int i = 0; i < handlers.Count; i++) Invoke(handlers[i], f);
            }

            var id = f._canID;
            for (int i = 0; i < masked.Count; i++)
            {
                if ((id & masked[i].Mask) == masked[i].Match) Invoke(masked[i].Handler, f);
            }
        }

        private static void Invoke(Action<CanFrame> handler, CanFrame f)
        {
            try
            {
                handler(f);
            }
            catch (Exception ex)
            {
                // The exception was previously passed as a FORMAT ARGUMENT to a
                // message with no placeholder, so a sink doing
                // string.Format(message, args) discarded it and the actual error
                // text was lost.
                Log.Error(ex, "CAN route handler threw");
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
            var cts = new CancellationTokenSource();
            _cts = cts;
            _running = true;

            // Capture the token in a local. The lambda used to read the _cts FIELD,
            // which is evaluated when the thread first runs - so a Dispose() that
            // nulled _cts before the thread got going produced a
            // NullReferenceException on an unhandled background thread, taking the
            // process down.
            var token = cts.Token;
            _thread = new Thread(() => Loop(token))
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal, // tune for your needs
                Name = "CanDispatcher"
            };
            _thread.Start();
        }

        private void Loop(CancellationToken token)
        {
            // This runs on a dedicated background thread, so anything escaping here
            // is an unhandled exception that terminates the whole process. Contain it.
            try
            {
                LoopCore(token);
            }
            catch (ObjectDisposedException)
            {
                // The dispatcher was disposed underneath us; nothing left to do.
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "CAN dispatcher scheduler thread terminated");
            }
        }

        private void LoopCore(CancellationToken token)
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

                bool stop;
                lock (_owner._jobsLock)
                {
                    j.Enabled = false;
                    _owner._jobs.Remove(j);
                    stop = _owner._jobs.Count == 0;
                }

                _owner._wake.Set();

                // Deadlock fix: this used to call Stop_NoLock() - which Joins the
                // dispatcher thread - while still holding _jobsLock. The loop
                // takes _jobsLock on every iteration, so if it happened to be
                // waiting for the lock, Join() waited for the loop and the loop
                // waited for us. Stop outside the lock.
                if (stop) _owner.StopThread();
            }
        }

        /// <summary>
        /// Stops the scheduler thread. Must NOT be called while holding
        /// <c>_jobsLock</c>: it joins a thread that acquires that lock.
        /// </summary>
        private bool StopThread()
        {
            Thread? thread;
            CancellationTokenSource? tokenSource;

            lock (_jobsLock)
            {
                thread = _thread;
                tokenSource = _cts;
                _thread = null;
                _cts = null;
                _running = false;
            }

            if (thread is null) return true;

            try { tokenSource?.Cancel(); } catch { }
            _wake.Set();

            // Never join ourselves: a producer callback disposing the dispatcher
            // runs on this very thread.
            bool exited = true;
            if (thread != Thread.CurrentThread)
            {
                try { exited = thread.Join(TimeSpan.FromSeconds(2)); } catch { exited = false; }
            }
            else
            {
                exited = false;   // still on the stack
            }

            tokenSource?.Dispose();
            return exited;
        }

        public void Dispose()
        {
            // Only dispose the wake handle once the loop is provably gone: the loop
            // waits on it every iteration, and disposing it underneath a still-running
            // thread would throw ObjectDisposedException on a background thread.
            if (StopThread())
            {
                _wake.Dispose();
            }
        }

        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

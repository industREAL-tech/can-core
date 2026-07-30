using System.Collections.Concurrent;
using industREAL.CAN.CORE;
using Xunit;

namespace industREAL.CAN.CORE.Tests;

public class CanDispatcherTests
{
    private static CanDispatcher NewDispatcher(
        ConcurrentQueue<CanFrame>? sent = null, Action? onFlush = null)
        => new CanDispatcher(f => sent?.Enqueue(f), () => onFlush?.Invoke());

    private static CanFrame Frame(uint id) => new CanFrame(CanDLC.DLC_1) { _canID = id };

    [Fact]
    public void RegisterExact_DeliversToTheRegisteredId()
    {
        using var d = NewDispatcher();
        uint seen = 0;
        d.RegisterExact(0x123, f => seen = f._canID);

        d.Route(Frame(0x123));
        Assert.Equal(0x123u, seen);
    }

    [Fact]
    public void RegisterExact_DoesNotDeliverToOtherIds()
    {
        using var d = NewDispatcher();
        int calls = 0;
        d.RegisterExact(0x123, _ => calls++);

        d.Route(Frame(0x124));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void RegisterExact_SecondHandlerDoesNotEvictTheFirst()
    {
        // Regression: `_exact[id] = handler` silently replaced any prior handler,
        // so a second subscriber unsubscribed the first with no diagnostic.
        using var d = NewDispatcher();
        int first = 0, second = 0;
        d.RegisterExact(0x200, _ => first++);
        d.RegisterExact(0x200, _ => second++);

        d.Route(Frame(0x200));

        Assert.Equal(1, first);
        Assert.Equal(1, second);
    }

    [Fact]
    public void ExactAndMaskedSubscribersBothSeeTheSameFrame()
    {
        // Regression: both lookups returned after the first match, so fan-out was
        // impossible - a frame could never reach an exact and a masked subscriber.
        using var d = NewDispatcher();
        int exact = 0, masked = 0;
        d.RegisterExact(0x40106, _ => exact++);
        d.RegisterMasked(0x40100, 0xFFFFFFF0, _ => masked++);

        d.Route(Frame(0x40106));

        Assert.Equal(1, exact);
        Assert.Equal(1, masked);
    }

    [Fact]
    public void EveryMatchingMaskedSubscriberIsInvoked()
    {
        using var d = NewDispatcher();
        var hits = new List<int>();
        d.RegisterMasked(0x40100, 0xFFFFFFF0, _ => hits.Add(1));
        d.RegisterMasked(0x40000, 0xFFFF0000, _ => hits.Add(2));

        d.Route(Frame(0x40106));

        Assert.Equal(new[] { 1, 2 }, hits);
    }

    [Fact]
    public void AThrowingHandlerDoesNotPreventTheOthersFromSeeingTheFrame()
    {
        using var d = NewDispatcher();
        bool later = false;
        d.RegisterExact(0x300, _ => throw new InvalidOperationException("boom"));
        d.RegisterExact(0x300, _ => later = true);

        d.Route(Frame(0x300));   // must not propagate

        Assert.True(later);
    }

    [Fact]
    public void RouteToleratesNull()
    {
        using var d = NewDispatcher();
        d.RegisterExact(1, _ => { });
        d.Route(null!);
    }

    [Fact]
    public void RegisterRejectsNullHandlers()
    {
        using var d = NewDispatcher();
        Assert.Throws<ArgumentNullException>(() => d.RegisterExact(1, null!));
        Assert.Throws<ArgumentNullException>(() => d.RegisterMasked(1, 1, null!));
    }

    [Fact]
    public void ConcurrentRegistrationDoesNotDisruptRouting()
    {
        // Regression: Route() read the dictionary and iterated the list with no
        // synchronisation while Register* mutated them, which could throw
        // InvalidOperationException or corrupt the dictionary.
        using var d = NewDispatcher();
        int delivered = 0;
        d.RegisterExact(0x555, _ => Interlocked.Increment(ref delivered));

        // Registration is copy-on-write, so it is O(table size); keep the count
        // bounded rather than spinning, which would make this quadratic.
        const int registrations = 500;
        const int routes = 20_000;

        var registrar = Task.Run(() =>
        {
            uint id = 0x1000;
            for (int i = 0; i < registrations; i++)
            {
                d.RegisterExact(id++, _ => { });
                d.RegisterMasked(id, 0xFFFFFF00, _ => { });
                Thread.Yield();
            }
        });

        for (int i = 0; i < routes; i++) d.Route(Frame(0x555));

        Assert.True(registrar.Wait(TimeSpan.FromSeconds(30)), "registrar did not finish");
        Assert.Equal(routes, delivered);
    }

    [Fact]
    public void PeriodicProducerIsCalledAndItsFrameIsEnqueuedAndFlushed()
    {
        var sent = new ConcurrentQueue<CanFrame>();
        int flushes = 0;
        using var d = NewDispatcher(sent, () => Interlocked.Increment(ref flushes));

        using (d.RegisterPeriodicOutput(5, () => Frame(0x777)))
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (sent.Count < 3 && DateTime.UtcNow < deadline) Thread.Sleep(5);
        }

        Assert.True(sent.Count >= 3, $"expected >=3 produced frames, got {sent.Count}");
        Assert.True(flushes >= 1, $"expected at least one flush, got {flushes}");
        Assert.All(sent, f => Assert.Equal(0x777u, f._canID));
    }

    [Fact]
    public void AProducerReturningNullEnqueuesNothing()
    {
        var sent = new ConcurrentQueue<CanFrame>();
        int flushes = 0;
        using var d = NewDispatcher(sent, () => Interlocked.Increment(ref flushes));

        using (d.RegisterPeriodicOutput(5, () => null))
        {
            Thread.Sleep(120);
        }

        Assert.Empty(sent);
        Assert.Equal(0, flushes);
    }

    [Fact]
    public void DisposingTheLastJobHandleDoesNotDeadlock()
    {
        // Regression: JobHandle.Dispose() held _jobsLock and then called
        // Stop_NoLock(), which Joins the scheduler thread - and the scheduler
        // takes _jobsLock every iteration. Whoever got there first waited for the
        // other forever.
        var completed = Task.Run(() =>
        {
            var d = NewDispatcher();
            var handle = d.RegisterPeriodicOutput(1, () => Frame(0x888));
            Thread.Sleep(50);          // let the loop start contending for the lock
            handle.Dispose();          // used to hang here
            d.Dispose();
        });

        Assert.True(completed.Wait(TimeSpan.FromSeconds(10)),
                    "disposing the last periodic job deadlocked");
    }

    [Fact]
    public void DisposeIsSafeWithJobsStillRegistered()
    {
        var d = NewDispatcher();
        d.RegisterPeriodicOutput(5, () => Frame(0x999));
        Thread.Sleep(30);

        var completed = Task.Run(() => d.Dispose());
        Assert.True(completed.Wait(TimeSpan.FromSeconds(10)), "Dispose() hung");
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var d = NewDispatcher();
        d.RegisterPeriodicOutput(5, () => Frame(0x111)).Dispose();
        d.Dispose();
        d.Dispose();
    }

    [Fact]
    public void RegisterPeriodicOutputRejectsSubMillisecondPeriods()
    {
        using var d = NewDispatcher();
        Assert.Throws<ArgumentOutOfRangeException>(() => d.RegisterPeriodicOutput(0, () => null));
    }
}

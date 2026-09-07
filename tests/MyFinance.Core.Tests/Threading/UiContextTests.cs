using System.Collections.Concurrent;
using MyFinance.Core.Threading;

namespace MyFinance.Core.Tests.Threading;

/// <summary>
/// The regression tests for a cross-thread collection write.
/// </summary>
/// <remarks>
/// The fault these pin down was found by the diagnostics log, not by a test: a page rebuilt
/// its bound collection after awaiting background work, the resumption landed on a pool
/// thread, and WPF threw where nobody was listening. The mechanism is reproduced here without
/// WPF — work finished on a pool thread, awaited with no ambient context — because that is the
/// part that can be tested on any machine.
/// </remarks>
public sealed class UiContextTests
{
    [Fact]
    public async Task Work_that_finishes_on_a_pool_thread_resumes_on_the_captured_context()
    {
        using var pump = new PumpedThread();

        UiContext ui = pump.Run(UiContext.Capture);

        int resumedOn = await Task.Run(async () =>
        {
            // No ambient context here — exactly the state a fire-and-forget call site leaves
            // behind, and the state in which ConfigureAwait(true) resumes on the pool.
            SynchronizationContext.Current.ShouldBeNull();

            await ui.SwitchTo();

            return Environment.CurrentManagedThreadId;
        });

        resumedOn.ShouldBe(pump.ThreadId);
    }

    [Fact]
    public async Task Switching_when_already_on_the_context_does_not_post()
    {
        using var pump = new PumpedThread();

        UiContext ui = pump.Run(UiContext.Capture);
        int before = pump.PostCount;

        // Awaited from the captured thread itself. Posting here would queue work behind the
        // caller on a pump that is not draining, which is a deadlock rather than a slowdown.
        int resumedOn = await pump.Run(async () =>
        {
            await ui.SwitchTo();
            return Environment.CurrentManagedThreadId;
        });

        resumedOn.ShouldBe(pump.ThreadId);
        pump.PostCount.ShouldBe(before);
    }

    [Fact]
    public async Task A_context_that_was_never_captured_runs_inline()
    {
        int callingThread = Environment.CurrentManagedThreadId;

        await UiContext.Inline.SwitchTo();

        Environment.CurrentManagedThreadId.ShouldBe(callingThread);
    }

    [Fact]
    public void Capturing_where_there_is_no_context_gives_the_inline_one()
    {
        UiContext? captured = null;

        // On its own thread: a bare pool thread has no context, and clearing the test
        // runner's would leak into every test that follows.
        var thread = new Thread(() => captured = UiContext.Capture()) { IsBackground = true };
        thread.Start();
        thread.Join();

        captured.ShouldBeSameAs(UiContext.Inline);
    }

    [Fact]
    public void The_default_context_is_inline()
    {
        // A harmless default rather than none: a headless host, a unit test or design-time
        // must complete rather than hang or throw.
        UiContext.Current.ShouldNotBeNull();
        UiContext.Current.IsCurrent.ShouldBeTrue();
    }

    [Fact]
    public void A_captured_context_is_not_current_on_another_thread()
    {
        using var pump = new PumpedThread();

        UiContext ui = pump.Run(UiContext.Capture);

        ui.IsCurrent.ShouldBeFalse();
        pump.Run(() => ui.IsCurrent).ShouldBeTrue();
    }

    /// <summary>A thread with a synchronization context that drains a queue, like a dispatcher.</summary>
    private sealed class PumpedThread : IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
        private readonly Thread _thread;
        private int _posts;

        public PumpedThread()
        {
            using var started = new ManualResetEventSlim();

            _thread = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new QueueContext(this));
                started.Set();

                foreach ((SendOrPostCallback callback, object? state) in _queue.GetConsumingEnumerable())
                {
                    callback(state);
                }
            })
            {
                IsBackground = true,
                Name = "pumped",
            };

            _thread.Start();
            started.Wait();
        }

        public int ThreadId => _thread.ManagedThreadId;

        public int PostCount => Volatile.Read(ref _posts);

        /// <summary>Runs something on the pumped thread and waits for its result.</summary>
        public T Run<T>(Func<T> work)
        {
            T result = default!;
            using var done = new ManualResetEventSlim();

            _queue.Add((_ =>
            {
                result = work();
                done.Set();
            }, null));

            done.Wait();
            return result;
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join(TimeSpan.FromSeconds(5));
            _queue.Dispose();
        }

        private sealed class QueueContext : SynchronizationContext
        {
            private readonly PumpedThread _owner;

            public QueueContext(PumpedThread owner) => _owner = owner;

            public override void Post(SendOrPostCallback d, object? state)
            {
                Interlocked.Increment(ref _owner._posts);
                _owner._queue.Add((d, state));
            }

            public override void Send(SendOrPostCallback d, object? state) => Post(d, state);
        }
    }
}

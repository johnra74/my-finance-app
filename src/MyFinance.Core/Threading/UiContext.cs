namespace MyFinance.Core.Threading;

/// <summary>
/// A way back onto the interface thread that does not depend on what happens to be ambient.
/// </summary>
/// <remarks>
/// <para>
/// <c>ConfigureAwait(true)</c> does not mean "come back to the interface thread". It means
/// "resume on whatever <see cref="SynchronizationContext"/> was current when this await was
/// reached, and if there was none, resume inline on whichever thread completed the task".
/// Work started from a fire-and-forget call site — of which this application has twenty-odd —
/// frequently has no such context, and the resumption then lands on a pool thread. Writing to
/// a bound <c>ObservableCollection</c> from there throws, and because nobody awaited the task,
/// nobody sees it: the page simply does not refresh.
/// </para>
/// <para>
/// This captures the context once, at startup, where the interface thread is known for
/// certain, and hands back an awaitable that posts to it. Being in <c>MyFinance.Core</c> and
/// not a <c>Dispatcher.Invoke</c> in the WPF layer is what makes it testable — constitution 4.
/// </para>
/// </remarks>
public sealed class UiContext
{
    private readonly SynchronizationContext? _context;
    private readonly int _threadId;

    private UiContext(SynchronizationContext? context, int threadId)
    {
        _context = context;
        _threadId = threadId;
    }

    /// <summary>A context that runs everything on the calling thread.</summary>
    /// <remarks>Declared first: <see cref="Current"/> defaults to it, and static initializers
    /// run in declaration order.</remarks>
    public static UiContext Inline { get; } = new(null, 0);

    /// <summary>
    /// The context everything marshals to. Inline until something captures a real one.
    /// </summary>
    /// <remarks>
    /// The default is deliberately harmless rather than absent: unit tests, design-time and
    /// any future headless host get an instance that completes synchronously instead of a
    /// null reference or a hang.
    /// </remarks>
    public static UiContext Current { get; private set; } = Inline;

    /// <summary>True when the calling thread is the one this was captured on.</summary>
    public bool IsCurrent =>
        _context is null || Environment.CurrentManagedThreadId == _threadId;

    /// <summary>Captures the calling thread and its synchronization context.</summary>
    public static UiContext Capture() =>
        SynchronizationContext.Current is SynchronizationContext context
            ? new UiContext(context, Environment.CurrentManagedThreadId)
            : Inline;

    /// <summary>Sets the context every <c>SwitchTo</c> marshals to. Called once, at startup.</summary>
    public static void SetCurrent(UiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Current = context;
    }

    /// <summary>
    /// Awaited to continue on the interface thread.
    /// </summary>
    /// <remarks>
    /// Already being on that thread completes synchronously, so the ordinary path costs
    /// nothing and cannot deadlock on a context that is waiting for the caller.
    /// </remarks>
    public UiAwaitable SwitchTo() => new(this);

    private void Post(Action continuation)
    {
        if (_context is null)
        {
            continuation();
            return;
        }

        _context.Post(static state => ((Action)state!)(), continuation);
    }

    /// <summary>The awaitable returned by <see cref="SwitchTo"/>.</summary>
    public readonly struct UiAwaitable
    {
        private readonly UiContext _owner;

        internal UiAwaitable(UiContext owner) => _owner = owner;

        public UiAwaiter GetAwaiter() => new(_owner);
    }

    /// <summary>The awaiter returned by <see cref="UiAwaitable.GetAwaiter"/>.</summary>
    public readonly struct UiAwaiter : System.Runtime.CompilerServices.INotifyCompletion
    {
        private readonly UiContext _owner;

        internal UiAwaiter(UiContext owner) => _owner = owner;

        public bool IsCompleted => _owner.IsCurrent;

        public void OnCompleted(Action continuation)
        {
            ArgumentNullException.ThrowIfNull(continuation);
            _owner.Post(continuation);
        }

        public void GetResult()
        {
        }
    }
}

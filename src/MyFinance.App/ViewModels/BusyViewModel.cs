using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.Core.Progress;
using MyFinance.Core.Threading;

namespace MyFinance.App.ViewModels;

/// <summary>
/// Runs slow work without freezing the window, and says how it is going.
/// </summary>
/// <remarks>
/// <para>
/// The three things that have to happen together — get off the UI thread, report progress,
/// allow cancelling — live in one method, because getting two of the three right is what
/// produced the frozen window this replaces. Awaiting a data service directly looks
/// asynchronous and is not: SQLite's provider implements its async methods as synchronous
/// wrappers, so nineteen thousand inserts run on the thread that would otherwise be
/// repainting, and Windows calls the application "Not Responding".
/// </para>
/// <para>
/// Running a whole service call on a pool thread is safe because every service creates its own
/// short-lived context from <c>IBookContextFactory</c>; nothing is shared across threads.
/// </para>
/// </remarks>
public abstract partial class BusyViewModel : ObservableObject
{
    private CancellationTokenSource? _cancellation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    private bool _isBusy;

    /// <summary>What the work is doing now, for the overlay to show.</summary>
    [ObservableProperty]
    private WorkProgress _progress;

    /// <summary>Set when the last operation was stopped rather than finished.</summary>
    [ObservableProperty]
    private bool _wasCancelled;

    /// <summary>Whether there is something running that can be stopped.</summary>
    public bool CanStop => IsBusy && _cancellation is not null;

    /// <summary>Asks the running operation to stop.</summary>
    /// <remarks>
    /// Named Stop rather than Cancel because a dialog's Cancel button already means "close
    /// this and change nothing", and the two would be confused at the call site.
    /// </remarks>
    [RelayCommand]
    protected void Stop() => _cancellation?.Cancel();

    /// <summary>Runs work in the background, reporting progress, and returns its result.</summary>
    protected async Task<T?> RunBusyAsync<T>(
        string stage,
        Func<IProgress<WorkProgress>, CancellationToken, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        // Constructed here, on the UI thread, because Progress<T> captures the
        // synchronization context it is built on and posts its callbacks back to it. Built
        // anywhere else it would raise property changes on a pool thread and WPF would throw.
        var reporter = new Progress<WorkProgress>(report => Progress = report);

        using var cancellation = new CancellationTokenSource();

        _cancellation = cancellation;
        WasCancelled = false;
        Progress = WorkProgress.Starting(stage);
        IsBusy = true;
        OnPropertyChanged(nameof(CanStop));

        bool cancelled = false;

        try
        {
            try
            {
                return await Task.Run(
                    () => work(reporter, cancellation.Token),
                    cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Asked for, so not an error. The caller decides what to show. Recorded in a
                // local rather than set here, because this runs on the pool thread and
                // raising a property change from there is the very thing being fixed.
                cancelled = true;
                return default;
            }
            finally
            {
                // Explicit, not ambient. ConfigureAwait(true) returns to the interface thread
                // only when a context happened to be current at the await, and from a
                // fire-and-forget call site — of which there are twenty-odd — it often is
                // not. The resumption then lands on a pool thread, the page's collection is
                // rebuilt from there, and WPF throws where nobody is listening.
                //
                // In a finally so that it holds on every path: finished, stopped, or thrown.
                // Everything below, and everything the caller does afterwards, is therefore
                // on the interface thread.
                await UiContext.Current.SwitchTo();
            }
        }
        finally
        {
            _cancellation = null;
            WasCancelled = cancelled;
            IsBusy = false;
            Progress = default;
            OnPropertyChanged(nameof(CanStop));
        }
    }

    /// <summary>Runs work in the background with nothing to return.</summary>
    protected async Task<bool> RunBusyAsync(
        string stage,
        Func<IProgress<WorkProgress>, CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        bool finished = await RunBusyAsync<bool>(
            stage,
            async (reporter, token) =>
            {
                await work(reporter, token).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(true);

        return finished;
    }
}

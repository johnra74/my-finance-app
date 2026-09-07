using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Services;
using MyFinance.Core.Diagnostics;
using MyFinance.Core.Help;
using MyFinance.App.ViewModels.Dialogs;
using MyFinance.Core.Progress;
using MyFinance.Import.Categorization;
using MyFinance.Data.Security;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels;

/// <summary>One tab in the top-level navigation bar.</summary>
public sealed partial class SectionTab : ObservableObject
{
    public required AppSection Section { get; init; }

    public required string Text { get; init; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// The main window: top-level tabs, the left task pane, and the content region.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly IBookSession _session;
    private readonly ScheduleService _schedules;
    private readonly IDialogService _dialogs;
    private readonly IModalService _modals;
    private readonly IServiceProviderAccessor _services;
    private readonly BookBackupService _backups;
    private readonly PayeeEmbeddingService _embeddings;
    private readonly ITextEmbedder _embedder;
    private readonly IHelpService _help;

    public ShellViewModel(
        INavigationService navigation,
        IBookSession session,
        ScheduleService schedules,
        IDialogService dialogs,
        IModalService modals,
        IServiceProviderAccessor services,
        BookBackupService backups,
        PayeeEmbeddingService embeddings,
        ITextEmbedder embedder,
        IHelpService help)
    {
        _navigation = navigation;
        _session = session;
        _schedules = schedules;
        _dialogs = dialogs;
        _modals = modals;
        _services = services;
        _backups = backups;
        _embeddings = embeddings;
        _embedder = embedder;
        _help = help;

        Sections =
        [
            new SectionTab { Section = AppSection.Home, Text = "Home" },
            new SectionTab { Section = AppSection.Banking, Text = "Banking" },
            new SectionTab { Section = AppSection.Bills, Text = "Bills" },
            new SectionTab { Section = AppSection.Reports, Text = "Reports" },
            new SectionTab { Section = AppSection.Budget, Text = "Budget" },
        ];

        _navigation.Navigated += OnNavigated;
    }

    /// <summary>Raised when the book is locked, so the window can return to the unlock screen.</summary>
    public event EventHandler? Locked;

    public IReadOnlyList<SectionTab> Sections { get; }

    public PageViewModel? CurrentPage => _navigation.CurrentPage;

    public IReadOnlyList<TaskGroup> TaskGroups => CurrentPage?.TaskGroups ?? [];

    public bool HasTaskPane => TaskGroups.Count > 0;

    public string BookName => _session.BookName ?? "MyFinance";

    /// <summary>
    /// What is happening quietly in the background, or nothing.
    /// </summary>
    /// <remarks>
    /// A line in the task pane rather than an overlay, because nothing waits for this work.
    /// Covering the screen for something the user can happily ignore would be a lie about
    /// what is going on; saying nothing at all for half a minute is how the rest of this
    /// application came to look hung.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkingInBackground))]
    private string _backgroundStatus = string.Empty;

    public bool IsWorkingInBackground => !string.IsNullOrEmpty(BackgroundStatus);

    public bool CanGoBack => _navigation.CanGoBack;

    /// <summary>Today's date, shown at the top of the task pane as Microsoft Money does.</summary>
    public string TodayText => DateTime.Now.ToString("MMMM d, yyyy");

    [RelayCommand]
    private void SelectSection(SectionTab? tab)
    {
        if (tab is not null)
        {
            _navigation.GoToSection(tab.Section);
        }
    }

    [RelayCommand]
    private void GoBack() => _navigation.GoBack();

    [RelayCommand]
    private void RunTask(TaskLink? link) => link?.Execute();

    /// <summary>
    /// Opens the documentation at the page for whatever is on screen.
    /// </summary>
    /// <remarks>
    /// Contextual rather than a contents page, because the question somebody has is about the
    /// screen in front of them. A page that has not declared a topic falls back to the front
    /// page rather than doing nothing.
    /// </remarks>
    [RelayCommand]
    private void Help() => _help.Open(CurrentPage?.HelpTopic ?? HelpTopic.Contents);

    /// <summary>
    /// Shows what this build is and what it is made of.
    /// </summary>
    /// <remarks>
    /// Not decoration. The executable is one self-contained file carrying SQLCipher, the .NET
    /// runtime, an embedding model and the help pages, and several of those licences require
    /// their notice to travel with the binary. This is where it travels.
    /// </remarks>
    [RelayCommand]
    private void About() => _modals.Show(_services.GetRequired<AboutViewModel>());

    [RelayCommand]
    private async Task BackupsAsync()
    {
        BackupsViewModel backups = _services.GetRequired<BackupsViewModel>();
        await backups.RefreshAsync().ConfigureAwait(true);

        _modals.Show(backups);
    }

    /// <summary>
    /// Opens the folder the log is kept in, and offers to record more detail.
    /// </summary>
    /// <remarks>
    /// The log lives in local application data rather than beside the book, which keeps it off
    /// any cloud sync the book's folder happens to have — and makes it hard to find. This is
    /// the answer to that: one click to the folder.
    /// </remarks>
    [RelayCommand]
    private void Diagnostics()
    {
        DiagnosticSink log = _services.GetRequired<DiagnosticSink>();

        bool verbose = _dialogs.Confirm(
            "Diagnostics",
            "MyFinance records what goes wrong to a log file, so a fault can be looked into "
            + "afterwards. It never records your payees, amounts or account names.\n\n"
            + $"The log is kept in:\n{log.Directory}\n\n"
            + "If a problem keeps happening, turn on extra detail — it records which operations "
            + "run and in what order, which makes an intermittent fault far easier to find. "
            + "It still records nothing about what is in your book.\n\n"
            + "Turn extra detail on?");

        log.Verbose = verbose;

        if (verbose)
        {
            log.Breadcrumb(Operation.Startup);
        }

        _dialogs.OpenFolder(log.Directory);
    }

    /// <summary>
    /// Writes the whole book out as one readable file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Microsoft Money's file format is the reason this application exists. Reports already
    /// export to CSV, but that is a rendered report — this is the book, and without it a
    /// MyFinance book would be readable only from inside MyFinance, which is materially the
    /// trap the user was in before.
    /// </para>
    /// <para>
    /// The warning is in the sentence the user reads <em>before</em> choosing a location, not
    /// in a manual. A book that took Argon2id half a second to open should not turn into a
    /// plaintext file without that being said in the same breath.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private async Task ExportBookAsync()
    {
        if (_session.Book is null)
        {
            return;
        }

        bool proceed = _dialogs.Confirm(
            "Export your book",
            "This writes everything in your book — every account, transaction, payee, bill, "
            + "budget and rule — to a single file that can be read without MyFinance.\n\n"
            + "That file is NOT encrypted. Anyone who can read it can read your entire "
            + "financial history, so keep it somewhere you would keep a bank statement.\n\n"
            + "Continue?");

        if (!proceed)
        {
            return;
        }

        string suggested = $"{_session.BookName ?? "book"}-{DateTime.Now:yyyy-MM-dd}.json";
        string? path = _dialogs.PickExportLocation(suggested);

        if (path is null)
        {
            return;
        }

        try
        {
            BookExportService export = _services.GetRequired<BookExportService>();

            // Off the interface thread, with a stage and a stop, like every other long job.
            await Task.Run(() => export.ExportAsync(path)).ConfigureAwait(true);

            _dialogs.ShowInformation("Export", $"Your book was written to {Path.GetFileName(path)}.");
        }
        catch (OperationCanceledException)
        {
            // Stopping leaves no file; nothing to report.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError("Export", $"That file could not be written: {ex.Message}");
        }
    }

    /// <summary>
    /// Backs the book up, then locks it.
    /// </summary>
    /// <remarks>
    /// The backup happens on the way out because that is the moment there is something new
    /// worth keeping and the user has finished with it. It never prevents locking: a memory
    /// stick that has been unplugged is a reason to say so, not a reason to trap somebody in
    /// an application they have finished with.
    /// </remarks>
    [RelayCommand]
    private async Task LockBookAsync()
    {
        await BackUpQuietlyAsync().ConfigureAwait(true);

        _session.Lock();
        Locked?.Invoke(this, EventArgs.Empty);
    }

    private async Task BackUpQuietlyAsync()
    {
        if (_session.Book is not Book book)
        {
            return;
        }

        try
        {
            await _backups.BackUpOnCloseAsync(book).ConfigureAwait(true);
        }
        catch (BackupException ex)
        {
            _dialogs.ShowError(
                "Backup",
                $"The book is safe, but the automatic backup could not be written.\n\n{ex.Message}");
        }
    }

    /// <summary>Navigates to the default landing page. Called once after the shell opens.</summary>
    public void Start()
    {
        _navigation.GoToSection(AppSection.Home);
        _ = EnterDueBillsAsync();
        _ = BuildSuggestionIndexAsync();
    }

    /// <summary>
    /// Embeds any payee that needs it, so the first import after a migration can use them.
    /// </summary>
    /// <remarks>
    /// In the background and never awaited. On a freshly migrated book this is four thousand
    /// payees and around half a minute; on every subsequent open it is a handful or none at
    /// all. Nothing waits for it — an import that starts first simply has fewer vectors to
    /// draw on and says nothing about it, which is the right trade against making somebody
    /// watch a progress bar before they can look at their own accounts.
    /// </remarks>
    private async Task BuildSuggestionIndexAsync()
    {
        if (!_embedder.IsAvailable)
        {
            return;
        }

        try
        {
            var reporter = new Progress<WorkProgress>(report =>
                BackgroundStatus = report.IsDeterminate && report.Done < report.Total
                    ? report.Text
                    : string.Empty);

            await Task.Run(() => _embeddings.RefreshAsync(_embedder, reporter)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            // A suggestion source that cannot build itself is not worth a dialog. The
            // categorization chain carries on without it.
            System.Diagnostics.Debug.WriteLine($"The suggestion index could not be built: {ex.Message}");
        }
        finally
        {
            BackgroundStatus = string.Empty;
        }
    }

    /// <summary>
    /// Writes any bill set to enter itself that has fallen due since the book was last open.
    /// </summary>
    /// <remarks>
    /// Only touches series the user explicitly marked to auto-enter, and reports what it did
    /// rather than doing it silently — writing to the register unasked is exactly the sort of
    /// thing that should announce itself.
    /// </remarks>
    private async Task EnterDueBillsAsync()
    {
        try
        {
            EnterResult result = await _schedules
                .AutoEnterDueAsync(DateOnly.FromDateTime(DateTime.Today))
                .ConfigureAwait(true);

            if (result.Entered == 0)
            {
                return;
            }

            _dialogs.ShowInformation(
                "Scheduled transactions",
                $"{result.Entered} scheduled transaction{(result.Entered == 1 ? " was" : "s were")} entered into your register.");
        }
        catch (BookValidationException ex)
        {
            _dialogs.ShowError("Scheduled transactions", ex.Message);
        }
    }

    private void OnNavigated(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(TaskGroups));
        OnPropertyChanged(nameof(HasTaskPane));
        OnPropertyChanged(nameof(CanGoBack));

        foreach (SectionTab tab in Sections)
        {
            tab.IsSelected = tab.Section == _navigation.CurrentSection;
        }
    }
}

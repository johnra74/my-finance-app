using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyFinance.App.Services;
using MyFinance.App.ViewModels;
using MyFinance.App.ViewModels.Pages;
using MyFinance.App.ViewModels.Dialogs;
using MyFinance.App.Views;
using MyFinance.App.Views.Dialogs;
using MyFinance.Data;
using MyFinance.Import.Categorization;
using MyFinance.Semantics;
using MyFinance.Data.Security;
using MyFinance.Core.Diagnostics;
using MyFinance.Core.Threading;
using MyFinance.Data.Services;

namespace MyFinance.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private DiagnosticLog? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The one place the interface thread is known for certain. Captured here so that
        // background work has somewhere definite to come back to, rather than depending on
        // whatever synchronization context happens to be current at each await.
        UiContext.SetCurrent(UiContext.Capture());

        // Built before anything else, so a failure during startup is still recorded.
        _log = DiagnosticLogFactory.Create();
        _log.Describe(typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown");
        _log.Breadcrumb(Operation.Startup);

        _services = BuildServices();
        _services.GetRequiredService<DiagnosticSink>().Use(_log);

        // Three hooks, because there are three ways an exception leaves. Only the first
        // existed before, and it recorded nothing.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnBackgroundThreadException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        ShowStartupWindow();
    }

    /// <summary>
    /// Takes the closing backup before the session goes away.
    /// </summary>
    /// <remarks>
    /// Closing the window is how most sessions end, so the backup has to happen here as well
    /// as on the Lock button. Failures are swallowed: an application that refuses to shut
    /// down is worse than a missed backup, and the next one will be along shortly.
    /// </remarks>
    private void BackUpOnExit()
    {
        if (_services?.GetService<IBookSession>() is not IBookSession session
            || session.Book is not Book book)
        {
            return;
        }

        BookBackupService backups = _services.GetRequiredService<BookBackupService>();

        try
        {
            // Started on the thread pool rather than awaited here: nothing then tries to
            // continue back onto a dispatcher that is already shutting down. The timeout is
            // for the case nobody can test — a backup folder on a network drive that has
            // gone away — where hanging forever on exit is the worst possible behaviour.
            if (!Task.Run(() => backups.BackUpOnCloseAsync(book)).Wait(TimeSpan.FromSeconds(30)))
            {
                Debug.WriteLine("The closing backup did not finish in time and was abandoned.");
            }
        }
        catch (AggregateException ex)
        {
            Debug.WriteLine($"The closing backup failed: {ex.InnerException?.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        BackUpOnExit();

        // Last, so anything that failed on the way out is still recorded.
        _log?.Breadcrumb(Operation.CloseBook);
        _log?.Dispose();

        // Disposing the session locks the book and zeroes the key.
        _services?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IBookSession, BookSession>();
        services.AddSingleton<DiagnosticSink>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IHelpService, HelpService>();
        services.AddSingleton<IRecentBooksService, RecentBooksService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IServiceProviderAccessor, ServiceProviderAccessor>();
        services.AddSingleton<IModalService>(_ => BuildModalService());

        // The data services reach the open book through the session, which is the only
        // thing that knows whether one is unlocked.
        services.AddSingleton<IBookContextFactory>(sp => sp.GetRequiredService<IBookSession>());
        services.AddSingleton<AccountService>();
        services.AddSingleton<CategoryService>();
        services.AddSingleton<PayeeService>();
        services.AddSingleton<RegisterService>();
        services.AddSingleton<ReconcileService>();
        // Loaded once and shared: the model is 23 MB in memory and a second copy would buy
        // nothing. Create() never throws, so a machine that cannot load it simply gets an
        // embedder that reports itself unavailable.
        services.AddSingleton<ITextEmbedder>(_ => OnnxTextEmbedder.Create());
        services.AddSingleton<PayeeEmbeddingService>();

        // A singleton because the point of it is the cache: one trained classifier shared by
        // the register and the importer, rather than one per dialog.
        services.AddSingleton(sp => new SuggestionService(
            sp.GetRequiredService<IBookContextFactory>(),
            sp.GetRequiredService<ITextEmbedder>()));
        services.AddSingleton(sp => new ImportService(
            sp.GetRequiredService<IBookContextFactory>(),
            sp.GetRequiredService<ITextEmbedder>()));
        services.AddSingleton<SettingsService>();
        services.AddSingleton<CategorizationRuleService>();
        services.AddSingleton<ScheduleService>();
        services.AddSingleton<ReportService>();
        services.AddSingleton<BudgetService>();
        services.AddSingleton<MigrationService>();
        services.AddSingleton<BookBackupService>();
        services.AddSingleton<BookExportService>();
        services.AddSingleton<InvestmentService>();

        services.AddTransient<StartupViewModel>();
        services.AddSingleton<ShellViewModel>();

        // Pages are singletons so returning to a section keeps its scroll position and
        // filters; each reloads its data in OnNavigatedToAsync.
        services.AddSingleton<HomePageViewModel>();
        services.AddSingleton<AccountListPageViewModel>();
        services.AddSingleton<CategoriesPageViewModel>();
        services.AddSingleton<PayeesPageViewModel>();
        services.AddSingleton<RulesPageViewModel>();
        services.AddSingleton<BillsPageViewModel>();
        services.AddSingleton<ForecastPageViewModel>();
        services.AddSingleton<ReportsPageViewModel>();
        services.AddSingleton<BudgetPageViewModel>();

        // The register is transient: it is opened against a particular account, and a
        // singleton would carry the previous account's rows into the next one.
        services.AddTransient<RegisterPageViewModel>();

        // The wizard holds the state of one file, so a fresh one is needed per import.
        services.AddTransient<ImportWizardViewModel>();
        services.AddTransient<ImportHistoryViewModel>();
        services.AddTransient<MigrationWizardViewModel>();
        services.AddTransient<BackupsViewModel>();
        services.AddTransient<AboutViewModel>();

        return services.BuildServiceProvider();
    }

    /// <summary>Maps each editor view model onto the window that hosts it.</summary>
    private static ModalService BuildModalService()
    {
        var modals = new ModalService();

        modals.Register<AccountEditorViewModel>(() => new AccountEditorWindow());
        modals.Register<TransactionEditorViewModel>(() => new TransactionEditorWindow());
        modals.Register<SplitEditorViewModel>(() => new SplitEditorWindow());
        modals.Register<CategoryEditorViewModel>(() => new CategoryEditorWindow());
        modals.Register<ReconcileViewModel>(() => new ReconcileWindow());
        modals.Register<ImportWizardViewModel>(() => new ImportWizardWindow());
        modals.Register<ImportHistoryViewModel>(() => new ImportHistoryWindow());
        modals.Register<RuleEditorViewModel>(() => new RuleEditorWindow());
        modals.Register<ScheduleEditorViewModel>(() => new ScheduleEditorWindow());
        modals.Register<BudgetEditorViewModel>(() => new BudgetEditorWindow());
        modals.Register<MigrationWizardViewModel>(() => new MigrationWizardWindow());
        modals.Register<BackupsViewModel>(() => new BackupsWindow());
        modals.Register<AboutViewModel>(() => new AboutWindow());

        return modals;
    }

    private void ShowStartupWindow()
    {
        StartupViewModel viewModel = _services!.GetRequiredService<StartupViewModel>();
        var window = new StartupWindow { DataContext = viewModel };

        viewModel.Unlocked += (_, _) =>
        {
            ShowShellWindow();
            window.Close();
        };

        MainWindow = window;
        window.Show();
    }

    private void ShowShellWindow()
    {
        ShellViewModel viewModel = _services!.GetRequiredService<ShellViewModel>();
        var window = new ShellWindow { DataContext = viewModel };

        viewModel.Locked += (_, _) =>
        {
            ShowStartupWindow();
            window.Close();
        };

        MainWindow = window;
        window.Show();
        viewModel.Start();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Recorded before the box is shown: if showing it fails too, the original fault is
        // already on disk.
        _log?.Failure(Operation.UnhandledOnInterfaceThread, e.Exception);

        MessageBox.Show(
            $"Something went wrong and the action was cancelled.\n\n{e.Exception.Message}",
            "MyFinance",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Keeping the app alive protects unsaved work far better than tearing it down; the
        // failed operation has already been rolled back by its own transaction.
        e.Handled = true;
    }

    /// <summary>
    /// A failure on a thread nobody is watching.
    /// </summary>
    /// <remarks>
    /// The process is going down and cannot be saved. What can be saved is the reason, which
    /// until now went with it — there was no hook here at all.
    /// </remarks>
    private void OnBackgroundThreadException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _log?.Failure(Operation.UnhandledOnBackgroundThread, exception);
        }

        // Drained here rather than left to Dispose: this process is about to end.
        _log?.Flush();
    }

    /// <summary>
    /// A failure inside a task nobody awaited.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The quietest of the three, and the one most likely to explain an intermittent fault.
    /// There are twenty-odd <c>_ = SomethingAsync()</c> call sites — every page's filter
    /// handler, navigation, the auto-entry and index build at startup — and until now an
    /// exception in any of them was dropped in silence. No message box, no log, nothing: the
    /// page simply did not refresh.
    /// </para>
    /// <para>
    /// <c>SetObserved</c> keeps that outcome exactly as it was. The log is added beside the
    /// behaviour, not in place of it.
    /// </para>
    /// </remarks>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _log?.Failure(Operation.UnobservedTask, e.Exception);
        e.SetObserved();
    }
}
